using UnityEngine;

namespace SlimeJumping.Platforms
{
    [RequireComponent(typeof(Rigidbody2D))]
    [DefaultExecutionOrder(-100)]
    public abstract class PlatformMotor : MonoBehaviour
    {
        protected Rigidbody2D rb;

        public Vector2 DeltaPosition { get; private set; }
        public float DeltaRotation { get; private set; }
        public Vector2 Velocity { get; private set; }
        public float AngularVelocity { get; private set; }

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
        }

        protected abstract (Vector2 position, float rotation) ComputeMotion(float dt);

        public virtual void ApplyRiderInfluence(Vector2 riderWorldPos, float riderMass, Vector2 riderVelocity, bool isCharging) { }
        public virtual void RemoveRider() { }
    }
}
