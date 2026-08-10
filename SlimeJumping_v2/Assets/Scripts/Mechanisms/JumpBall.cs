using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    /// <summary>Trigger bounce ball; launch speed = max(fixedJumpSpeed, incomingSpeed * percent + bonus), with an optional consume/respawn cycle.</summary>
    [RequireComponent(typeof(Collider2D))]
    public class JumpBall : MonoBehaviour
    {
        [Header("Jump")]
        [Tooltip("Minimum fixed jump speed. Used when the slime arrives slowly.")]
        [SerializeField] private float fixedJumpSpeed = 18f;
        [Tooltip("Fraction of incoming slime speed retained on bounce.")]
        [Range(0f, 2f)]
        [SerializeField] private float incomingSpeedPercent = 0.7f;
        [Tooltip("Extra speed added on top of the scaled incoming speed.")]
        [SerializeField] private float bonusSpeed = 5f;
        [Tooltip("Seconds before the ball can bounce again (only used when !consumable).")]
        [SerializeField] private float cooldown = 0.3f;
        [Tooltip("Keep horizontal (perpendicular-to-up) velocity component on bounce.")]
        [SerializeField] private bool preserveHorizontal = true;

        [Header("Visual Bob")]
        [Tooltip("Visual child transform. Driven by localPosition sine wave — must NOT carry the collider.")]
        [SerializeField] private Transform visual;
        [Tooltip("Peak-to-peak bob distance (local units).")]
        [SerializeField] private float bobAmplitude = 0.2f;
        [Tooltip("Bob cycles per second.")]
        [SerializeField] private float bobFrequency = 0.7f;
        [Tooltip("Randomize starting phase in Awake so placed balls don't bob in sync.")]
        [SerializeField] private bool randomizePhase = true;

        [Header("Consume / Respawn")]
        [Tooltip("If true, the ball disappears on bounce and respawns after respawnDelay.")]
        [SerializeField] private bool consumable = true;
        [Tooltip("Seconds the consume (shrink) animation plays.")]
        [SerializeField] private float consumeDuration = 0.25f;
        [Tooltip("Seconds the ball stays invisible before the respawn animation starts.")]
        [SerializeField] private float respawnDelay = 2.5f;
        [Tooltip("Seconds the respawn (grow) animation plays. Collider is still off while this plays.")]
        [SerializeField] private float respawnDuration = 0.3f;
        [Tooltip("Visual scale over consume — X: 0→1 time, Y: scale multiplier (1=full, 0=gone).")]
        [SerializeField] private AnimationCurve consumeScaleCurve =
            new AnimationCurve(new Keyframe(0f, 1f, 0f, 0f), new Keyframe(1f, 0f, -2f, 0f));
        [Tooltip("Visual scale over respawn — X: 0→1 time, Y: scale multiplier (0=gone, 1=full).")]
        [SerializeField] private AnimationCurve respawnScaleCurve =
            new AnimationCurve(new Keyframe(0f, 0f, 0f, 2f), new Keyframe(1f, 1f, 0f, 0f));

        [Header("SFX")]
        [SerializeField] private SlimeJumping.Audio.SoundDef consumeSound;
        [SerializeField] private SlimeJumping.Audio.SoundDef respawnSound;

        private enum State { Idle, Consuming, Gone, Respawning }

        private State state = State.Idle;
        private float stateTimer;
        private float cooldownTimer;

        private Collider2D ownCollider;
        private Vector3 visualBaseLocalPos;
        private Vector3 visualBaseLocalScale;
        private bool visualCached;
        private float bobPhase;

        void Reset()
        {
            var col = GetComponent<Collider2D>();
            if (col != null) col.isTrigger = true;
        }

        void Awake()
        {
            ownCollider = GetComponent<Collider2D>();
            if (visual != null)
            {
                visualBaseLocalPos = visual.localPosition;
                visualBaseLocalScale = visual.localScale;
                visualCached = true;
            }
            bobPhase = randomizePhase ? Random.value * Mathf.PI * 2f : 0f;
        }

        void Update()
        {
            if (SlimeJumping.Effects.WorldTimeFreezeService.IsFrozen) return;
            if (!visualCached) return;

            switch (state)
            {
                case State.Idle:
                {
                    float y = Mathf.Sin(Time.time * bobFrequency * Mathf.PI * 2f + bobPhase) * (bobAmplitude * 0.5f);
                    visual.localPosition = visualBaseLocalPos + Vector3.up * y;
                    break;
                }
                case State.Consuming:
                {
                    float t = 1f - stateTimer / Mathf.Max(0.0001f, consumeDuration);
                    visual.localScale = visualBaseLocalScale * consumeScaleCurve.Evaluate(Mathf.Clamp01(t));
                    break;
                }
                case State.Respawning:
                {
                    float t = 1f - stateTimer / Mathf.Max(0.0001f, respawnDuration);
                    visual.localScale = visualBaseLocalScale * respawnScaleCurve.Evaluate(Mathf.Clamp01(t));
                    break;
                }
                // Gone: visual GameObject is disabled, nothing to update.
            }
        }

        void FixedUpdate()
        {
            if (cooldownTimer > 0f)
                cooldownTimer -= Time.fixedDeltaTime;

            if (state == State.Consuming || state == State.Gone || state == State.Respawning)
            {
                stateTimer -= Time.fixedDeltaTime;
                if (stateTimer <= 0f)
                {
                    switch (state)
                    {
                        case State.Consuming: EnterGone(); break;
                        case State.Gone:      EnterRespawning(); break;
                        case State.Respawning:EnterIdle(); break;
                    }
                }
            }
        }

        void OnTriggerEnter2D(Collider2D other) => TryBounce(other);
        void OnTriggerStay2D(Collider2D other) => TryBounce(other);

        private void TryBounce(Collider2D other)
        {
            if (state != State.Idle) return;
            if (!consumable && cooldownTimer > 0f) return;

            var slime = other.GetComponentInParent<SlimeController>();
            if (slime == null) return;

            Vector2 up = ((Vector2)transform.up).normalized;
            Vector2 incomingVel = slime.Velocity;
            float incomingSpeed = incomingVel.magnitude;
            float scaled = incomingSpeed * incomingSpeedPercent + bonusSpeed;
            float jumpSpeed = Mathf.Max(fixedJumpSpeed, scaled);

            Vector2 newVel = up * jumpSpeed;
            if (preserveHorizontal)
            {
                Vector2 horizontalDir = new Vector2(up.y, -up.x);
                newVel += horizontalDir * Vector2.Dot(incomingVel, horizontalDir);
            }

            slime.ForceAirborneWithVelocity(newVel);

            if (consumable) EnterConsuming();
            else cooldownTimer = cooldown;
        }

        // --- State transitions ---

        private void EnterConsuming()
        {
            state = State.Consuming;
            stateTimer = Mathf.Max(0.0001f, consumeDuration);
            if (ownCollider != null) ownCollider.enabled = false;
            PlaySfx(consumeSound);
        }

        private void EnterGone()
        {
            state = State.Gone;
            stateTimer = Mathf.Max(0f, respawnDelay);
            if (visualCached) visual.gameObject.SetActive(false);
        }

        private void EnterRespawning()
        {
            state = State.Respawning;
            stateTimer = Mathf.Max(0.0001f, respawnDuration);
            if (visualCached)
            {
                visual.gameObject.SetActive(true);
                visual.localPosition = visualBaseLocalPos;
                visual.localScale = visualBaseLocalScale * respawnScaleCurve.Evaluate(0f);
            }
            PlaySfx(respawnSound);
        }

        private void EnterIdle()
        {
            state = State.Idle;
            stateTimer = 0f;
            if (ownCollider != null) ownCollider.enabled = true;
            if (visualCached)
            {
                visual.gameObject.SetActive(true);
                visual.localScale = visualBaseLocalScale;
            }
        }

        private void PlaySfx(SlimeJumping.Audio.SoundDef def)
        {
            SoundService.Play(def, transform.position);
        }

        /// <summary>JumpBallAudioManager broadcasts a shared set with this.</summary>
        public void ApplyAudioSettings(SlimeJumping.Audio.SoundDef consume, SlimeJumping.Audio.SoundDef respawn)
        {
            consumeSound = consume;
            respawnSound = respawn;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.6f);
            Vector3 center = transform.position;
            Vector3 up = ((Vector2)transform.up).normalized;
            float len = Mathf.Clamp(fixedJumpSpeed * 0.05f, 0.5f, 3f);

            Gizmos.DrawLine(center, center + up * len);
            Vector3 perp = Vector3.Cross(up, Vector3.forward).normalized;
            Vector3 tip = center + up * len;
            Gizmos.DrawLine(tip, tip - up * 0.25f + perp * 0.12f);
            Gizmos.DrawLine(tip, tip - up * 0.25f - perp * 0.12f);
        }
    }
}
