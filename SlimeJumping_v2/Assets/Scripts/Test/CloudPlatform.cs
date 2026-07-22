using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Platforms
{
    // 구름 발판. 슬라임이 올라타면:
    //   1) 무게로 아래로 눌림(sink) — 붙어있는 슬라임도 같이 내려감.
    //   2) 리바운드로 올라오며 슬라임을 위로 발사 + 구름은 몽실 puff 로 흩어지며 소멸.
    //   3) 발사 높이 = 점프(Space) 타이밍. 안 누르면 조금만 뜸, 바닥 리바운드 순간에 맞추면 최대.
    // 비주얼(CloudPuffCluster)은 자식. 콜라이더/발사는 이 컴포넌트(root). 둘 분리.
    // 발사는 SlimeController.ForceAirborneWithVelocity (JumpPad 와 동일 경로).
    [RequireComponent(typeof(BoxCollider2D))]
    [RequireComponent(typeof(Rigidbody2D))]
    public class CloudPlatform : MonoBehaviour, IPlatformEffect
    {
        [Header("Visual")]
        public CloudDisperser disperser;   // 자식 구름 비주얼(prefab 기반). 없으면 자동 탐색.

        [Header("눌림 / 리바운드")]
        public float sinkDepth = 0.3f;     // 눌리는 깊이.
        public float sinkTime = 0.14f;     // 바닥까지 시간.
        public float launchDelay = 0.05f;  // 바닥 후 발사까지(리바운드).

        [Header("발사 (타이밍)")]
        public float baseHopSpeed = 4.5f;      // 점프 안 누르면.
        public float maxLaunchSpeed = 14f;     // 완벽 타이밍.
        public float timingWindow = 0.16f;     // 이상 시점 ± 관대함.
        public KeyCode jumpKey = KeyCode.Space;

        [Header("발사 후 구름 상승/소멸")]
        public float riseVisualHeight = 1.1f;
        public float riseVisualTime = 0.9f;

        [Header("Respawn")]
        public bool respawn = true;
        public float respawnDelay = 1.5f;

        Rigidbody2D rb;
        BoxCollider2D col;
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
            if (rb == null) rb = GetComponent<Rigidbody2D>();
            if (col == null) col = GetComponent<BoxCollider2D>();
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
            if (seqT >= sinkTime + launchDelay) DoLaunch();
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
            if (state == St.Sinking && !jumpPressed && Input.GetKeyDown(jumpKey))
            {
                jumpPressed = true;
                jumpPressT = seqT;
            }
        }

        void FixedUpdate()
        {
            if (state != St.Gone) return;
            goneT += Time.fixedDeltaTime;
            float u = Mathf.Clamp01(goneT / riseVisualTime);
            rb.MovePosition(restPos + Vector2.up * (riseVisualHeight * (1f - (1f - u) * (1f - u))));
            if (respawn && goneT >= respawnDelay) Respawn();
        }

        float SinkOffset(float t)
        {
            if (t <= sinkTime)
            {
                float u = Mathf.Clamp01(t / sinkTime);
                return -sinkDepth * (1f - (1f - u) * (1f - u));   // easeOut 하강.
            }
            float r = Mathf.Clamp01((t - sinkTime) / Mathf.Max(0.01f, launchDelay));
            return -sinkDepth * (1f - r);                          // 리바운드 상승.
        }

        void DoLaunch()
        {
            float quality = 0f;
            if (jumpPressed)
            {
                float ideal = sinkTime;    // 바닥 근처 = 최적.
                quality = Mathf.Clamp01(1f - Mathf.Abs(jumpPressT - ideal) / timingWindow);
            }
            float speed = Mathf.Lerp(baseHopSpeed, maxLaunchSpeed, quality);
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
