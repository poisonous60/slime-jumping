using UnityEngine;

namespace SlimeJumping.Platforms
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlatformCarrier : MonoBehaviour
    {
        Rigidbody2D rb;
        PlatformMotor currentPlatform;

        public bool IsOnPlatform => currentPlatform != null;

        void Awake() => rb = GetComponent<Rigidbody2D>();

        public void Attach(Collider2D surfaceCollider)
        {
            currentPlatform = surfaceCollider != null
                ? surfaceCollider.GetComponentInParent<PlatformMotor>()
                : null;
        }

        public void Detach() => currentPlatform = null;

        /// <summary>
        /// Platform velocity including rotational tangential contribution.
        /// Add to linearVelocity for smooth interpolation.
        /// </summary>
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
    }
}
