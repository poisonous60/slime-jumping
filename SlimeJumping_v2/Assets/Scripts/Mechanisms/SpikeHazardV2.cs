using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Effects;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// Spike v2: Pogostuck-style warp. On contact, takes override control and sweeps
    /// the slime along a 3D arc (XY eased from entry to exitPoint, Z arcing forward
    /// toward the camera at the midpoint, plus an initial bounce perpendicular to
    /// the spike). Releases at exitPoint with velocity 0.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class SpikeHazardV2 : MonoBehaviour, IExternalController
    {
        [Header("Path")]
        [Tooltip("World-space landing target. Slime arrives here with zero velocity.")]
        [SerializeField] private Transform exitPoint;

        [Tooltip("Total warp duration (seconds).")]
        [SerializeField] private float duration = 0.6f;

        [Tooltip("XY speed graph: X = normalized time (0..1), Y = normalized progress (0..1). " +
                 "Slope = speed. Steep middle + flat ends = snappy midpoint. " +
                 "Flat start + steep end = slow takeoff, fast landing, etc.")]
        [SerializeField]
        private AnimationCurve speedCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Z Depth")]
        [Tooltip("Peak Z offset applied at the arc midpoint. Negative = toward the camera in a standard URP 2D setup. " +
                 "Higher magnitude = more dramatic 3D loop.")]
        [SerializeField] private float zDepth = -2f;

        [Tooltip("Normalized Z offset over time (0..1 input, 0..1 output). Peak = 1 means full zDepth.")]
        [SerializeField]
        private AnimationCurve zArc = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 4f),
            new Keyframe(0.5f, 1f, 0f, 0f),
            new Keyframe(1f, 0f, -4f, 0f));

        [Header("Bounce on Impact")]
        [Tooltip("Pop-off distance when slime hits the spike, measured along spike's local up axis (world units). " +
                 "Stacks on top of the XY path.")]
        [SerializeField] private float bouncePower = 1.2f;

        [Tooltip("Bounce envelope (0..1 input = normalized time, 0..1 output = bounce fraction). " +
                 "Default: sharp rise, quick fall — the 'pop' of recoil.")]
        [SerializeField]
        private AnimationCurve bounceCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 8f),
            new Keyframe(0.18f, 1f, 0f, 0f),
            new Keyframe(0.55f, 0f, -1f, 0f),
            new Keyframe(1f, 0f, 0f, 0f));

        [Header("Camera Shake")]
        [Tooltip("Cinemachine impulse fired at the moment of contact. Leave null to skip shake.")]
        [SerializeField] private ImpulseEmitter impulseEmitter;

        [Tooltip("Impulse force applied to the emitter. Scales Cinemachine impulse source's default amplitude.")]
        [SerializeField] private float impulseForce = 1f;

        [Header("Re-trigger")]
        [Tooltip("Delay after release before this trigger can fire again. Prevents instant re-warp if the exit is near another spike.")]
        [SerializeField] private float cooldown = 0.3f;

        private SlimeController activeSlime;
        private Rigidbody2D activeRb;
        private Vector2 startXY;
        private Vector2 endXY;
        private Vector2 bounceDir;
        private float startZ;
        private float timer;
        private float cooldownTimer;

        void Reset()
        {
            GetComponent<Collider2D>().isTrigger = true;
        }

        void Update()
        {
            if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (activeSlime != null) return;
            if (cooldownTimer > 0f) return;

            var slime = other.GetComponentInParent<SlimeController>();
            if (slime == null) return;
            if (slime.HasOverride) return;

            if (exitPoint == null)
            {
                Debug.LogWarning($"[SpikeHazardV2] {name} has no exitPoint assigned — ignoring hit.", this);
                return;
            }

            if (impulseEmitter != null)
                impulseEmitter.Emit(impulseForce);

            slime.SetOverride(this);
        }

        public void OnTakeControl(SlimeController slime)
        {
            activeSlime = slime;
            activeRb = slime.GetComponent<Rigidbody2D>();

            startXY = activeRb.position;
            endXY = exitPoint.position;
            startZ = activeRb.transform.position.z;
            bounceDir = transform.up;

            timer = 0f;
            slime.OverrideVelocity(Vector2.zero);
        }

        public void OnReleaseControl(SlimeController slime)
        {
            if (activeRb != null)
            {
                var tr = activeRb.transform;
                var p = tr.position;
                tr.position = new Vector3(p.x, p.y, startZ);
            }

            cooldownTimer = cooldown;
            activeSlime = null;
            activeRb = null;
        }

        public void ExternalUpdate(SlimeController slime) { }

        public void ExternalFixedUpdate(SlimeController slime, float dt)
        {
            timer += dt;
            float tRaw = duration > 0.0001f ? Mathf.Clamp01(timer / duration) : 1f;

            float tPath = speedCurve.Evaluate(tRaw);
            float tZ = zArc.Evaluate(tRaw);
            float tBounce = bounceCurve.Evaluate(tRaw);

            Vector2 xy = Vector2.LerpUnclamped(startXY, endXY, tPath) + bounceDir * (bouncePower * tBounce);
            float z = startZ + zDepth * tZ;

            activeRb.position = xy;
            var tr = activeRb.transform;
            tr.position = new Vector3(xy.x, xy.y, z);

            slime.OverrideVelocity(Vector2.zero);

            if (tRaw >= 1f)
            {
                slime.ReleaseOverride(this);
            }
        }

        // --- Gizmos ---

        void OnDrawGizmos()
        {
            if (exitPoint == null) return;

            Vector3 a = transform.position;
            Vector3 b = exitPoint.position;
            float aZ = a.z;
            Vector3 bDir = transform.up;

            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.85f);
            Vector3 prev = a;
            const int steps = 32;
            for (int i = 1; i <= steps; i++)
            {
                float tRaw = i / (float)steps;
                float tPath = speedCurve != null ? speedCurve.Evaluate(tRaw) : tRaw;
                float tZ = zArc != null ? zArc.Evaluate(tRaw) : Mathf.Sin(tRaw * Mathf.PI);
                float tBounce = bounceCurve != null ? bounceCurve.Evaluate(tRaw) : 0f;

                Vector3 pos = Vector3.LerpUnclamped(a, b, tPath);
                pos += (Vector3)(bDir * (bouncePower * tBounce));
                pos.z = aZ + zDepth * tZ;
                Gizmos.DrawLine(prev, pos);
                prev = pos;
            }

            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.9f);
            Gizmos.DrawWireSphere(b, 0.2f);
            Gizmos.DrawLine(b + Vector3.left * 0.2f, b + Vector3.right * 0.2f);
            Gizmos.DrawLine(b + Vector3.up * 0.2f, b + Vector3.down * 0.2f);
        }

        void OnDrawGizmosSelected()
        {
            var col = GetComponent<Collider2D>();
            if (col == null) return;

            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.15f);
            if (col is BoxCollider2D box)
            {
                Matrix4x4 old = Gizmos.matrix;
                Gizmos.matrix = Matrix4x4.TRS(
                    transform.TransformPoint(box.offset),
                    transform.rotation, transform.lossyScale);
                Gizmos.DrawCube(Vector3.zero, box.size);
                Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.5f);
                Gizmos.DrawWireCube(Vector3.zero, box.size);
                Gizmos.matrix = old;
            }
            else if (col is CircleCollider2D circle)
            {
                Vector3 center = transform.TransformPoint(circle.offset);
                float r = circle.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y);
                Gizmos.DrawSphere(center, r);
                Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.5f);
                Gizmos.DrawWireSphere(center, r);
            }
        }
    }
}
