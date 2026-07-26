using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Platforms
{
    // 구름 발판. 슬라임이 올라타면:
    //   1) 무게로 아래로 눌림(sink) — 붙어있는 슬라임도 같이 내려감.
    //   2) 리바운드로 올라오며 슬라임을 위로 발사 + 구름은 몽실 puff 로 흩어지며 소멸.
    //   3) 발사 높이 = 점프(Space) 타이밍. 안 누르면 조금만 뜸, 바닥 리바운드 순간에 맞추면 최대.
    // 비주얼은 자식 씬 메시(CloudDisperser.sceneCloud). 콜라이더 = 메시 실루엣 PolygonCollider2D
    // (MeshToCollider2DGenerator 가 만드는 {mesh}_Collider2D wrapper 자식). 둘 분리.
    // 발사는 SlimeController.ForceAirborneWithVelocity (JumpPad 와 동일 경로).
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

        enum St { Idle, Sinking, Gone }
        St state = St.Idle;
        float seqT;
        bool jumpPressed;
        float jumpPressT;
        SlimeController rider;
        float goneT;

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
            jumpPressed = false;
            rider = slime;
        }

        public void OnSlimeStay(SlimeController slime, float dt)
        {
            if (state != St.Sinking) return;
            seqT += dt;
            rb.MovePosition(restPos + Vector2.up * SinkOffset(seqT));   // 붙은 슬라임도 같이.
            if (seqT >= cfg.sinkTime + cfg.launchDelay) DoLaunch();
        }

        public void OnSlimeDetach(SlimeController slime)
        {
            if (state == St.Sinking)   // 발사 전 스스로 이탈 → 취소, 구름 복원.
            {
                state = St.Idle;
                rider = null;
                rb.MovePosition(restPos);
            }
        }

        // 입력은 매 프레임 폴링(FixedUpdate 의 OnSlimeStay 는 GetKeyDown 놓칠 수 있음).
        void Update()
        {
            if (state == St.Sinking && !jumpPressed && Input.GetKeyDown(cfg.jumpKey))
            {
                jumpPressed = true;
                jumpPressT = seqT;
            }
        }

        void FixedUpdate()
        {
            if (state != St.Gone) return;
            goneT += Time.fixedDeltaTime;
            float u = Mathf.Clamp01(goneT / cfg.riseVisualTime);
            rb.MovePosition(restPos + Vector2.up * (cfg.riseVisualHeight * (1f - (1f - u) * (1f - u))));
            if (cfg.respawn && goneT >= cfg.respawnDelay) Respawn();
        }

        float SinkOffset(float t)
        {
            if (t <= cfg.sinkTime)
            {
                float u = Mathf.Clamp01(t / cfg.sinkTime);
                return -cfg.sinkDepth * (1f - (1f - u) * (1f - u));   // easeOut 하강.
            }
            float r = Mathf.Clamp01((t - cfg.sinkTime) / Mathf.Max(0.01f, cfg.launchDelay));
            return -cfg.sinkDepth * (1f - r);                          // 리바운드 상승.
        }

        void DoLaunch()
        {
            float quality = 0f;
            if (jumpPressed)
            {
                float ideal = cfg.sinkTime;    // 바닥 근처 = 최적.
                quality = Mathf.Clamp01(1f - Mathf.Abs(jumpPressT - ideal) / cfg.timingWindow);
            }
            float speed = Mathf.Lerp(cfg.baseHopSpeed, cfg.maxLaunchSpeed, quality);
            if (rider != null) rider.ForceAirborneWithVelocity(Vector2.up * speed);

            if (disperser != null) disperser.Disperse();
            if (col != null) col.enabled = false;

            state = St.Gone;
            goneT = 0f;
            rider = null;
        }

        void Respawn()
        {
            rb.MovePosition(restPos);
            if (disperser != null) disperser.ResetToRest();
            if (col != null) col.enabled = true;
            state = St.Idle;
        }
    }
}
