using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.UI
{
    /// One jelly title letter in the menu arena: baked glyph ink contours (holes
    /// included) drive the shared jelly collider/wave, and the visual is a
    /// subdivided quad grid sampling the TMP SDF atlas via the MenuGlyphJelly
    /// shader — holes need no mesh topology because the SDF alpha carves them.
    /// Each grid vertex is bound to the contour by SquishyCloud-style K=4 arc IDW.
    /// Local space = font units; the transform scale converts to world.
    [DefaultExecutionOrder(-100)] // 속성은 상속 보장이 없어 구체 클래스에도 명시.
    public sealed class MenuLetterJelly : MenuJellyBase
    {
        public struct GlyphRenderData
        {
            public Vector2 inkMin;
            public Vector2 inkMax;
            public Vector2 uvAdd;     // font units → atlas px: glyphRect.xy − inkMin
            public Vector2 uvScale;   // 1 / atlas size
            public float paddingFu;   // atlas padding, font units (glow band coverage)
        }

        const int K = 4;

        Mesh mesh;
        Vector3[] restVerts;
        Vector3[] vertScratch;
        int[] vertInfluence;
        float[] vertWeight;

        public static MenuLetterJelly Create(Transform parent, string name, int layer,
            Vector2 worldOrigin, float worldPerFontUnit, MenuGlyphContours.GlyphEntry entry,
            GlyphRenderData render, Material material, MenuSlimeArenaConfig cfg,
            SlimeController slime)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(worldOrigin.x, worldOrigin.y, cfg.letterZOffset);
            go.transform.localScale = Vector3.one * worldPerFontUnit;
            var jelly = go.AddComponent<MenuLetterJelly>();

            var paths = new Vector2[entry.paths.Length][];
            for (int i = 0; i < paths.Length; i++) paths[i] = entry.paths[i].points;
            if (!jelly.InitJelly(cfg, slime, paths, 1f / Mathf.Max(1e-5f, worldPerFontUnit)))
            {
                Destroy(go);
                return null;
            }
            jelly.BuildSkin(render, material);
            return jelly;
        }

        void BuildSkin(GlyphRenderData r, Material material)
        {
            Vector2 lo = r.inkMin - Vector2.one * r.paddingFu;
            Vector2 hi = r.inkMax + Vector2.one * r.paddingFu;
            float step = Mathf.Max(0.5f, cfg.skinGridStep * toLocal);
            int nx = Mathf.Max(2, Mathf.CeilToInt((hi.x - lo.x) / step));
            int ny = Mathf.Max(2, Mathf.CeilToInt((hi.y - lo.y) / step));

            int vc = (nx + 1) * (ny + 1);
            restVerts = new Vector3[vc];
            vertScratch = new Vector3[vc];
            var uvs = new Vector2[vc];
            vertInfluence = new int[vc * K];
            vertWeight = new float[vc * K];

            float radius = cfg.visualInfluenceRadius * toLocal;
            float r2 = radius * radius;
            int arcW = Mathf.Clamp(cfg.mappingArcWindow, 1, Mathf.Max(1, (wave.PointCount - 1) / 4));
            var bestIdx = new int[K];
            var bestD2 = new float[K];

            for (int y = 0; y <= ny; y++)
            {
                for (int x = 0; x <= nx; x++)
                {
                    int v = y * (nx + 1) + x;
                    var pos = new Vector2(Mathf.Lerp(lo.x, hi.x, (float)x / nx),
                                          Mathf.Lerp(lo.y, hi.y, (float)y / ny));
                    restVerts[v] = pos;
                    vertScratch[v] = pos;
                    uvs[v] = Vector2.Scale(pos + r.uvAdd, r.uvScale);
                    BindVertex(v, pos, r2, arcW, bestIdx, bestD2);
                }
            }

            var tris = new int[nx * ny * 6];
            int t = 0;
            for (int y = 0; y < ny; y++)
            {
                for (int x = 0; x < nx; x++)
                {
                    int v = y * (nx + 1) + x;
                    tris[t++] = v; tris[t++] = v + nx + 1; tris[t++] = v + 1;
                    tris[t++] = v + 1; tris[t++] = v + nx + 1; tris[t++] = v + nx + 2;
                }
            }

            mesh = new Mesh { name = "GlyphJelly" };
            mesh.MarkDynamic();
            mesh.vertices = restVerts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            var b = mesh.bounds;
            b.Expand(cfg.maxDisplacement * toLocal * 2f);
            mesh.bounds = b;

            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        // 최근접 윤곽점 주변 arc 만 후보 — XY 거리만 쓰면 다른 획 윤곽이 섞인다.
        void BindVertex(int v, Vector2 pos, float r2, int arcW, int[] bestIdx, float[] bestD2)
        {
            var rest = wave.rest;
            int n = rest.Length;
            int nearest = -1;
            float nearestD2 = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                float d2 = (rest[i] - pos).sqrMagnitude;
                if (d2 < nearestD2) { nearestD2 = d2; nearest = i; }
            }

            for (int k = 0; k < K; k++) { bestIdx[k] = -1; bestD2[k] = float.MaxValue; }
            if (nearest >= 0 && nearestD2 <= r2)
            {
                // arc 는 nearest 가 속한 path 안에서만 감는다 — 평탄화 배열을 넘으면 구멍 윤곽이 섞인다.
                wave.GetPathRange(nearest, out int st, out int c);
                int arc = Mathf.Min(arcW, Mathf.Max(1, (c - 1) / 2));
                for (int j = -arc; j <= arc; j++)
                {
                    int i = st + (((nearest - st + j) % c) + c) % c;
                    float d2 = (rest[i] - pos).sqrMagnitude;
                    if (d2 > r2) continue;
                    for (int k = 0; k < K; k++)
                    {
                        if (d2 >= bestD2[k]) continue;
                        for (int m = K - 1; m > k; m--) { bestD2[m] = bestD2[m - 1]; bestIdx[m] = bestIdx[m - 1]; }
                        bestD2[k] = d2; bestIdx[k] = i;
                        break;
                    }
                }
            }

            float wSum = 0f;
            for (int k = 0; k < K; k++)
            {
                int ci = bestIdx[k];
                int slot = v * K + k;
                if (ci < 0) { vertInfluence[slot] = -1; vertWeight[slot] = 0f; continue; }
                float w = 1f / (bestD2[k] + 1e-4f);
                vertInfluence[slot] = ci;
                vertWeight[slot] = w;
                wSum += w;
            }
            if (wSum > 0f)
                for (int k = 0; k < K; k++) vertWeight[v * K + k] /= wSum;
        }

        protected override void ApplyVisual(float maxOff)
        {
            float maxOff2 = maxOff * maxOff;
            for (int v = 0; v < restVerts.Length; v++)
            {
                Vector2 off = Vector2.zero;
                int baseI = v * K;
                for (int k = 0; k < K; k++)
                {
                    int ci = vertInfluence[baseI + k];
                    if (ci < 0) continue;
                    off += (wave.OffsetAt(ci, maxOff) + wave.inwardNormal[ci] * expelPush[ci])
                           * vertWeight[baseI + k];
                }
                off += BodyOffset(restVerts[v]);
                if (off.sqrMagnitude > maxOff2) off *= maxOff / off.magnitude;
                vertScratch[v] = restVerts[v] + (Vector3)off;
            }
            mesh.SetVertices(vertScratch);
        }

        protected override void ResetVisual()
        {
            if (mesh != null) mesh.SetVertices(restVerts);
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }
    }
}
