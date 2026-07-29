using UnityEngine;

namespace SlimeJumping.Platforms
{
    public class PendulumPlatform : PlatformMotor
    {
        [SerializeField] Vector2 localPivot = new Vector2(0f, 3f);
        [SerializeField, Min(0.01f)] float ropeLength = 3f;
        [SerializeField] float gravity = 10f;
        [SerializeField] float damping = 0.02f;
        [SerializeField] float startAngleDeg = 30f;

        [Header("Rider Inertia")]
        [SerializeField] float landingImpulseScale = 1f;
        [SerializeField] float riderPosAngAccelScale = 0.1f;

        [Header("Cosmetic Board Tilt (spring-damper on top of rope swing)")]
        [Tooltip("Visual-only tilt applied on top of the rope swing angle. " +
                 "Represents the board flexing under the rider's weight. " +
                 "Set tiltStiffness = 0 to disable.")]
        [SerializeField] float maxTiltAngle = 15f;
        [SerializeField] float tiltStiffness = 20f;
        [SerializeField] float tiltDamping = 5f;

        [Tooltip("Fraction of the rope-swing angle visually applied to the board. " +
                 "1.0 = board tilts fully with rope (physical pendulum). " +
                 "0.3 = board stays relatively level like a swing seat, even as rope swings wide.")]
        [SerializeField, Range(0f, 1f)] float swayRatio = 1f;

        [Header("Rope Visual")]
        [Tooltip("LineRenderers drawn from pivot to board. Each rope's Z is taken from its own " +
                 "transform, so you can stack ropes at different Z depths. Uses world-space positions.")]
        [SerializeField] LineRenderer[] ropes;

        Vector2 worldPivot;
        float angle;
        float angularVel;

        float pendingRiderAngAccel;
        bool riderAttached;

        float halfWidth;
        float currentTilt;
        float targetTilt;
        float tiltVelocity;

        protected override void Awake()
        {
            base.Awake();

            worldPivot = rb.position + localPivot;

            angle = startAngleDeg * Mathf.Deg2Rad;
            angularVel = 0f;

            var box = GetComponentInChildren<BoxCollider2D>();
            halfWidth = box != null ? box.size.x * box.transform.lossyScale.x * 0.5f : 1f;
        }

        public override void ApplyRiderInfluence(Vector2 riderWorldPos, float mass, Vector2 riderVelocity, bool isCharging)
        {
            if (!riderAttached)
            {
                angularVel += (riderVelocity.x / ropeLength) * landingImpulseScale;
                riderAttached = true;
            }

            // rope torque arm is world-horizontal from the pivot, NOT board-local: board-local
            // rotates with the board and pumps energy in when the rider sticks to the side face.
            float armX = riderWorldPos.x - worldPivot.x;
            pendingRiderAngAccel += -gravity * mass * armX * riderPosAngAccelScale;

            // cosmetic tilt is board-local: it asks which end of the plank the rider stands on.
            float localX = transform.InverseTransformPoint(riderWorldPos).x;
            float ratio = Mathf.Clamp(localX / halfWidth, -1f, 1f);
            targetTilt = Mathf.Clamp(-ratio * maxTiltAngle * mass, -maxTiltAngle, maxTiltAngle);
        }

        public override void RemoveRider()
        {
            riderAttached = false;
            targetTilt = 0f;
        }

        protected override (Vector2, float) ComputeMotion(float dt)
        {
            // rope swing physics (radians)
            float angularAccel = -(gravity / ropeLength) * Mathf.Sin(angle)
                                  - damping * angularVel
                                  + pendingRiderAngAccel;
            angularVel += angularAccel * dt;
            angle += angularVel * dt;
            pendingRiderAngAccel = 0f;

            // cosmetic board tilt (degrees) — spring-damper toward targetTilt
            float springForce = (targetTilt - currentTilt) * tiltStiffness;
            float dampingForce = -tiltVelocity * tiltDamping;
            tiltVelocity += (springForce + dampingForce) * dt;
            currentTilt += tiltVelocity * dt;

            Vector2 offset = new Vector2(
                Mathf.Sin(angle) * ropeLength,
                -Mathf.Cos(angle) * ropeLength
            );

            float rotDeg = angle * Mathf.Rad2Deg * swayRatio + currentTilt;

            return (worldPivot + offset, rotDeg);
        }

        void LateUpdate()
        {
            if (ropes == null) return;

            Vector2 boardPos = transform.position;
            for (int i = 0; i < ropes.Length; i++)
            {
                var rope = ropes[i];
                if (rope == null) continue;

                rope.useWorldSpace = true;
                rope.positionCount = 2;
                float z = rope.transform.position.z;
                rope.SetPosition(0, new Vector3(worldPivot.x, worldPivot.y, z));
                rope.SetPosition(1, new Vector3(boardPos.x, boardPos.y, z));
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Vector2 origin = rb != null ? rb.position : (Vector2)transform.position;
            Vector2 pivotPos = Application.isPlaying ? worldPivot : origin + localPivot;
            Gizmos.DrawLine(pivotPos, origin);
            Gizmos.DrawSphere(pivotPos, 0.1f);
        }
    }
}
