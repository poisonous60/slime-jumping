using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlimeJumping.Rendering
{
    /// One per moving/rotating platform: owns a local-space trail RT and feeds it to the
    /// renderer's MaterialPropertyBlock. Paint frame is THIS transform, not the renderer's —
    /// for a 3D-tilted mesh put this on an unrotated parent to keep the paint plane on world XY.
    [AddComponentMenu("Rendering/Trail/Local Trail Receiver")]
    public class LocalTrailReceiver : MonoBehaviour, ITrailTarget
    {
        [Header("Local-space bounds (padded ~0.2 beyond visible platform)")]
        [SerializeField] private Vector2 localMin  = new Vector2(-1.5f, -0.5f);
        [SerializeField] private Vector2 localSize = new Vector2(3.0f, 1.0f);
        [Range(4, 80)]
        [SerializeField] private int pixelsPerUnit = 20;
        [Tooltip("Multiplies every stamp landing on this platform. >1 = goo reads stronger here " +
                 "(e.g. hidden platforms where the trail is the only always-visible hint).")]
        [Min(0f)]
        [SerializeField] private float stampStrengthMultiplier = 1f;
        [Tooltip("Multiplies every stamp's radius on this platform. The global landing radius is " +
                 "tuned for terrain — on a platform a few units wide it covers a third per landing; " +
                 "<1 shrinks marks back to distinct blobs.")]
        [Min(0f)]
        [SerializeField] private float stampRadiusMultiplier = 1f;

        [Header("Routing")]
        [Tooltip("The platform's physics collider — the one SlimeController detects on contact. " +
                 "On split-GameObject platforms (visual rotates, collider static), drag it here. " +
                 "Left empty → auto-resolved via GetComponentInParent on Awake/OnValidate.")]
        [SerializeField] private Collider2D contactCollider;

        [Tooltip("Renderer that receives the trail mask via MaterialPropertyBlock. Leave empty " +
                 "to auto-resolve (GetComponent, then GetComponentInChildren). Must exist before OnEnable.")]
        [SerializeField] private Renderer targetRenderer;

        [Header("Persistence")]
        [Tooltip("Optional stable GUID. Empty = save key derived from hierarchy path (fragile across renames). " +
                 "Set via context menu 'Trail / Generate Stable ID' for shipping platforms.")]
        [SerializeField] private string overrideId = "";

        private RenderTexture rt;
        // Cold-route RT — same dim/format as warm, split by WorldTrailManager.CurrentFreezeT.
        private RenderTexture rtCold;
        private LocalColdTarget coldTarget;
        private MaterialPropertyBlock mpb;
        private Matrix4x4 lastPushedL2W;
        private Vector2Int resolution;
        // Per route: a shared flag lets whichever target saves first clear the other's, and the
        // persistence round-robin then skips it — the cold file would go stale.
        private bool dirty;
        private bool dirtyCold;
        private static readonly int IdLocalTrailMask     = Shader.PropertyToID("_LocalTrailMask");
        private static readonly int IdLocalTrailMaskCold = Shader.PropertyToID("_LocalTrailMaskCold");
        private static readonly int IdLocalTrailMin      = Shader.PropertyToID("_LocalTrailMin");
        private static readonly int IdLocalTrailSize     = Shader.PropertyToID("_LocalTrailSize");
        private static readonly int IdLocalTrailW2L      = Shader.PropertyToID("_LocalTrailW2L");

        public Collider2D ContactCollider => contactCollider;
        public Renderer TargetRenderer => targetRenderer;
        public float StampStrengthMultiplier
        {
            get => stampStrengthMultiplier;
            set => stampStrengthMultiplier = Mathf.Max(0f, value);
        }
        public float StampRadiusMultiplier
        {
            get => stampRadiusMultiplier;
            set => stampRadiusMultiplier = Mathf.Max(0f, value);
        }
        public Vector2 LocalMin => localMin;
        public Vector2 LocalSize => localSize;
        public int PixelsPerUnit => pixelsPerUnit;
        public string OverrideId => overrideId;

        // 512² R8 ≈ 256 KB per platform; ground-sized RTs belong on WorldTrailManager instead.
        public const int MaxResolutionPerSide = 512;

        public Vector2Int PlannedResolution
        {
            get
            {
                // pixelsPerUnit is per WORLD unit, so lossyScale must be folded in — otherwise
                // a scaled parent stretches a tiny RT across a large visible area.
                Vector3 ls = transform.lossyScale;
                int w = Mathf.CeilToInt(localSize.x * Mathf.Abs(ls.x) * pixelsPerUnit);
                int h = Mathf.CeilToInt(localSize.y * Mathf.Abs(ls.y) * pixelsPerUnit);
                return new Vector2Int(
                    Mathf.Clamp(w, 1, MaxResolutionPerSide),
                    Mathf.Clamp(h, 1, MaxResolutionPerSide));
            }
        }

        // Unclamped dimensions — lets OnValidate warn when the cap silently drops resolution.
        public Vector2Int RawPlannedResolution
        {
            get
            {
                Vector3 ls = transform.lossyScale;
                int w = Mathf.CeilToInt(localSize.x * Mathf.Abs(ls.x) * pixelsPerUnit);
                int h = Mathf.CeilToInt(localSize.y * Mathf.Abs(ls.y) * pixelsPerUnit);
                return new Vector2Int(Mathf.Max(1, w), Mathf.Max(1, h));
            }
        }

        // -------------------- ITrailTarget --------------------

        public RenderTexture Rt => rt;

        public string SaveKey
        {
            get
            {
                if (!string.IsNullOrEmpty(overrideId)) return "local_" + overrideId;
                int sceneIdx = SceneManager.GetActiveScene().buildIndex;
                return "local_" + sceneIdx + "_" + HierarchyPath(transform);
            }
        }

        public Vector2Int Resolution => resolution;
        public bool Dirty => dirty;
        public void MarkDirty() => dirty = true;
        public void ClearDirty() => dirty = false;

        public TrailTargetMeta BuildMeta()
        {
            return new TrailTargetMeta
            {
                version = TrailPersistence.SaveVersion,
                resolutionX = resolution.x,
                resolutionY = resolution.y,
                originX = localMin.x,
                originY = localMin.y,
                sizeX = localSize.x,
                sizeY = localSize.y,
                format = TrailPersistence.Format,
            };
        }

        public void OnLoadedInto() { /* nothing extra */ }

        // -------------------- Lifecycle --------------------

        private void Awake()
        {
            ResolveTargetRenderer();
            if (contactCollider == null) contactCollider = GetComponentInParent<Collider2D>();
        }

        private void OnEnable()
        {
            ResolveTargetRenderer();
            if (targetRenderer == null)
            {
                Debug.LogError($"[Trail] LocalTrailReceiver on '{name}' has no targetRenderer. Disabling.", this);
                enabled = false;
                return;
            }

            resolution = PlannedResolution;
            // See WorldTrailManager for the rationale on RenderTextureReadWrite.Linear.
            rt = new RenderTexture(resolution.x, resolution.y, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
                autoGenerateMips = false,
                name = "LocalTrail_" + name,
            };
            rt.Create();
            Graphics.Blit(Texture2D.blackTexture, rt);

            // The black blit is required: a fresh RT holds undefined GPU memory, which shows
            // as garbage tint through the cold sampler in stage1 (no ColdZone).
            rtCold = new RenderTexture(resolution.x, resolution.y, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
                autoGenerateMips = false,
                name = "LocalTrailCold_" + name,
            };
            rtCold.Create();
            Graphics.Blit(Texture2D.blackTexture, rtCold);

            mpb = new MaterialPropertyBlock();
            targetRenderer.GetPropertyBlock(mpb);
            mpb.SetTexture(IdLocalTrailMask,     rt);
            mpb.SetTexture(IdLocalTrailMaskCold, rtCold);
            mpb.SetVector (IdLocalTrailMin,  new Vector4(localMin.x,  localMin.y,  0f, 0f));
            mpb.SetVector (IdLocalTrailSize, new Vector4(localSize.x, localSize.y, 0f, 0f));
            mpb.SetMatrix (IdLocalTrailW2L,  transform.worldToLocalMatrix);
            targetRenderer.SetPropertyBlock(mpb);
            lastPushedL2W = transform.localToWorldMatrix;

            TrailColliderRegistry.Register(contactCollider, this);

            var mgr = WorldTrailManager.Instance;
            if (mgr != null && mgr.Persistence != null)
            {
                mgr.Persistence.Register(this);
                mgr.Persistence.TryLoad(this);

                // Separate ITrailTarget with a distinct save key, so warm-only saves stay loadable.
                coldTarget = new LocalColdTarget(this);
                mgr.Persistence.Register(coldTarget);
                mgr.Persistence.TryLoad(coldTarget);
            }
        }

        private void OnDisable()
        {
            TrailColliderRegistry.Unregister(contactCollider);

            var mgr = WorldTrailManager.Instance;
            if (mgr != null && mgr.Persistence != null)
            {
                // Final flush — a destroyed platform would otherwise lose recorded progress.
                if (dirty)
                {
                    try { mgr.Persistence.SaveSync(this); } catch { /* best-effort */ }
                }
                if (dirtyCold && coldTarget != null)
                {
                    try { mgr.Persistence.SaveSync(coldTarget); } catch { /* best-effort */ }
                }
                mgr.Persistence.Unregister(this);
                if (coldTarget != null) mgr.Persistence.Unregister(coldTarget);
            }

            if (rt != null) { rt.Release(); Object.Destroy(rt); rt = null; }
            if (rtCold != null) { rtCold.Release(); Object.Destroy(rtCold); rtCold = null; }
            coldTarget = null;
        }

        private void LateUpdate()
        {
            // Unity auto-updates unity_WorldToObject for the renderer, but _LocalTrailW2L is a
            // possibly-separate transform, so it must be pushed manually when it moves.
            if (targetRenderer == null || mpb == null) return;
            // Matrix compare, not transform.hasChanged: a moving ancestor never flags the child.
            Matrix4x4 l2w = transform.localToWorldMatrix;
            if (l2w == lastPushedL2W) return;
            lastPushedL2W = l2w;
            targetRenderer.GetPropertyBlock(mpb);
            mpb.SetMatrix(IdLocalTrailW2L, transform.worldToLocalMatrix);
            targetRenderer.SetPropertyBlock(mpb);
        }

        private void ResolveTargetRenderer()
        {
            if (targetRenderer != null) return;
            targetRenderer = GetComponent<Renderer>();
            if (targetRenderer == null) targetRenderer = GetComponentInChildren<Renderer>();
        }

        // -------------------- Stamp API --------------------

        public void StampAt(Vector2 worldPos, float worldRadius, float strength)
        {
            strength *= stampStrengthMultiplier;
            worldRadius *= stampRadiusMultiplier;
            var mgr = WorldTrailManager.Instance;
            if (mgr == null || rt == null) return;

            // Must use the RECEIVER's transform (the one pushed as _LocalTrailW2L), not the
            // renderer's, or stamps land somewhere other than where the shader samples.
            Vector3 lp = transform.InverseTransformPoint(new Vector3(worldPos.x, worldPos.y, 0f));
            Vector2 uv = new Vector2(
                (lp.x - localMin.x) / localSize.x,
                (lp.y - localMin.y) / localSize.y);

            // Skip stamps whose center is well outside this platform's local rect.
            if (uv.x < -0.1f || uv.x > 1.1f || uv.y < -0.1f || uv.y > 1.1f) return;

            // worldRadius is world-space, localSize unscaled-local: without the lossyScale
            // divide, a scaled parent chain makes each stamp cover most of the RT.
            Vector3 ls = transform.lossyScale;
            float sx = Mathf.Max(1e-4f, Mathf.Abs(ls.x));
            float sy = Mathf.Max(1e-4f, Mathf.Abs(ls.y));
            Vector2 radiusUV = new Vector2(
                worldRadius / (localSize.x * sx),
                worldRadius / (localSize.y * sy));

            // Split warm/cold by freezeT (single-slime stage assumption); skip the
            // sub-threshold half so a fully-frozen slime wastes no draw on the warm RT.
            float t = mgr.CurrentFreezeT;
            float warmS = strength * (1f - t);
            float coldS = strength * t;
            if (warmS > 0.001f) { mgr.BlitToTarget(rt, uv, radiusUV, warmS); dirty = true; }
            if (coldS > 0.001f && rtCold != null) { mgr.BlitToTarget(rtCold, uv, radiusUV, coldS); dirtyCold = true; }
        }

        public void ClearTrail()
        {
            if (rt != null) { Graphics.Blit(Texture2D.blackTexture, rt); dirty = true; }
            if (rtCold != null) { Graphics.Blit(Texture2D.blackTexture, rtCold); dirtyCold = true; }
        }

        // -------------------- Editor helpers --------------------

#if UNITY_EDITOR
        private void OnValidate()
        {
            pixelsPerUnit = Mathf.Clamp(pixelsPerUnit, 4, 80);
            stampStrengthMultiplier = Mathf.Max(0f, stampStrengthMultiplier);
            stampRadiusMultiplier = Mathf.Max(0f, stampRadiusMultiplier);
            if (contactCollider == null)
            {
                contactCollider = GetComponentInParent<Collider2D>();
            }
            if (targetRenderer == null) ResolveTargetRenderer();
            var raw = RawPlannedResolution;
            if (raw.x > MaxResolutionPerSide || raw.y > MaxResolutionPerSide)
            {
                Debug.LogWarning($"[Trail] LocalTrailReceiver on '{name}' wants {raw.x}x{raw.y} " +
                                 $"(localSize × lossyScale × PPU) but is clamped to {MaxResolutionPerSide}². " +
                                 "Reduce pixelsPerUnit, or bake the parent scale into the mesh (Blender Ctrl+A) " +
                                 "so lossyScale approaches 1.", this);
            }
        }

        [ContextMenu("Trail / Auto-fit to Renderer Bounds")]
        private void AutoFitBounds()
        {
            if (targetRenderer == null) ResolveTargetRenderer();
            var r = targetRenderer;
            if (r == null) return;

            UnityEditor.Undo.RecordObject(this, "Trail: auto-fit bounds");

            // Prefer collider > mesh > renderer bounds: SkinnedMeshRenderer.bounds is the
            // bone-sweep envelope and overshoots the rest-pose silhouette badly.
            if (!TryGetAutoFitWorldCorners(r, out var corners))
            {
                Debug.LogWarning($"[Trail] Auto-fit: no usable bounds source on '{name}'.", this);
                return;
            }

            Vector2 lmin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 lmax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 lp = transform.InverseTransformPoint(corners[i]);
                if (lp.x < lmin.x) lmin.x = lp.x;
                if (lp.y < lmin.y) lmin.y = lp.y;
                if (lp.x > lmax.x) lmax.x = lp.x;
                if (lp.y > lmax.y) lmax.y = lp.y;
            }

            // 0.2 world units → local via lossyScale, else a scaled parent inflates the margin.
            Vector3 ls = transform.lossyScale;
            float padX = 0.2f / Mathf.Max(1e-4f, Mathf.Abs(ls.x));
            float padY = 0.2f / Mathf.Max(1e-4f, Mathf.Abs(ls.y));
            lmin -= new Vector2(padX, padY);
            lmax += new Vector2(padX, padY);
            localMin = lmin;
            localSize = lmax - lmin;

            UnityEditor.EditorUtility.SetDirty(this);
        }

        private bool TryGetAutoFitWorldCorners(Renderer r, out Vector3[] corners)
        {
            // 1. Contact collider = exactly the surface the slime can touch.
            if (contactCollider != null)
            {
                FillCorners(contactCollider.bounds, out corners);
                return true;
            }

            // 2. Mesh bounds transformed via the RENDERER's transform (not the receiver's —
            //    they can differ). Avoids SkinnedMeshRenderer.bounds' bone-sweep inflation.
            Mesh mesh = null;
            if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
            else if (r.TryGetComponent<MeshFilter>(out var mf)) mesh = mf.sharedMesh;

            if (mesh != null)
            {
                var mb = mesh.bounds;
                Vector3[] os = new Vector3[8];
                os[0] = new Vector3(mb.min.x, mb.min.y, mb.min.z);
                os[1] = new Vector3(mb.max.x, mb.min.y, mb.min.z);
                os[2] = new Vector3(mb.min.x, mb.max.y, mb.min.z);
                os[3] = new Vector3(mb.max.x, mb.max.y, mb.min.z);
                os[4] = new Vector3(mb.min.x, mb.min.y, mb.max.z);
                os[5] = new Vector3(mb.max.x, mb.min.y, mb.max.z);
                os[6] = new Vector3(mb.min.x, mb.max.y, mb.max.z);
                os[7] = new Vector3(mb.max.x, mb.max.y, mb.max.z);
                corners = new Vector3[8];
                var rt = r.transform;
                for (int i = 0; i < 8; i++) corners[i] = rt.TransformPoint(os[i]);
                return true;
            }

            // 3. Fallback: renderer.bounds (bone-sweep on SkinnedMeshRenderer, may overshoot).
            FillCorners(r.bounds, out corners);
            return true;
        }

        private static void FillCorners(Bounds wb, out Vector3[] corners)
        {
            corners = new Vector3[8];
            corners[0] = new Vector3(wb.min.x, wb.min.y, wb.min.z);
            corners[1] = new Vector3(wb.max.x, wb.min.y, wb.min.z);
            corners[2] = new Vector3(wb.min.x, wb.max.y, wb.min.z);
            corners[3] = new Vector3(wb.max.x, wb.max.y, wb.min.z);
            corners[4] = new Vector3(wb.min.x, wb.min.y, wb.max.z);
            corners[5] = new Vector3(wb.max.x, wb.min.y, wb.max.z);
            corners[6] = new Vector3(wb.min.x, wb.max.y, wb.max.z);
            corners[7] = new Vector3(wb.max.x, wb.max.y, wb.max.z);
        }

        [ContextMenu("Trail / Generate Stable ID")]
        private void GenerateStableId()
        {
            if (!string.IsNullOrEmpty(overrideId))
            {
                if (!UnityEditor.EditorUtility.DisplayDialog(
                    "Regenerate Stable ID?",
                    "An overrideId is already set. Regenerating will orphan the existing save file.\n\nContinue?",
                    "Regenerate", "Cancel")) return;
            }
            UnityEditor.Undo.RecordObject(this, "Trail: generate stable id");
            overrideId = System.Guid.NewGuid().ToString("N");
            UnityEditor.EditorUtility.SetDirty(this);
        }

        [ContextMenu("Trail / Clear This Platform")]
        private void DebugClear()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[Trail] Play Mode only."); return; }
            ClearTrail();
        }

        private static GUIStyle gizmoLabelStyle;
        private void OnDrawGizmosSelected()
        {
            if (gizmoCorners == null || gizmoCorners.Length != 4) gizmoCorners = new Vector3[4];
            gizmoCorners[0] = transform.TransformPoint(new Vector3(localMin.x,               localMin.y,               0f));
            gizmoCorners[1] = transform.TransformPoint(new Vector3(localMin.x + localSize.x, localMin.y,               0f));
            gizmoCorners[2] = transform.TransformPoint(new Vector3(localMin.x + localSize.x, localMin.y + localSize.y, 0f));
            gizmoCorners[3] = transform.TransformPoint(new Vector3(localMin.x,               localMin.y + localSize.y, 0f));

            UnityEditor.Handles.DrawSolidRectangleWithOutline(gizmoCorners, gizmoFill, gizmoOutline);

            // Cache the label string — otherwise it allocates every scene-view repaint.
            var res = PlannedResolution;
            if (res != gizmoLabelCachedRes || !Mathf.Approximately(localSize.x, gizmoLabelCachedSize.x) || !Mathf.Approximately(localSize.y, gizmoLabelCachedSize.y))
            {
                long bytes = (long)res.x * res.y;
                string mem = bytes < 1024 ? bytes + " B" : (bytes / 1024f).ToString("0.0") + " KB";
                gizmoLabelText = $"Local Trail\n{localSize.x:0.##} × {localSize.y:0.##}  ·  RT {res.x}×{res.y}  ·  {mem}";
                gizmoLabelCachedRes = res;
                gizmoLabelCachedSize = localSize;
            }

            if (gizmoLabelStyle == null)
            {
                gizmoLabelStyle = new GUIStyle(UnityEditor.EditorStyles.helpBox) { alignment = TextAnchor.MiddleCenter, fontSize = 10 };
                gizmoLabelStyle.normal.textColor = Color.white;
            }
            UnityEditor.Handles.Label((gizmoCorners[0] + gizmoCorners[2]) * 0.5f, gizmoLabelText, gizmoLabelStyle);
        }

        private static readonly Color gizmoFill    = new Color(0.3f, 0.8f, 1f, 0.15f);
        private static readonly Color gizmoOutline = new Color(0.3f, 0.8f, 1f, 1f);
        private Vector3[] gizmoCorners;
        private string gizmoLabelText;
        private Vector2Int gizmoLabelCachedRes;
        private Vector2 gizmoLabelCachedSize;
#endif

        private static readonly List<string> pathScratch = new List<string>(8);
        private static string HierarchyPath(Transform t)
        {
            pathScratch.Clear();
            for (var p = t; p != null; p = p.parent) pathScratch.Add(p.name);
            var buf = new System.Text.StringBuilder(64);
            for (int i = pathScratch.Count - 1; i >= 0; i--)
            {
                if (i < pathScratch.Count - 1) buf.Append('/');
                buf.Append(pathScratch[i]);
            }
            return buf.ToString();
        }

        // -------------------- Cold trail save adapter --------------------
        // Wraps rtCold as its own ITrailTarget; "_cold" key suffix keeps warm-only saves loadable.
        private sealed class LocalColdTarget : ITrailTarget
        {
            private readonly LocalTrailReceiver owner;
            public LocalColdTarget(LocalTrailReceiver o) { owner = o; }

            public RenderTexture Rt => owner.rtCold;
            public string SaveKey => owner.SaveKey + "_cold";
            public Vector2Int Resolution => owner.resolution;

            public bool Dirty => owner.dirtyCold;
            public void MarkDirty() => owner.dirtyCold = true;
            public void ClearDirty() => owner.dirtyCold = false;

            public TrailTargetMeta BuildMeta() => owner.BuildMeta();
            public void OnLoadedInto() { }
        }
    }
}
