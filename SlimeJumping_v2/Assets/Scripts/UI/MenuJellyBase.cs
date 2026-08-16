using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.UI
{
    /// Shared jelly surface driver for menu arena letters/buttons: owns the
    /// PolygonCollider2D + MenuContourWave pair, ports the AsciiArtWobble rider loop
    /// (IsTouchingSurface polling, landing impulse, riding press + roll trail,
    /// launch pop, sleep when calm) and keeps the collider on the wave via SetPath
    /// every tick. On top of the ripple it runs SquishyCloud's whole-body channel —
    /// bottom-anchored vertical compression with parabolic side bulge — and the wave
    /// itself is zero-meaned per path, so pressing one spot bulges the rest instead
    /// of shrinking the silhouette (letter-shaped jelly, volume preserved).
    /// Subclasses only draw: ApplyVisual during motion, ResetVisual at rest.
    /// Local units = world × toLocal (letters run in font units).
    [DefaultExecutionOrder(-100)] // SlimeController probe 이전 — 콜라이더가 이번 틱 표면이어야 밀착.
    public abstract class MenuJellyBase : MonoBehaviour
    {
        protected MenuSlimeArenaConfig cfg;
        protected readonly MenuContourWave wave = new MenuContourWave();
        protected PolygonCollider2D poly;
        protected float toLocal = 1f;

        SlimeController slime;
        Vector2[][] pathScratch;
        Vector2 lastFootLocal;
        Vector2 lastPushLocal = Vector2.down;
        float bodyComp;   // + = 위에서 아래로 눌린 양 (로컬 단위)
        float bodyVel;
        float bottomY;
        float heightLocal;
        float centerX;
        bool hadRider;
        bool asleep = true;
        bool colliderAtRest = true;
        float nextSlimeSearch;

        public bool DebugAsleep => asleep;
        public float DebugWavePeak => wave.PointCount > 0 ? wave.PeakAmplitude() : 0f;
        public float DebugBodyComp => bodyComp;

        protected bool InitJelly(MenuSlimeArenaConfig config, SlimeController slimeRef,
                                 Vector2[][] paths, float worldToLocal)
        {
            cfg = config;
            slime = slimeRef;
            toLocal = worldToLocal;

            // 퇴화 path (스테일 베이크 에셋 등) 는 콜라이더가 깨진다 — 걸러내고 없으면 실패.
            var valid = new System.Collections.Generic.List<Vector2[]>(paths.Length);
            foreach (var p in paths)
                if (p != null && p.Length >= 3) valid.Add(p);
            if (valid.Count == 0) return false;
            wave.Build(valid, cfg.waveResolution * toLocal);

            float minY = float.MaxValue, maxY = float.MinValue, cx = 0f;
            foreach (var pt in wave.rest)
            {
                if (pt.y < minY) minY = pt.y;
                if (pt.y > maxY) maxY = pt.y;
                cx += pt.x;
            }
            bottomY = minY;
            heightLocal = Mathf.Max(0.01f, maxY - minY);
            centerX = cx / wave.PointCount;

            poly = gameObject.AddComponent<PolygonCollider2D>();
            poly.pathCount = wave.PathCount;
            pathScratch = new Vector2[wave.PathCount][];
            for (int p = 0; p < wave.PathCount; p++)
            {
                pathScratch[p] = new Vector2[wave.PathPointCount(p)];
                wave.FillPath(p, pathScratch[p], 0f);
                poly.SetPath(p, pathScratch[p]);
            }
            return true;
        }

        protected abstract void ApplyVisual(float maxOffLocal);
        protected virtual void ResetVisual() { }

        /// 바닥 고정 세로 압축 + 포물선 옆구리 bulge (SquishyCloud 식) — 임의 로컬 점에 적용.
        protected Vector2 BodyOffset(Vector2 restPos)
        {
            float h01 = Mathf.Clamp01((restPos.y - bottomY) / heightLocal);
            float compFrac = bodyComp / heightLocal;
            return new Vector2(
                (restPos.x - centerX) * (cfg.bulgeRatio * compFrac * 4f * h01 * (1f - h01)),
                -bodyComp * h01);
        }

        /// 윤곽점 합성 변위 = 파동(법선) + 전신 압축, 합산 후 상한 클램프.
        protected Vector2 SurfaceOffsetAt(int idx, float maxOff)
        {
            Vector2 off = wave.OffsetAt(idx, maxOff) + BodyOffset(wave.rest[idx]);
            if (off.sqrMagnitude > maxOff * maxOff) off *= maxOff / off.magnitude;
            return off;
        }

        void FixedUpdate()
        {
            if (poly == null) return;
            SlimeController rider = ContactRider();

            if (rider != null && !hadRider) OnLand(rider);
            else if (rider == null && hadRider) OnLeave();
            hadRider = rider != null;

            if (asleep && rider == null) return;
            asleep = false;

            float dt = Time.fixedDeltaTime;
            StepWave(rider, dt);
            StepBody(rider, dt);
            ApplyCollider();
            ApplyVisual(cfg.maxDisplacement * toLocal);
            TrySleep(rider);
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
            if (slime.SurfaceCollider != poly) return null;
            return slime;
        }

        void OnLand(SlimeController rider)
        {
            asleep = false;
            UpdateFoot(rider);
            float impact = Mathf.Max(0f, rider.ImpactNormalSpeed);
            float k = wave.ClampedSpring(Time.fixedDeltaTime, cfg.waveSpring, cfg.waveSpread);
            float depth = (cfg.dentDepth + cfg.dentDepthPerSpeed * impact) * toLocal;
            wave.Inject(depth * Mathf.Sqrt(k), lastFootLocal, lastPushLocal, DentRadiusLocal(rider));

            float body = Mathf.Min(impact * cfg.impactCompressionPerSpeed, cfg.impactCompressionMax) * heightLocal;
            bodyVel += body * 2f * Mathf.PI * cfg.bodyFrequency;
        }

        void OnLeave()
        {
            asleep = false;
            float k = wave.ClampedSpring(Time.fixedDeltaTime, cfg.waveSpring, cfg.waveSpread);
            // 음수 = 바깥 팽창 — 발사 반동으로 접촉부가 부풀며 퍼진다.
            wave.Inject(-cfg.popDepth * toLocal * Mathf.Sqrt(k), lastFootLocal, lastPushLocal,
                        cfg.visualInfluenceRadius * toLocal * 2f);
        }

        void UpdateFoot(SlimeController rider)
        {
            Vector2 footWorld = rider.Position - rider.SurfaceNormal * rider.SlimeRadius;
            lastFootLocal = transform.InverseTransformPoint(footWorld);
            Vector2 push = transform.InverseTransformDirection(-rider.SurfaceNormal);
            lastPushLocal = push.sqrMagnitude > 1e-8f ? push.normalized : Vector2.down;
        }

        float DentRadiusLocal(SlimeController rider)
            => rider.SlimeRadius * cfg.dentRadius * toLocal;

        void StepWave(SlimeController rider, float dt)
        {
            float pressDepth = 0f, pressRadius = 0f;
            if (rider != null)
            {
                UpdateFoot(rider);
                pressRadius = DentRadiusLocal(rider);
                Vector2 v = rider.Velocity;
                float slide = (v - Vector2.Dot(v, rider.SurfaceNormal) * rider.SurfaceNormal).magnitude;
                float roll = Mathf.Min(cfg.rollWavePerSpeed * slide, cfg.dentDepth);
                pressDepth = (cfg.restDentDepth + roll) * toLocal;
            }
            wave.Step(dt, cfg.waveSpring, cfg.waveSpread, cfg.waveDamping,
                      pressDepth, pressRadius, lastFootLocal, lastPushLocal,
                      cfg.maxSurfaceSpeed * toLocal, cfg.contactCalmDamping);
        }

        void StepBody(SlimeController rider, float dt)
        {
            float target = (rider != null ? cfg.loadCompression : 0f) * heightLocal;
            float omega = 2f * Mathf.PI * cfg.bodyFrequency;
            float accel = omega * omega * (target - bodyComp) - 2f * cfg.bodyDamping * omega * bodyVel;
            bodyVel += accel * dt;
            float vMax = cfg.maxSurfaceSpeed * toLocal;
            bodyVel = Mathf.Clamp(bodyVel, -vMax, vMax);
            bodyComp += bodyVel * dt;

            float cap = cfg.impactCompressionMax * heightLocal;
            if (bodyComp > cap) { bodyComp = cap; if (bodyVel > 0f) bodyVel = 0f; }
            float floor2 = -cap * 0.5f;   // 위쪽 오버슈트는 절반까지만.
            if (bodyComp < floor2) { bodyComp = floor2; if (bodyVel < 0f) bodyVel = 0f; }
        }

        void ApplyCollider()
        {
            float eps = cfg.restEpsilon * toLocal;
            float maxOff = cfg.maxDisplacement * toLocal;
            float maxDelta = 0f;
            int idx = 0;
            for (int p = 0; p < wave.PathCount; p++)
            {
                var dst = pathScratch[p];
                for (int i = 0; i < dst.Length; i++, idx++)
                {
                    Vector2 off = SurfaceOffsetAt(idx, maxOff);
                    dst[i] = wave.rest[idx] + off;
                    float d = off.magnitude;
                    if (d > maxDelta) maxDelta = d;
                }
            }
            if (maxDelta >= eps || !colliderAtRest)
            {
                for (int p = 0; p < wave.PathCount; p++)
                    poly.SetPath(p, pathScratch[p]);
                colliderAtRest = maxDelta < eps;
            }
        }

        void TrySleep(SlimeController rider)
        {
            if (rider != null) return;
            float eps = cfg.restEpsilon * toLocal;
            if (Mathf.Abs(bodyComp) > eps || Mathf.Abs(bodyVel) > eps) return;
            if (!wave.IsCalm(eps)) return;
            wave.Clear();
            bodyComp = 0f;
            bodyVel = 0f;
            for (int p = 0; p < wave.PathCount; p++)
            {
                wave.FillPath(p, pathScratch[p], 0f);
                poly.SetPath(p, pathScratch[p]);
            }
            colliderAtRest = true;
            ResetVisual();
            asleep = true;
        }
    }
}
