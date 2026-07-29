using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Platforms
{
    /// <summary>
    /// Head advances along an ISnakePath; body follows via a trail-vs-chain blend (followness).
    /// One root PolygonCollider2D gets one path per on-plane segment run. Rider carry is custom:
    /// arc-offset-from-head captured on attach, MovePosition by the surface delta each FixedUpdate.
    /// </summary>
    [ExecuteAlways]
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
        [Tooltip("Optional prefab. If Head Visual is empty, this is instantiated as a child of the snake at Awake and assigned as the head. Author the prefab at its final size — SnakeSpine only writes position + rotation, never scale. Author forward = +X so the 2D yaw rotation lines it up with the motion direction, and put the visible face on the +Z side (toward the camera).")]
        [SerializeField] GameObject headPrefab;
        [Tooltip("Optional renderer to receive the on-plane / off-plane tint via MaterialPropertyBlock. Use this to target only the head's skin material when the head has multiple child renderers (e.g., eyes and mouth that should keep their own color). If left empty, we fall back to a MeshRenderer on headVisual's root, if any.")]
        [SerializeField] Renderer headSkinRenderer;
        [Tooltip("Local Euler offset applied to headVisual BEFORE the motion yaw, letting you realign a prefab whose forward axis isn't +X. " +
                 "Final rotation = yaw(Z) * Euler(offset). Examples — prefab faces +Y: (0,0,-90). Prefab faces -X: (0,0,180). " +
                 "3D prefab whose face points +Z (out of the screen): (0,-90,0) brings the face onto +X. Tweak until the eyes face the motion direction.")]
        [SerializeField] Vector3 headRotationOffsetEuler;
        [Tooltip("Point on the head (in head-local space AFTER Head Rotation Offset is applied; X=forward along motion, Y=side, Z=toward camera) where the body tube meets the head. The head visual is positioned so this point lands exactly on the path arc — i.e., the body's front ring is always flush with this spot on the head, closing the neck gap. Default (0,0,0) sits the head pivot on the path. Example: placeholder cube extends ±0.5 in local X, so (-0.5, 0, 0) joins the body at the head's back face. Read every FixedUpdate — tweak in play mode to find the right spot live.")]
        [SerializeField] Vector3 headBodyJointLocal;
        [Tooltip("If on, the head rotation is built as an orthonormal basis with +X = motion direction and +Y projected onto world-up, so the head NEVER flips upside-down on curves that reverse horizontally. Cost: when the snake moves leftward (motion.x < 0), the head's +Z side rotates away from the camera (the 'face' disappears from view) — you can't keep +X=motion, +Y=world-up, and +Z=toward-camera all at once without mirroring. If off, falls back to pure Z-axis yaw (face stays toward camera, but head flips upside-down for motion.x < 0). When motion is near-vertical this mode uses world +Z as the up fallback so the head doesn't pop through the degenerate case.")]
        [SerializeField] bool keepHeadUpright = true;
        [Tooltip("Number of body segments behind the head. Body is rendered as a single dynamic ribbon mesh sweeping this many spine samples; changing this value resizes the ribbon live.")]
        [Min(1)] [SerializeField] int bodySegmentCount = 8;

        [Header("Shape")]
        [Tooltip("Arc-length spacing between adjacent segment centers (meters).")]
        [Min(0.01f)] [SerializeField] float segmentLength = 1f;
        [Tooltip("Base radius of the tube cross-section at the head (meters). Per-segment collider half-width tracks bodyRadius × radiusProfile so the polygon collider tapers with the visual tube.")]
        [Min(0.01f)] [SerializeField] float bodyRadius = 0.3f;
        [Tooltip("Radius multiplier sampled at i/(N-1): 0 = head-adjacent, 1 = tail. Default 1→0.25 tapers toward the tail.")]
        [SerializeField] AnimationCurve radiusProfile = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.25f));
        [Tooltip("Cross-section sides. 8 reads as round at normal camera distances.")]
        [Range(3, 24)] [SerializeField] int radialSegments = 8;
        [Tooltip("Visual rings emitted between adjacent spine anchors. 1 = one ring per spine segment (sharp folds at bends). 4 is a good default — Catmull-Rom interpolates the spine so the tube bends smoothly without changing the collider or simulation (collider stays per-segment).")]
        [Range(1, 8)] [SerializeField] int visualRingsPerSegment = 4;

        [Header("Material")]
        [Tooltip("Single material shared by BOTH the head and the procedural body tube, so the snake reads as one continuous creature. " +
                 "Assign the project's Quibli (Quibli/Stylized Lit) white material here. If left empty, the body falls back to whatever " +
                 "material the head already uses, so the two still match. SnakeSpine drives the head's renderer to this material at runtime " +
                 "and in the edit preview — the head prefab's own material is overridden, not edited.")]
        [SerializeField] Material snakeMaterial;

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

        [Header("Editor Preview")]
        [Tooltip("Edit-mode only. When on, the static scene-view preview lays the snake as a straight " +
                 "line along Preview Straight Dir (instead of following the path's tangent at the start), " +
                 "so the head and body are perfectly collinear and head↔body alignment (headBodyJointLocal, " +
                 "headRotationOffsetEuler) is easy to dial in. Has no effect at runtime.")]
        [SerializeField] bool editorPreviewStraight = true;
        [Tooltip("World-space direction the head faces in the straight edit preview. Body extends opposite. " +
                 "Default (1,0,0) = head points right, body to the left — a clean horizontal reference.")]
        [SerializeField] Vector3 editorPreviewStraightDir = Vector3.right;

        [Header("Debug")]
        [SerializeField] bool drawGizmos = true;
        [Tooltip("If on, logs spine/mesh/collider state to the console once per second during play.")]
        [SerializeField] bool debugLog = false;
        float debugLogAccum;

        // ---- Runtime state ----

        ISnakePath path;
        PolygonCollider2D poly;

        // Prefab-instantiated head, kept out of the serialized headVisual field — writing it there
        // dirties the scene and leaks a reference to a non-persistent preview object.
        Transform headInstance;
        Transform ActiveHead => headVisual != null ? headVisual : headInstance;

        float headArcLen;
        Vector3 lastHeadTangent = Vector3.right;

        struct TrailSample { public Vector3 worldPos; public float arcStamp; }
        readonly List<TrailSample> trail = new List<TrailSample>();

        // Segment 3D targets. Index 0 = head, 1..N-1 = body.
        Vector3[] segPos3D;
        Vector3[] segPosPrev3D;
        int SegmentCount => 1 + Mathf.Max(0, bodySegmentCount);

        // Dynamic body ribbon — one MeshRenderer under the root, rebuilt every FixedUpdate.
        GameObject bodyMeshGO;
        MeshFilter bodyMeshFilter;
        MeshRenderer bodyMeshRenderer;
        Mesh bodyMesh;
        readonly List<Vector3> scratchVerts  = new List<Vector3>();
        readonly List<int>     scratchTris   = new List<int>();  // single submesh — uniform snake material, no two-tone
        readonly List<Vector2> scratchUVs    = new List<Vector2>();

        // Scratch XY & perpendicular per segment, reused each frame for mesh + collider.
        Vector2[] segXY;
        Vector2[] segPerp;
        bool[]    segOnPlane;

        // Per-segment half-width (meters). Drives the collider's perp extrusion; the visual tube
        // interpolates its own radii from these.
        float[] segHalfW;

        // Fine samples rebuilt each frame in RebuildBodyMesh: Catmull-Rom positions on segPos3D,
        // TNB parallel-transported so adjacent rings share a twist-free basis.
        Vector3[] fineWorldPos;
        Vector3[] fineTangent;
        Vector3[] fineNormal;
        Vector3[] fineBinormal;
        float[]   fineRadius;

        // Scratch for poly.SetPath calls — reused per frame.
        readonly List<Vector2[]> scratchPaths = new List<Vector2[]>();

        // Rider carry state.
        SlimeController rider;
        Rigidbody2D riderRb;
        bool riderAttachedPrev;
        float riderArcOffsetFromHead;

        // Fallback material, static so we don't leak one per instance. Normally unused.
        static Material _sharedBodyMat;

        // ---- Unity ----

        void Awake()
        {
            // No Rigidbody2D on purpose: a Kinematic body with a per-frame morphing polygon path
            // gives ambiguous depenetration (rider stuck under the body, land/jump flipping).
            poly = GetComponent<PolygonCollider2D>();
            // Edit mode: the collider stays empty (runtime-built) — writing pathCount dirties the scene.
            if (Application.isPlaying) poly.pathCount = 0;

            ResolvePath();
            AllocateSegmentArrays();
            EnsureHeadVisual();
            EnsureBodyMesh();
        }

        void EnsureHeadVisual()
        {
            // Instance goes to headInstance, never the serialized field. SnakeSpine never writes
            // localScale, so the prefab ships at its authored size.
            if (headVisual != null) { headInstance = null; return; }
            if (headPrefab == null) return;
            if (headInstance != null) return; // already have a live instance this session
            var go = Instantiate(headPrefab, transform);
            go.name = headPrefab.name + " (Head Preview)";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            // Edit mode: DontSave keeps the preview instance out of the scene file.
            go.hideFlags = Application.isPlaying ? HideFlags.None : HideFlags.DontSave;
            headInstance = go.transform;
        }

        void Start()
        {
            // Start, not OnEnable: WaypointSnakePath.Rebuild runs in Awake — seeding earlier reads
            // a half-initialized path and spawns the body at the snake origin.
            SeedInitialPose();
        }

        void Update()
        {
            // Unity never calls FixedUpdate in edit mode, so rebuild a static preview here — the
            // scene view then shows the shipped head/body join while tuning the joint offsets.
            if (Application.isPlaying) return;
            SeedInitialPose();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
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
            // Edit mode: DontSave keeps the preview ribbon out of the scene file.
            bodyMeshGO.hideFlags = Application.isPlaying ? HideFlags.None : HideFlags.DontSave;
            bodyMeshFilter = bodyMeshGO.GetComponent<MeshFilter>();
            if (bodyMeshFilter == null) bodyMeshFilter = bodyMeshGO.AddComponent<MeshFilter>();
            bodyMeshRenderer = bodyMeshGO.GetComponent<MeshRenderer>();
            if (bodyMeshRenderer == null) bodyMeshRenderer = bodyMeshGO.AddComponent<MeshRenderer>();
            if (bodyMesh == null)
            {
                bodyMesh = new Mesh { name = "SnakeBodyMesh" };
                bodyMesh.MarkDynamic();
                // Procedural mesh has no asset backing — DontSave avoids an edit-mode leak warning.
                bodyMesh.hideFlags = HideFlags.DontSave;
            }
            bodyMeshFilter.sharedMesh = bodyMesh;

            ApplyMaterials();
        }

        /// <summary>Material for the whole snake: snakeMaterial → head's own material → shared URP Lit fallback.</summary>
        Material ResolveSnakeMaterial()
        {
            if (snakeMaterial != null) return snakeMaterial;

            Transform head = ActiveHead;
            if (head != null)
            {
                Renderer hr = headSkinRenderer != null ? headSkinRenderer : head.GetComponentInChildren<MeshRenderer>();
                if (hr != null && hr.sharedMaterial != null) return hr.sharedMaterial;
            }

            if (_sharedBodyMat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader != null)
                    _sharedBodyMat = new Material(shader) { name = "SnakeBody (fallback)", hideFlags = HideFlags.DontSave };
            }
            return _sharedBodyMat;
        }

        /// <summary>Drives body tube and head renderers to one material; guarded on inequality so the
        /// edit-mode preview doesn't re-dirty the scene every frame.</summary>
        void ApplyMaterials()
        {
            Material mat = ResolveSnakeMaterial();
            if (mat == null) return;

            if (bodyMeshRenderer != null)
            {
                var bm = bodyMeshRenderer.sharedMaterials;
                if (bm == null || bm.Length != 1 || bm[0] != mat)
                    bodyMeshRenderer.sharedMaterials = new Material[] { mat };
            }

            // Every material slot of every child renderer, else a multi-submesh head leaves stray
            // slots their original color.
            Transform head = ActiveHead;
            if (head != null)
            {
                var rends = head.GetComponentsInChildren<MeshRenderer>(true);
                for (int i = 0; i < rends.Length; i++)
                {
                    var slots = rends[i].sharedMaterials;
                    if (slots == null || slots.Length == 0) { rends[i].sharedMaterial = mat; continue; }
                    bool needs = false;
                    for (int k = 0; k < slots.Length; k++) if (slots[k] != mat) { needs = true; break; }
                    if (!needs) continue;
                    for (int k = 0; k < slots.Length; k++) slots[k] = mat;
                    rends[i].sharedMaterials = slots;
                }
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
            }
        }

        void SeedInitialPose()
        {
            ResolvePath();
            AllocateSegmentArrays();
            // Edit-mode Update path: a domain reload can wipe these, so re-ensure every tick.
            EnsureHeadVisual();
            EnsureBodyMesh();
            if (path == null || SegmentCount < 1) return;

            headArcLen = 0f;
            Vector3 headPos, headTan;
            path.SampleWorld(headArcLen, out headPos, out headTan);

            // Finite-arc step, not the analytic Catmull-Rom tangent: at a corner waypoint with a
            // far previous control point the tangent can point backward, laying the body ahead.
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

#if UNITY_EDITOR
            // Edit preview only: a straight seed direction makes head↔body alignment tunable.
            if (!Application.isPlaying && editorPreviewStraight)
            {
                Vector3 d = editorPreviewStraightDir;
                if (d.sqrMagnitude > 1e-8f) lastHeadTangent = d.normalized;
            }
#endif

            int n = SegmentCount;
            segPos3D[0] = headPos;
            // Straight line behind the head, not back-sampled along the loop — a short/tight loop
            // would place the tail next to the head.
            for (int i = 1; i < n; i++)
                segPos3D[i] = headPos - lastHeadTangent * (i * segmentLength);
            System.Array.Copy(segPos3D, segPosPrev3D, n);

            // Pre-fill the trail so followness=1 lookups resolve on frame 1.
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
            ApplyMaterials();
            // Runtime only — building in edit mode serializes the path and dirties the scene.
            if (Application.isPlaying) RebuildCollider();
        }

        void FixedUpdate()
        {
            if (path == null) return;
            int n = SegmentCount;
            if (n < 1) return;

            float dt = Time.fixedDeltaTime;

            // One SpeedAt query per step; the error across waypoint boundaries at 50Hz doesn't
            // justify sub-stepping.
            float speed = path.SpeedAt(headArcLen);
            headArcLen += speed * dt;

            Vector3 headPos, headTan;
            path.SampleWorld(headArcLen, out headPos, out headTan);
            if (headTan.sqrMagnitude > 1e-8f) lastHeadTangent = headTan.normalized;

            // Snapshot prev positions before we overwrite (for rider carry delta).
            System.Array.Copy(segPos3D, segPosPrev3D, n);

            trail.Add(new TrailSample { worldPos = headPos, arcStamp = headArcLen });
            float oldestNeeded = headArcLen - (n + 2) * segmentLength;
            int drop = 0;
            while (drop < trail.Count - 1 && trail[drop + 1].arcStamp < oldestNeeded) drop++;
            if (drop > 0) trail.RemoveRange(0, drop);

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
            ApplyMaterials();
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

            // Bracket (i-1, i) straddling arcStamp, scanned from the tail. Both bounds must be
            // tested — matching only the upper one extrapolates from the two newest entries.
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

        /// <summary>Local XY, smoothed perpendicular and on-plane flag per segment, shared by mesh and
        /// collider. Perp comes from the central-difference tangent so neighbours agree — no kinks at bends.</summary>
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

        /// <summary>Per-segment collider half-width from radiusProfile; kept out of the mesh builder
        /// because the collider needs it every frame even when the mesh rebuild is skipped.</summary>
        void ComputeSegmentHalfWidths()
        {
            int n = SegmentCount;
            for (int i = 0; i < n; i++)
            {
                float profileT = n > 1 ? i / (float)(n - 1) : 0f;
                segHalfW[i] = Mathf.Max(0.001f, bodyRadius * radiusProfile.Evaluate(profileT));
            }
        }

        // Catmull-Rom (tension 0.5). Duplicated from WaypointSnakePath to avoid a cross-class dep.
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
            Transform head = ActiveHead;
            if (head == null || n < 1) return;

            Quaternion baseYaw;
            if (keepHeadUpright)
            {
                // +X = motion tangent, +Y projected onto world-up so the head never flips. Cost:
                // +Z rotates away from the camera on leftward motion — can't have all three.
                Vector3 motionDir = new Vector3(lastHeadTangent.x, lastHeadTangent.y, 0f);
                if (motionDir.sqrMagnitude < 1e-10f) motionDir = Vector3.right;
                motionDir.Normalize();
                Vector3 yCandidate = Vector3.up - Vector3.Dot(Vector3.up, motionDir) * motionDir;
                if (yCandidate.sqrMagnitude < 1e-6f)
                {
                    // Motion (near-)parallel to world-up — yLocal undefined; world +Z is the
                    // least-bad "up" here.
                    yCandidate = Vector3.forward;
                }
                yCandidate.Normalize();
                Vector3 zLocal = Vector3.Cross(motionDir, yCandidate).normalized;
                baseYaw = Quaternion.LookRotation(zLocal, yCandidate);
            }
            else
            {
                float rotDeg = Mathf.Atan2(lastHeadTangent.y, lastHeadTangent.x) * Mathf.Rad2Deg;
                baseYaw = Quaternion.Euler(0f, 0f, rotDeg);
            }
            // Right-multiply: the prefab's authored forward is rotated onto +X first, then yaw.
            Quaternion targetRot = baseYaw * Quaternion.Euler(headRotationOffsetEuler);
            // headBodyJointLocal lands on the path sample, where the body tube's front ring sits,
            // which is what closes the neck gap. Scale is never written.
            Vector3 targetPos = segPos3D[0] - targetRot * headBodyJointLocal;

            if (Application.isPlaying)
            {
                head.rotation = targetRot;
                head.position = targetPos;
            }
            else
            {
                // Edit-mode preview snap. Only write when the pose differs, else a serialized head
                // re-dirties the scene every editor frame.
                if ((head.position - targetPos).sqrMagnitude > 1e-8f) head.position = targetPos;
                if (Quaternion.Angle(head.rotation, targetRot) > 0.01f)  head.rotation = targetRot;
            }
        }

        /// <summary>Tube swept along the spine: visualRingsPerSegment Catmull-Rom rings per band, TNB frame
        /// parallel-transported so the radial basis stays twist-free at sharp turns. Single submesh.</summary>
        void RebuildBodyMesh()
        {
            if (bodyMesh == null) return;
            int n = SegmentCount;
            int R = Mathf.Max(3, radialSegments);
            int K = Mathf.Max(1, visualRingsPerSegment);
            scratchVerts.Clear();
            scratchTris.Clear();
            scratchUVs.Clear();

            if (n < 2) { bodyMesh.Clear(); return; }

            int fineCount = (n - 1) * K + 1;
            EnsureFineArrays(fineCount);

            // Virtual endpoint mirroring (p0/p3 reflected) — without it the tube creases at the
            // head-adjacent and tail rings.
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
                }
            }
            // Closing sample at the tail.
            fineWorldPos[fineCount - 1]  = segPos3D[n - 1];
            fineRadius[fineCount - 1]    = segHalfW[n - 1];

            // Head-ward convention: tangent[j] points from j+1 toward j-1, matching the 2D perp
            // convention so +tangent is "forward" off the head for the cap offsets.
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

            // Ring vertices: vert index = j*R + r for fine sample j, radial slot r.
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

            for (int j = 0; j < fineCount - 1; j++)
            {
                int ringA = j * R;
                int ringB = (j + 1) * R;
                for (int r = 0; r < R; r++)
                {
                    int rNext = (r + 1) % R;
                    int a0 = ringA + r;
                    int a1 = ringA + rNext;
                    int bb0 = ringB + r;
                    int bb1 = ringB + rNext;
                    scratchTris.Add(a0); scratchTris.Add(bb1); scratchTris.Add(bb0);
                    scratchTris.Add(a0); scratchTris.Add(a1);  scratchTris.Add(bb1);
                }
            }

            // Front cap fan.
            for (int r = 0; r < R; r++)
            {
                int rNext = (r + 1) % R;
                scratchTris.Add(frontCapIdx); scratchTris.Add(r); scratchTris.Add(rNext);
            }

            // Back cap fan.
            {
                int tailRing = (fineCount - 1) * R;
                for (int r = 0; r < R; r++)
                {
                    int rNext = (r + 1) % R;
                    scratchTris.Add(backCapIdx); scratchTris.Add(tailRing + rNext); scratchTris.Add(tailRing + r);
                }
            }

            bodyMesh.Clear();
            if (scratchVerts.Count > 0)
            {
                bodyMesh.indexFormat = scratchVerts.Count > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16;
                bodyMesh.SetVertices(scratchVerts);
                bodyMesh.subMeshCount = 1;
                bodyMesh.SetTriangles(scratchTris, 0);
                bodyMesh.SetUVs(0, scratchUVs);
                bodyMesh.RecalculateNormals();
                bodyMesh.RecalculateBounds();
            }
        }

        void RebuildCollider()
        {
            if (poly == null) return;
            int n = SegmentCount;
            if (n < 1) { poly.pathCount = 0; return; }

            scratchPaths.Clear();

            // One polygon per on-plane run, with caps half a segmentLength past head and tail so
            // the collider covers the visual footprint — otherwise the slime falls off the head.
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

            // SurfaceCollider alone isn't enough — SurfaceProbe sets it while the slime is Airborne
            // and merely grazing us; carrying then overrides velocity and the slime drifts upward.
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

            // Head-body joint marker at segPos3D[0] — where the body's front ring and
            // headBodyJointLocal meet.
            Gizmos.color = new Color(1f, 0.25f, 0.9f, 0.9f);
            Gizmos.DrawWireSphere(segPos3D[0], 0.08f);
        }
    }
}
