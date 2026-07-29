using UnityEngine;

namespace SlimeJumping.Visual
{
    // Pure-code pendulum for decorative hanging bodies. Pivot = this transform's origin; the visual
    // mesh goes in a child offset below it, since this script only writes world rotation.
    // HingeJoint2D rejected: chained Dynamic hinges make Box2D's Baumgarte stabilization spring
    // vertically under fast base motion, reading as a bounce instead of a sway.
    [DisallowMultipleComponent]
    public class AppleSwayPendulum : MonoBehaviour
    {
        [Tooltip("Distance from the pivot (this transform) to the body's centre of mass, in metres. " +
                 "Should roughly match the visual's localPosition.y magnitude.")]
        [SerializeField] float length = 0.4f;

        [SerializeField] float gravity = 9.81f;

        [Tooltip("Angular damping coefficient. Higher = swing settles faster.")]
        [SerializeField] float damping = 2.0f;

        [Tooltip("How strongly the parent's horizontal acceleration perturbs the pendulum.")]
        [SerializeField] float horizontalDriveGain = 1.0f;

        [Tooltip("How strongly the parent's vertical acceleration perturbs the pendulum (treated " +
                 "as extra effective gravity).")]
        [SerializeField] float verticalDriveGain = 1.0f;

        [Tooltip("Hard clamp on swing angle in degrees. Prevents runaway spin if the drive spikes.")]
        [SerializeField] float maxAngleDegrees = 80f;

        float angle;        // world-space swing angle in radians; 0 = hanging straight down
        float angularVel;
        Vector2 lastParentPos;
        Vector2 lastParentVel;
        Rigidbody2D parentRb;
        bool initialized;

        void OnEnable()
        {
            angle = 0f;
            angularVel = 0f;
            initialized = false;
            parentRb = transform.parent != null ? transform.parent.GetComponent<Rigidbody2D>() : null;
            ApplyRotation();
        }

        void FixedUpdate()
        {
            if (transform.parent == null) return;
            float dt = Time.fixedDeltaTime;
            if (dt <= 0f) return;

            // RB position, not transform: an interpolated RB's transform lags the step and injects bogus accel spikes.
            Vector2 parentPos = parentRb != null ? parentRb.position
                                                 : (Vector2)transform.parent.position;

            if (!initialized)
            {
                lastParentPos = parentPos;
                lastParentVel = Vector2.zero;
                initialized = true;
            }

            Vector2 parentVel = (parentPos - lastParentPos) / dt;
            Vector2 parentAccel = (parentVel - lastParentVel) / dt;
            lastParentPos = parentPos;
            lastParentVel = parentVel;

            // Point-mass pendulum, θ CCW-positive from straight-down, base accel as pseudo-force:
            //   α = -(sinθ · (g + ay·vGain) + cosθ · ax·hGain) / L - c · ω
            float sin = Mathf.Sin(angle);
            float cos = Mathf.Cos(angle);
            float angularAccel =
                -(sin * (gravity + parentAccel.y * verticalDriveGain)
                  + cos * parentAccel.x * horizontalDriveGain) / Mathf.Max(length, 0.0001f)
                - damping * angularVel;

            angularVel += angularAccel * dt;
            angle += angularVel * dt;

            float maxRad = maxAngleDegrees * Mathf.Deg2Rad;
            if (angle > maxRad)  { angle = maxRad;  if (angularVel > 0f) angularVel = 0f; }
            if (angle < -maxRad) { angle = -maxRad; if (angularVel < 0f) angularVel = 0f; }

            ApplyRotation();
        }

        void ApplyRotation()
        {
            // World, not local: the mesh must keep hanging down in world however the parent rotates.
            transform.rotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.9f, 0.2f);
            Gizmos.DrawSphere(transform.position, 0.05f);

            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.6f);
            Vector3 down = Application.isPlaying
                ? (Vector3)(new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * length)
                : Vector3.down * length;
            Gizmos.DrawLine(transform.position, transform.position + down);
        }
    }
}
