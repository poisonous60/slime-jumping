using UnityEngine;
using SlimeJumping.Physics;

namespace SlimeJumping.Platforms
{
    [RequireComponent(typeof(Rigidbody2D))]
    [DefaultExecutionOrder(-100)]
    public abstract class PlatformMotor : MonoBehaviour
    {
        protected Rigidbody2D rb;

        public Vector2 DeltaPosition { get; private set; }
        public float DeltaRotation { get; private set; }

        // virtual so subclasses whose motion isn't captured by rigid TRS deltas
        // (e.g. a mesh-deforming branch) can report effective surface velocity
        // for PlatformCarrier. Base set-path in FixedUpdate still runs; the
        // override just shadows the getter.
        public virtual Vector2 Velocity { get; protected set; }
        public virtual float AngularVelocity { get; protected set; }

        Vector2 prevPosition;
        float prevRotation;

        protected virtual void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            prevPosition = rb.position;
            prevRotation = rb.rotation;
        }

        // Re-seed the delta baseline on every activation. A motor that slept (SetActive false)
        // skips FixedUpdate while its subclass state may still advance or be reset, so the first
        // post-wake DeltaPosition would be the whole gap — a one-frame Velocity spike that
        // PlatformCarrier would hand to a rider.
        protected virtual void OnEnable()
        {
            if (rb == null) return;
            prevPosition = rb.position;
            prevRotation = rb.rotation;
        }

        void FixedUpdate()
        {
            var (targetPos, targetRot) = ComputeMotion(Time.fixedDeltaTime);

            rb.MovePosition(targetPos);
            rb.MoveRotation(targetRot);

            DeltaPosition = targetPos - prevPosition;
            DeltaRotation = targetRot - prevRotation;
            Velocity = DeltaPosition / Time.fixedDeltaTime;
            AngularVelocity = DeltaRotation / Time.fixedDeltaTime;

            prevPosition = targetPos;
            prevRotation = targetRot;

            OnAfterMotion(Time.fixedDeltaTime);
        }

        protected abstract (Vector2 position, float rotation) ComputeMotion(float dt);

        // Hook for subclasses that drive child transforms (e.g. multi-segment chains)
        // after the base rigidbody has been moved. Default no-op.
        protected virtual void OnAfterMotion(float dt) { }

        public virtual float VelocityTransferScale => 1f;

        public virtual Vector2 GetRiderCarryDelta(Vector2 riderWorldPos, float dt)
        {
            Vector2 delta = DeltaPosition;

            if (Mathf.Abs(DeltaRotation) > 0.001f)
            {
                float rad = DeltaRotation * Mathf.Deg2Rad;
                float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
                Vector2 arm = riderWorldPos - (Vector2)transform.position;
                Vector2 rotatedArm = new Vector2(arm.x * c - arm.y * s, arm.x * s + arm.y * c);
                delta += rotatedArm - arm;
            }

            return delta;
        }

        // SurfaceMaterial pushed onto the slime's override stack while attached to this
        // platform. PlatformCarrier handles the Push/Pop on Attach/Detach — mirrors the
        // WaterCurrentZone trigger pattern but driven by carrier attach state. Null = no
        // override (slime keeps the host SurfaceTag's material).
        public virtual SurfaceMaterial OverrideSurfaceMaterial => null;

        public virtual void ApplyRiderInfluence(Vector2 riderWorldPos, float riderMass, Vector2 riderVelocity, bool isCharging) { }
        public virtual void RemoveRider() { }
    }
}
