using UnityEngine;
using UnityEngine.Serialization;
using SlimeJumping.Effects;
using SlimeJumping.Controller;
using SlimeJumping.Audio;
using SlimeJumping.Platforms.Snow;
using SlimeJumping.Visual;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// Stage 2 눈덩이. 위에서 밟으면 점프, 옆에서 부딪히면 밀려남 — 어느 쪽이든 부서짐.
    /// 위치는 raycast 로 스크립트 제어 (Rigidbody2D 는 Kinematic), 콜라이더는 trigger 감지 전용.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Collider2D))]
    public class Snowball : MonoBehaviour
    {
        public enum Mode { Rolling, Flying }

        enum SnowballPhase { Active, Falling, Crumbling, Gone }

        [Header("Mode")]
        [SerializeField] Mode mode = Mode.Rolling;

        [Header("Motion (shared)")]
        [FormerlySerializedAs("speed")]
        [Tooltip("Rolling 속도 cap (m/s). 중력 가속이 이 값 이상으로 못 올라감. Flying 은 이 값 그대로 사용.")]
        [SerializeField] float maxSpeed = 6f;
        [Tooltip("Rolling 시작 속도 (m/s). 평지면 이 속도 유지, 슬로프면 gravity 로 가감속.")]
        [SerializeField] float initialSpeed = 2f;
        [Tooltip("표면 tangent 따라 적용되는 중력 (m/s²). 0 = constant-speed (옛 동작). 슬로프 가감속의 강도.")]
        [SerializeField] float gravity = 20f;
        [Tooltip("눈덩이 반지름 (m). Trigger collider 반지름 + visualRoot.localScale (= radius*2 의 unit sphere 가정) + 거리 기반 회전 계수를 자동 동기화.")]
        [SerializeField] float radius = 0.5f;
        [Tooltip("굴러갈 수 있는 발판 레이어. 자기 자신 레이어는 제외해야 raycast 가 자기 콜라이더에 안 걸린다.")]
        [SerializeField] LayerMask terrainMask;

        [Header("Rolling")]
        [Tooltip("±1. Surface tangent 방향. Spawner 가 override.")]
        [SerializeField] int direction = 1;
        [Tooltip("현재 표면 대비 +X° 이상 가팔라진 오르막 만나면 부서짐.")]
        [SerializeField] float breakSlopeDelta = 30f;
        [Tooltip("현재 표면 대비 -X° 이상 가팔라진 내리막(볼록 코너) 만나면 표면을 떠나 Falling 진입. 속도는 보존(현재 motionDir × currentSpeed). 절벽 lip 에서 ballistic 하게 떨어지는 효과.")]
        [SerializeField] float detachSlopeDelta = 45f;
        [Tooltip("Forward-down ray 의 forward 거리 (m).")]
        [SerializeField] float lookahead = 0.3f;
        [Tooltip("Forward-down ray 의 down 깊이 (m). hit 못 하면 Falling 진입.")]
        [SerializeField] float rayDepth = 0.5f;
        [Tooltip("Rolling 모드의 최대 수명 (s). Active+Rolling 동안 누적, 초과 시 조용히 제거 (no VFX). 0 이하 = 무제한.")]
        [SerializeField] float rollingMaxLifetime = 0f;

        [Header("Falling sub-state")]
        [Tooltip("절벽 진입 후 적용할 중력 (m/s²). Unity 의 전역 중력과 무관 — 자체 적분.")]
        [SerializeField] float fallGravity = 30f;
        [Tooltip("낙하 중 수평 속도 감쇠 비율 (1/s). 0 = 완전 보존, 1 = 1초에 ~63% 감쇠.")]
        [SerializeField] float horizontalDecay = 0f;
        [Tooltip("낙하 한계 시간 (s). 초과 시 조용히 제거 (no VFX).")]
        [SerializeField] float maxFallTime = 3f;
        [Tooltip("착지 시 표면 각도 (deg, 절대각, 0=평지/수직up). 이하면 다시 Rolling, 초과면 break.")]
        [SerializeField] float maxLandingAngle = 45f;

        [Header("Flying")]
        [Tooltip("등속 직진 방향 (정규화 안 된 값 OK — 내부에서 정규화).")]
        [SerializeField] Vector2 flyDirection = Vector2.left;
        [Tooltip("Flying 모드의 수명 (s). 초과 시 조용히 제거.")]
        [SerializeField] float maxLifetime = 5f;
        [Tooltip("Flying 모드의 시각 회전 속도 (deg/s). 부호는 flyDirection.x 가 결정.")]
        [SerializeField] float flightSpinSpeed = 360f;

        [Header("Slime — Top Stomp (JumpBall pattern)")]
        [Tooltip("Top stomp 의 최소 점프 속도. 슬라임이 천천히 닿아도 이만큼은 보장.")]
        [SerializeField] float fixedJumpSpeed = 18f;
        [Range(0f, 2f)]
        [Tooltip("입사 속도의 보존 비율. JumpBall 의 incomingSpeedPercent.")]
        [SerializeField] float incomingSpeedPercent = 0.7f;
        [SerializeField] float bonusSpeed = 5f;
        [Range(0f, 1f)]
        [Tooltip("위 접촉 판정 임계치. (slime - snowball).normalized.y 가 이 값 이상이면 위.")]
        [SerializeField] float topNormalThreshold = 0.5f;
        [Tooltip("입사 시 수평 운동량 보존 (점프 방향에 수직인 성분).")]
        [SerializeField] bool preserveHorizontalOnStomp = true;

        [Header("Slime — Side Knockback")]
        [Tooltip("옆 충돌 시 적용할 속도 magnitude. 슬라임 기존 속도는 덮어씀. Stun 없음.")]
        [SerializeField] float knockbackSpeed = 12f;
        [Range(0f, 1f)]
        [Tooltip("Knockback 방향에 위쪽으로 섞을 비율. 0 = 순수 수평, 1 = 위쪽만.")]
        [SerializeField] float knockbackUpBias = 0.3f;
        [Range(0f, 1f)]
        [Tooltip("옆 충돌 1회당 SlimeFrostController.freezeT 에 더해질 양 (0~1, clamp). 0 = 비활성. Top stomp 는 영향 없음.")]
        [SerializeField] float sideHitFreezeBump = 0.2f;

        [Header("Visuals")]
        [Tooltip("회전 적용 대상 자식 transform. Collider 와 분리되어야 한다 (CLAUDE.md 규약).")]
        [SerializeField] Transform visualRoot;
        [Tooltip("부서질 때 끌 메인 visual renderer. 보통 visualRoot 의 메쉬 렌더러.")]
        [SerializeField] Renderer visualRenderer;

        [Header("Lifecycle")]
        [Tooltip("부서짐 후 부활 대기 시간 (s). 음수 = 일회용 (destroy / pool 반환).")]
        [SerializeField] float respawnTime = 3f;
        [Tooltip("부활 시점 overlap-clear 검사할 레이어. 시작 위치에 다른 객체가 있으면 부활 대기.")]
        [SerializeField] LayerMask blockingLayers = ~0;
        [Tooltip("부서짐 visual 지속 시간 (s). 이후 Gone 으로 전환.")]
        [SerializeField] float crumbleDuration = 0.2f;

        [Header("Audio")]
        [Tooltip("3D positional, looping AudioSource. Rolling/Flying 동안 클립을 갈아 재생.")]
        [SerializeField] AudioSource loopSource;
        [SerializeField] AudioClip rollingClip;
        [SerializeField] AudioClip flyingClip;
        [SerializeField] AudioClip breakClip;
        [Range(0f, 1f)]
        [SerializeField] float sfxVolume = 1f;
        [Tooltip("슬라임이 윗면을 밟았을 때 (top stomp).")]
        [SerializeField] SoundDef stompSound;
        [Tooltip("슬라임이 옆면에 부딪혔을 때 (side knockback).")]
        [SerializeField] SoundDef sideHitSound;

        [Header("Break VFX")]
        [Tooltip("부서질 때 spawn 할 작은 조각 prefab (Rigidbody2D + visual). spawn 시점에 fragmentLifetime 후 destroy.")]
        [SerializeField] GameObject crumbleFragmentPrefab;
        [SerializeField] int fragmentBurstCount = 6;
        [SerializeField] float fragmentSpeed = 4f;
        [Tooltip("Spawn 된 조각 prefab 의 lifetime (s). 이후 자동 destroy.")]
        [SerializeField] float fragmentLifetime = 1.5f;
        [Tooltip("부서질 때 함께 재생할 dust 파티클 (옵션, 자식 transform 으로 두면 안전).")]
        [SerializeField] ParticleSystem snowDustParticles;
        [Tooltip("Cinemachine impulse 카메라 셰이크 (옵션). 같은 GameObject 에 ImpulseEmitter 부착 필요.")]
        [SerializeField] SlimeJumping.Effects.ImpulseEmitter impulseEmitter;
        [Tooltip("카메라 셰이크 force. impulseEmitter 가 null 이면 무시.")]
        [SerializeField] float cameraShakeForce = 0.5f;

        [Header("Snow Interaction — Continuous (Rolling)")]
        [Tooltip("표면이 SnowField 일 때 매 FixedUpdate 누적되는 carve depth (m/s). 굴러가면서 매끈한 도랑이 파임. 0 = carve 비활성.")]
        [SerializeField] float carveDepthPerSec = 0.06f;
        [Tooltip("Carve 반경 = ball.radius × this multiplier. 1.0 = 공 반경과 동일 폭의 도랑, 0.7 = 약간 좁게, 1.3 = 약간 넓게.")]
        [SerializeField] float carveRadiusMultiplier = 1.0f;
        [Tooltip("SnowField.SampleDepthRatio01 이 이 값보다 클 때만 carve / grow / rolling VFX 활성. 자기가 이미 다 파먹은 도랑 위를 다시 굴러갈 때 무한히 자라거나 입자가 솟지 않도록 게이트. 0.05 = 거의 비었으면 끔.")]
        [Range(0f, 1f)]
        [SerializeField] float snowActiveRatioThreshold = 0.05f;

        [Header("Snow Interaction — Growth")]
        [Tooltip("Per-meter rolled 성장량. X = 현재 반경(m), Y = 미터당 자라는 양(m / m). 기본: 작은 공일수록 빠르게 자라고 큰 공이 되면 둔화 → 0 (max 도달 전 자연 감속).")]
        [SerializeField] AnimationCurve growthPerMeterByRadius = new AnimationCurve(
            new Keyframe(0.5f, 0.05f),
            new Keyframe(1.5f, 0.02f),
            new Keyframe(3.0f, 0f)
        );
        [Tooltip("성장 하드 캡 (m). 곡선이 0 으로 안 떨어져도 이 값을 넘지 않음.")]
        [SerializeField] float maxRadius = 3f;

        [Header("Snow Interaction — Landing Splash")]
        [Tooltip("절벽에서 떨어져 SnowField 위에 다시 착지할 때 중앙에 파이는 깊이 (m).")]
        [SerializeField] float splashDipDepth = 0.18f;
        [Tooltip("중앙 분화구 반경 (m). dipRadius < ringRadius 이어야 둔덕이 보임.")]
        [SerializeField] float splashDipRadius = 0.6f;
        [Tooltip("분화구 옆에 솟는 둔덕 높이 (m). heightMax 캡(SnowSettings.heightMaxMultiplier)에 의해 cap 됨.")]
        [SerializeField] float splashRingRaise = 0.10f;
        [Tooltip("Raise 가 적용되는 외곽 반경 (m). 이 안쪽 verts 가 둔덕이 됨.")]
        [SerializeField] float splashRingRadius = 1.2f;
        [Tooltip("이 속도(m/s) 이하의 부드러운 착지에는 splash 발동 안 함.")]
        [SerializeField] float splashMinImpactSpeed = 1.5f;

        [Header("Snow Interaction — VFX")]
        [Tooltip("굴러가는 동안 활성화되는 ParticleSystem (자식). RateOverDistance 로 prefab 에서 속도 자동 스케일. 위 게이트로 emission 만 토글.")]
        [SerializeField] ParticleSystem rollingSnowParticles;
        [Tooltip("착지 splash 순간 한 번 burst 하는 ParticleSystem. snowDustParticles(부서짐 burst)와 분리 — 시각 언어 다름.")]
        [SerializeField] ParticleSystem landingSplashParticles;

        // --- Runtime ---
        Rigidbody2D rb;
        SnowballPhase phase = SnowballPhase.Active;
        Vector2 surfaceNormal = Vector2.up;
        Vector2 motionDir = Vector2.right;   // 정규화된 진행 방향 (world)
        float currentSpeed;                  // Rolling 의 현재 속력 (m/s, ≥ 0). 중력으로 가감속, maxSpeed 로 cap.
        float visualAngleRad;                // 누적 시각 회전 (rad, signed)
        float spinRateRad;                   // 현재 회전 각속도 (rad/s, signed). FixedUpdate 가 set, Update 가 적분.

        // Falling sub-state runtime — phaseTimer 는 Flying 수명 / Crumbling 카운트와 공유
        Vector2 fallVelocity;
        float phaseTimer;

        // phaseTimer 는 Falling 의 maxFallTime 으로 덮어쓰이므로 Rolling 수명은 별도 카운터.
        float rollingLifeRemaining;

        // Respawn state
        Vector2 startPosition;
        int startDirection;
        float startRadius;
        Collider2D[] myColliders;
        bool readyToRespawn;

        // Pool 매니지드 인스턴스가 구독 — null 이면 일회용 destroy
        public event System.Action<Snowball> OnDespawn;

        void Reset()
        {
            // Setup 시점에 trigger 로 시작하도록 — 디자이너가 잊어도 동작 일관성
            var col = GetComponent<Collider2D>();
            if (col != null) col.isTrigger = true;
            var r = GetComponent<Rigidbody2D>();
            if (r != null) r.bodyType = RigidbodyType2D.Kinematic;
        }

        void OnValidate()
        {
            ApplySizeFromRadius();
        }

        void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            // Kinematic 으로 강제 — Dynamic 으로 잘못 두면 자체 중력으로 떨어짐
            rb.bodyType = RigidbodyType2D.Kinematic;
            // rb.position 직접 할당은 텔레포트라 보간 안 됨 → 연속 이동은 MovePosition.
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            myColliders = GetComponents<Collider2D>();

            // 런타임에서도 한 번 동기화 — prefab 시점과 인스턴스 시점에 radius 가 다를 수 있음
            ApplySizeFromRadius();

            // 부활 시 시작 상태 복원용. ProbeInitialSurface 의 surface-snap 전에 캐시.
            startPosition = rb.position;
            startDirection = direction;
            startRadius = radius;
            currentSpeed = initialSpeed;

            if (mode == Mode.Rolling)
            {
                ProbeInitialSurface();
                rollingLifeRemaining = rollingMaxLifetime;
            }
            else
                phaseTimer = maxLifetime;

            StartLoop();
        }

        void ApplySizeFromRadius()
        {
            if (radius <= 0f) return;

            // Visual: unit sphere mesh 가정 → diameter = radius * 2
            if (visualRoot != null)
                visualRoot.localScale = Vector3.one * (radius * 2f);

            // CircleCollider2D 만 동기화 — 다른 콜라이더 타입은 디자이너 수동 설정.
            var circle = GetComponent<CircleCollider2D>();
            if (circle != null) circle.radius = radius;
        }

        void Update()
        {
            if (WorldTimeFreezeService.IsFrozen) return;
            // 시각 회전은 frame rate 로 적분 — FixedUpdate 만으로는 60fps 위에서 끊김.
            visualAngleRad += spinRateRad * Time.deltaTime;
            UpdateVisualRotation();
        }

        void FixedUpdate()
        {
            if (WorldTimeFreezeService.IsFrozen) return;

            switch (phase)
            {
                case SnowballPhase.Active:
                    if (mode == Mode.Rolling) FixedUpdateRolling();
                    else                      FixedUpdateFlying();
                    break;
                case SnowballPhase.Falling:
                    FixedUpdateFalling();
                    break;
                case SnowballPhase.Crumbling:
                    TickCrumbling(Time.fixedDeltaTime);
                    break;
                case SnowballPhase.Gone:
                    TickGone(Time.fixedDeltaTime);
                    break;
            }
        }

        // ---------- Rolling ----------

        void ProbeInitialSurface()
        {
            // 시작 시 발 밑을 찾아 surface 정렬. 슬라임의 attach 패턴과 동일한 절차.
            float castDistance = rayDepth + radius * 2f;
            RaycastHit2D hit = Physics2D.Raycast(rb.position, Vector2.down, castDistance, terrainMask);
            if (!hit.collider) return;

            surfaceNormal = hit.normal;
            Vector2 perp = new Vector2(hit.normal.y, -hit.normal.x);
            motionDir = (perp * direction).normalized;
            rb.position = hit.point + hit.normal * radius;
        }

        void FixedUpdateRolling()
        {
            float dt = Time.fixedDeltaTime;

            if (rollingMaxLifetime > 0f)
            {
                rollingLifeRemaining -= dt;
                if (rollingLifeRemaining <= 0f) { SilentCleanup(); return; }
            }

            // 표면 tangent 중력. 오르막 정점에서 속력이 음수가 되면 direction 을 뒤집어 복귀.
            if (gravity > 0f)
            {
                currentSpeed += -gravity * motionDir.y * dt;
                if (currentSpeed < 0f)
                {
                    direction = -direction;
                    motionDir = -motionDir;
                    currentSpeed = -currentSpeed;
                }
            }
            currentSpeed = Mathf.Min(currentSpeed, maxSpeed);

            Vector2 step = motionDir * (currentSpeed * dt);
            Vector2 nextPos = rb.position + step;

            // 0) Forward wall probe — forward-down probe 는 -surfaceNormal 방향만 봐서 수직 벽을
            //    놓친다 (벽 옆을 지나치며 floor 만 hit).
            RaycastHit2D wallHit = Physics2D.Raycast(
                rb.position, motionDir, radius + lookahead, terrainMask);
            if (wallHit.collider)
            {
                Vector2 wallPerp = new Vector2(wallHit.normal.y, -wallHit.normal.x);
                Vector2 wallMotion = Vector2.Dot(wallPerp, motionDir) >= 0f ? wallPerp : -wallPerp;
                float oldSlope = SignedSlopeAngleDeg(motionDir);
                float wallSlope = SignedSlopeAngleDeg(wallMotion);
                if (wallSlope - oldSlope > breakSlopeDelta)
                {
                    Break();
                    return;
                }
            }

            // 1) Forward-down preview probe — break 판정 전용, 단독으로 EnterFalling 금지.
            //    코너 직전에 EnterFalling 하면 Falling 의 CircleCast 가 아직 tangent 인 상태에서
            //    distance=0 hit → 같은 자리로 복귀, Active↔Falling 진동.
            Vector2 forwardProbeOrigin = nextPos + motionDir * lookahead;
            RaycastHit2D forwardHit = Physics2D.Raycast(
                forwardProbeOrigin, -surfaceNormal, rayDepth + radius, terrainMask);

            Vector2 forwardNormal = Vector2.zero;
            Vector2 forwardMotion = Vector2.zero;
            if (forwardHit.collider)
            {
                forwardNormal = forwardHit.normal;
                Vector2 forwardPerp = new Vector2(forwardNormal.y, -forwardNormal.x);
                forwardMotion = Vector2.Dot(forwardPerp, motionDir) >= 0f ? forwardPerp : -forwardPerp;

                float oldSlopeDeg = SignedSlopeAngleDeg(motionDir);
                float newSlopeDeg = SignedSlopeAngleDeg(forwardMotion);
                if (newSlopeDeg - oldSlopeDeg > breakSlopeDelta)
                {
                    Break();
                    return;
                }
                // 볼록 코너 — surface snap 대신 ballistic 이탈, 속도는 EnterFalling 이 보존.
                if (oldSlopeDeg - newSlopeDeg > detachSlopeDelta)
                {
                    rb.MovePosition(nextPos);
                    EnterFalling();
                    return;
                }
            }

            // 2) Snap probe — nextPos 바로 아래 표면에 안착. 여기가 실제 위치 갱신.
            Vector2 snapOrigin = nextPos + surfaceNormal * radius;
            RaycastHit2D snapHit = Physics2D.Raycast(
                snapOrigin, -surfaceNormal, radius * 2f + rayDepth, terrainMask);

            if (snapHit.collider)
            {
                Vector2 targetPos = snapHit.point + snapHit.normal * radius;
                surfaceNormal = snapHit.normal;
                Vector2 sPerp = new Vector2(snapHit.normal.y, -snapHit.normal.x);
                motionDir = (Vector2.Dot(sPerp, motionDir) >= 0f ? sPerp : -sPerp).normalized;
                rb.MovePosition(targetPos);
            }
            else if (forwardHit.collider)
            {
                // 좁은 gap — forward-probed surface 로 fallback, 다음 프레임이면 정상 복귀.
                surfaceNormal = forwardNormal;
                motionDir = forwardMotion;
                rb.MovePosition(nextPos);
            }
            else
            {
                // 진짜 절벽. nextPos 까지 진행해 tangent 에서 벗어난 뒤 EnterFalling —
                // 안 그러면 다음 CircleCast 가 즉시 hit 으로 잡혀 phase 가 진동한다.
                rb.MovePosition(nextPos);
                EnterFalling();
                return;
            }

            // 시각 회전: 진행 방향에 따라 시계/반시계. 우측 진행 = 시계 (-Z). Update 가 적분.
            spinRateRad = -(direction * currentSpeed) / radius;

            // forwardHit fallback 케이스에선 snapHit.collider == null 이라 snow 상호작용이 자연 비활성.
            SnowField field = null;
            bool onActiveSnow = false;
            if (snapHit.collider != null
                && SnowFieldRegistry.TryGet(snapHit.collider, out field)
                && field != null)
            {
                float ratio = field.SampleDepthRatio01(snapHit.point);
                onActiveSnow = ratio > snowActiveRatioThreshold;
            }

            if (onActiveSnow)
            {
                if (carveDepthPerSec > 0f)
                    field.Carve(snapHit.point,
                                carveDepthPerSec * dt,
                                radius * carveRadiusMultiplier);

                if (growthPerMeterByRadius != null && radius < maxRadius)
                {
                    float growth = growthPerMeterByRadius.Evaluate(radius) * step.magnitude;
                    if (growth > 0f)
                    {
                        radius = Mathf.Min(maxRadius, radius + growth);
                        ApplySizeFromRadius();
                    }
                }
            }

            // Rolling VFX — emission 만 토글. RateOverDistance 는 prefab 측 설정에 위임.
            if (rollingSnowParticles != null)
            {
                var emission = rollingSnowParticles.emission;
                if (emission.enabled != onActiveSnow)
                    emission.enabled = onActiveSnow;
            }
        }

        // 진행 방향의 경사각 (deg). 진행 부호와 무관하게 오르막 양수, 내리막 음수.
        static float SignedSlopeAngleDeg(Vector2 motion)
        {
            return Mathf.Atan2(motion.y, Mathf.Abs(motion.x)) * Mathf.Rad2Deg;
        }

        // ---------- Slime / Snowball trigger ----------

        void OnTriggerEnter2D(Collider2D other) => HandleTrigger(other);
        void OnTriggerStay2D(Collider2D other) => HandleTrigger(other);

        void HandleTrigger(Collider2D other)
        {
            // Falling (절벽 낙하 구간) 도 충돌 유효. Crumbling/Gone 은 콜라이더가 이미 disabled.
            if (phase != SnowballPhase.Active && phase != SnowballPhase.Falling) return;

            // 상대 눈덩이도 자기 trigger 로 break 하므로 여기서는 자기 자신만.
            var otherSnowball = other.GetComponentInParent<Snowball>();
            if (otherSnowball != null && otherSnowball != this)
            {
                Break();
                return;
            }

            var slime = other.GetComponentInParent<SlimeController>();
            if (slime == null) return;

            HandleSlimeContact(slime);
        }

        void HandleSlimeContact(SlimeController slime)
        {
            Vector2 toSlime = (slime.Position - rb.position).normalized;
            if (toSlime.y > topNormalThreshold)
                StompJump(slime);
            else
                KnockbackPush(slime, toSlime);

            Break();
        }

        void StompJump(SlimeController slime)
        {
            SoundService.Play(stompSound, rb.position);

            // 굴러가는 눈덩이는 회전 중이라 transform.up 이 흔들림 — 슬라임 기준을 쓴다.
            Vector2 jumpDir = slime.CurrentState == SlimeController.SlimeState.Airborne
                ? Vector2.up
                : slime.SurfaceNormal;

            Vector2 incomingVel = slime.Velocity;
            float incomingSpeed = incomingVel.magnitude;
            float scaled = incomingSpeed * incomingSpeedPercent + bonusSpeed;
            float jumpSpeed = Mathf.Max(fixedJumpSpeed, scaled);

            Vector2 newVel = jumpDir * jumpSpeed;
            if (preserveHorizontalOnStomp)
            {
                Vector2 horizontal = new Vector2(jumpDir.y, -jumpDir.x);
                newVel += horizontal * Vector2.Dot(incomingVel, horizontal);
            }
            slime.ForceAirborneWithVelocity(newVel);
        }

        void KnockbackPush(SlimeController slime, Vector2 toSlime)
        {
            SoundService.Play(sideHitSound, rb.position);

            Vector2 dir = (toSlime + Vector2.up * knockbackUpBias).normalized;
            slime.ForceAirborneWithVelocity(dir * knockbackSpeed);

            if (sideHitFreezeBump > 0f)
            {
                var frost = slime.GetComponentInChildren<SlimeFrostController>();
                if (frost != null) frost.AddFreezeT(sideHitFreezeBump);
            }
        }

        // ---------- Falling sub-state ----------

        void EnterFalling()
        {
            phase = SnowballPhase.Falling;
            fallVelocity = motionDir * currentSpeed;
            phaseTimer = maxFallTime;
        }

        void FixedUpdateFalling()
        {
            float dt = Time.fixedDeltaTime;

            if (horizontalDecay > 0f)
            {
                float decayFactor = Mathf.Exp(-horizontalDecay * dt);
                fallVelocity.x *= decayFactor;
            }
            fallVelocity.y -= fallGravity * dt;

            Vector2 step = fallVelocity * dt;
            float stepDistance = step.magnitude;
            if (stepDistance < 1e-5f)
            {
                phaseTimer -= dt;
                if (phaseTimer <= 0f) SilentCleanup();
                return;
            }

            RaycastHit2D hit = Physics2D.CircleCast(
                rb.position, radius, step / stepDistance, stepDistance, terrainMask);

            if (hit.collider)
            {
                float landingAngleDeg = Vector2.Angle(hit.normal, Vector2.up);
                if (landingAngleDeg <= maxLandingAngle)
                    ResumeRollingFromLanding(hit, fallVelocity);
                else
                    Break();
                return;
            }

            rb.MovePosition(rb.position + step);
            spinRateRad = -fallVelocity.x / radius;

            phaseTimer -= dt;
            if (phaseTimer <= 0f) SilentCleanup();
        }

        // ---------- Flying mode ----------

        void FixedUpdateFlying()
        {
            float dt = Time.fixedDeltaTime;
            Vector2 dir = flyDirection.sqrMagnitude > 1e-6f
                ? flyDirection.normalized
                : Vector2.left;
            Vector2 step = dir * (maxSpeed * dt);
            float stepDistance = step.magnitude;

            if (stepDistance > 1e-5f)
            {
                RaycastHit2D hit = Physics2D.CircleCast(
                    rb.position, radius, step / stepDistance, stepDistance, terrainMask);
                if (hit.collider)
                {
                    Break();
                    return;
                }
                rb.MovePosition(rb.position + step);
            }

            // 시각 회전 — flyDirection.x 부호로 회전 방향 결정 (수직 비행이면 0). Update 가 적분.
            float spinSign = Mathf.Sign(dir.x);
            spinRateRad = -spinSign * flightSpinSpeed * Mathf.Deg2Rad;

            phaseTimer -= dt;
            if (phaseTimer <= 0f) SilentCleanup();
        }

        void ResumeRollingFromLanding(RaycastHit2D hit, Vector2 lastFallVel)
        {
            surfaceNormal = hit.normal;
            Vector2 perp = new Vector2(hit.normal.y, -hit.normal.x);
            // 수평 운동량 부호로 새 motion 방향 선택. 정지에 가까우면 원래 direction 유지.
            float horizontalSign = Mathf.Abs(lastFallVel.x) > 0.05f
                ? Mathf.Sign(lastFallVel.x)
                : direction;
            motionDir = (perp.x * horizontalSign >= 0f ? perp : -perp).normalized;
            // direction 은 respawn 때 startDirection 으로 복원되므로 여기서 바꿔도 안전.
            direction = motionDir.x >= 0f ? 1 : -1;

            rb.position = hit.point + hit.normal * radius;
            // 낙하 운동량을 표면 tangent 로 투영. motionDir 부호가 맞춰져 있어 dot 은 보통 양수.
            currentSpeed = Mathf.Clamp(Vector2.Dot(lastFallVel, motionDir), 0f, maxSpeed);
            phase = SnowballPhase.Active;
            fallVelocity = Vector2.zero;

            float impactSpeed = lastFallVel.magnitude;
            if (impactSpeed >= splashMinImpactSpeed
                && hit.collider != null
                && SnowFieldRegistry.TryGet(hit.collider, out var landingField)
                && landingField != null)
            {
                landingField.Splash(hit.point,
                                    splashDipDepth, splashDipRadius,
                                    splashRingRaise, splashRingRadius);

                if (landingSplashParticles != null)
                {
                    landingSplashParticles.transform.position = hit.point;
                    landingSplashParticles.Play(true);
                }
            }
        }

        // ---------- Public API for spawner ----------

        /// <summary>Pool 매니지드 Snowball 을 시작 위치/발사 방향으로 재무장. Spawner.Get 후 호출.</summary>
        public void Initialize(Vector2 worldPos, Vector2 launchDir)
        {
            // 풀 매니지드 인스턴스가 자체 부활하면 풀에 영영 안 돌아감 — 명시적으로 비활성화.
            respawnTime = -1f;

            Vector2 dirN = launchDir.sqrMagnitude > 1e-6f
                ? launchDir.normalized
                : Vector2.right;

            rb.position = worldPos;
            // direction 부호는 ResumeRollingFromLanding fallback 과 EnterActive 의 motionDir 복원에 쓰임.
            direction = dirN.x >= 0f ? 1 : -1;
            startPosition = worldPos;
            startDirection = direction;

            surfaceNormal = Vector2.up;
            motionDir = dirN;
            fallVelocity = Vector2.zero;
            currentSpeed = initialSpeed;
            // SnowField 위에서 자라난 반경을 시작 값으로 되돌림.
            radius = startRadius;
            ApplySizeFromRadius();
            ResetVisualState();
            readyToRespawn = false;
            ShowVisualAndCollider();

            if (mode == Mode.Rolling)
            {
                // Rolling 수명 리셋 — Falling 분기로 발사돼도 착지 후 Rolling 진입 시 사용됨.
                rollingLifeRemaining = rollingMaxLifetime;
                // 수평이면 표면 snap, 대각선/수직이면 Falling phase 로 ballistic 발사.
                bool nearHorizontal = Mathf.Abs(dirN.y) < 0.05f;
                if (nearHorizontal)
                {
                    motionDir = (Vector2.right * direction).normalized;
                    phase = SnowballPhase.Active;
                    ProbeInitialSurface();
                }
                else
                {
                    phase = SnowballPhase.Falling;
                    fallVelocity = dirN * initialSpeed;
                    phaseTimer = maxFallTime;
                }
            }
            else
            {
                flyDirection = launchDir;
                phase = SnowballPhase.Active;
                phaseTimer = maxLifetime;
            }

            StartLoop();
        }

        void ResetVisualState()
        {
            // 풀 재사용 인스턴스가 옛 회전 값을 한 프레임도 보이지 않도록 accumulator + transform 둘 다 리셋.
            visualAngleRad = 0f;
            spinRateRad = 0f;
            if (visualRoot != null) visualRoot.localRotation = Quaternion.identity;
        }

        // ---------- Lifecycle: Break / Crumbling / Gone ----------

        void Break() => EnterCrumbling();

        void EnterCrumbling()
        {
            phase = SnowballPhase.Crumbling;
            phaseTimer = crumbleDuration;
            spinRateRad = 0f;

            // 파편 burst — designer 가 prefab 의 lifetime 으로 자체 destroy 처리
            SpawnFragments();
            if (snowDustParticles != null) snowDustParticles.Play(true);
            if (impulseEmitter != null && cameraShakeForce > 0f)
                impulseEmitter.Emit(cameraShakeForce);

            StopLoop();
            if (breakClip != null)
                SoundService.PlayAt(breakClip, rb.position, SoundCategory.SFX_Object, sfxVolume);

            // Rolling trail 즉시 차단 — 부서졌는데 입자가 한 프레임 더 emit 되면 어색.
            if (rollingSnowParticles != null)
            {
                var emission = rollingSnowParticles.emission;
                if (emission.enabled) emission.enabled = false;
            }

            // Visual / collider off — Crumbling 동안 trigger 재발동 막음
            HideVisualAndCollider();
        }

        void TickCrumbling(float dt)
        {
            phaseTimer -= dt;
            if (phaseTimer <= 0f) EnterGone();
        }

        void SpawnFragments()
        {
            if (crumbleFragmentPrefab == null) return;
            for (int i = 0; i < fragmentBurstCount; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float speedJitter = Random.Range(0.7f, 1.3f);
                var frag = Instantiate(crumbleFragmentPrefab, rb.position, Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
                var fragRb = frag.GetComponent<Rigidbody2D>();
                if (fragRb != null)
                {
                    fragRb.linearVelocity = dir * (fragmentSpeed * speedJitter);
                    fragRb.angularVelocity = Random.Range(-180f, 180f);
                }
                if (fragmentLifetime > 0f)
                    Destroy(frag, fragmentLifetime);
            }
        }

        void EnterGone()
        {
            phase = SnowballPhase.Gone;
            HideVisualAndCollider();

            if (respawnTime < 0f)
            {
                if (OnDespawn != null) OnDespawn.Invoke(this);
                else Destroy(gameObject);
                return;
            }
            phaseTimer = respawnTime;
            readyToRespawn = false;
        }

        void TickGone(float dt)
        {
            if (respawnTime < 0f) return; // 일회용은 EnterGone 에서 이미 처리됨

            if (!readyToRespawn)
            {
                phaseTimer -= dt;
                if (phaseTimer <= 0f) readyToRespawn = true;
                return;
            }

            // 시작 위치가 비어 있을 때만 부활 (CrumblingPlatform.UpdateGone 패턴)
            if (Physics2D.OverlapCircle(startPosition, radius, blockingLayers) != null) return;

            EnterActive();
        }

        void EnterActive()
        {
            phase = SnowballPhase.Active;
            rb.position = startPosition;
            direction = startDirection;
            motionDir = (Vector2.right * direction).normalized;
            surfaceNormal = Vector2.up;
            fallVelocity = Vector2.zero;
            currentSpeed = initialSpeed;
            // 자라난 반경 리셋 — Initialize 와 동일 패턴.
            radius = startRadius;
            ApplySizeFromRadius();
            ResetVisualState();
            ShowVisualAndCollider();

            if (mode == Mode.Rolling)
            {
                ProbeInitialSurface();
                rollingLifeRemaining = rollingMaxLifetime;
            }
            else
                phaseTimer = maxLifetime;

            StartLoop();
        }

        void SilentCleanup()
        {
            // 시간/거리 한계 초과 — VFX 없이 곧장 Gone 으로. 부활 정책은 EnterGone 이 처리.
            StopLoop();
            EnterGone();
        }

        // ---------- Audio ----------

        void StartLoop()
        {
            if (loopSource == null) return;
            AudioClip clip = mode == Mode.Rolling ? rollingClip : flyingClip;
            if (clip == null) return;
            loopSource.clip   = clip;
            loopSource.loop   = true;
            loopSource.volume = sfxVolume;
            loopSource.Play();
        }

        void StopLoop()
        {
            if (loopSource == null) return;
            if (loopSource.isPlaying) loopSource.Stop();
        }

        /// <summary>SnowballAudioManager 가 씬 내 모든 Snowball 에 broadcast 하는 진입점.</summary>
        public void ApplyAudioSettings(AudioClip rolling, AudioClip flying, AudioClip breakSfx, float volume)
        {
            rollingClip = rolling;
            flyingClip  = flying;
            breakClip   = breakSfx;
            sfxVolume   = volume;
            if (loopSource != null && loopSource.isPlaying)
            {
                AudioClip current = mode == Mode.Rolling ? rollingClip : flyingClip;
                if (current != null && current != loopSource.clip)
                    loopSource.clip = current;
                loopSource.volume = sfxVolume;
            }
        }

        void HideVisualAndCollider()
        {
            if (visualRenderer != null) visualRenderer.enabled = false;
            if (myColliders != null)
                foreach (var c in myColliders) if (c != null) c.enabled = false;
        }

        void ShowVisualAndCollider()
        {
            if (visualRenderer != null) visualRenderer.enabled = true;
            if (myColliders != null)
                foreach (var c in myColliders) if (c != null) c.enabled = true;
        }

        // ---------- Visual ----------

        void UpdateVisualRotation()
        {
            if (visualRoot == null) return;
            visualRoot.localRotation = Quaternion.Euler(0f, 0f, visualAngleRad * Mathf.Rad2Deg);
        }

        // ---------- Gizmos ----------

        void OnDrawGizmosSelected()
        {
            Vector3 pos = Application.isPlaying ? (Vector3)rb.position : transform.position;
            Vector3 mDir = Application.isPlaying ? (Vector3)motionDir : Vector3.right * Mathf.Sign(direction);

            Gizmos.color = new Color(0.6f, 0.85f, 1f, 0.8f);
            Vector3 rayOrigin = pos + mDir * lookahead;
            Vector3 normalDir = Application.isPlaying ? (Vector3)surfaceNormal : Vector3.up;
            Gizmos.DrawLine(rayOrigin, rayOrigin - normalDir * (rayDepth + radius));

            Gizmos.color = Color.white;
            Vector3 tip = pos + mDir * (radius + 0.4f);
            Gizmos.DrawLine(pos, tip);
        }
    }
}
