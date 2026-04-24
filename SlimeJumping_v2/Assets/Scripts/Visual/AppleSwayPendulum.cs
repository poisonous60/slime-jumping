using UnityEngine;

namespace SlimeJumping.Visual
{
    // Pure-code pendulum for decorative swinging bodies (e.g. an apple hanging from a branch tip).
    // The stem/pivot is THIS transform's origin — parent it to whatever should drive it (e.g. the
    // branch decoration cube) and the stem will track that parent with zero compliance. The visual
    // mesh should be placed as a child offset below the origin; this script only sets world rotation,
    // so the mesh swings around the stem like a real pendulum.
    //
    // We deliberately avoid HingeJoint2D here. Chaining two Dynamic-body hinges (branch→cube→apple)
    // makes Box2D's Baumgarte stabilization visibly spring vertically under fast base motion, which
    // reads as the apple "bouncing" up and down instead of swaying.
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

            // Read parent position from the Rigidbody2D if present — transform.position on an
            // interpolated RB lags the physics step and would inject bogus acceleration spikes.
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

            // Point-mass pendulum of length L, angle θ CCW-positive from straight-down.
            //   body offset r = (L sinθ, -L cosθ)
            //   forces (parent frame) = gravity + pseudo-force from base accel
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
            // World rotation — deliberately not local, so the mesh keeps hanging "down in world"
            // no matter how the parent (branch tip cube) rotates.
            transform.rotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg);
        }

        void OnDrawGizmos()
        {
            // Stem point — a small yellow marker at this transform's origin.
            Gizmos.color = new Color(1f, 0.9f, 0.2f);
            Gizmos.DrawSphere(transform.position, 0.05f);

            // Rope line from stem down to the configured centre-of-mass distance.
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.6f);
            Vector3 down = Application.isPlaying
                ? (Vector3)(new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * length)
                : Vector3.down * length;
            Gizmos.DrawLine(transform.position, transform.position + down);
        }
    }
}
