using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Platforms
{
    /// <summary>
    /// Multi-segment snake that follows an ISnakePath. Head advances along the path at
    /// per-path-segment speeds; body follows via a trail-vs-chain blend (followness knob).
    /// A single root PolygonCollider2D receives one simple quad path per on-plane segment
    /// (DeformablePlatform-style multi-path). Visuals are plain child Transforms (cubes
    /// for the prototype; head's visual is separately swappable for a face-decorated model).
    /// Rider carry is custom: slime's arc-offset-from-head is captured on attach, and each
    /// FixedUpdate the slime is MovePosition'd by the surface delta at that arc offset.
    /// </summary>
    [DefaultExecutionOrder(-150)]
    [RequireComponent(typeof(PolygonCollider2D))]
    public class SnakeSpine : MonoBehaviour
    {
        [Header("Path Source (implements ISnakePath)")]
        [Tooltip("Assign any MonoBehaviour that implements ISnakePath. Usually a WaypointSnakePath on the same GameObject.")]
        [SerializeField] MonoBehaviour pathProviderBehaviour;

        [Header("Segments")]
        [Tooltip("Head visual — the transform that SnakeSpine writes position/rotation to each frame. Leave empty if you assign Head Prefab below; it will be instantiated at runtime and the root transform auto-assigned here.")]
        [SerializeField] Transform headVisual;
        [Tooltip("Optional prefab. If Head Visual is empty, this is instantiated as a child of the snake at Awake and assigned as the head. Author the prefab with its forward axis along +X so the 2D yaw rotation lines it up with the motion direction, and with the visible face on the +Z side (toward the camera).")]
        [SerializeField] GameObject headPrefab;
        [Tooltip("If on, SnakeSpine forces headVisual.localScale to (segmentLength, segmentWidth, segmentWidth) every frame — useful for the placeholder cube. Turn off when headVisual is a sculpted/composite head authored at its own final size, otherwise nested child offsets (eyes, mouth) will smear.")]
        [SerializeField] bool overrideHeadScale = true;
        [Tooltip("Optional renderer to receive the on-plane / off-plane tint via MaterialPropertyBlock. Use this to target only the head's skin material when the head has multiple child renderers (e.g., eyes and mouth that should keep their own color). If left empty, we fall back to a MeshRenderer on headVisual's root, if any.")]
        [SerializeField] Renderer headSkinRenderer;
        [Tooltip("Local Euler offset applied to headVisual BEFORE the motion yaw, letting you realign a prefab whose forward axis isn't +X. " +
                 "Final rotation = yaw(Z) * Euler(offset). Examples — prefab faces +Y: (0,0,-90). Prefab faces -X: (0,0,180). " +
                 "3D prefab whose face points +Z (out of the screen): (0,-90,0) brings the face onto +X. Tweak until the eyes face the motion direction.")]
        [SerializeField] Vector3 headRotationOffsetEuler;
        [Tooltip("Point on the head (in head-local space AFTER Head Rotation Offset is applied; X=forward along motion, Y=side, Z=toward camera) where the body tube meets the head. The head visual is positioned so this point lands exactly on the path arc — i.e., the body's front ring is always flush with this spot on the head, closing the neck gap. Default (0,0,0) sits the head pivot on the path. Example: placeholder cube extends ±0.5 in local X, so (-0.5, 0, 0) joins the body at the head's back face. Read every FixedUpdate — tweak in play mode to find the right spot live.")]
        [SerializeField] Vector3 headBodyJointLocal;
        [Tooltip("Number of body segments behind the head. Body is rendered as a single dynamic ribbon mesh sweeping this many spine samples; changing this value resizes the ribbon live.")]
        [Min(1)] [SerializeField] int bodySegmentCount = 8;

        [Header("Shape")]
        [Tooltip("Arc-length spacing between adjacent segment centers (meters).")]
        [Min(0.01f)] [SerializeField] float segmentLength = 1f;
        [Tooltip("Head cube placeholder cross-section (meters). No longer feeds the body collider — per-segment collider half-width now tracks bodyRadius * radiusProfile so the collider tapers with the visual tube.")]
        [Min(0.01f)] [SerializeField] float segmentWidth = 0.6f;
        [Tooltip("Base radius of the tube cross-section at the head (meters). Visual only — 2D collider still uses segmentWidth.")]
        [Min(0.01f)] [SerializeField] float bodyRadius = 0.3f;
        [Tooltip("Radius multiplier sampled at i/(N-1): 0 = head-adjacent, 1 = tail. Default 1→0.25 tapers toward the tail.")]
        [SerializeField] AnimationCurve radiusProfile = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.25f));
        [Tooltip("Cross-section sides. 8 reads as round at normal camera distances.")]
        [Range(3, 24)] [SerializeField] int radialSegments = 8;
        [Tooltip("Visual rings emitted between adjacent spine anchors. 1 = one ring per spine segment (sharp folds at bends). 4 is a good default — Catmull-Rom interpolates the spine so the tube bends smoothly without changing the collider or simulation (collider stays per-segment).")]
        [Range(1, 8)] [SerializeField] int visualRingsPerSegment = 4;

        [Header("Colors")]
        [Tooltip("Head visual tint when the head's collider is on the Z=0 plane (collision active).")]
        [SerializeField] Color headColor = new Color(0.90f, 0.25f, 0.25f);
        [Tooltip("Body visual tint when the body segment's collider is on the Z=0 plane.")]
        [SerializeField] Color bodyColor = new Color(0.25f, 0.75f, 0.35f);
        [Tooltip("Tint applied when a segment has drifted off the Z=0 plane — signals that its collider path is omitted this frame.")]
        [SerializeField] Color offPlaneColor = new Color(0.35f, 0.35f, 0.35f, 1f);

        [Header("Follow")]
        [Tooltip("0 = rope-like distance-constrained chain (straightens behind head, lags on turns).\n" +
                 "1 = snake-game trail history (body exactly retraces head's past path).\n" +
                 "Intermediate values blend the two targets linearly.")]
        [Range(0f, 1f)] [SerializeField] float followness = 0.6f;

        [Header("Z Plane Gating")]
        [Tooltip("Evaluated at i/(N-1) for body segment i (0 = head-adjacent, 1 = tail). Multiplies the segment's target Z. " +
                 "Default Linear(0,1,1,0): head-adjacent follows Z fully, tail stays planar.")]
        [SerializeField] AnimationCurve zFollowProfile = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        [Tooltip("|segZ| > threshold omits that segment's collider path for this frame.")]
        [Min(0f)] [SerializeField] float zColliderThreshold = 0.05f;

        [Header("Debug")]
        [SerializeField] bool drawGizmos = true;
        [Tooltip("If on, logs spine/mesh/collider state to the console once per second during play.")]
        [SerializeField] bool debugLog = false;
        float debugLogAccum;

        // ---- Runtime state ----

        ISnakePath path;
        PolygonCollider2D poly;

        float headArcLen;
        Vector3 lastHeadTangent = Vector3.right;

        struct TrailSample { public Vector3 worldPos; public float arcStamp; }
        readonly List<TrailSample> trail = new List<TrailSample>();

        // Segment 3D targets. Index 0 = head, 1..N-1 = body.
        Vector3[] segPos3D;
        Vector3[] segPosPrev3D;
        int SegmentCount => 1 + Mathf.Max(0, bodySegmentCount);

        // Dynamic body ribbon — one MeshRenderer under the root, rebuilt every FixedUpdate.
        // Replaces the old N-cube body layout; smoothly connected cross-sections hide bend seams.
        GameObject bodyMeshGO;
        MeshFilter bodyMeshFilter;
        MeshRenderer bodyMeshRenderer;
        Mesh bodyMesh;
        readonly List<Vector3> scratchVerts  = new List<Vector3>();
        readonly List<int>     scratchTrisOn = new List<int>();  // submesh 0: on-plane segments (bodyColor)
        readonly List<int>     scratchTrisOff= new List<int>();  // submesh 1: off-plane segments (offPlaneColor)
        readonly List<Vector2> scratchUVs    = new List<Vector2>();

        // Scratch XY & perpendicular per segment, reused each frame for mesh + collider.
        Vector2[] segXY;
        Vector2[] segPerp;
        bool[]    segOnPlane;

        // Per-segment half-width / radius (meters). Drives the polygon collider's perp extrusion
        // along the coarse spine; the visual tube samples its own (interpolated) radii from these.
        float[] segHalfW;

        // Fine sample arrays — (bodySegmentCount) * visualRingsPerSegment + 1 entries, rebuilt
        // each frame inside RebuildBodyMesh. Positions come from Catmull-Rom interpolation of
        // segPos3D; TNB frames are parallel-transported along the fine tangents so adjacent
        // rings share a twist-free basis (smooth tube even at sharp bends).
        Vector3[] fineWorldPos;
        Vector3[] fineTangent;
        Vector3[] fineNormal;
        Vector3[] fineBinormal;
        float[]   fineRadius;
        int[]     fineSpineBand; // which spine band (i..i+1) this fine sample starts in — used for on/off-plane submesh routing

        // Scratch for poly.SetPath calls — reused per frame.
        readonly List<Vector2[]> scratchPaths = new List<Vector2[]>();

        // Rider carry state.
        SlimeController rider;
        Rigidbody2D riderRb;
        bool riderAttachedPrev;
        float riderArcOffsetFromHead;

        // MPB reused across all visuals — lets us override _BaseColor without per-cube material instances.
        MaterialPropertyBlock mpb;
        static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");

        // Shared default URP Lit material for the body ribbon. Reused across all SnakeSpines so
        // we don't leak a material per instance. The MPB on each body renderer supplies the tint.
        static Material _sharedBodyMat;

        // ---- Unity ----

        void Awake()
        {
            // Static collider pattern (mirrors DeformablePlatform / CrumblingPlatform).
            // Earlier versions attached a Kinematic Rigidbody2D with no MovePosition calls —
            // Unity then treated the snake as a zero-velocity kinematic body whose polygon
            // path morphed each frame, causing ambiguous depenetration (rider stuck under
            // the body, land/jump detection flipping). Removing the Rigidbody2D lets the
            // changing path be processed as a static-world rebuild, same as DeformablePlatform.
            poly = GetComponent<PolygonCollider2D>();
            poly.pathCount = 0;

            ResolvePath();
            AllocateSegmentArrays();
            EnsureHeadVisual();
            EnsureBodyMesh();
        }

        void EnsureHeadVisual()
        {
            // Prefab path: if nothing is wired to headVisual yet, instantiate the prefab as a
            // child of the snake root and auto-assign. We don't touch headPrefab or overrideHeadScale
            // here — the user controls those via the inspector. A prefab user will typically also
            // untick overrideHeadScale so their authored size is preserved.
            if (headVisual != null) return;
            if (headPrefab == null) return;
            var go = Instantiate(headPrefab, transform);
            go.name = headPrefab.name;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            headVisual = go.transform;
        }

        void Start()
        {
            // Deferred to Start so every component's Awake (especially WaypointSnakePath's
            // Rebuild) has already run. Seeding in OnEnable would read a half-initialized
            // path and spawn the body near the snake origin instead of along the route.
            SeedInitialPose();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            // Resize scratch arrays live when bodySegmentCount changes in the inspector.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                AllocateSegmentArrays();
                EnsureBodyMesh();
            };
        }
#endif

        void EnsureBodyMesh()
        {
            // Look for an existing BodyMesh child from a prior session; otherwise create one.
            if (bodyMeshGO == null)
            {
                var existing = transform.Find("BodyMesh");
                if (existing != null)
                    bodyMeshGO = existing.gameObject;
                else
                {
                    bodyMeshGO = new GameObject("BodyMesh");
                    bodyMeshGO.transform.SetParent(transform, false);
                    bodyMeshGO.layer = gameObject.layer;
                }
            }
            bodyMeshFilter = bodyMeshGO.GetComponent<MeshFilter>();
            if (bodyMeshFilter == null) bodyMeshFilter = bodyMeshGO.AddComponent<MeshFilter>();
            bodyMeshRenderer = bodyMeshGO.GetComponent<MeshRenderer>();
            if (bodyMeshRenderer == null) bodyMeshRenderer = bodyMeshGO.AddComponent<MeshRenderer>();
            if (bodyMesh == null)
            {
                bodyMesh = new Mesh { name = "SnakeBodyMesh" };
                bodyMesh.MarkDynamic();
            }
            bodyMeshFilter.sharedMesh = bodyMesh;

            // AddComponent<MeshRenderer> leaves .sharedMaterial = null, which renders as
            // Unity's magenta "missing shader" and (depending on version) can flip invisible
            // once an MPB is applied. Assign a shared URP Lit material so the MPB tint reads.
            if (_sharedBodyMat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader != null)
                {
                    _sharedBodyMat = new Material(shader) { name = "SnakeBody (shared)" };
                }
            }
            // Two material slots — one per submesh (on-plane / off-plane). Both reference the
            // same shared material; per-submesh MPBs supply the tint in ApplyBodyMeshColor.
            if (_sharedBodyMat != null)
            {
                var mats = bodyMeshRenderer.sharedMaterials;
                if (mats == null || mats.Length != 2 || mats[0] != _sharedBodyMat || mats[1] != _sharedBodyMat)
                    bodyMeshRenderer.sharedMaterials = new Material[] { _sharedBodyMat, _sharedBodyMat };
            }
        }

        void ResolvePath()
        {
            path = null;
            if (pathProviderBehaviour != null && pathProviderBehaviour is ISnakePath p) path = p;
            if (path == null) path = GetComponent<ISnakePath>();
        }

        void AllocateSegmentArrays()
        {
            int n = SegmentCount;
            if (segPos3D == null || segPos3D.Length != n)
            {
                segPos3D = new Vector3[n];
                segPosPrev3D = new Vector3[n];
            }
            if (segXY == null || segXY.Length != n)
            {
                segXY = new Vector2[n];
                segPerp = new Vector2[n];
                segOnPlane = new bool[n];
            }
            if (segHalfW == null || segHalfW.Length != n)
            {
                segHalfW = new float[n];
            }
        }

        void EnsureFineArrays(int fineCount)
        {
            if (fineWorldPos == null || fineWorldPos.Length != fineCount)
            {
                fineWorldPos  = new Vector3[fineCount];
                fineTangent   = new Vector3[fineCount];
                fineNormal    = new Vector3[fineCount];
                fineBinormal  = new Vector3[fineCount];
                fineRadius    = new float[fineCount];
                fineSpineBand = new int[fineCount];
            }
        }

        void SeedInitialPose()
        {
            ResolvePath();
            AllocateSegmentArrays();
            if (path == null || SegmentCount < 1) return;

            headArcLen = 0f;
            Vector3 headPos, headTan;
            path.SampleWorld(headArcLen, out headPos, out headTan);

            // Use a finite-arc step to derive seed direction, not the Catmull-Rom analytic
            // tangent. At a corner waypoint where the previous control point is far away
            // (e.g. a long loop-close segment), the analytic tangent can point "backward" —
            // which would lay the body ahead of the path direction instead of behind.
            float stepArc = Mathf.Max(0.001f, Mathf.Min(segmentLength, path.TotalArcLen * 0.05f));
            Vector3 aheadPos; Vector3 _aheadTan;
            path.SampleWorld(stepArc, out aheadPos, out _aheadTan);
            Vector3 seedDir = aheadPos - headPos;
            if (seedDir.sqrMagnitude > 1e-8f)
                lastHeadTangent = seedDir.normalized;
            else if (headTan.sqrMagnitude > 1e-8f)
                lastHeadTangent = headTan.normalized;
            else
                lastHeadTangent = Vector3.right;

            int n = SegmentCount;
            segPos3D[0] = headPos;
            // Straight line behind head along -tangent. Back-sampling the loop would place the
            // tail near the head when the loop is tight/short and the total arc is less than
            // a few snake lengths; users expect "snake starts in a line" instead.
            for (int i = 1; i < n; i++)
                segPos3D[i] = headPos - lastHeadTangent * (i * segmentLength);
            System.Array.Copy(segPos3D, segPosPrev3D, n);

            // Pre-fill trail with matching samples so followness=1 lookups work on frame 1
            // and the trail is continuous with where the head will actually advance next.
            trail.Clear();
            for (int i = n - 1; i >= 0; i--)
            {
                trail.Add(new TrailSample
                {
                    worldPos = segPos3D[i],
                    arcStamp = headArcLen - i * segmentLength,
                });
            }

            ComputeSegmentFrames();
            ComputeSegmentHalfWidths();
            WriteVisuals();
            RebuildBodyMesh();
            ApplyBodyMeshColor();
            RebuildCollider();
        }

        void FixedUpdate()
        {
            if (path == null) return;
            int n = SegmentCount;
            if (n < 1) return;

            float dt = Time.fixedDeltaTime;

            // 1. Advance head arc-length. SpeedAt is queried once per step; per-frame error across
            //    waypoint boundaries is tiny at 50Hz and not worth a sub-stepping loop for the prototype.
            float speed = path.SpeedAt(headArcLen);
            headArcLen += speed * dt;

            // 2. Sample head.
            Vector3 headPos, headTan;
            path.SampleWorld(headArcLen, out headPos, out headTan);
            if (headTan.sqrMagnitude > 1e-8f) lastHeadTangent = headTan.normalized;

            // Snapshot prev positions before we overwrite (for rider carry delta).
            System.Array.Copy(segPos3D, segPosPrev3D, n);

            // 3. Push trail.
            trail.Add(new TrailSample { worldPos = headPos, arcStamp = headArcLen });
            float oldestNeeded = headArcLen - (n + 2) * segmentLength;
            int drop = 0;
            while (drop < trail.Count - 1 && trail[drop + 1].arcStamp < oldestNeeded) drop++;
            if (drop > 0) trail.RemoveRange(0, drop);

            // 4. Head position + body chain.
            segPos3D[0] = headPos;
            for (int i = 1; i < n; i++)
            {
                Vector3 targetTrail = SampleTrailAt(headArcLen - i * segmentLength, i);
                Vector3 prev3D = segPos3D[i - 1];
                Vector3 cur3D = segPos3D[i];
                Vector3 dir = cur3D - prev3D;
                if (dir.sqrMagnitude < 1e-10f) dir = -lastHeadTangent;
                Vector3 targetChain = prev3D + dir.normalized * segmentLength;

                Vector3 raw = Vector3.Lerp(targetChain, targetTrail, followness);

                float zScale = zFollowProfile.Evaluate(n > 1 ? i / (float)(n - 1) : 0f);
                segPos3D[i] = new Vector3(raw.x, raw.y, raw.z * zScale);
            }

            ComputeSegmentFrames();
            ComputeSegmentHalfWidths();
            WriteVisuals();
            RebuildBodyMesh();
            ApplyBodyMeshColor();
            RebuildCollider();
            UpdateRiderCarry();

            if (debugLog)
            {
                debugLogAccum += dt;
                if (debugLogAccum >= 1f)
                {
                    debugLogAccum = 0f;
                    LogDebugSnapshot();
                }
            }
        }

        void LogDebugSnapshot()
        {
            int n = SegmentCount;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[SnakeSpine] DEBUG snapshot");
            sb.AppendLine($"  snake world pos = {transform.position:F2}, headArcLen = {headArcLen:F2}, path totalArc = {(path != null ? path.TotalArcLen : 0):F2}");
            sb.AppendLine($"  lastHeadTangent (world) = {lastHeadTangent:F3}");
            for (int i = 0; i < n; i++)
            {
                sb.AppendLine($"  seg[{i}] world={segPos3D[i]:F3} localXY={segXY[i]:F3} perp={segPerp[i]:F3} onPlane={segOnPlane[i]}");
            }

            if (bodyMesh != null)
            {
                var bnds = bodyMesh.bounds;
                sb.AppendLine($"  bodyMesh verts={bodyMesh.vertexCount} triIndices={(bodyMesh.triangles != null ? bodyMesh.triangles.Length : 0)} bounds center={bnds.center:F2} ext={bnds.extents:F2}");
                if (bodyMesh.vertexCount > 0)
                {
                    var vs = bodyMesh.vertices;
                    sb.AppendLine($"    v[0] local={vs[0]:F3}  v[last] local={vs[vs.Length - 1]:F3}");
                }
            }
            else
            {
                sb.AppendLine("  bodyMesh is NULL");
            }

            if (bodyMeshRenderer != null)
            {
                var m = bodyMeshRenderer.sharedMaterial;
                sb.AppendLine($"  MeshRenderer enabled={bodyMeshRenderer.enabled} active={bodyMeshGO.activeInHierarchy} layer={bodyMeshGO.layer} mat={(m == null ? "NULL" : m.shader.name)}");
            }

            if (poly != null)
            {
                sb.AppendLine($"  PolygonCollider2D pathCount={poly.pathCount}");
            }

            sb.AppendLine($"  trail.Count={trail.Count}");
            if (trail.Count > 0)
            {
                sb.AppendLine($"    trail[0] arc={trail[0].arcStamp:F2} pos={trail[0].worldPos:F2}");
                sb.AppendLine($"    trail[last] arc={trail[trail.Count - 1].arcStamp:F2} pos={trail[trail.Count - 1].worldPos:F2}");
            }

            Debug.Log(sb.ToString(), this);
        }

        Vector3 SampleTrailAt(float arcStamp, int segIndexFallback)
        {
            int count = trail.Count;
            if (count == 0) return segPos3D[segIndexFallback];

            if (arcStamp >= trail[count - 1].arcStamp) return trail[count - 1].worldPos;
            if (arcStamp <= trail[0].arcStamp) return trail[0].worldPos;

            // Find the bracket (i-1, i) whose stamps straddle arcStamp. Iterate from the tail
            // because most lookups are near the end of the buffer. The previous version
            // returned on the first `b.arcStamp >= arcStamp` hit without checking the lower
            // bound — extrapolating from the two most recent entries for every body lookup
            // (arcStamp is -i*segmentLength which is always below recent stamps).
            for (int i = count - 1; i > 0; i--)
            {
                float sa = trail[i - 1].arcStamp;
                if (sa <= arcStamp)
                {
                    float sb = trail[i].arcStamp;
                    float span = sb - sa;
                    if (span < 1e-6f) return trail[i].worldPos;
                    float t = (arcStamp - sa) / span;
                    return Vector3.Lerp(trail[i - 1].worldPos, trail[i].worldPos, t);
                }
            }
            return trail[0].worldPos;
        }

        /// <summary>
        /// Computes local XY, smoothed perpendicular, and on-plane flag per segment.
        /// Used by both the ribbon mesh and the polygon collider so they share edge geometry.
        /// Perpendicular is derived from the central-difference XY tangent so adjacent segments
        /// implicitly agree on the shared edge — no kinks at bends.
        /// </summary>
        void ComputeSegmentFrames()
        {
            int n = SegmentCount;
            Vector3 headTanLocal3 = transform.InverseTransformDirection(lastHeadTangent);
            Vector2 headTanLocal = new Vector2(headTanLocal3.x, headTanLocal3.y);
            if (headTanLocal.sqrMagnitude < 1e-12f) headTanLocal = Vector2.right;
            else headTanLocal.Normalize();

            for (int i = 0; i < n; i++)
            {
                Vector3 local = transform.InverseTransformPoint(segPos3D[i]);
                segXY[i] = new Vector2(local.x, local.y);
                segOnPlane[i] = Mathf.Abs(segPos3D[i].z) <= zColliderThreshold;
            }
            for (int i = 0; i < n; i++)
            {
                Vector2 tan;
                if (i == 0) tan = headTanLocal;
                else if (i == n - 1) tan = segXY[i - 1] - segXY[i];           // toward head
                else tan = segXY[i - 1] - segXY[i + 1];                       // central (head-ward)
                float tm = tan.magnitude;
                if (tm < 1e-6f) tan = Vector2.right; else tan /= tm;
                segPerp[i] = new Vector2(-tan.y, tan.x);
            }
        }

        /// <summary>
        /// Per-segment collider half-width, sampled from radiusProfile at i/(N-1). Driven here
        /// (instead of inside the mesh builder) because the polygon collider needs the same
        /// values every frame regardless of whether the mesh rebuild runs.
        /// </summary>
        void ComputeSegmentHalfWidths()
        {
            int n = SegmentCount;
            for (int i = 0; i < n; i++)
            {
                float profileT = n > 1 ? i / (float)(n - 1) : 0f;
                segHalfW[i] = Mathf.Max(0.001f, bodyRadius * radiusProfile.Evaluate(profileT));
            }
        }

        // Catmull-Rom (tension 0.5). Same form used by WaypointSnakePath and scores of other
        // spline utilities; pasted here to avoid adding a cross-class dependency for four lines.
        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }

        void WriteVisuals()
        {
            int n = SegmentCount;
            if (headVisual == null || n < 1) return;

            float rotDeg = Mathf.Atan2(lastHeadTangent.y, lastHeadTangent.x) * Mathf.Rad2Deg;
            // Offset is pre-applied in local space (right-multiply) so the prefab's authored
            // forward is first rotated onto +X, then the motion yaw turns +X toward the tangent.
            headVisual.rotation = Quaternion.Euler(0f, 0f, rotDeg) * Quaternion.Euler(headRotationOffsetEuler);
            // Place the head so headBodyJointLocal (in head-local space) lands exactly on the
            // path sample — this is where the body tube's front ring already sits, so choosing
            // a joint point at the back of the head closes the neck gap automatically.
            headVisual.position = segPos3D[0] - headVisual.rotation * headBodyJointLocal;
            if (overrideHeadScale)
                headVisual.localScale = new Vector3(segmentLength, segmentWidth, segmentWidth);

            bool headOn = segOnPlane != null ? segOnPlane[0] : true;
            ApplyVisualColor(headOn ? headColor : offPlaneColor);
        }

        void ApplyVisualColor(Color c)
        {
            // Prefer an explicit headSkinRenderer so composite heads (cube + eye + mouth children)
            // can tint only the skin. Fall back to a MeshRenderer on headVisual's root so the
            // original single-cube authoring keeps working without wiring the new field.
            Renderer target = headSkinRenderer;
            if (target == null && headVisual != null) target = headVisual.GetComponent<MeshRenderer>();
            if (target == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            target.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorID, c);
            target.SetPropertyBlock(mpb);
        }

        /// <summary>
        /// Body visual: dynamic 3D tube sweeping the spine. Each spine band (i..i+1) is subdivided
        /// into `visualRingsPerSegment` fine rings via Catmull-Rom interpolation of the spine
        /// positions, producing a smooth curve that doesn't kink at bends even though the
        /// simulation/collider only track `bodySegmentCount` coarse anchors. Fine tangents come
        /// from central differences on the fine positions and the TNB frame is parallel-transported
        /// along the fine sequence so the radial basis stays twist-free at sharp turns. Radii are
        /// linearly interpolated between segHalfW values.
        /// Band triangles are routed into submesh 0 (on-plane) or submesh 1 (off-plane) based on
        /// the source spine band's on-plane flag — so the two-tone tinting survives subdivision.
        /// Only RebuildCollider filters by on-plane.
        /// Vertices are built in world space (using Z-bearing positions) and converted to local
        /// via InverseTransformPoint, so Z excursions show up as the tube bowing out of the XY plane.
        /// </summary>
        void RebuildBodyMesh()
        {
            if (bodyMesh == null) return;
            int n = SegmentCount;
            int R = Mathf.Max(3, radialSegments);
            int K = Mathf.Max(1, visualRingsPerSegment);
            scratchVerts.Clear();
            scratchTrisOn.Clear();
            scratchTrisOff.Clear();
            scratchUVs.Clear();

            if (n < 2) { bodyMesh.Clear(); return; }

            int fineCount = (n - 1) * K + 1;
            EnsureFineArrays(fineCount);

            // --- Fine positions & radii via Catmull-Rom on the spine.
            // Virtual endpoint mirroring gives the first/last bands a smooth shape too — without
            // it the tube would still crease at the head-adjacent ring if the neighbors form an
            // angle. p0/p3 reflect p1/p2 around p1 (resp. p2) when no outer spine anchor exists.
            for (int b = 0; b < n - 1; b++)
            {
                Vector3 p1 = segPos3D[b];
                Vector3 p2 = segPos3D[b + 1];
                Vector3 p0 = b > 0         ? segPos3D[b - 1] : (2f * p1 - p2);
                Vector3 p3 = b + 2 < n     ? segPos3D[b + 2] : (2f * p2 - p1);
                float r1 = segHalfW[b];
                float r2 = segHalfW[b + 1];
                for (int k = 0; k < K; k++)
                {
                    float t = k / (float)K;
                    int j = b * K + k;
                    fineWorldPos[j]  = CatmullRom(p0, p1, p2, p3, t);
                    fineRadius[j]    = Mathf.Lerp(r1, r2, t);
                    fineSpineBand[j] = b;
                }
            }
            // Closing sample at the tail.
            fineWorldPos[fineCount - 1]  = segPos3D[n - 1];
            fineRadius[fineCount - 1]    = segHalfW[n - 1];
            fineSpineBand[fineCount - 1] = n - 2;

            // --- Fine tangents: central difference on fine positions (head-ward convention,
            // i.e., tangent[j] points from j+1 toward j-1, matching the 2D perp convention so
            // front/back cap offsets read naturally: +tangent goes "forward" off the head).
            for (int j = 0; j < fineCount; j++)
            {
                Vector3 t;
                if (j == 0)                   t = fineWorldPos[0] - fineWorldPos[1];
                else if (j == fineCount - 1)  t = fineWorldPos[j - 1] - fineWorldPos[j];
                else                          t = fineWorldPos[j - 1] - fineWorldPos[j + 1];
                if (t.sqrMagnitude < 1e-10f) t = lastHeadTangent;
                if (t.sqrMagnitude < 1e-10f) t = Vector3.right;
                fineTangent[j] = t.normalized;
            }

            // --- Parallel-transport TNB frame along the fine tangents.
            Vector3 refUp = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(fineTangent[0], refUp)) > 0.99f) refUp = Vector3.forward;
            Vector3 b0 = Vector3.Cross(fineTangent[0], refUp);
            if (b0.sqrMagnitude < 1e-10f) b0 = Vector3.Cross(fineTangent[0], Vector3.forward);
            b0.Normalize();
            Vector3 n0 = Vector3.Cross(b0, fineTangent[0]).normalized;
            fineBinormal[0] = b0;
            fineNormal[0]   = n0;
            for (int j = 1; j < fineCount; j++)
            {
                Vector3 tPrev = fineTangent[j - 1];
                Vector3 tCur  = fineTangent[j];
                Vector3 axis  = Vector3.Cross(tPrev, tCur);
                float axisSq = axis.sqrMagnitude;
                Vector3 nPrev = fineNormal[j - 1];
                Vector3 bPrev = fineBinormal[j - 1];
                if (axisSq < 1e-8f)
                {
                    fineNormal[j]   = nPrev;
                    fineBinormal[j] = bPrev;
                }
                else
                {
                    float cosA = Mathf.Clamp(Vector3.Dot(tPrev, tCur), -1f, 1f);
                    float angDeg = Mathf.Acos(cosA) * Mathf.Rad2Deg;
                    Quaternion q = Quaternion.AngleAxis(angDeg, axis / Mathf.Sqrt(axisSq));
                    fineNormal[j]   = (q * nPrev).normalized;
                    fineBinormal[j] = (q * bPrev).normalized;
                }
            }

            // --- Ring vertices: vert index = j*R + r for fine sample j, radial slot r.
            for (int j = 0; j < fineCount; j++)
            {
                float u = fineCount > 1 ? j / (float)(fineCount - 1) : 0f;
                float ri = fineRadius[j];
                Vector3 nrm = fineNormal[j];
                Vector3 bin = fineBinormal[j];
                Vector3 center = fineWorldPos[j];
                for (int r = 0; r < R; r++)
                {
                    float ang = (2f * Mathf.PI * r) / R;
                    Vector3 offsetWorld = (Mathf.Cos(ang) * bin + Mathf.Sin(ang) * nrm) * ri;
                    Vector3 local = transform.InverseTransformPoint(center + offsetWorld);
                    scratchVerts.Add(local);
                    scratchUVs.Add(new Vector2(u, r / (float)R));
                }
            }

            // Cap centers, pushed half a segmentLength past each end along the end tangent.
            int frontCapIdx = fineCount * R;
            int backCapIdx  = fineCount * R + 1;
            Vector3 frontCapWorld = fineWorldPos[0]             + fineTangent[0]             * (segmentLength * 0.5f);
            Vector3 backCapWorld  = fineWorldPos[fineCount - 1] - fineTangent[fineCount - 1] * (segmentLength * 0.5f);
            scratchVerts.Add(transform.InverseTransformPoint(frontCapWorld));
            scratchUVs.Add(new Vector2(0f, 0.5f));
            scratchVerts.Add(transform.InverseTransformPoint(backCapWorld));
            scratchUVs.Add(new Vector2(1f, 0.5f));

            // Body bands between fine ring j and j+1. Submesh routing uses the starting sample's
            // source spine band flag so tinting boundaries follow the coarse on-plane transitions.
            for (int j = 0; j < fineCount - 1; j++)
            {
                int bandSrc = fineSpineBand[j];
                var tris = segOnPlane[bandSrc] ? scratchTrisOn : scratchTrisOff;
                int ringA = j * R;
                int ringB = (j + 1) * R;
                for (int r = 0; r < R; r++)
                {
                    int rNext = (r + 1) % R;
                    int a0 = ringA + r;
                    int a1 = ringA + rNext;
                    int bb0 = ringB + r;
                    int bb1 = ringB + rNext;
                    tris.Add(a0); tris.Add(bb1); tris.Add(bb0);
                    tris.Add(a0); tris.Add(a1);  tris.Add(bb1);
                }
            }

            // Front cap fan — submesh keyed by head segment's on-plane flag.
            {
                var tris = segOnPlane[0] ? scratchTrisOn : scratchTrisOff;
                for (int r = 0; r < R; r++)
                {
                    int rNext = (r + 1) % R;
                    tris.Add(frontCapIdx); tris.Add(r); tris.Add(rNext);
                }
            }

            // Back cap fan — submesh keyed by tail segment's on-plane flag.
            {
                var tris = segOnPlane[n - 1] ? scratchTrisOn : scratchTrisOff;
                int tailRing = (fineCount - 1) * R;
                for (int r = 0; r < R; r++)
                {
                    int rNext = (r + 1) % R;
                    tris.Add(backCapIdx); tris.Add(tailRing + rNext); tris.Add(tailRing + r);
                }
            }

            bodyMesh.Clear();
            if (scratchVerts.Count > 0)
            {
                bodyMesh.indexFormat = scratchVerts.Count > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16;
                bodyMesh.SetVertices(scratchVerts);
                bodyMesh.subMeshCount = 2;
                bodyMesh.SetTriangles(scratchTrisOn, 0);
                bodyMesh.SetTriangles(scratchTrisOff, 1);
                bodyMesh.SetUVs(0, scratchUVs);
                bodyMesh.RecalculateNormals();
                bodyMesh.RecalculateBounds();
            }
        }

        void ApplyBodyMeshColor()
        {
            if (bodyMeshRenderer == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();

            // Submesh 0 = on-plane (collision-active) → bodyColor.
            bodyMeshRenderer.GetPropertyBlock(mpb, 0);
            mpb.SetColor(BaseColorID, bodyColor);
            bodyMeshRenderer.SetPropertyBlock(mpb, 0);

            // Submesh 1 = off-plane (no collision, just visual) → offPlaneColor.
            bodyMeshRenderer.GetPropertyBlock(mpb, 1);
            mpb.SetColor(BaseColorID, offPlaneColor);
            bodyMeshRenderer.SetPropertyBlock(mpb, 1);
        }

        void RebuildCollider()
        {
            if (poly == null) return;
            int n = SegmentCount;
            if (n < 1) { poly.pathCount = 0; return; }

            scratchPaths.Clear();

            // Walk on-plane runs same as the mesh, emitting a single multi-vertex polygon per
            // run. Caps extend half a segmentLength past head (front) and tail (back), so the
            // collider covers the full footprint of the visual — no more "slime falls off the
            // front of the head" gap. Per-anchor half-width is read from segHalfW so the
            // collider tapers along with the visual tube.
            int runStart = -1;
            for (int i = 0; i <= n; i++)
            {
                bool end = (i == n);
                bool onPlane = !end && segOnPlane[i];
                if (onPlane && runStart < 0) runStart = i;
                else if ((!onPlane || end) && runStart >= 0)
                {
                    EmitColliderRun(runStart, i - 1);
                    runStart = -1;
                }
            }

            poly.pathCount = scratchPaths.Count;
            for (int k = 0; k < scratchPaths.Count; k++) poly.SetPath(k, scratchPaths[k]);
        }

        void EmitColliderRun(int s, int e)
        {
            int count = e - s + 1;
            if (count < 1) return;

            bool capFront = (s == 0);
            bool capBack  = (e == SegmentCount - 1);

            // Build ordered list of spine anchor points (local XY) with a cap at each end.
            int anchorCount = count + (capFront ? 1 : 0) + (capBack ? 1 : 0);
            Vector2[] anchors = new Vector2[anchorCount];
            Vector2[] perps   = new Vector2[anchorCount];
            float[]   halfWs  = new float[anchorCount];

            int slot = 0;
            if (capFront)
            {
                Vector2 tan0 = new Vector2(segPerp[s].y, -segPerp[s].x);
                anchors[slot] = segXY[s] + tan0 * (segmentLength * 0.5f);
                perps[slot] = segPerp[s];
                halfWs[slot] = segHalfW[s];
                slot++;
            }
            for (int i = s; i <= e; i++)
            {
                anchors[slot] = segXY[i];
                perps[slot] = segPerp[i];
                halfWs[slot] = segHalfW[i];
                slot++;
            }
            if (capBack)
            {
                Vector2 tanN = new Vector2(segPerp[e].y, -segPerp[e].x);
                anchors[slot] = segXY[e] - tanN * (segmentLength * 0.5f);
                perps[slot] = segPerp[e];
                halfWs[slot] = segHalfW[e];
                slot++;
            }

            // Outline: walk +perp side forward, then -perp side backward. 2*anchorCount points.
            Vector2[] path = new Vector2[anchorCount * 2];
            for (int k = 0; k < anchorCount; k++)
                path[k] = anchors[k] + perps[k] * halfWs[k];
            for (int k = 0; k < anchorCount; k++)
            {
                int idx = anchorCount - 1 - k;
                path[anchorCount + k] = anchors[idx] - perps[idx] * halfWs[idx];
            }
            scratchPaths.Add(path);
        }

        // ---- Rider carry ----

        void UpdateRiderCarry()
        {
            if (rider == null)
            {
                rider = FindAnyObjectByType<SlimeController>();
                if (rider != null) riderRb = rider.GetComponent<Rigidbody2D>();
            }
            if (rider == null || riderRb == null) return;

            // SurfaceCollider alone isn't enough — SurfaceProbe sets it whenever the slime
            // overlaps our polygon, including while the slime is Airborne and just grazing the
            // underside. Carrying in that case MovePosition's the slime along the snake's surface
            // delta, overriding velocity integration — slime appears to stick and drift upward
            // with the snake despite a large downward rb.linearVelocity. Gate on state to only
            // carry when the slime is actually resting on us.
            bool restingOnSnake = rider.CurrentState == SlimeController.SlimeState.Attached
                                  || rider.CurrentState == SlimeController.SlimeState.Charging;
            bool attached = restingOnSnake && rider.SurfaceCollider == poly;
            if (!attached)
            {
                riderAttachedPrev = false;
                return;
            }

            int n = SegmentCount;
            if (n < 2) { riderAttachedPrev = attached; return; }

            if (!riderAttachedPrev)
            {
                // Find closest segment in XY; use that as the arc offset from head.
                Vector2 rp = rider.Position;
                int bestIdx = 0;
                float bestD2 = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    Vector2 sp = new Vector2(segPos3D[i].x, segPos3D[i].y);
                    float d2 = (sp - rp).sqrMagnitude;
                    if (d2 < bestD2) { bestD2 = d2; bestIdx = i; }
                }
                riderArcOffsetFromHead = bestIdx * segmentLength;
                riderAttachedPrev = true;
                return; // no carry on attach frame
            }

            // Carry by the spine-surface delta at the rider's cached arc offset.
            Vector2 prev = SampleSegArrayXY(segPosPrev3D, riderArcOffsetFromHead);
            Vector2 cur  = SampleSegArrayXY(segPos3D,     riderArcOffsetFromHead);
            Vector2 delta = cur - prev;
            if (delta.sqrMagnitude > 1e-10f)
                riderRb.MovePosition(riderRb.position + delta);
        }

        Vector2 SampleSegArrayXY(Vector3[] arr, float arcOffsetFromHead)
        {
            int n = arr.Length;
            float t = Mathf.Clamp(arcOffsetFromHead / segmentLength, 0f, n - 1);
            int lo = Mathf.Clamp(Mathf.FloorToInt(t), 0, n - 1);
            int hi = Mathf.Clamp(lo + 1, 0, n - 1);
            float f = t - lo;
            Vector3 a = arr[lo];
            Vector3 b = arr[hi];
            return new Vector2(Mathf.Lerp(a.x, b.x, f), Mathf.Lerp(a.y, b.y, f));
        }

        // ---- Gizmos ----

        void OnDrawGizmos()
        {
            if (!drawGizmos) return;
            if (segPos3D == null || segPos3D.Length == 0) return;

            // XY spine
            Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.9f);
            for (int i = 0; i < segPos3D.Length - 1; i++)
            {
                Vector3 a = new Vector3(segPos3D[i].x,     segPos3D[i].y,     0f);
                Vector3 b = new Vector3(segPos3D[i + 1].x, segPos3D[i + 1].y, 0f);
                Gizmos.DrawLine(a, b);
            }

            // Z offset indicator — stems from XY spine to 3D pos
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.9f);
            for (int i = 0; i < segPos3D.Length; i++)
            {
                Vector3 flat = new Vector3(segPos3D[i].x, segPos3D[i].y, 0f);
                Gizmos.DrawLine(flat, segPos3D[i]);
            }

            // Head-body joint marker — magenta sphere at segPos3D[0], which is exactly where
            // the body tube's front ring sits and where headBodyJointLocal is pinned on the head.
            // Makes it visually obvious where to aim the joint while tweaking in the inspector.
            Gizmos.color = new Color(1f, 0.25f, 0.9f, 0.9f);
            Gizmos.DrawWireSphere(segPos3D[0], 0.08f);
        }
    }
}
