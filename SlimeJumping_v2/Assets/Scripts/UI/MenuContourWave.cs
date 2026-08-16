using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.UI
{
    /// Multi-path spring-damper contour wave for the menu jelly surfaces — the
    /// WaterZone_v2 / SquishyCloud / AsciiArtWobble wave core generalized to several
    /// closed loops (letter outline + hole counters) sharing one field. Units are
    /// caller-local. Paths must follow the ink-on-left winding contract so the
    /// inward normal is the left of travel. Explicit integration is auto-clamped at
    /// dt²(spring + 4·spread) ≤ 3.6 like the platform lineage.
    public sealed class MenuContourWave
    {
        public Vector2[] rest;
        public Vector2[] inwardNormal;
        public float[] disp;
        public float[] vel;
        float[] contactF;  // 이번 Step 의 접촉 falloff (0 = 자유 표면)
        int[] pathStart;   // pathCount+1 prefix offsets into the flat arrays

        public int PointCount => rest != null ? rest.Length : 0;
        public int PathCount => pathStart != null ? pathStart.Length - 1 : 0;

        public void Build(IReadOnlyList<Vector2[]> paths, float maxSeg)
        {
            var pts = new List<Vector2>();
            pathStart = new int[paths.Count + 1];
            for (int p = 0; p < paths.Count; p++)
            {
                pathStart[p] = pts.Count;
                Subdivide(paths[p], maxSeg, pts);
            }
            pathStart[paths.Count] = pts.Count;
            rest = pts.ToArray();
            int n = rest.Length;
            inwardNormal = new Vector2[n];
            disp = new float[n];
            vel = new float[n];
            contactF = new float[n];
            for (int p = 0; p < PathCount; p++)
            {
                int s = pathStart[p], c = pathStart[p + 1] - s;
                for (int i = 0; i < c; i++)
                {
                    Vector2 tangent = rest[s + (i + 1) % c] - rest[s + (i - 1 + c) % c];
                    Vector2 left = new Vector2(-tangent.y, tangent.x);
                    inwardNormal[s + i] = left.sqrMagnitude > 1e-10f ? left.normalized : Vector2.up;
                }
            }
        }

        static void Subdivide(Vector2[] src, float maxSeg, List<Vector2> dst)
        {
            for (int i = 0; i < src.Length; i++)
            {
                Vector2 a = src[i], b = src[(i + 1) % src.Length];
                dst.Add(a);
                int div = Mathf.FloorToInt(Vector2.Distance(a, b) / Mathf.Max(1e-4f, maxSeg));
                for (int j = 1; j <= div; j++)
                    dst.Add(Vector2.Lerp(a, b, (float)j / (div + 1)));
            }
        }

        public int PathPointCount(int p) => pathStart[p + 1] - pathStart[p];

        /// 평탄화 인덱스가 속한 path 의 [start, start+count) — arc 순회는 path 안에서만 감아야 한다.
        public void GetPathRange(int pointIndex, out int start, out int count)
        {
            int p = 0;
            while (pathStart[p + 1] <= pointIndex) p++;
            start = pathStart[p];
            count = pathStart[p + 1] - start;
        }

        public float ClampedSpring(float dt, float spring, float spread)
        {
            float stab = dt * dt * (spring + 4f * spread);
            return stab > 3.6f ? spring * (3.6f / stab) : spring;
        }

        public void Inject(float velocityAmp, Vector2 foot, Vector2 push, float radius)
        {
            for (int i = 0; i < rest.Length; i++)
            {
                float f = Falloff(Vector2.Distance(rest[i], foot), radius);
                if (f <= 0f) continue;
                float align = Vector2.Dot(push, inwardNormal[i]);
                if (align <= 0f) continue;
                vel[i] += velocityAmp * f * align;
            }
        }

        public void Step(float dt, float spring, float spread, float damping,
                         float pressDepth, float pressRadius, Vector2 foot, Vector2 push,
                         float maxVel, float contactDamping = 0f)
        {
            float k = spring, s = spread;
            float stab = dt * dt * (k + 4f * s);
            if (stab > 3.6f)
            {
                float f = 3.6f / stab;
                k *= f;
                s *= f;
            }

            bool pressing = pressRadius > 0f;
            for (int i = 0; i < rest.Length; i++)
                contactF[i] = pressing ? Falloff(Vector2.Distance(rest[i], foot), pressRadius) : 0f;

            for (int p = 0; p < PathCount; p++)
            {
                int st = pathStart[p], c = pathStart[p + 1] - st;
                for (int i = 0; i < c; i++)
                {
                    int idx = st + i;
                    float l = disp[st + (i - 1 + c) % c];
                    float r = disp[st + (i + 1) % c];
                    float accel = -s * (2f * disp[idx] - l - r) - k * disp[idx] - damping * vel[idx];
                    float f = contactF[idx];
                    if (f > 0f)
                    {
                        if (pressDepth > 0f)
                        {
                            float align = Vector2.Dot(push, inwardNormal[idx]);
                            if (align > 0f) accel += pressDepth * k * f * align;   // 평형점 ≈ pressDepth.
                        }
                        // 접촉부 추가 감쇠 — 발밑(측벽 포함)을 평형에 눌러 앉혀 슬라임과 싸우지 않게.
                        accel -= contactDamping * f * vel[idx];
                    }
                    vel[idx] += accel * dt;
                }
            }
            for (int i = 0; i < disp.Length; i++)
            {
                vel[i] = Mathf.Clamp(vel[i], -maxVel, maxVel);
                // 접촉부 후퇴 금지 — 표면이 슬라임에서 물러나는(바깥) 속도만 소거, 따라 눌리는 건 허용.
                if (contactF[i] > 0f && vel[i] < 0f) vel[i] *= 1f - contactF[i];
                disp[i] += vel[i] * dt;
            }
            // 부피 보존: path 별 Σdisp=0. 재분배는 비접촉 표면에만 실어 슬라임 밑을 잡아빼지 않는다.
            for (int p = 0; p < PathCount; p++)
            {
                int st = pathStart[p], c = pathStart[p + 1] - st;
                float sum = 0f, freeW = 0f;
                for (int i = 0; i < c; i++)
                {
                    sum += disp[st + i];
                    freeW += 1f - contactF[st + i];
                }
                if (freeW < 0.5f) continue;   // path 전체가 발밑(구멍 링 등)이면 재분배 생략.
                float shift = sum / freeW;
                for (int i = 0; i < c; i++)
                    disp[st + i] -= shift * (1f - contactF[st + i]);
            }
        }

        public Vector2 OffsetAt(int idx, float maxOff)
        {
            float d = Mathf.Clamp(disp[idx], -maxOff, maxOff);
            return inwardNormal[idx] * d;
        }

        public void FillPath(int p, Vector2[] dst, float maxOff)
        {
            int st = pathStart[p], c = pathStart[p + 1] - st;
            for (int i = 0; i < c; i++)
                dst[i] = rest[st + i] + OffsetAt(st + i, maxOff);
        }

        /// K=4 IDW of the nearest contour arc — SquishyCloud's vertex mapping as an
        /// on-demand query for sparse callers (button visual verts).
        public Vector2 SampleOffset(Vector2 point, float radius, int arcWindow, float maxOff)
        {
            if (rest == null || rest.Length == 0) return Vector2.zero;
            int nearest = -1;
            float nearestD2 = float.MaxValue;
            for (int i = 0; i < rest.Length; i++)
            {
                float d2 = (rest[i] - point).sqrMagnitude;
                if (d2 < nearestD2) { nearestD2 = d2; nearest = i; }
            }
            if (nearest < 0 || nearestD2 > radius * radius) return Vector2.zero;

            GetPathRange(nearest, out int st, out int c);
            int arc = Mathf.Clamp(arcWindow, 1, Mathf.Max(1, (c - 1) / 2));

            Vector2 sum = Vector2.zero;
            float wSum = 0f;
            for (int j = -arc; j <= arc; j++)
            {
                int idx = st + (nearest - st + j + c) % c;
                float d2 = (rest[idx] - point).sqrMagnitude;
                if (d2 > radius * radius) continue;
                float w = 1f / (d2 + 1e-4f);
                sum += OffsetAt(idx, maxOff) * w;
                wSum += w;
            }
            return wSum > 0f ? sum / wSum : Vector2.zero;
        }

        public bool IsCalm(float eps)
        {
            for (int i = 0; i < disp.Length; i++)
                if (Mathf.Abs(disp[i]) > eps || Mathf.Abs(vel[i]) > eps) return false;
            return true;
        }

        public float PeakAmplitude()
        {
            float peak = 0f;
            for (int i = 0; i < disp.Length; i++)
            {
                float a = Mathf.Abs(disp[i]);
                if (a > peak) peak = a;
            }
            return peak;
        }

        public void Clear()
        {
            if (disp == null) return;
            System.Array.Clear(disp, 0, disp.Length);
            System.Array.Clear(vel, 0, vel.Length);
        }

        // smoothstep — 중심 1, 반경 밖 0.
        public static float Falloff(float dist, float radius)
        {
            if (radius <= 0f) return 0f;
            float t = 1f - Mathf.Clamp01(dist / radius);
            return t * t * (3f - 2f * t);
        }
    }
}
