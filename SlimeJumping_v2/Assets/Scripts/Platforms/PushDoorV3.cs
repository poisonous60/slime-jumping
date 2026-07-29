using UnityEngine;

namespace SlimeJumping.Platforms
{
    // Slime impact -> Y-axis swing on a target Transform. BC2D footprint is rebuilt
    // every FixedUpdate from the leaf mesh corners; past openAngleDeg it disables.
    [RequireComponent(typeof(BoxCollider2D))]
    public class PushDoorV3 : PlatformMotor
    {
        [Header("Target")]
        [Tooltip("회전할 Transform (보통 leaf mesh). localRotation 만 변경됨.")]
        [SerializeField] Transform leaf;

        [Tooltip("회전축 (leaf 의 local 좌표계). 표준 FBX 도어 = (0,1,0) Y.")]
        [SerializeField] Vector3 rotationAxis = Vector3.up;

        [Tooltip("BC2D footprint 계산용 mesh. 비우면 leaf 의 MeshFilter 자동 사용.")]
        [SerializeField] MeshFilter leafMeshSource;

        [Header("Impact -> Angular Velocity")]
        [Tooltip("X = impactMag / impactReference, Y = 주입할 angular velocity 계수.")]
        [SerializeField] AnimationCurve impactCurve = new AnimationCurve(
            new Keyframe(0f,   0f,   0f,   0f),
            new Keyframe(0.2f, 0.08f),
            new Keyframe(1f,   1f,   1.2f, 1.2f),
            new Keyframe(2f,   1.3f, 0.3f, 0.3f));

        [Tooltip("|relVel| * mass 가 이 값일 때 곡선 X=1 로 정규화.")]
        [SerializeField] float impactReference = 8f;

        [Tooltip("곡선 Y=1 일 때 주입될 angular velocity (deg/s).")]
        [SerializeField] float impulseScale = 200f;

        [Header("Dynamics (return + drag)")]
        [Tooltip("0도 쪽으로 당기는 angular spring (1/s²). 0 = 자동 복귀 안 함.")]
        [SerializeField] float returnStiffness = 5f;

        [Tooltip("각속도 감쇠 (1/s).")]
        [SerializeField] float angularDrag = 2f;

        [Tooltip("hard clamp (deg). 물리적 최대 회전.")]
        [SerializeField, Range(1f, 180f)] float maxAngleDeg = 90f;

        [Header("Pass-through")]
        [Tooltip("|angle| 이 이 값 이상이면 BC2D.enabled=false → 슬라임 통과.")]
        [SerializeField, Range(1f, 180f)] float openAngleDeg = 60f;

        [Tooltip("콜라이더 비활성 최소 시간 (슬라임 통과 시간 확보).")]
        [SerializeField] float minOpenTime = 1f;

        [Tooltip("재활성 시 overlap 체크할 layer (보통 슬라임 layer).")]
        [SerializeField] LayerMask reactivateCheckLayers;

        [Header("One-Way (Optional)")]
        [SerializeField] bool oneWay;
        [SerializeField] int oneWayDirection = 1;

        [Tooltip("문이 슬라임과 반대 방향으로 회전할 때 체크. 회전 방향 부호만 뒤집음.")]
        [SerializeField] bool invertRotation;

        [Header("Editor Preview (edit mode only)")]
        [Tooltip("Edit Mode 에서 이 슬라이더를 움직이면 leaf 가 바로 회전 + BC2D 가 footprint 를 따라옴. " +
                 "Scene 뷰에서 visual 과 collider 가 일치하는지 확인용. Play 모드에선 무시됨. " +
                 "0 으로 되돌리면 원래 회전 복원.")]
        [SerializeField, Range(-180f, 180f)] float editorPreviewAngleDeg = 0f;

        // Closed-pose baseline captured at edit time; serialized so play mode starts from it.
        [SerializeField, HideInInspector] Quaternion savedClosedLeafRotation = Quaternion.identity;
        [SerializeField, HideInInspector] bool savedClosedLeafRotationValid = false;

        // ---- runtime state ----
        float currentAngleDeg;
        float angularVelocityDeg;
        float openTimer;
        bool wasInContact;

        BoxCollider2D doorCollider;
        Quaternion baseLeafLocalRotation = Quaternion.identity;
        Bounds leafMeshBounds = new Bounds(Vector3.zero, Vector3.one);
        bool hasLeafMesh;
        Vector2 cachedDoorNormal;
        Collider2D[] overlapResults = new Collider2D[4];

        protected override void Awake()
        {
            base.Awake();
            doorCollider = GetComponent<BoxCollider2D>();

            if (leaf != null)
            {
                baseLeafLocalRotation = savedClosedLeafRotationValid
                    ? savedClosedLeafRotation
                    : leaf.localRotation;
                leaf.localRotation = baseLeafLocalRotation;
            }
            else
            {
                Debug.LogWarning($"[PushDoorV3] {name}: leaf Transform 미할당 — 회전 안 함.", this);
            }

            MeshFilter mf = leafMeshSource;
            if (mf == null && leaf != null) mf = leaf.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                leafMeshBounds = mf.sharedMesh.bounds;
                hasLeafMesh = true;
            }
            else
            {
                Debug.LogWarning($"[PushDoorV3] {name}: leaf MeshFilter 없음 — BC2D 자동 fit 안 됨. " +
                    "Inspector 의 Leaf Mesh Source 에 직접 할당하거나 leaf 에 MeshFilter 가 있는지 확인.", this);
            }

            float rad = rb.rotation * Mathf.Deg2Rad;
            cachedDoorNormal = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        }

        public override void ApplyRiderInfluence(Vector2 riderWorldPos, float riderMass,
                                                  Vector2 riderVelocity, bool isCharging)
        {
            // Edge-triggered: one impulse per attachment.
            if (wasInContact) return;
            wasInContact = true;

            float vRel = Vector2.Dot(riderVelocity - Velocity, cachedDoorNormal);
            float impactMag = Mathf.Abs(vRel) * Mathf.Max(riderMass, 0.0001f);
            float dir = Mathf.Sign(vRel);

            if (oneWay)
            {
                float allowed = Mathf.Sign(oneWayDirection);
                if (dir * allowed < 0f) return;
            }

            float normalized = impactMag / Mathf.Max(impactReference, 0.0001f);
            float transferred = impactCurve.Evaluate(normalized);
            float signFix = invertRotation ? -1f : 1f;
            angularVelocityDeg += dir * signFix * transferred * impulseScale;
        }

        public override void RemoveRider()
        {
            wasInContact = false;
        }

        protected override (Vector2, float) ComputeMotion(float dt)
        {
            if (openTimer > 0f) openTimer -= dt;

            angularVelocityDeg += -returnStiffness * currentAngleDeg * dt;
            angularVelocityDeg *= Mathf.Max(0f, 1f - angularDrag * dt);
            currentAngleDeg += angularVelocityDeg * dt;

            if (oneWay)
            {
                float sign = Mathf.Sign(oneWayDirection);
                if (sign > 0 && currentAngleDeg < 0f)
                {
                    currentAngleDeg = 0f;
                    angularVelocityDeg = Mathf.Max(angularVelocityDeg, 0f);
                }
                else if (sign < 0 && currentAngleDeg > 0f)
                {
                    currentAngleDeg = 0f;
                    angularVelocityDeg = Mathf.Min(angularVelocityDeg, 0f);
                }
            }

            if (currentAngleDeg > maxAngleDeg)
            {
                currentAngleDeg = maxAngleDeg;
                if (angularVelocityDeg > 0f) angularVelocityDeg = 0f;
            }
            else if (currentAngleDeg < -maxAngleDeg)
            {
                currentAngleDeg = -maxAngleDeg;
                if (angularVelocityDeg < 0f) angularVelocityDeg = 0f;
            }

            ApplyLeafRotation();    // visual swing first
            bool hasZ0Crossing = UpdateColliderShape();  // collider follows the door's slice at world Z=0
            UpdateColliderEnabledState(hasZ0Crossing);

            // Door body itself doesn't translate.
            return (rb.position, rb.rotation);
        }

        void ApplyLeafRotation()
        {
            if (leaf == null) return;
            leaf.localRotation = baseLeafLocalRotation
                                 * Quaternion.AngleAxis(currentAngleDeg, rotationAxis);
        }

        // AABB edge index pairs (corner indices differ by exactly one bit).
        // Corner bit layout: bit0=±X, bit1=±Y, bit2=±Z.
        static readonly int[] AabbEdgeA = { 0, 0, 0, 1, 1, 2, 2, 3, 4, 4, 5, 6 };
        static readonly int[] AabbEdgeB = { 1, 2, 4, 3, 5, 3, 6, 7, 5, 6, 7, 7 };

        // Footprint = 2D AABB of the leaf AABB's intersection with the gameplay plane Z=0.
        // Returns false when the door no longer crosses Z=0 (collider must go inactive).
        bool UpdateColliderShape()
        {
            if (doorCollider == null || leaf == null || !hasLeafMesh) return false;

            Vector3 c = leafMeshBounds.center;
            Vector3 e = leafMeshBounds.extents;
            Vector3 c0 = leaf.TransformPoint(new Vector3(c.x - e.x, c.y - e.y, c.z - e.z));
            Vector3 c1 = leaf.TransformPoint(new Vector3(c.x + e.x, c.y - e.y, c.z - e.z));
            Vector3 c2 = leaf.TransformPoint(new Vector3(c.x - e.x, c.y + e.y, c.z - e.z));
            Vector3 c3 = leaf.TransformPoint(new Vector3(c.x + e.x, c.y + e.y, c.z - e.z));
            Vector3 c4 = leaf.TransformPoint(new Vector3(c.x - e.x, c.y - e.y, c.z + e.z));
            Vector3 c5 = leaf.TransformPoint(new Vector3(c.x + e.x, c.y - e.y, c.z + e.z));
            Vector3 c6 = leaf.TransformPoint(new Vector3(c.x - e.x, c.y + e.y, c.z + e.z));
            Vector3 c7 = leaf.TransformPoint(new Vector3(c.x + e.x, c.y + e.y, c.z + e.z));
            Vector3[] cw = { c0, c1, c2, c3, c4, c5, c6, c7 };

            Vector2 wmin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 wmax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            bool anyCross = false;

            for (int i = 0; i < 12; i++)
            {
                Vector3 a = cw[AabbEdgeA[i]];
                Vector3 b = cw[AabbEdgeB[i]];
                if (a.z > 0f && b.z > 0f) continue;
                if (a.z < 0f && b.z < 0f) continue;

                float dz = b.z - a.z;
                if (Mathf.Abs(dz) < 0.0001f)
                {
                    // Edge parallel to Z=0 → take both endpoints.
                    if (a.x < wmin.x) wmin.x = a.x; if (a.x > wmax.x) wmax.x = a.x;
                    if (a.y < wmin.y) wmin.y = a.y; if (a.y > wmax.y) wmax.y = a.y;
                    if (b.x < wmin.x) wmin.x = b.x; if (b.x > wmax.x) wmax.x = b.x;
                    if (b.y < wmin.y) wmin.y = b.y; if (b.y > wmax.y) wmax.y = b.y;
                    anyCross = true;
                    continue;
                }
                float t = -a.z / dz;
                if (t < 0f || t > 1f) continue;
                float px = a.x + (b.x - a.x) * t;
                float py = a.y + (b.y - a.y) * t;
                if (px < wmin.x) wmin.x = px; if (px > wmax.x) wmax.x = px;
                if (py < wmin.y) wmin.y = py; if (py > wmax.y) wmax.y = py;
                anyCross = true;
            }

            if (!anyCross) return false;

            Vector3 worldCenter = new Vector3((wmin.x + wmax.x) * 0.5f, (wmin.y + wmax.y) * 0.5f, 0f);
            Vector2 worldSize = wmax - wmin;
            Vector3 localCenter = transform.InverseTransformPoint(worldCenter);
            Vector3 lossy = transform.lossyScale;
            float sx = Mathf.Max(Mathf.Abs(lossy.x), 0.0001f);
            float sy = Mathf.Max(Mathf.Abs(lossy.y), 0.0001f);
            doorCollider.offset = new Vector2(localCenter.x, localCenter.y);
            doorCollider.size = new Vector2(
                Mathf.Max(worldSize.x / sx, 0.01f),
                Mathf.Max(worldSize.y / sy, 0.01f));
            return true;
        }

        void UpdateColliderEnabledState(bool hasZ0Crossing)
        {
            if (doorCollider == null) return;

            bool shouldBeOpen = !hasZ0Crossing || Mathf.Abs(currentAngleDeg) >= openAngleDeg;

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

        void OnValidate()
        {
            openAngleDeg = Mathf.Clamp(openAngleDeg, 1f, 180f);
            maxAngleDeg = Mathf.Clamp(maxAngleDeg, openAngleDeg, 180f);
            oneWayDirection = oneWayDirection >= 0 ? 1 : -1;
            impactReference = Mathf.Max(0.1f, impactReference);
            impulseScale = Mathf.Max(0f, impulseScale);
            returnStiffness = Mathf.Max(0f, returnStiffness);
            angularDrag = Mathf.Max(0f, angularDrag);
            minOpenTime = Mathf.Max(0f, minOpenTime);

#if UNITY_EDITOR
            // Defer to avoid "SendMessage cannot be called during validation" warnings.
            UnityEditor.EditorApplication.delayCall += ApplyEditorPreview;
#endif
        }

#if UNITY_EDITOR
        void ApplyEditorPreview()
        {
            // Component may have been destroyed (delayCall is queued globally).
            if (this == null) return;
            if (Application.isPlaying) return;
            if (leaf == null) return;

            Vector3 axis = rotationAxis.sqrMagnitude > 0.0001f
                ? rotationAxis.normalized
                : Vector3.up;

            // First touch snapshots the closed reference and re-anchors the slider at 0,
            // else the previous preview bakes in and re-applied deltas drift.
            if (!savedClosedLeafRotationValid)
            {
                savedClosedLeafRotation = leaf.localRotation;
                savedClosedLeafRotationValid = true;
                editorPreviewAngleDeg = 0f;
            }

            leaf.localRotation = savedClosedLeafRotation
                                  * Quaternion.AngleAxis(editorPreviewAngleDeg, axis);

            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.EditorUtility.SetDirty(leaf);

            UpdateColliderShapeEditor();
        }

        [ContextMenu("Capture Current Leaf Rotation as Closed")]
        void CaptureCurrentLeafAsClosed()
        {
            if (leaf == null)
            {
                Debug.LogWarning($"[PushDoorV3] {name}: leaf 미할당.", this);
                return;
            }
            savedClosedLeafRotation = leaf.localRotation;
            savedClosedLeafRotationValid = true;
            editorPreviewAngleDeg = 0f;
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[PushDoorV3] {name}: 현재 leaf rotation 을 닫힘 기준으로 저장. " +
                      $"closed = {leaf.localRotation.eulerAngles}", this);
        }

        [ContextMenu("Reset Leaf to Identity (clear baked rotation)")]
        void ResetLeafToIdentity()
        {
            if (leaf == null) return;
            leaf.localRotation = Quaternion.identity;
            savedClosedLeafRotation = Quaternion.identity;
            savedClosedLeafRotationValid = true;
            editorPreviewAngleDeg = 0f;
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.EditorUtility.SetDirty(leaf);
        }

        void UpdateColliderShapeEditor()
        {
            if (leaf == null) return;
            var bc = GetComponent<BoxCollider2D>();
            if (bc == null) return;

            MeshFilter mf = leafMeshSource;
            if (mf == null) mf = leaf.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;

            Bounds lb = mf.sharedMesh.bounds;
            Vector3 c = lb.center;
            Vector3 e = lb.extents;
            Vector3 c0 = leaf.TransformPoint(new Vector3(c.x - e.x, c.y - e.y, c.z - e.z));
            Vector3 c1 = leaf.TransformPoint(new Vector3(c.x + e.x, c.y - e.y, c.z - e.z));
            Vector3 c2 = leaf.TransformPoint(new Vector3(c.x - e.x, c.y + e.y, c.z - e.z));
            Vector3 c3 = leaf.TransformPoint(new Vector3(c.x + e.x, c.y + e.y, c.z - e.z));
            Vector3 c4 = leaf.TransformPoint(new Vector3(c.x - e.x, c.y - e.y, c.z + e.z));
            Vector3 c5 = leaf.TransformPoint(new Vector3(c.x + e.x, c.y - e.y, c.z + e.z));
            Vector3 c6 = leaf.TransformPoint(new Vector3(c.x - e.x, c.y + e.y, c.z + e.z));
            Vector3 c7 = leaf.TransformPoint(new Vector3(c.x + e.x, c.y + e.y, c.z + e.z));
            Vector3[] cw = { c0, c1, c2, c3, c4, c5, c6, c7 };

            Vector2 wmin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 wmax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            bool anyCross = false;
            for (int i = 0; i < 12; i++)
            {
                Vector3 a = cw[AabbEdgeA[i]];
                Vector3 b = cw[AabbEdgeB[i]];
                if (a.z > 0f && b.z > 0f) continue;
                if (a.z < 0f && b.z < 0f) continue;
                float dz = b.z - a.z;
                if (Mathf.Abs(dz) < 0.0001f)
                {
                    if (a.x < wmin.x) wmin.x = a.x; if (a.x > wmax.x) wmax.x = a.x;
                    if (a.y < wmin.y) wmin.y = a.y; if (a.y > wmax.y) wmax.y = a.y;
                    if (b.x < wmin.x) wmin.x = b.x; if (b.x > wmax.x) wmax.x = b.x;
                    if (b.y < wmin.y) wmin.y = b.y; if (b.y > wmax.y) wmax.y = b.y;
                    anyCross = true;
                    continue;
                }
                float t = -a.z / dz;
                if (t < 0f || t > 1f) continue;
                float px = a.x + (b.x - a.x) * t;
                float py = a.y + (b.y - a.y) * t;
                if (px < wmin.x) wmin.x = px; if (px > wmax.x) wmax.x = px;
                if (py < wmin.y) wmin.y = py; if (py > wmax.y) wmax.y = py;
                anyCross = true;
            }

            // No Z=0 crossing → collapse collider so the preview shows it as gone.
            if (!anyCross)
            {
                bc.offset = Vector2.zero;
                bc.size = new Vector2(0.01f, 0.01f);
                UnityEditor.EditorUtility.SetDirty(bc);
                return;
            }

            Vector3 worldCenter = new Vector3((wmin.x + wmax.x) * 0.5f, (wmin.y + wmax.y) * 0.5f, 0f);
            Vector2 worldSize = wmax - wmin;
            Vector3 localCenter = transform.InverseTransformPoint(worldCenter);
            Vector3 lossy = transform.lossyScale;
            float sx = Mathf.Max(Mathf.Abs(lossy.x), 0.0001f);
            float sy = Mathf.Max(Mathf.Abs(lossy.y), 0.0001f);
            bc.offset = new Vector2(localCenter.x, localCenter.y);
            bc.size = new Vector2(
                Mathf.Max(worldSize.x / sx, 0.01f),
                Mathf.Max(worldSize.y / sy, 0.01f));
            UnityEditor.EditorUtility.SetDirty(bc);
        }
#endif

        void OnDrawGizmosSelected()
        {
            if (leaf != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawSphere(leaf.position, 0.08f);
                Vector3 axis = rotationAxis.sqrMagnitude > 0.0001f ? rotationAxis.normalized : Vector3.up;
                Vector3 axisWorld = leaf.TransformDirection(axis);
                Gizmos.DrawLine(leaf.position - axisWorld * 0.5f, leaf.position + axisWorld * 0.5f);
            }

            if (doorCollider != null && Application.isPlaying)
            {
                Gizmos.color = doorCollider.enabled
                    ? new Color(0.2f, 1f, 0.3f, 0.8f)
                    : new Color(1f, 0.3f, 0.3f, 0.5f);
                Vector3 wcenter = transform.TransformPoint(
                    new Vector3(doorCollider.offset.x, doorCollider.offset.y, 0f));
                Vector3 wsize = new Vector3(
                    doorCollider.size.x * Mathf.Abs(transform.lossyScale.x),
                    doorCollider.size.y * Mathf.Abs(transform.lossyScale.y),
                    0.05f);
                Gizmos.DrawWireCube(wcenter, wsize);
            }
        }
    }
}
