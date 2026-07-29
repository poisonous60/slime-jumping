using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Physics;

namespace SlimeJumping.Platforms
{
    [RequireComponent(typeof(Rigidbody2D))]
    [DefaultExecutionOrder(-200)]
    public class PlatformCarrier : MonoBehaviour
    {
        Rigidbody2D rb;
        SlimeController slime;
        PlatformMotor currentPlatform;
        // Kept so the pop targets the same instance even if the platform swaps its field mid-attach.
        SurfaceMaterial pushedSurfaceMaterial;

        [SerializeField] float slimeMass = 1f;

        public bool IsOnPlatform => currentPlatform != null;
        /// <summary>The motor the rider is currently attached to, or null.</summary>
        public PlatformMotor CurrentPlatform => currentPlatform;

        void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            slime = GetComponent<SlimeController>();
        }

        Vector2 RiderVelocity => slime != null ? slime.Velocity : Vector2.zero;

        public void Attach(Collider2D surfaceCollider)
        {
            PlatformMotor nextPlatform = surfaceCollider
                ? surfaceCollider.GetComponentInParent<PlatformMotor>()
                : null;

            if (nextPlatform == currentPlatform) return;

            if (currentPlatform != null) currentPlatform.RemoveRider();
            // Pop before taking the new platform, else chained hops leak overrides onto the stack.
            PopSurfaceOverride();

            currentPlatform = nextPlatform;

            if (currentPlatform != null)
            {
                PushSurfaceOverride(currentPlatform.OverrideSurfaceMaterial);
                currentPlatform.ApplyRiderInfluence(rb.position, slimeMass, RiderVelocity, RiderIsCharging);
            }
        }

        public void Detach()
        {
            if (currentPlatform != null) currentPlatform.RemoveRider();
            PopSurfaceOverride();
            currentPlatform = null;
        }

        void PushSurfaceOverride(SurfaceMaterial mat)
        {
            if (mat == null || slime == null) return;
            slime.PushSurfaceMaterialOverride(mat);
            pushedSurfaceMaterial = mat;
        }

        void PopSurfaceOverride()
        {
            if (pushedSurfaceMaterial == null || slime == null) { pushedSurfaceMaterial = null; return; }
            slime.PopSurfaceMaterialOverride(pushedSurfaceMaterial);
            pushedSurfaceMaterial = null;
        }

        void FixedUpdate()
        {
            if (currentPlatform == null) return;
            currentPlatform.ApplyRiderInfluence(rb.position, slimeMass, RiderVelocity, RiderIsCharging);
        }

        bool RiderIsCharging => slime != null && slime.IsCharging;

        /// <summary>Platform velocity including rotational tangential contribution.</summary>
        public Vector2 GetEffectiveVelocity()
        {
            if (currentPlatform == null) return Vector2.zero;

            Vector2 vel = currentPlatform.Velocity;

            if (Mathf.Abs(currentPlatform.AngularVelocity) > 0.001f)
            {
                float radPerSec = currentPlatform.AngularVelocity * Mathf.Deg2Rad;
                Vector2 arm = rb.position - (Vector2)currentPlatform.transform.position;
                vel += new Vector2(-arm.y, arm.x) * radPerSec;
            }

            return vel;
        }

        /// <summary>Effective velocity scaled by VelocityTransferScale; use only at launch/hop.</summary>
        public Vector2 GetLaunchVelocity()
        {
            if (currentPlatform == null) return Vector2.zero;
            return GetEffectiveVelocity() * currentPlatform.VelocityTransferScale;
        }

        /// <summary>Carry the rider as a position offset (not linearVelocity) — velocity baking
        /// gives dynamic/kinematic integration jitter on moving platforms.</summary>
        public void ApplyPositionCarry(float carryScale)
        {
            if (currentPlatform == null || carryScale == 0f) return;

            Vector2 delta = currentPlatform.GetRiderCarryDelta(rb.position, Time.fixedDeltaTime);
            rb.MovePosition(rb.position + delta * carryScale);
        }
    }
}
