using UnityEngine;

namespace SlimeJumping.Platforms
{
    public class PushDoor : PlatformMotor
    {
        [Header("Push (while slime is touching)")]
        [SerializeField] float pushAccel = 15f;
        [SerializeField] float pushMaxSpeed = 3f;

        [Header("Return (after slime leaves)")]
        [SerializeField] float returnStiffness = 10f;
        [SerializeField] float returnDamping = 3f;

        [Header("Limits (degrees)")]
        [SerializeField, Range(1f, 90f)] float openAngleDeg = 60f;

        [Header("One-Way")]
        [SerializeField] bool oneWay;
        [SerializeField] int oneWayDirection = 1;

        [Header("Visual")]
        [SerializeField] Vector3 hingeOffset = new Vector3(0f, 0f, 2f);
        [SerializeField] Vector3 rotationAxis = Vector3.up;
        [SerializeField] Transform visualTransform;

        [Header("Open State")]
        [SerializeField] float minOpenTime = 1f;
        [SerializeField] LayerMask reactivateCheckLayers;

        float doorDepth => Mathf.Max(transform.position.z + hingeOffset.z, 0.01f);
        float openThreshold => doorDepth * Mathf.Sin(openAngleDeg * Mathf.Deg2Rad);
        Vector2 cachedRight;

        Vector2 basePosition;
        float baseRotation;

        float currentX;
        float xVelocity;
        float pushDirection; // -1, 0, or +1
        float openTimer;

        BoxCollider2D doorCollider;
        Collider2D[] overlapResults = new Collider2D[4];

        protected override void Awake()
        {
            base.Awake();
            basePosition = rb.position;
            baseRotation = rb.rotation;
            doorCollider = GetComponent<BoxCollider2D>();
            CacheConstants();
        }

        void CacheConstants()
        {
            float rad = baseRotation * Mathf.Deg2Rad;
            cachedRight = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        }

        // Mass-independent push by design: any slime pushes equally
        public override void ApplyRiderInfluence(Vector2 riderWorldPos, float riderMass, Vector2 riderVelocity, bool isCharging)
        {
            Vector2 toRider = riderWorldPos - rb.position;
            float side = Mathf.Sign(Vector2.Dot(toRider, cachedRight));
            pushDirection = -side; // door pushes away from slime
        }

        public override void RemoveRider()
        {
            pushDirection = 0f;
        }

        protected override (Vector2, float) ComputeMotion(float dt)
        {
            if (openTimer > 0f)
                openTimer -= dt;

            bool isPushing = pushDirection != 0f;

            if (isPushing)
            {
                xVelocity += pushDirection * pushAccel * dt;
                xVelocity = Mathf.Clamp(xVelocity, -pushMaxSpeed, pushMaxSpeed);
            }
            else
            {
                float force = -returnStiffness * currentX - returnDamping * xVelocity;
                xVelocity += force * dt;
            }

            currentX += xVelocity * dt;

            if (oneWay)
            {
                float sign = Mathf.Sign(oneWayDirection);
                if (sign > 0 && currentX < 0f) { currentX = 0f; xVelocity = Mathf.Max(xVelocity, 0f); }
                if (sign < 0 && currentX > 0f) { currentX = 0f; xVelocity = Mathf.Min(xVelocity, 0f); }
            }

            UpdateColliderState();

            return (basePosition + cachedRight * currentX, baseRotation);
        }

        void UpdateColliderState()
        {
            if (doorCollider == null) return;

            bool shouldBeOpen = Mathf.Abs(currentX) >= openThreshold;

            if (shouldBeOpen && doorCollider.enabled)
            {
                doorCollider.enabled = false;
                openTimer = minOpenTime;
            }
            else if (!doorCollider.enabled && openTimer <= 0f && !shouldBeOpen)
            {
                doorCollider.enabled = true;

                ContactFilter2D filter = new ContactFilter2D
                {
                    useLayerMask = true,
                    layerMask = reactivateCheckLayers,
                    useTriggers = false
                };

                if (doorCollider.Overlap(filter, overlapResults) > 0)
                {
                    doorCollider.enabled = false;
                    openTimer = 0.1f; // retry delay to prevent per-frame flicker
                }
            }
        }

        void LateUpdate()
        {
            if (visualTransform == null) return;

            // Compensate for collider movement so hinge stays fixed in world
            visualTransform.localPosition = hingeOffset - Vector3.right * currentX;
            float ratio = Mathf.Clamp(currentX / doorDepth, -1f, 1f);
            float theta = -Mathf.Asin(ratio) * Mathf.Rad2Deg;
            visualTransform.localRotation = Quaternion.AngleAxis(theta, rotationAxis);
        }

        void OnValidate()
        {
            openAngleDeg = Mathf.Clamp(openAngleDeg, 1f, 90f);
            oneWayDirection = oneWayDirection >= 0 ? 1 : -1;
        }

        void OnDrawGizmos()
        {
            Vector3 hingeWorld = transform.TransformPoint(hingeOffset - Vector3.right * currentX);
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(hingeWorld, 0.1f);
        }
    }
}
