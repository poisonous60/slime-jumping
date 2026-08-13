using TMPro;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Platforms
{
    /// <summary>
    /// 아스키아트 발판 출렁임: SquishyCloud 의 윤곽 스프링-댐퍼 파동을 이식했다(전신 압축·bulge
    /// 는 구름 전용이라 제외). 착지 임펄스 + 탑승 중 발밑 눌림/이동 트레일 + 이탈 팽창이
    /// 외곽 윤곽(콜라이더 최대 둘레 path)을 타고 번진다. 비주얼은 아스키답게 — 글자는
    /// 왜곡 없는 강체(쿼드 통째 이동)이고 이동량은 셀 격자 스텝으로 양자화돼 칸 단위로
    /// 뚝뚝 움직인다(waveQuantizeCells 0 = 부드러운 연속 모드). 콜라이더는 양자화 없이
    /// 연속 추종 — 비주얼만 움직이면 슬라임이 떠 보이고, 계단식이면 probe 가 튄다.
    /// 안쪽 법선은 winding 기반(진행방향 좌측 = 내부) — 오목 실루엣에서 centroid 방식은
    /// 뒤집힌다. 잔잔해지면 완전 휴면이라 정지 발판 비용 0.
    /// </summary>
    [DefaultExecutionOrder(-100)] // SlimeController probe 이전 — 콜라이더가 이번 틱 표면이어야 밀착.
    [RequireComponent(typeof(AsciiArtPlatform))]
    public class AsciiArtWobble : MonoBehaviour
    {
        AsciiArtPlatformConfig cfg;
        AsciiArtPlatform platform;
        PolygonCollider2D poly;
        TMP_Text text;
        SlimeController slime;

        // --- 윤곽 파동 (root-local) ---
        int waveIdx = -1;
        int n;
        Vector2[] restContour;
        Vector2[] inwardNormal;
        float[] disp;   // + = 안쪽 눌림.
        float[] vel;
        Vector2[] pathScratch;
        bool colliderAtRest = true;

        // --- TMP 글리프 매핑 (글자 = 강체 쿼드) ---
        const int K = 4;
        TMP_MeshInfo[] restMesh;
        int vertCount;
        int glyphCount;
        int[] glyphInfluence;   // [g*K], -1 = 미사용.
        float[] glyphWeight;

        float cellW, cellH;
        Vector2 lastFootLocal;
        Vector2 lastPushLocal = Vector2.down;
        bool hadRider;
        bool asleep = true;
        bool stabilityWarned;
        bool mappingDirty;
        bool rebuilding;
        float nextSlimeSearch;

        readonly SlimeJumping.Audio.StrainLoop wobbleLoop = new SlimeJumping.Audio.StrainLoop();

        void Awake()
        {
            platform = GetComponent<AsciiArtPlatform>();
            cfg = platform.Config;
            poly = GetComponent<PolygonCollider2D>();
            text = platform.VisualText;
            cellW = platform.CellWidth;
            cellH = platform.CellHeight;
            slime = FindFirstObjectByType<SlimeController>();

            if (cfg == null || poly == null || text == null || cellH <= 0f)
            {
                Debug.LogError($"AsciiArtWobble '{name}': config/collider/text/metrics 준비 안 됨 — 비활성화.", this);
                enabled = false;
                return;
            }
            BuildContour();
            BuildGlyphMapping();
            if (n == 0 || glyphCount == 0) enabled = false;
        }

        void OnEnable()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
        }

        void OnDisable()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
            ResetDeformation();
        }

        // 같은 정점 수로 재생성돼도 rest 캐시가 낡는다 — 길이 비교만으로는 못 잡는다.
        void OnTextChanged(Object obj)
        {
            if (!rebuilding && obj == text) mappingDirty = true;
        }

        // --- 구축 ---------------------------------------------------------

        void BuildContour()
        {
            float best = 0f;
            for (int p = 0; p < poly.pathCount; p++)
            {
                var path = poly.GetPath(p);
                float perim = 0f;
                for (int i = 0; i < path.Length; i++)
                    perim += Vector2.Distance(path[i], path[(i + 1) % path.Length]);
                if (perim > best) { best = perim; waveIdx = p; }
            }
            if (waveIdx < 0) { n = 0; return; }

            // 긴 직선 엣지를 세분 — 파동장은 점 위에만 살아서, 성기면 발자국 반경 안에 점이 없어 주입 0.
            float maxSeg = Mathf.Max(0.01f, cfg.waveResolutionCells) * Mathf.Min(cellW, cellH);
            restContour = Subdivide(poly.GetPath(waveIdx), maxSeg);
            n = restContour.Length;
            inwardNormal = new Vector2[n];
            disp = new float[n];
            vel = new float[n];
            pathScratch = new Vector2[n];

            for (int i = 0; i < n; i++)
            {
                Vector2 tangent = restContour[(i + 1) % n] - restContour[(i - 1 + n) % n];
                Vector2 left = new Vector2(-tangent.y, tangent.x); // 진행방향 좌측 = 내부(빌더 winding 계약).
                inwardNormal[i] = left.sqrMagnitude > 1e-8f ? left.normalized : Vector2.up;
            }
        }

        static Vector2[] Subdivide(Vector2[] src, float maxSeg)
        {
            var pts = new System.Collections.Generic.List<Vector2>(src.Length * 2);
            for (int i = 0; i < src.Length; i++)
            {
                Vector2 a = src[i], b = src[(i + 1) % src.Length];
                pts.Add(a);
                int div = Mathf.FloorToInt(Vector2.Distance(a, b) / maxSeg);
                for (int j = 1; j <= div; j++)
                    pts.Add(Vector2.Lerp(a, b, (float)j / (div + 1)));
            }
            return pts.ToArray();
        }

        void BuildGlyphMapping()
        {
            rebuilding = true; // 아래 ForceMeshUpdate 가 TEXT_CHANGED 를 다시 쏘는 재귀 차단.
            try { text.ForceMeshUpdate(); }
            finally { rebuilding = false; }
            mappingDirty = false;
            var ti = text.textInfo;
            if (ti.meshInfo.Length == 0) { glyphCount = 0; return; }
            restMesh = ti.CopyMeshInfoVertexData();
            vertCount = ti.meshInfo[0].vertexCount;
            glyphCount = vertCount / 4;
            if (glyphCount == 0) return;

            glyphInfluence = new int[glyphCount * K];
            glyphWeight = new float[glyphCount * K];

            float radius = ToLocalLength(cfg.visualInfluenceRadius);
            float r2 = radius * radius;
            int arcW = Mathf.Clamp(cfg.mappingArcWindow, 1, Mathf.Max(1, (n - 1) / 4));
            var verts = restMesh[0].vertices;
            var bestIdx = new int[K];
            var bestD2 = new float[K];

            for (int g = 0; g < glyphCount; g++)
            {
                // 글리프 중심 기준 매핑 — Visual 자식이 identity 라 TMP local == root local (빌더 계약).
                int v0 = g * 4;
                Vector2 xy = (Vector2)(verts[v0] + verts[v0 + 1] + verts[v0 + 2] + verts[v0 + 3]) * 0.25f;

                int nearest = -1;
                float nearestD2 = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    float d2 = (restContour[i] - xy).sqrMagnitude;
                    if (d2 < nearestD2) { nearestD2 = d2; nearest = i; }
                }

                for (int k = 0; k < K; k++) { bestIdx[k] = -1; bestD2[k] = float.MaxValue; }
                if (nearest >= 0 && nearestD2 <= r2)
                {
                    // 최근접 윤곽점 주변 arc 만 후보 — XY 거리만 쓰면 다른 획의 윤곽이 섞인다.
                    for (int j = -arcW; j <= arcW; j++)
                    {
                        int i = (nearest + j + n) % n;
                        float d2 = (restContour[i] - xy).sqrMagnitude;
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
                    int slot = g * K + k;
                    if (ci < 0) { glyphInfluence[slot] = -1; glyphWeight[slot] = 0f; continue; }
                    float w = 1f / (bestD2[k] + 1e-4f);
                    glyphInfluence[slot] = ci;
                    glyphWeight[slot] = w;
                    wSum += w;
                }
                if (wSum > 0f)
                    for (int k = 0; k < K; k++) glyphWeight[g * K + k] /= wSum;
            }

            // 매 틱 RecalculateBounds 대신 최대 변위 포함 보수적 bounds 1회 고정.
            var mesh = ti.meshInfo[0].mesh;
            var b = mesh.bounds;
            b.Expand(cfg.maxTotalDisplacementCells * cellH * 2f);
            mesh.bounds = b;
        }

        // X/Y 평균 — 비균일 스케일에서 반경이 한 축에 쏠리지 않는다.
        float ToLocalLength(float world)
            => 0.5f * (transform.InverseTransformVector(Vector3.right * world).magnitude
                     + transform.InverseTransformVector(Vector3.up * world).magnitude);

        // --- 시뮬 ---------------------------------------------------------

        void FixedUpdate()
        {
            SlimeController rider = ContactRider();

            if (rider != null && !hadRider) OnLand(rider);
            else if (rider == null && hadRider) OnLeave();
            hadRider = rider != null;

            if (asleep && rider == null) return;

            float dt = Time.fixedDeltaTime;
            StepWave(rider, dt);
            ApplyToCollider();
            ApplyMesh();
            DriveWobbleSound();
            TrySleep(rider);
        }

        // 진폭을 따라가는 이어지는 소리 — 착지 한 방이 아니라 잦아드는 동안 같이 잦아든다.
        void DriveWobbleSound()
        {
            float peak = 0f;
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Abs(disp[i]);
                if (a > peak) peak = a;
            }
            float full = Mathf.Max(0.001f, cfg.wobbleFullAmplitudeCells * cellH);
            wobbleLoop.Drive(cfg.wobbleSound, transform.TransformPoint(lastFootLocal),
                             peak / full, cfg.wobblePitchAtRest, cfg.wobblePitchAtFull);
        }

        SlimeController ContactRider()
        {
            if (slime == null)
            {
                // 늦게 스폰/교체된 슬라임 회수 — 매 틱 전역 탐색은 비싸서 1초 간격.
                if (Time.time < nextSlimeSearch) return null;
                nextSlimeSearch = Time.time + 1f;
                slime = FindFirstObjectByType<SlimeController>();
                if (slime == null) return null;
            }
            if (!slime.IsTouchingSurface) return null;
            var surf = slime.SurfaceCollider;
            if (surf == null || (surf != poly && !surf.transform.IsChildOf(transform))) return null;
            return slime;
        }

        void OnLand(SlimeController rider)
        {
            asleep = false;
            UpdateFoot(rider);
            float k = ClampedWaveSpring();
            float depth = (cfg.dentDepthCells + cfg.dentDepthPerSpeedCells * Mathf.Max(0f, rider.ImpactNormalSpeed)) * cellH;
            InjectWave(depth * Mathf.Sqrt(k), lastFootLocal, lastPushLocal, DentRadiusLocal(rider));
        }

        void OnLeave()
        {
            asleep = false;
            float k = ClampedWaveSpring();
            // 음수 = 바깥 팽창 — 발사 반동으로 접촉부가 부풀며 퍼진다.
            InjectWave(-cfg.popWaveDepthCells * cellH * Mathf.Sqrt(k), lastFootLocal, lastPushLocal,
                       ToLocalLength(cfg.visualInfluenceRadius) * 2f);
        }

        void UpdateFoot(SlimeController rider)
        {
            Vector2 footWorld = rider.Position - rider.SurfaceNormal * rider.SlimeRadius;
            lastFootLocal = transform.InverseTransformPoint(footWorld);
            Vector2 push = transform.InverseTransformDirection(-rider.SurfaceNormal);
            lastPushLocal = push.sqrMagnitude > 1e-8f ? push.normalized : Vector2.down;
        }

        float DentRadiusLocal(SlimeController rider)
            => ToLocalLength(rider.SlimeRadius * cfg.dentRadius);

        float ClampedWaveSpring()
        {
            float dt = Time.fixedDeltaTime;
            float stab = dt * dt * (cfg.waveSpring + 4f * cfg.waveSpread);
            return stab > 3.6f ? cfg.waveSpring * (3.6f / stab) : cfg.waveSpring;
        }

        static float Falloff(float dist, float radius)
            => radius <= 0f ? 0f : Mathf.Clamp01(1f - dist / radius);

        void InjectWave(float velocityAmp, Vector2 footLocal, Vector2 pushLocal, float radius)
        {
            for (int i = 0; i < n; i++)
            {
                float f = Falloff(Vector2.Distance(restContour[i], footLocal), radius);
                if (f <= 0f) continue;
                float align = Vector2.Dot(pushLocal, inwardNormal[i]);
                if (align <= 0f) continue;
                vel[i] += velocityAmp * f * align;
            }
        }

        void StepWave(SlimeController rider, float dt)
        {
            float k = cfg.waveSpring, s = cfg.waveSpread, c = cfg.waveDamping;
            // 폐곡선 최고 모드(spring + 4·spread)가 50Hz 명시 적분 한계를 넘으면 자동 축소.
            float stab = dt * dt * (k + 4f * s);
            if (stab > 3.6f)
            {
                float f = 3.6f / stab;
                k *= f; s *= f;
                if (!stabilityWarned)
                {
                    stabilityWarned = true;
                    Debug.LogWarning($"AsciiArtWobble '{name}': waveSpring/waveSpread 가 50Hz 안정 한계 초과 — 자동 축소.", this);
                }
            }

            float pressDepth = 0f, pressRadius = 0f;
            Vector2 footLocal = lastFootLocal, pushLocal = lastPushLocal;
            if (rider != null)
            {
                UpdateFoot(rider);
                footLocal = lastFootLocal; pushLocal = lastPushLocal;
                pressRadius = DentRadiusLocal(rider);
                Vector2 v = rider.Velocity;
                float slide = (v - Vector2.Dot(v, rider.SurfaceNormal) * rider.SurfaceNormal).magnitude;
                float roll = Mathf.Min(cfg.rollWavePerSpeedCells * slide, cfg.dentDepthCells);
                pressDepth = (cfg.restDentDepthCells + roll) * cellH;
            }

            for (int i = 0; i < n; i++)
            {
                int l = (i - 1 + n) % n, r = (i + 1) % n;
                float accel = -s * (2f * disp[i] - disp[l] - disp[r]) - k * disp[i] - c * vel[i];
                if (pressDepth > 0f)
                {
                    float f = Falloff(Vector2.Distance(restContour[i], footLocal), pressRadius);
                    if (f > 0f)
                    {
                        float align = Vector2.Dot(pushLocal, inwardNormal[i]);
                        if (align > 0f) accel += pressDepth * k * f * align;   // 평형점 ≈ pressDepth.
                    }
                }
                vel[i] += accel * dt;
            }
            // 표면 이동 속도 캡 — probe 가 못 따라가면 착지가 끊긴다.
            float vMax = ToLocalLength(cfg.maxSurfaceSpeed);
            for (int i = 0; i < n; i++)
            {
                vel[i] = Mathf.Clamp(vel[i], -vMax, vMax);
                disp[i] += vel[i] * dt;
            }
        }

        void ApplyToCollider()
        {
            float eps = cfg.restEpsilonCells * cellH;
            float maxOff = cfg.maxTotalDisplacementCells * cellH;
            // 글자와 같은 격자 스텝으로 양자화 — 연속 콜라이더는 스텝 이동하는 글자와 눈에 띄게 벌어진다.
            float qx = cfg.waveQuantizeCells * cellW;
            float qy = cfg.waveQuantizeCells * cellH;
            float maxDelta = 0f;
            for (int i = 0; i < n; i++)
            {
                float d = Mathf.Clamp(disp[i], -maxOff, maxOff);
                float ox = inwardNormal[i].x * d;
                float oy = inwardNormal[i].y * d;
                if (qx > 0f) ox = Mathf.Round(ox / qx) * qx;
                if (qy > 0f) oy = Mathf.Round(oy / qy) * qy;
                pathScratch[i] = restContour[i] + new Vector2(ox, oy);
                float ad = Mathf.Max(Mathf.Abs(ox), Mathf.Abs(oy));
                if (ad > maxDelta) maxDelta = ad;
            }
            if (maxDelta >= eps || !colliderAtRest)
            {
                poly.SetPath(waveIdx, pathScratch);
                colliderAtRest = maxDelta < eps;
            }
        }

        void ApplyMesh()
        {
            var ti = text.textInfo;
            var live = ti.meshInfo[0].vertices;
            var rest = restMesh[0].vertices;
            if (mappingDirty || live.Length != rest.Length)
            {
                BuildGlyphMapping();
                live = ti.meshInfo[0].vertices;
                rest = restMesh[0].vertices;
            }

            float maxOff = cfg.maxTotalDisplacementCells * cellH;
            float qx = cfg.waveQuantizeCells * cellW;
            float qy = cfg.waveQuantizeCells * cellH;
            for (int g = 0; g < glyphCount; g++)
            {
                float ox = 0f, oy = 0f;
                int baseI = g * K;
                for (int k = 0; k < K; k++)
                {
                    int ci = glyphInfluence[baseI + k];
                    if (ci < 0) continue;
                    float w = glyphWeight[baseI + k] * disp[ci];
                    ox += inwardNormal[ci].x * w;
                    oy += inwardNormal[ci].y * w;
                }
                float sq = ox * ox + oy * oy;
                if (sq > maxOff * maxOff)
                {
                    float scale = maxOff / Mathf.Sqrt(sq);
                    ox *= scale; oy *= scale;
                }
                // 격자 스텝 양자화 — 글자가 칸 단위로 이동해야 아스키로 읽힌다.
                if (qx > 0f) ox = Mathf.Round(ox / qx) * qx;
                if (qy > 0f) oy = Mathf.Round(oy / qy) * qy;

                int v0 = g * 4;
                for (int v = v0; v < v0 + 4; v++)
                    live[v] = new Vector3(rest[v].x + ox, rest[v].y + oy, rest[v].z);
            }
            text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
        }

        void TrySleep(SlimeController rider)
        {
            if (rider != null) return;
            float eps = cfg.restEpsilonCells * cellH;
            for (int i = 0; i < n; i++)
                if (Mathf.Abs(disp[i]) > eps || Mathf.Abs(vel[i]) > eps) return;
            ResetDeformation();
            asleep = true;
        }

        void ResetDeformation()
        {
            wobbleLoop.Stop();
            if (n == 0) return;
            System.Array.Clear(disp, 0, n);
            System.Array.Clear(vel, 0, n);
            if (poly != null && waveIdx >= 0) poly.SetPath(waveIdx, restContour);
            colliderAtRest = true;
            if (text != null && restMesh != null && restMesh.Length > 0)
            {
                var ti = text.textInfo;
                if (ti.meshInfo.Length > 0 && ti.meshInfo[0].vertices.Length == restMesh[0].vertices.Length)
                {
                    System.Array.Copy(restMesh[0].vertices, ti.meshInfo[0].vertices, restMesh[0].vertices.Length);
                    text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
                }
            }
        }
    }
}
