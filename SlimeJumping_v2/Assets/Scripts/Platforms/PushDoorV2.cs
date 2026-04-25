using UnityEngine;

namespace SlimeJumping.Platforms
{
    [RequireComponent(typeof(BoxCollider2D))]
    public class PushDoorV2 : PlatformMotor
    {
        [Header("Impulse Transfer")]
        [Tooltip("슬라임 법선속도 × 질량이 이 값일 때 곡선 X=1 로 정규화")]
        [SerializeField] float impactReference = 8f;

        [Tooltip("X: 정규화 충격량(impactMag/impactReference), Y: 실제 전달 계수")]
        [SerializeField] AnimationCurve impulseResponseCurve = new AnimationCurve(
            new Keyframe(0f,   0f,   0f,   0f),
            new Keyframe(0.2f, 0.08f),
            new Keyframe(1f,   1f,   1.2f, 1.2f),
            new Keyframe(2f,   1.3f, 0.3f, 0.3f));

        [Tooltip("Y=1일 때 문에 주입될 xVelocity (m/s)")]
        [SerializeField] float impulseScale = 2.5f;

        [Tooltip("이 값 미만 충격은 문을 열지 않고 흔들림(jiggle)만")]
        [SerializeField] float openThresholdImpact = 1.5f;

        [Tooltip("임계 미달 시 xVelocity 에 주입되는 흔들림 계수 (충격량 × 이 값)")]
        [SerializeField] float jiggleScale = 0.15f;

        [Header("Inertia Dynamics")]
        [Tooltip("닫힘 위치로 당기는 스프링 강도")]
        [SerializeField] float returnStiffness = 10f;

        [Tooltip("속도에 비례하는 공기저항 (1/s)")]
        [SerializeField] float angularDrag = 2f;

        [Header("Limits")]
        [SerializeField, Range(1f, 90f)] float openAngleDeg = 60f;

        [Header("One-Way")]
        [SerializeField] bool oneWay;
        [SerializeField] int oneWayDirection = 1;

        [Header("Visual (auto-wired via Reset)")]
        [SerializeField] Vector3 hingeOffset = new Vector3(0f, 0f, 2f);
        [SerializeField] Vector3 rotationAxis = Vector3.up;
        [SerializeField] Transform visualTransform;

        [Tooltip("Local direction the leaf extends from its pivot when closed. (0,0,-1) = salloon style (default). Set to (1,0,0) for 3D door FBX where leaf points along +X from hinge. MUST be non-parallel to rotationAxis or arc collapses to a line.")]
        [SerializeField] Vector3 leafForwardLocal = new Vector3(0f, 0f, -1f);

        [Tooltip("Panel length used for asin mapping. 0 = auto (transform.position.z + hingeOffset.z, salloon-door layout). Set explicitly when leaf extends in a non-Z direction.")]
        [SerializeField] float doorDepthOverride = 0f;

        [Header("Open State")]
        [SerializeField] float minOpenTime = 1f;
        [SerializeField] LayerMask reactivateCheckLayers;

        // Removed contactCooldown auto-rearm: it caused continuous re-impulse every
        // tick while slime stayed attached, making v2 feel identical to v1's
        // continuous-push model. wasInContact now resets ONLY on RemoveRider() —
        // i.e. one impulse per attachment, exactly the "edge-triggered" intent.

        // Cached at Awake so runtime changes to transform.position.z or hingeOffset.z
        // don't mid-flight change clamp range / open threshold. Edit hingeOffset in
        // the Inspector while stopped; changes take effect on next play.
        float cachedDoorDepth;
        float cachedOpenThreshold;
        Vector2 cachedRight;

        Vector2 basePosition;
        float baseRotation;

        float currentX;
        float xVelocity;
        float openTimer;

        // Contact-edge tracking: impulse fires once per new contact, not every FixedUpdate.
        // Resets only when slime detaches (RemoveRider).
        bool wasInContact;

        BoxCollider2D doorCollider;
        Collider2D[] overlapResults = new Collider2D[4];

        void Reset()
        {
            if (visualTransform == null)
                visualTransform = FindVisualChild();
            var hinge = transform.Find("Hinge");
            if (hinge != null)
                hingeOffset = hinge.localPosition;
        }

        Transform FindVisualChild()
        {
            var t = transform.Find("Leaf");
            if (t != null) return t;
            t = transform.Find("DoorLeaf");
            if (t != null) return t;
            t = transform.Find("VisualPivot");
            return t;
        }

        protected override void Awake()
        {
            base.Awake();
            basePosition = rb.position;
            baseRotation = rb.rotation;
            doorCollider = GetComponent<BoxCollider2D>();
            CacheConstants();

            if (visualTransform == null)
                Debug.LogWarning($"[PushDoorV2] {name}: visualTransform is null — door won't rotate visually. " +
                                 "Assign Leaf in Inspector or use 'Convert Selection to PushDoorV2' menu.", this);
        }

        void CacheConstants()
        {
            float rad = baseRotation * Mathf.Deg2Rad;
            cachedRight = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

            // Z is 2D-physics-invariant, so using transform.position.z here is safe.
            // Captured once at Awake — moving the door at runtime won't rescale clamp.
            float autoDepth = transform.position.z + hingeOffset.z;
            cachedDoorDepth = doorDepthOverride > 0f
                ? doorDepthOverride
                : Mathf.Max(autoDepth, 0.01f);
            cachedOpenThreshold = cachedDoorDepth * Mathf.Sin(openAngleDeg * Mathf.Deg2Rad);
        }

        public override void ApplyRiderInfluence(Vector2 riderWorldPos, float riderMass, Vector2 riderVelocity, bool isCharging)
        {
            // Edge-triggered: impulse only on the frame contact begins
            if (wasInContact) return;
            wasInContact = true;

            // Project relative velocity onto door normal (cachedRight)
            Vector2 doorVelocity = Velocity;
            float vRel = Vector2.Dot(riderVelocity - doorVelocity, cachedRight);
            float impactMag = Mathf.Abs(vRel) * Mathf.Max(riderMass, 0.0001f);
            // Newton's 3rd law: door receives momentum in the same direction as the
            // slime's velocity along the door normal. (Previously had a stray '-' that
            // pushed the door TOWARD the slime — visible bug.)
            float dir = Mathf.Sign(vRel);

            if (oneWay)
            {
                float allowed = Mathf.Sign(oneWayDirection);
                // Wrong-side impact acts as a wall: ComputeMotion clamps currentX on
                // the disallowed side, so adding jiggle here would be silently erased.
                // Return without touching xVelocity — feels like solid resistance.
                if (dir * allowed < 0f) return;
            }

            if (impactMag < openThresholdImpact)
            {
                // Soft tap: only a small jiggle, don't commit to opening
                xVelocity += dir * impactMag * jiggleScale;
                return;
            }

            float normalized = impactMag / Mathf.Max(impactReference, 0.0001f);
            float transferred = impulseResponseCurve.Evaluate(normalized);
            xVelocity += dir * transferred * impulseScale;
        }

        public override void RemoveRider()
        {
            wasInContact = false;
        }

        protected override (Vector2, float) ComputeMotion(float dt)
        {
            if (openTimer > 0f)
                openTimer -= dt;

            // Inertia + drag + spring (no active push acceleration)
            float spring = -returnStiffness * currentX;
            xVelocity += spring * dt;
            xVelocity *= Mathf.Max(0f, 1f - angularDrag * dt);
            currentX += xVelocity * dt;

            if (oneWay)
            {
                float sign = Mathf.Sign(oneWayDirection);
                if (sign > 0 && currentX < 0f) { currentX = 0f; xVelocity = Mathf.Max(xVelocity, 0f); }
                if (sign < 0 && currentX > 0f) { currentX = 0f; xVelocity = Mathf.Min(xVelocity, 0f); }
            }

            // Hard clamp at ±doorDepth so asin never saturates numerically
            if (currentX > cachedDoorDepth) { currentX = cachedDoorDepth; if (xVelocity > 0f) xVelocity = 0f; }
            if (currentX < -cachedDoorDepth) { currentX = -cachedDoorDepth; if (xVelocity < 0f) xVelocity = 0f; }

            UpdateColliderState();

            return (basePosition + cachedRight * currentX, baseRotation);
        }

        void UpdateColliderState()
        {
            if (doorCollider == null) return;

            bool shouldBeOpen = Mathf.Abs(currentX) >= cachedOpenThreshold;

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
                    openTimer = 0.1f;
                }
            }
        }

        void LateUpdate()
        {
            if (visualTransform == null) return;

            visualTransform.localPosition = hingeOffset - Vector3.right * currentX;
            float ratio = Mathf.Clamp(currentX / cachedDoorDepth, -1f, 1f);
            float theta = -Mathf.Asin(ratio) * Mathf.Rad2Deg;
            visualTransform.localRotation = Quaternion.AngleAxis(theta, rotationAxis);
        }

        void OnValidate()
        {
            openAngleDeg = Mathf.Clamp(openAngleDeg, 1f, 90f);
            oneWayDirection = oneWayDirection >= 0 ? 1 : -1;
            // Floor at 0.1 (not 0.0001) so designer-set tiny values don't extrapolate
            // the response curve into runaway territory.
            impactReference = Mathf.Max(0.1f, impactReference);
            impulseScale = Mathf.Max(0f, impulseScale);
            jiggleScale = Mathf.Max(0f, jiggleScale);
            openThresholdImpact = Mathf.Max(0f, openThresholdImpact);
            returnStiffness = Mathf.Max(0f, returnStiffness);
            angularDrag = Mathf.Max(0f, angularDrag);
            minOpenTime = Mathf.Max(0f, minOpenTime);
        }

        void OnDrawGizmos()
        {
            Vector3 hingeWorld = transform.TransformPoint(hingeOffset - Vector3.right * currentX);
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(hingeWorld, 0.1f);
        }

        void OnDrawGizmosSelected()
        {
            // Draw the swing range so designers can see how far the door can open.
            // Editor-time uses transform.position.z + hingeOffset.z (same formula as
            // runtime cachedDoorDepth) so the gizmo matches in-game extents.
            Vector3 axisLocal = rotationAxis.sqrMagnitude > 0.0001f ? rotationAxis.normalized : Vector3.up;
            float depth = doorDepthOverride > 0f
                ? doorDepthOverride
                : Mathf.Max(transform.position.z + hingeOffset.z, 0.01f);

            Vector3 leafFwd = leafForwardLocal.sqrMagnitude > 0.0001f
                ? leafForwardLocal.normalized
                : Vector3.back;
            Vector3 closedDirLocal = leafFwd * depth;
            Vector3 hingeWorld = transform.TransformPoint(hingeOffset);

            // Hinge axis indicator (short line through hinge)
            Vector3 axisWorld = transform.TransformDirection(axisLocal);
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(hingeWorld - axisWorld * 0.5f, hingeWorld + axisWorld * 0.5f);

            // Closed-position leaf line (yellow ray from hinge to leaf tip)
            Vector3 closedTip = transform.TransformPoint(hingeOffset + closedDirLocal);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(hingeWorld, closedTip);

            // Sanity: if leafForwardLocal is parallel to rotationAxis, the arc
            // collapses (rotation around the leaf-direction does nothing). Draw a
            // red X at the hinge to flag the misconfig.
            float dotAL = Mathf.Abs(Vector3.Dot(leafFwd, axisLocal));
            if (dotAL > 0.99f)
            {
                Gizmos.color = Color.red;
                float r = depth * 0.2f;
                Gizmos.DrawLine(hingeWorld + Vector3.right * r + Vector3.up * r,
                                hingeWorld - Vector3.right * r - Vector3.up * r);
                Gizmos.DrawLine(hingeWorld + Vector3.right * r - Vector3.up * r,
                                hingeWorld - Vector3.right * r + Vector3.up * r);
                return;
            }

            // Swing arc + extreme positions
            if (oneWay)
            {
                float allowedSign = oneWayDirection >= 0 ? +1f : -1f;
                float extremeAngle = -openAngleDeg * allowedSign;
                DrawSwingGizmo(hingeWorld, closedDirLocal, axisLocal, 0f, extremeAngle);
            }
            else
            {
                DrawSwingGizmo(hingeWorld, closedDirLocal, axisLocal, -openAngleDeg, +openAngleDeg);
            }
        }

        void DrawSwingGizmo(Vector3 hingeWorld, Vector3 closedDirLocal, Vector3 axisLocal,
                            float fromAngle, float toAngle)
        {
            const int segments = 24;
            Gizmos.color = new Color(0.2f, 0.9f, 0.3f, 0.9f);

            Vector3 prev = Vector3.zero;
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float angle = Mathf.Lerp(fromAngle, toAngle, t);
                Vector3 dirLocal = Quaternion.AngleAxis(angle, axisLocal) * closedDirLocal;
                Vector3 worldPt = transform.TransformPoint(hingeOffset + dirLocal);

                if (i > 0) Gizmos.DrawLine(prev, worldPt);
                prev = worldPt;

                if (i == 0 || i == segments)
                    Gizmos.DrawLine(hingeWorld, worldPt); // ray to extreme positions
            }
        }
    }
}
