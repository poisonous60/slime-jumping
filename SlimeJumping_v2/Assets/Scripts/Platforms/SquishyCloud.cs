using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Platforms
{
    /// <summary>
    /// 말랑 구름 비주얼: CloudPlatform 계열 옆에 붙는 독립 변형 컴포넌트. 3계층 설계 —
    /// 전신 압축 스프링(탑승 하중 + 착지 임펄스, Puffy Lift/Celeste 결합) + 실루엣 윤곽 파동
    /// (발밑 dent 가 spread 항으로 구름 전체에 물결처럼 번짐, WaterZone_v2 계열).
    /// 콜라이더도 같은 필드로 SetPath 추종한다 — 비주얼만 내리면 슬라임이 떠 보인다(유저 확인).
    /// 표면 속도는 maxSurfaceSpeed 로 캡해 probe 가 항상 추적 가능. 정점 매핑은 최근접 윤곽점
    /// 주변 arc 로 제한 — XY 거리만 보면 다른 lobe 윤곽이 섞여 정점이 관통(색 섞임)한다.
    /// 라이더 신호는 CloudPlatform 이벤트(RiderAttached/Popped/Released)로 받고, 탑승 하중은
    /// SurfaceCollider 가 이 구름의 자식일 때만 건다 — 차징 코요테엔 Rider 참조가 남기 때문.
    /// 삭제하면 구름은 기존 CloudPlatform 거동으로 완전 복귀한다.
    /// </summary>
    [DefaultExecutionOrder(-100)] // SlimeController probe 이전 — 콜라이더가 항상 이번 틱 표면이어야 밀착.
    public class SquishyCloud : MonoBehaviour
    {
        [Header("Config")]
        [Tooltip("비우면 Assets/Resources/SquishyCloudConfig.asset 공유 튜닝.")]
        [SerializeField] SquishyCloudConfig configOverride;

        [Header("Scene refs")]
        [Tooltip("변형할 구름 메시. 비우면 CloudDisperser.sceneCloud 에서 자동 탐색.")]
        [SerializeField] MeshFilter visualMesh;

        SquishyCloudConfig cfg;
        CloudPlatform cloud;

        // --- 윤곽 파동 필드 (root-local) ---
        Vector2[] restContour;
        Vector2[] inwardNormal;
        float[] disp;   // + = 안쪽 눌림
        float[] vel;
        float[] contourH01;
        int n;

        // --- 콜라이더 팔로워 ---
        PolygonCollider2D poly;
        Vector2[] restPath;       // poly-local
        Vector2[] pathScratch;
        Matrix4x4 rootToPolyVec;
        bool colliderAtRest = true;

        // --- 전신 압축 ---
        float bodyComp;   // + = 아래로 눌림 (root-local 거리)
        float bodyVel;

        // --- 메시 팔로워 ---
        const int K = 4;
        Mesh meshInstance;
        Mesh originalSharedMesh;
        Vector3[] restVerts;      // child-local
        Vector3[] restRoot;       // root-local
        Vector3[] vertScratch;
        int[] vertInfluence;      // [v*K], -1 = 미사용
        float[] vertWeight;
        Matrix4x4 rootToChildVec;
        float bottomY, heightLocal, centerX;

        Vector2 lastFootLocal;
        Vector2 lastPushLocal = Vector2.down;
        bool asleep = true;
        bool resetDone;
        bool stabilityWarned;

        void Awake()
        {
            cfg = SquishyCloudConfig.Resolve(configOverride);
            cloud = GetComponent<CloudPlatform>();
            if (cloud == null)
            {
                Debug.LogError($"SquishyCloud '{name}': CloudPlatform 이 같은 오브젝트에 없어 비활성화.", this);
                enabled = false;
                return;
            }
            BuildContour();
            BuildVisual();
        }

        void OnEnable()
        {
            if (cloud == null) return;
            cloud.RiderAttached += OnRiderAttached;
            cloud.RiderPopped += OnRiderPopped;
            cloud.RiderReleased += OnRiderReleased;
        }

        void OnDisable()
        {
            if (cloud != null)
            {
                cloud.RiderAttached -= OnRiderAttached;
                cloud.RiderPopped -= OnRiderPopped;
                cloud.RiderReleased -= OnRiderReleased;
            }
            ResetDeformation();
        }

        void OnDestroy()
        {
            // 컴포넌트만 제거돼도 구름 비주얼이 남게 원본 공유 메시로 복원 후 인스턴스 파기.
            if (visualMesh != null && originalSharedMesh != null) visualMesh.sharedMesh = originalSharedMesh;
            if (meshInstance != null) Destroy(meshInstance);
        }

        // --- 구축 ---------------------------------------------------------

        void BuildContour()
        {
            poly = GetComponentInChildren<PolygonCollider2D>(true);
            if (poly == null) { n = 0; return; }
            if (poly.pathCount > 1)
                Debug.LogWarning($"SquishyCloud '{name}': 실루엣 path 가 {poly.pathCount}개 — 0번만 사용.", this);

            var path = poly.GetPath(0);
            n = path.Length;
            restContour  = new Vector2[n];
            inwardNormal = new Vector2[n];
            disp = new float[n];
            vel  = new float[n];
            contourH01  = new float[n];
            restPath    = new Vector2[n];
            pathScratch = new Vector2[n];
            System.Array.Copy(path, restPath, n);
            rootToPolyVec = poly.transform.worldToLocalMatrix * transform.localToWorldMatrix;

            var pt = poly.transform;
            Vector2 centroid = Vector2.zero;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                Vector3 local = transform.InverseTransformPoint(pt.TransformPoint(path[i]));
                restContour[i] = new Vector2(local.x, local.y);
                centroid += restContour[i];
                if (local.y < minY) minY = local.y;
                if (local.y > maxY) maxY = local.y;
            }
            centroid /= Mathf.Max(1, n);
            bottomY = minY;
            heightLocal = Mathf.Max(0.01f, maxY - minY);
            centerX = centroid.x;

            for (int i = 0; i < n; i++)
            {
                contourH01[i] = Mathf.Clamp01((restContour[i].y - bottomY) / heightLocal);
                Vector2 tangent = restContour[(i + 1) % n] - restContour[(i - 1 + n) % n];
                Vector2 nrm = new Vector2(-tangent.y, tangent.x);
                if (Vector2.Dot(nrm, centroid - restContour[i]) < 0f) nrm = -nrm;
                inwardNormal[i] = nrm.sqrMagnitude > 1e-8f ? nrm.normalized : Vector2.up;
            }
        }

        void BuildVisual()
        {
            if (visualMesh == null && cloud.disperser != null && cloud.disperser.sceneCloud != null)
                visualMesh = cloud.disperser.sceneCloud.GetComponentInChildren<MeshFilter>(true);
            if (visualMesh == null) visualMesh = GetComponentInChildren<MeshFilter>(true);
            if (visualMesh == null || n == 0) { meshInstance = null; return; }

            originalSharedMesh = visualMesh.sharedMesh;
            meshInstance = visualMesh.mesh;   // per-renderer 인스턴스라 공유 에셋은 안전.
            meshInstance.MarkDynamic();
            restVerts = meshInstance.vertices;
            int vc = restVerts.Length;
            vertScratch = new Vector3[vc];
            System.Array.Copy(restVerts, vertScratch, vc);
            restRoot = new Vector3[vc];
            vertInfluence = new int[vc * K];
            vertWeight = new float[vc * K];
            rootToChildVec = visualMesh.transform.worldToLocalMatrix * transform.localToWorldMatrix;

            float radiusLocal = ToLocalLength(cfg.visualInfluenceRadius);
            float r2 = radiusLocal * radiusLocal;
            var bestIdx = new int[K];
            var bestD2 = new float[K];

            // 최근접 윤곽점 주변 arc 만 후보로 — XY 거리만 쓰면 다른 lobe 윤곽이 섞여 정점이 관통한다.
            int arcW = Mathf.Clamp(cfg.mappingArcWindow, 1, Mathf.Max(1, (n - 1) / 4));

            for (int v = 0; v < vc; v++)
            {
                restRoot[v] = transform.InverseTransformPoint(visualMesh.transform.TransformPoint(restVerts[v]));
                Vector2 xy = new Vector2(restRoot[v].x, restRoot[v].y);

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
                    int slot = v * K + k;
                    if (ci < 0) { vertInfluence[slot] = -1; vertWeight[slot] = 0f; continue; }
                    // 윤곽점의 안쪽 법선과 (윤곽점→정점) 방향 정합 — 얇은 부위에서 반대면 오염 차단.
                    Vector2 toVert = xy - restContour[ci];
                    float d = Mathf.Sqrt(bestD2[k]);
                    float align = d > 1e-4f ? Mathf.Max(0f, Vector2.Dot(inwardNormal[ci], toVert / d)) : 1f;
                    float w = align / (bestD2[k] + 1e-4f);
                    vertInfluence[slot] = ci;
                    vertWeight[slot] = w;
                    wSum += w;
                }
                if (wSum > 0f)
                    for (int k = 0; k < K; k++) vertWeight[v * K + k] /= wSum;
            }

            // 매 틱 RecalculateBounds 대신 최대 변위를 포함한 보수적 bounds 를 1회 고정.
            float pad = ToChildLength(cfg.maxTotalDisplacement * heightLocal);
            var b = meshInstance.bounds;
            b.Expand(pad * 2f);
            meshInstance.bounds = b;
        }

        // X/Y 평균 — 비균일 스케일에서도 반경이 한 축에 쏠리지 않는다.
        float ToLocalLength(float world)
            => 0.5f * (transform.InverseTransformVector(Vector3.right * world).magnitude
                     + transform.InverseTransformVector(Vector3.up * world).magnitude);

        float ToChildLength(float rootLen)
            => rootToChildVec.MultiplyVector(Vector3.right * rootLen).magnitude;

        // --- 이벤트 -------------------------------------------------------

        void OnRiderAttached(SlimeController slime, float impactSpeed)
        {
            if (n == 0 || meshInstance == null) return;
            asleep = false;
            UpdateFoot(slime);

            float k = ClampedWaveSpring();
            float depth = (cfg.dentDepth + cfg.dentDepthPerSpeed * Mathf.Max(0f, impactSpeed)) * heightLocal;
            InjectWave(depth * Mathf.Sqrt(k), lastFootLocal, lastPushLocal, DentRadiusLocal(slime));

            float body = Mathf.Min(impactSpeed * cfg.impactCompressionPerSpeed, cfg.impactCompressionMax) * heightLocal;
            bodyVel += body * 2f * Mathf.PI * cfg.bodyFrequency;
        }

        void OnRiderPopped(Vector2 dir, float speed)
        {
            if (n == 0 || meshInstance == null) return;
            asleep = false;
            float k = ClampedWaveSpring();
            var rider = cloud.Rider;
            if (rider != null) UpdateFoot(rider);
            // 음수 = 바깥 팽창 — 발사 반동으로 접촉부가 부풀며 퍼진다.
            InjectWave(-cfg.popWaveDepth * heightLocal * Mathf.Sqrt(k), lastFootLocal, lastPushLocal,
                       ToLocalLength(cfg.visualInfluenceRadius) * 2f);
        }

        void OnRiderReleased()
        {
            asleep = false;   // 하중 해제 → 스프링이 잔동을 재생하도록 깨워둔다.
        }

        // --- 시뮬 ---------------------------------------------------------

        void FixedUpdate()
        {
            if (n == 0 || meshInstance == null) return;

            if (DisperserBusy())
            {
                if (!resetDone) ResetDeformation();
                return;
            }
            resetDone = false;

            SlimeController rider = ContactRider();
            if (asleep && rider == null) return;

            float dt = Time.fixedDeltaTime;
            StepWave(rider, dt);
            StepBody(rider, dt);
            ApplyToCollider();
            ApplyMesh();
            TrySleep(rider);
        }

        // 콜라이더도 같은 필드를 따른다 — 비주얼만 내려가면 슬라임이 떠 보인다.
        void ApplyToCollider()
        {
            float eps = cfg.restEpsilon * heightLocal;
            float maxOff = cfg.maxTotalDisplacement * heightLocal;
            float compFrac = bodyComp / heightLocal;
            float maxDelta = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 off = inwardNormal[i] * disp[i];
                float h01 = contourH01[i];
                off.y -= bodyComp * h01;
                off.x += (restContour[i].x - centerX) * (cfg.bulgeRatio * compFrac * 4f * h01 * (1f - h01));
                if (off.sqrMagnitude > maxOff * maxOff) off *= maxOff / off.magnitude;

                Vector3 p = rootToPolyVec.MultiplyVector(new Vector3(off.x, off.y, 0f));
                pathScratch[i] = restPath[i] + new Vector2(p.x, p.y);
                float d = off.magnitude;
                if (d > maxDelta) maxDelta = d;
            }
            if (maxDelta >= eps || !colliderAtRest)
            {
                poly.SetPath(0, pathScratch);
                colliderAtRest = maxDelta < eps;
            }
        }

        SlimeController ContactRider()
        {
            var r = cloud.Rider;
            if (r == null) return null;
            // 차징 grace 는 SurfaceCollider 를 캐시로 복원하므로 실제 접촉 여부도 함께 본다.
            if (!r.IsTouchingSurface) return null;
            var surf = r.SurfaceCollider;
            if (surf == null || !surf.transform.IsChildOf(transform)) return null;
            return r;
        }

        void UpdateFoot(SlimeController rider)
        {
            Vector2 footWorld = rider.Position - rider.SurfaceNormal * rider.SlimeRadius;
            lastFootLocal = transform.InverseTransformPoint(footWorld);
            // 비균일 스케일에서 방향이 왜곡되지 않게 정규화.
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
            // 폐곡선 최고 모드(springK + 4·spread) 이 50Hz 명시 적분 한계를 넘으면 자동 축소.
            float stab = dt * dt * (k + 4f * s);
            if (stab > 3.6f)
            {
                float f = 3.6f / stab;
                k *= f; s *= f;
                if (!stabilityWarned)
                {
                    stabilityWarned = true;
                    Debug.LogWarning($"SquishyCloud '{name}': waveSpring/waveSpread 가 50Hz 안정 한계 초과 — 자동 축소 적용.", this);
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
                float roll = Mathf.Min(cfg.rollWavePerSpeed * slide, cfg.dentDepth);
                pressDepth = (cfg.restDentDepth + roll) * heightLocal;
            }

            for (int i = 0; i < n; i++)
            {
                int l = (i - 1 + n) % n, r2 = (i + 1) % n;
                float accel = -s * (2f * disp[i] - disp[l] - disp[r2]) - k * disp[i] - c * vel[i];
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

        void StepBody(SlimeController rider, float dt)
        {
            float target = (rider != null ? cfg.loadCompression : 0f) * heightLocal;
            float omega = 2f * Mathf.PI * cfg.bodyFrequency;
            float accel = omega * omega * (target - bodyComp) - 2f * cfg.bodyDamping * omega * bodyVel;
            bodyVel += accel * dt;
            float vMax = ToLocalLength(cfg.maxSurfaceSpeed);
            bodyVel = Mathf.Clamp(bodyVel, -vMax, vMax);
            bodyComp += bodyVel * dt;

            float cap = cfg.impactCompressionMax * heightLocal;
            if (bodyComp > cap) { bodyComp = cap; if (bodyVel > 0f) bodyVel = 0f; }
            float floor2 = -cap * 0.5f;   // 위쪽 오버슈트는 절반까지만.
            if (bodyComp < floor2) { bodyComp = floor2; if (bodyVel < 0f) bodyVel = 0f; }
        }

        void ApplyMesh()
        {
            int vc = restVerts.Length;
            float compFrac = bodyComp / heightLocal;
            float maxOff = cfg.maxTotalDisplacement * heightLocal;
            float maxOff2 = maxOff * maxOff;

            for (int v = 0; v < vc; v++)
            {
                Vector3 off = Vector3.zero;
                int baseI = v * K;
                for (int k = 0; k < K; k++)
                {
                    int ci = vertInfluence[baseI + k];
                    if (ci < 0) continue;
                    float w = vertWeight[baseI + k] * disp[ci];
                    off.x += inwardNormal[ci].x * w;
                    off.y += inwardNormal[ci].y * w;
                }

                float h01 = Mathf.Clamp01((restRoot[v].y - bottomY) / heightLocal);
                off.y -= bodyComp * h01;   // 바닥 고정, 위가 내려앉는다.
                // bulge 는 X만 — Z 로 밀면 내부 lobe 가 앞면을 뚫고 나와 색이 섞인다.
                off.x += (restRoot[v].x - centerX) * (cfg.bulgeRatio * compFrac * 4f * h01 * (1f - h01));

                if (off.sqrMagnitude > maxOff2) off *= maxOff / off.magnitude;
                vertScratch[v] = restVerts[v] + rootToChildVec.MultiplyVector(off);
            }
            meshInstance.SetVertices(vertScratch);
        }

        void TrySleep(SlimeController rider)
        {
            if (rider != null) return;
            float eps = cfg.restEpsilon * heightLocal;
            if (Mathf.Abs(bodyComp) > eps || Mathf.Abs(bodyVel) > eps) return;
            for (int i = 0; i < n; i++)
                if (Mathf.Abs(disp[i]) > eps || Mathf.Abs(vel[i]) > eps) return;
            ResetDeformation();
        }

        void ResetDeformation()
        {
            if (disp != null) System.Array.Clear(disp, 0, n);
            if (vel != null) System.Array.Clear(vel, 0, n);
            bodyComp = 0f; bodyVel = 0f;
            if (meshInstance != null && restVerts != null) meshInstance.SetVertices(restVerts);
            // colliderAtRest 는 sub-epsilon 변형 경로에서도 true — 리셋 복원은 무조건.
            if (poly != null && restPath != null)
            {
                poly.SetPath(0, restPath);
                colliderAtRest = true;
            }
            asleep = true;
            resetDone = true;
        }

        bool DisperserBusy()
        {
            var d = cloud.disperser;
            if (d == null) return false;
            if (d.IsDispersing || d.IsReforming) return true;
            return d.sceneCloud != null && !d.sceneCloud.activeInHierarchy;
        }

        // smoothstep — 중심 1, 반경 밖 0.
        static float Falloff(float dist, float radius)
        {
            if (radius <= 0f) return 0f;
            float t = 1f - Mathf.Clamp01(dist / radius);
            return t * t * (3f - 2f * t);
        }
    }
}
