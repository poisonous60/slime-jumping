using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Platforms
{
    // 구름 발판: 올라타면 sink 시작 → 차징한 정도가 점프 높이를 정하고, 발사되면 puff 로 흩어져 소멸.
    //   안 누름   : idleFuseTime 만료 → idlePopSpeed 로 수직 팝.
    //   차징      : 슬라임 자신의 발사(뗌 또는 만충 오토런치)에 chargeMin~MaxPopSpeed 를
    //               차징 비율로 Lerp 한 값을 **하한**으로 적용. 뗀 타이밍 = 점프 높이.
    //               하한이라 에너지 전환·하이퍼대시로 더 빠른 발사는 안 깎는다(기존 releaseFloor 와 동일 구조).
    //   누른 채 holdFuseTime 만료 : 그 시점 차징 비율로 조준 방향 팝(오토런치가 보통 먼저라 안전망).
    // Space 감지는 slime.IsCharging 으로 한다. legacy Input 폴링은 공중 버퍼 입력을 놓치고,
    // 슬라임 자신의 차지 발사와 같은 키를 두고 경쟁해 서로 취소시켰다.
    // 비주얼 = 자식 씬 메시(CloudDisperser.sceneCloud), 콜라이더 = 실루엣 PolygonCollider2D wrapper 자식. 둘 분리.
    [RequireComponent(typeof(Rigidbody2D))]
    public class CloudPlatform : MonoBehaviour, IPlatformEffect
    {
        [Header("Config")]
        public CloudPlatformConfig configOverride;   // 비우면 Assets/Resources/CloudPlatformConfig.asset 공유 튜닝.

        [Header("Visual")]
        public CloudDisperser disperser;   // 구름 비주얼(씬 메시 기반). 없으면 자동 탐색.

        CloudPlatformConfig cfg;
        Rigidbody2D rb;
        Collider2D col;
        Vector2 restPos;

        enum St { Idle, Sinking, Gone, Regen }
        St state = St.Idle;
        float regenT;
        float seqT;
        float pressT = -1f;   // IsCharging 을 처음 본 seqT. 음수 = 아직 안 누름.
        float chargeRadiusRef = 1f;   // 이번 차징의 판정원 반경. Launch() 가 리셋하기 전 값.
        bool riderLaunched;   // 이번 탑승이 실제 차지 발사로 끝났는지.
        SlimeController rider;
        float goneT;
        readonly Collider2D[] overlapResults = new Collider2D[4];

        void Awake()
        {
            EnsureRefs();
            restPos = transform.position;   // rb.position 는 body sync 전 (0,0) 이라 transform 사용.
            if (disperser == null) disperser = GetComponentInChildren<CloudDisperser>();
        }

        void EnsureRefs()
        {
            if (cfg == null) cfg = CloudPlatformConfig.Resolve(configOverride);
            if (rb == null) rb = GetComponent<Rigidbody2D>();
            if (col == null) col = GetComponentInChildren<Collider2D>();   // 실루엣 콜라이더는 wrapper 자식에 붙음.
            if (rb != null && rb.bodyType != RigidbodyType2D.Kinematic)
            {
                rb.bodyType = RigidbodyType2D.Kinematic;
                rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            }
        }

        // ---- IPlatformEffect ----
        public void OnSlimeAttach(SlimeController slime)
        {
            EnsureRefs();
            if (state != St.Idle) return;
            state = St.Sinking;
            seqT = 0f;
            pressT = -1f;
            // 버퍼 입력 착지는 EnterChargingFromLanding 이 반경을 늘린 뒤 여길 부르고,
            // stick 지연 동안엔 OnSlimeStay 가 안 도니 여기서 먼저 잡는다.
            chargeRadiusRef = slime.ActiveChargeRadius;
            riderLaunched = false;
            rider = slime;
            slime.OnLaunch += OnRiderLaunch;
        }

        // Launch() 만 OnLaunch 를 쏜다. ForceAirborneWithVelocity(마찰 이탈, 다른 발판)와
        // CancelChargingToAirborne(차지 취소)은 안 쏘므로, 이게 "정말 발사했는가"의 유일한 판별식.
        void OnRiderLaunch(Vector2 pos, Vector2 vel, bool wasHop) => riderLaunched = true;

        public void OnSlimeStay(SlimeController slime, float dt)
        {
            if (state != St.Sinking) return;
            // 붙은 면이 이 구름이 아니면 퓨즈를 돌리지 않는다. SlimeController 는 착지 때만
            // CheckPlatformEffect 를 하므로, 옆 지형으로 기어 넘어가도 참조가 남아 있다.
            var surf = slime.SurfaceCollider;
            if (surf == null || !surf.transform.IsChildOf(transform)) return;

            seqT += dt;
            rb.MovePosition(restPos + Vector2.up * SinkOffset(seqT));   // 붙은 슬라임도 같이.

            bool charging = slime.IsCharging;   // 공중 버퍼 입력으로 착지 즉시 Charging 인 경우도 포함.
            if (charging)
            {
                if (pressT < 0f) pressT = seqT;
                chargeRadiusRef = slime.ActiveChargeRadius;   // 이번 차징의 실제 판정원.
            }

            float fuse = charging ? pressT + cfg.holdFuseTime : cfg.idleFuseTime;
            if (seqT < fuse) return;

            // 퓨즈 만료. 누르고 있었으면 그때까지 차진 정도로, 안 눌렀으면 조준이 없으니 수직 최소.
            if (charging) DoPop(slime.AimDirection, ChargeSpeed(slime));
            else DoPop(Vector2.up, cfg.idlePopSpeed);
        }

        public void OnSlimeDetach(SlimeController slime)
        {
            if (state != St.Sinking) return;

            // 차지 발사로 이탈. Launch() 는 velocity 확정 → OnLaunch → ClearPlatformEffect → launchSpeed 기록
            // 순서라 여기서 올린 속도가 그대로 반영된다. 하한으로만 올리고 조준 방향은 보존 —
            // 에너지 전환(고낙하) 발사는 15 를 넘을 수 있는데 덮어쓰면 그걸 깎는다.
            // 발사면이 이 구름일 때만 — 기어 넘어간 뒤 옆 지형에서 쏜 발사에 구름 값을 씌우면 안 된다.
            // 가장자리 코요테 발사는 SurfaceCollider 가 null 로 덮인 뒤라 캐시로 판별한다.
            var surf = slime.SurfaceCollider;
            if (surf == null) surf = slime.CachedSurfaceCollider;
            if (riderLaunched && surf != null && surf.transform.IsChildOf(transform))
            {
                float target = ChargeSpeed(slime);
                float speed = slime.Velocity.magnitude;
                if (speed > 0.0001f && speed < target)
                    slime.MultiplyVelocity(target / speed);
            }
            Consume();   // 밟은 구름은 어떤 경로로 나가든 소모.
        }

        // 차진 정도 → 하한 속도. 분모는 이번 차징의 판정원 반경(chargeRadiusRef) — 플레이어가 보는
        // 조준원의 어디까지 채웠나. ActiveChargeRadius 를 직접 읽으면 detach 에선 Launch() 가 이미
        // 기본값으로 리셋한 뒤고(에너지 전환 차징이 전부 만충으로 뭉개짐), hold-팝에선 확장값이라
        // 두 경로의 의미가 갈라진다. 캐시 하나로 양쪽을 같은 식으로 만든다.
        float ChargeSpeed(SlimeController slime)
        {
            float ratio = Mathf.Clamp01(slime.AimOffset.magnitude / Mathf.Max(0.0001f, chargeRadiusRef));
            return Mathf.Lerp(cfg.chargeMinPopSpeed, cfg.chargeMaxPopSpeed, ratio);
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            if (state == St.Regen)
            {
                // ThinIce 와 동일: 애니메이션 내내 콜라이더 off, 끝에서만 커밋.
                regenT += dt;
                if (regenT >= cfg.regenTime) Commit();
                return;
            }
            if (state != St.Gone) return;

            goneT += dt;
            float u = Mathf.Clamp01(goneT / cfg.riseVisualTime);
            rb.MovePosition(restPos + Vector2.up * (cfg.riseVisualHeight * (1f - (1f - u) * (1f - u))));

            if (!cfg.respawn || goneT < cfg.respawnDelay) return;
            if (IsRegenBlocked()) return;   // 슬라임이 자리에 겹쳐 있으면 대기.
            BeginRegen();
        }

        // 콜라이더를 잠깐 켜서 겹침만 보고 즉시 끈다(ThinIcePlatform.IsRegenBlocked 와 동일 이유 —
        // 켜둔 채로 조회하면 슬라임이 아직 실체 없는 구름 위에 올라앉는다).
        bool IsRegenBlocked()
        {
            if (col == null) return false;
            var filter = new ContactFilter2D { useLayerMask = true, layerMask = cfg.regenBlockLayers, useTriggers = false };
            col.enabled = true;
            int hits = col.Overlap(filter, overlapResults);
            col.enabled = false;
            return hits > 0;
        }

        void BeginRegen()
        {
            rb.MovePosition(restPos);
            if (disperser != null) disperser.Reform();
            state = St.Regen;
            regenT = 0f;
        }

        void Commit()
        {
            if (disperser != null) disperser.ResetToRest();
            if (col != null) col.enabled = true;
            state = St.Idle;
        }

        float SinkOffset(float t)
        {
            if (t <= cfg.sinkTime)
            {
                float u = Mathf.Clamp01(t / cfg.sinkTime);
                return -cfg.sinkDepth * (1f - (1f - u) * (1f - u));   // easeOut 하강.
            }
            float r = Mathf.Clamp01((t - cfg.sinkTime) / Mathf.Max(0.01f, cfg.reboundTime));
            return -cfg.sinkDepth * (1f - r);                          // 리바운드 상승.
        }

        // dir 은 호출부에서 미리 읽는다 — ForceAirborneWithVelocity 가 Charging 이면 aimOffset 을 지운다.
        void DoPop(Vector2 dir, float speed)
        {
            var s = rider;
            Consume();   // ForceAirborneWithVelocity 가 OnSlimeDetach 를 재진입시키므로 Gone 을 먼저.
            if (s != null) s.ForceAirborneWithVelocity(dir * speed);
        }

        void Consume()
        {
            if (rider != null) rider.OnLaunch -= OnRiderLaunch;
            if (disperser != null) disperser.Disperse();
            if (col != null) col.enabled = false;

            state = St.Gone;
            goneT = 0f;
            rider = null;
        }

    }
}
