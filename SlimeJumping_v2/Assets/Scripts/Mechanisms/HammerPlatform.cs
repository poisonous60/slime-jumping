using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    /// <summary>Swinging hammer: orbits head+pole around a fixed world pivot between centerAngle ± maxAngle; on contact launches the slime along the head velocity at the hit point (omega x arm).</summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [DefaultExecutionOrder(-100)]
    public class HammerPlatform : MonoBehaviour
    {
        [Header("Pivot")]
        [Tooltip("Swing pivot, local-space offset from the hammer root at rest. Default points to the top of the pole. The head orbits this point.")]
        [SerializeField] private Vector2 pivotLocalOffset = new Vector2(0f, 5.24f);

        [Header("Swing — Angle")]
        [Tooltip("Half-range of the swing, degrees. Head swings between centerAngle - maxAngle and centerAngle + maxAngle.")]
        [SerializeField, Range(0f, 175f)] private float maxAngle = 70f;
        [Tooltip("Center of the swing, degrees, relative to the authored rest pose (head hanging down). 0 = symmetric about straight-down.")]
        [SerializeField] private float centerAngle = 0f;

        [Header("Swing — Speed Profile")]
        [Tooltip("ON = true pendulum motion (sinusoidal SHM): fastest at the bottom, max acceleration at the extremes " +
                 "so it whips away from the top the way gravity would — the natural hammer feel. Ignores the curve below. " +
                 "OFF = use the custom speedProfile curve instead.")]
        [SerializeField] private bool usePendulumMotion = true;
        [Tooltip("Pendulum mode only. Extra dwell at the swing extremes. 0 = pure sinusoidal pendulum. " +
                 "Higher = head lingers longer at the top ends and whips faster through the bottom. " +
                 "~1-3 = subtle hang, 4+ = strong hang.")]
        [SerializeField, Range(0f, 8f)] private float pendulumDwell = 0f;
        [Tooltip("Seconds for one full back-and-forth (extreme -> other extreme -> back).")]
        [SerializeField, Min(0.05f)] private float period = 2f;
        [Tooltip("Used only when Use Pendulum Motion is OFF. X = phase across a half-sweep (0..1). " +
                 "Y = normalized angle (0 = one extreme, 1 = the other). " +
                 "Flat at the ends => hammer stops at the angle extremes. Steep in the middle => fastest at the lowest point. " +
                 "Note: EaseInOut (the old default) has ZERO acceleration at the ends, so the head dwells/floats at the top " +
                 "(robotic). For a pendulum feel use Pendulum Motion above, or shape a near-cosine curve.")]
        [SerializeField] private AnimationCurve speedProfile = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Launch")]
        [Tooltip("Scales launch strength. Final speed = headSpeedAtHitPoint * launchMultiplier.")]
        [SerializeField, Min(0f)] private float launchMultiplier = 1f;
        [Tooltip("Hard cap on launch speed (m/s).")]
        [SerializeField, Min(0f)] private float maxLaunchSpeed = 80f;
        [Tooltip("0 = launch purely along the head's swing direction (tangent). 1 = blend fully toward radial-outward from the pivot.")]
        [SerializeField, Range(0f, 1f)] private float outwardBias = 0.15f;
        [Tooltip("Seconds before the same slime can be struck again.")]
        [SerializeField, Min(0f)] private float hitCooldown = 0.25f;
        [Tooltip("Below this head speed at the hit point, the contact is ignored (no launch, no cooldown spent). " +
                 "Stops the near-stationary head at the swing extremes from nudging the slime and eating the cooldown " +
                 "before the fast swing-back can land a real hit.")]
        [SerializeField, Min(0f)] private float minLaunchSpeed = 0.5f;

        [Header("Size")]
        [Tooltip("Visual scale multiplier for the head mesh (hammer_head). Also scales the strike collider.")]
        [SerializeField, Min(0.01f)] private float headSizeMultiplier = 1f;
        [Tooltip("Visual scale multiplier for the pole mesh (hammer_lod).")]
        [SerializeField, Min(0.01f)] private float poleSizeMultiplier = 1f;

        [Header("References (auto-wired by setup)")]
        [SerializeField] private Transform headVisual;   // hammer_head
        [SerializeField] private Transform poleVisual;    // hammer_lod
        [SerializeField] private BoxCollider2D strikeCollider;
        [SerializeField] private Vector3 headBaseScale = Vector3.one;
        [SerializeField] private Vector3 poleBaseScale = Vector3.one;
        [SerializeField] private Vector2 strikeBaseSize = Vector2.one;
        [SerializeField] private Vector2 strikeBaseOffset = Vector2.zero;

        private Rigidbody2D rb;
        private Vector2 pivotWorld;
        private Vector2 armRest;       // pivot -> head at rest
        private float baseRotationDeg;
        private float timeAccum;
        private float prevAngleDeg;
        private float angularVelRad;   // head angular velocity about pivot, rad/s
        private float cooldownTimer;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            baseRotationDeg = rb.rotation;
            Vector2 basePos = rb.position;
            pivotWorld = basePos + (Vector2)(Quaternion.Euler(0f, 0f, baseRotationDeg) * pivotLocalOffset);
            armRest = basePos - pivotWorld;

            ApplySizes();

            timeAccum = 0f;
            prevAngleDeg = ComputeSwingAngle(0f);
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            timeAccum += dt;

            float swingDeg = ComputeSwingAngle(timeAccum);

            Quaternion q = Quaternion.Euler(0f, 0f, swingDeg);
            Vector2 newPos = pivotWorld + (Vector2)(q * armRest);
            rb.MovePosition(newPos);
            rb.MoveRotation(baseRotationDeg + swingDeg);

            float dAngDeg = Mathf.DeltaAngle(prevAngleDeg, swingDeg);
            angularVelRad = (dAngDeg * Mathf.Deg2Rad) / dt;
            prevAngleDeg = swingDeg;

            if (cooldownTimer > 0f) cooldownTimer -= dt;
        }

        /// <summary>Swing angle in degrees relative to rest; bottom of the swing = authored rest pose.</summary>
        private float ComputeSwingAngle(float t)
        {
            if (usePendulumMotion)
            {
                // Small-angle SHM; cos gives max acceleration at the extremes, not zero.
                float w = period > 0.0001f ? (2f * Mathf.PI / period) : 0f;
                float c = Mathf.Cos(w * t);
                // tanh soft-clip adds dwell at the extremes; dwell->0 reduces to pure cosine.
                float shaped = pendulumDwell > 0.001f
                    ? (float)(System.Math.Tanh(pendulumDwell * c) / System.Math.Tanh(pendulumDwell))
                    : c;
                return centerAngle - maxAngle * shaped;
            }

            float cyc = period > 0.0001f ? Mathf.Repeat(t, period) / period : 0f; // 0..1
            float phase = cyc < 0.5f ? cyc / 0.5f : 1f - (cyc - 0.5f) / 0.5f;      // 0..1..0
            float n = speedProfile.Evaluate(phase);                                // 0..1
            return centerAngle + Mathf.Lerp(-maxAngle, maxAngle, n);
        }

        private void OnTriggerEnter2D(Collider2D other) => TryHit(other);
        private void OnTriggerStay2D(Collider2D other) => TryHit(other);

        private void TryHit(Collider2D other)
        {
            if (cooldownTimer > 0f) return;

            var slime = other.GetComponentInParent<SlimeController>();
            if (slime == null) return;
            if (slime.HasOverride) return; // don't yank the slime during a cutscene / external override

            Vector2 hit = slime.Position;
            Vector2 arm = hit - pivotWorld;
            if (arm.sqrMagnitude < 0.0001f) arm = armRest;

            // Head linear velocity at the hit point: omega(z) x r.
            Vector2 tangent = angularVelRad * new Vector2(-arm.y, arm.x);
            float headSpeed = tangent.magnitude;
            if (headSpeed < minLaunchSpeed) return; // ignore near-stationary contact; don't spend the cooldown

            Vector2 dir;
            if (headSpeed > 0.0001f)
                dir = tangent / headSpeed;
            else
                dir = arm.sqrMagnitude > 0.0001f ? arm.normalized : Vector2.up;

            if (outwardBias > 0f && arm.sqrMagnitude > 0.0001f)
                dir = Vector2.Lerp(dir, arm.normalized, outwardBias).normalized;

            float speed = Mathf.Min(headSpeed * launchMultiplier, maxLaunchSpeed);
            slime.ForceAirborneWithVelocity(dir * speed);

            cooldownTimer = hitCooldown;
        }

        private void ApplySizes()
        {
            if (headVisual != null)
                headVisual.localScale = headBaseScale * headSizeMultiplier;
            if (poleVisual != null)
                poleVisual.localScale = poleBaseScale * poleSizeMultiplier;
            if (strikeCollider != null)
            {
                strikeCollider.size = strikeBaseSize * headSizeMultiplier;
                strikeCollider.offset = strikeBaseOffset;
            }
        }

        private void OnValidate()
        {
            if (!Application.isPlaying) ApplySizes();
        }

        private void OnDrawGizmosSelected()
        {
            Vector2 basePos = Application.isPlaying ? (Vector2)(pivotWorld + armRest) : (Vector2)transform.position;
            float rot = Application.isPlaying ? baseRotationDeg : transform.eulerAngles.z;
            Vector2 pivot = Application.isPlaying
                ? pivotWorld
                : basePos + (Vector2)(Quaternion.Euler(0f, 0f, rot) * pivotLocalOffset);

            float armLen = headVisual != null ? Vector2.Distance(pivot, headVisual.position)
                                              : Mathf.Max(0.1f, pivotLocalOffset.magnitude);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(pivot, armLen * 0.06f);

            Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.9f);
            Vector2 a = ArmDir(rot + centerAngle - maxAngle) * armLen + pivot;
            Vector2 b = ArmDir(rot + centerAngle + maxAngle) * armLen + pivot;
            Gizmos.DrawLine(pivot, a);
            Gizmos.DrawLine(pivot, b);

            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.5f);
            const int seg = 16;
            Vector2 prev = a;
            for (int i = 1; i <= seg; i++)
            {
                float ang = rot + centerAngle + Mathf.Lerp(-maxAngle, maxAngle, i / (float)seg);
                Vector2 cur = ArmDir(ang) * armLen + pivot;
                Gizmos.DrawLine(prev, cur);
                prev = cur;
            }
        }

        // Rest pose hangs straight down (local -Y), so an arm at swing-angle 0 points down.
        private static Vector2 ArmDir(float angleDeg)
            => (Vector2)(Quaternion.Euler(0f, 0f, angleDeg) * Vector3.down);
    }
}
