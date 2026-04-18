using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlimeJumping.Rendering
{
    /// One per moving/rotating platform. Owns a small local-space RT, pushes it into the
    /// target renderer's MaterialPropertyBlock, and routes stamps from world space into the
    /// receiver's local frame so the trail follows translation/rotation/tilt.
    ///
    /// Paint frame is the RECEIVER'S transform, not the renderer's — so for a 3D-tilted
    /// mesh, put this component on an unrotated parent/sibling GameObject to keep the paint
    /// plane aligned with world XY. (If receiver and renderer share a GameObject, the two
    /// matrices coincide and behavior matches the original object-space design.)
    [AddComponentMenu("Rendering/Trail/Local Trail Receiver")]
    public class LocalTrailReceiver : MonoBehaviour, ITrailTarget
    {
        [Header("Local-space bounds (padded ~0.2 beyond visible platform)")]
        [SerializeField] private Vector2 localMin  = new Vector2(-1.5f, -0.5f);
        [SerializeField] private Vector2 localSize = new Vector2(3.0f, 1.0f);
        [Range(4, 80)]
        [SerializeField] private int pixelsPerUnit = 20;

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
        private MaterialPropertyBlock mpb;
        private Vector2Int resolution;
        private bool dirty;
        private static readonly int IdLocalTrailMask = Shader.PropertyToID("_LocalTrailMask");
        private static readonly int IdLocalTrailMin  = Shader.PropertyToID("_LocalTrailMin");
        private static readonly int IdLocalTrailSize = Shader.PropertyToID("_LocalTrailSize");
        private static readonly int IdLocalTrailW2L  = Shader.PropertyToID("_LocalTrailW2L");

        public Collider2D ContactCollider => contactCollider;
        public Renderer TargetRenderer => targetRenderer;
        public Vector2 LocalMin => localMin;
        public Vector2 LocalSize => localSize;
        public int PixelsPerUnit => pixelsPerUnit;
        public string OverrideId => overrideId;

        // Per-platform RTs should stay small; ground-sized RTs belong on WorldTrailManager.
        // 512² R8 ≈ 256 KB per platform — a reasonable upper bound.
        public const int MaxResolutionPerSide = 512;

        public Vector2Int PlannedResolution
        {
            get
            {
                // pixelsPerUnit is "pixels per world unit". The shader samples in object space,
                // so the RT must cover localSize * lossyScale world units at the requested density —
                // otherwise a scaled parent stretches a tiny RT across a large visible area.
                Vector3 ls = transform.lossyScale;
                int w = Mathf.CeilToInt(localSize.x * Mathf.Abs(ls.x) * pixelsPerUnit);
                int h = Mathf.CeilToInt(localSize.y * Mathf.Abs(ls.y) * pixelsPerUnit);
                return new Vector2Int(
                    Mathf.Clamp(w, 1, MaxResolutionPerSide),
                    Mathf.Clamp(h, 1, MaxResolutionPerSide));
            }
        }

        // Unclamped, pre-cap dimensions — used by OnValidate to detect and warn when the
        // clamp is silently throwing away resolution.
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

            mpb = new MaterialPropertyBlock();
            targetRenderer.GetPropertyBlock(mpb);
            mpb.SetTexture(IdLocalTrailMask, rt);
            mpb.SetVector (IdLocalTrailMin,  new Vector4(localMin.x,  localMin.y,  0f, 0f));
            mpb.SetVector (IdLocalTrailSize, new Vector4(localSize.x, localSize.y, 0f, 0f));
            mpb.SetMatrix (IdLocalTrailW2L,  transform.worldToLocalMatrix);
            targetRenderer.SetPropertyBlock(mpb);
            // First push primes the matrix; LateUpdate keeps it fresh if either this transform
            // or the renderer's parent chain moves. hasChanged is set by the engine on any TRS edit.
            transform.hasChanged = false;

            TrailColliderRegistry.Register(contactCollider, this);

            var mgr = WorldTrailManager.Instance;
            if (mgr != null && mgr.Persistence != null)
            {
                mgr.Persistence.Register(this);
                mgr.Persistence.TryLoad(this);
            }
        }

        private void OnDisable()
        {
            TrailColliderRegistry.Unregister(contactCollider);

            var mgr = WorldTrailManager.Instance;
            if (mgr != null && mgr.Persistence != null)
            {
                // Final flush on teardown — don't lose recorded progress when a platform is destroyed.
                if (dirty)
                {
                    try { mgr.Persistence.SaveSync(this); } catch { /* best-effort */ }
                }
                mgr.Persistence.Unregister(this);
            }

            if (rt != null) { rt.Release(); Object.Destroy(rt); rt = null; }
        }

        private void LateUpdate()
        {
            // Keep the receiver's world→local matrix fresh for the shader when the paint
            // frame moves. Unity updates unity_WorldToObject for the renderer automatically,
            // but _LocalTrailW2L belongs to a possibly-separate transform, so we push on change.
            if (targetRenderer == null || mpb == null) return;
            if (!transform.hasChanged) return;
            targetRenderer.GetPropertyBlock(mpb);
            mpb.SetMatrix(IdLocalTrailW2L, transform.worldToLocalMatrix);
            targetRenderer.SetPropertyBlock(mpb);
            transform.hasChanged = false;
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
            var mgr = WorldTrailManager.Instance;
            if (mgr == null || rt == null) return;

            // World → receiver-local XY. InverseTransformPoint handles rotation and scale.
            // Must use the RECEIVER's transform (same one pushed to the shader as _LocalTrailW2L)
            // — not the renderer's — so stamps land where the shader samples.
            Vector3 lp = transform.InverseTransformPoint(new Vector3(worldPos.x, worldPos.y, 0f));
            Vector2 uv = new Vector2(
                (lp.x - localMin.x) / localSize.x,
                (lp.y - localMin.y) / localSize.y);

            // Skip stamps whose center is well outside this platform's local rect.
            if (uv.x < -0.1f || uv.x > 1.1f || uv.y < -0.1f || uv.y > 1.1f) return;

            // worldRadius is world-space; localSize is unscaled-local. Convert through
            // lossyScale — otherwise a scaled parent chain (e.g. Branch_Bouncy's ×3 × ×100)
            // makes each stamp cover most of the RT.
            Vector3 ls = transform.lossyScale;
            float sx = Mathf.Max(1e-4f, Mathf.Abs(ls.x));
            float sy = Mathf.Max(1e-4f, Mathf.Abs(ls.y));
            Vector2 radiusUV = new Vector2(
                worldRadius / (localSize.x * sx),
                worldRadius / (localSize.y * sy));
            mgr.BlitToTarget(rt, uv, radiusUV, strength);
            dirty = true;
        }

        public void ClearTrail()
        {
            if (rt != null) Graphics.Blit(Texture2D.blackTexture, rt);
            dirty = true;
        }

        // -------------------- Editor helpers --------------------

#if UNITY_EDITOR
        private void OnValidate()
        {
            pixelsPerUnit = Mathf.Clamp(pixelsPerUnit, 4, 80);
            if (contactCollider == null)
            {
                // Auto-wire on first setup so common case is zero-click.
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

            // Source preference: contactCollider.bounds > mesh.bounds (object-space, transformed)
            // > renderer.bounds. SkinnedMeshRenderer.bounds is the bone-sweep envelope, which
            // way overshoots the rest-pose silhouette for rig-driven meshes like SpringyBranchVisual.
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

            // 0.2 world-unit padding → local units via lossyScale so heavily scaled parents
            // don't blow the paintable rect out by 300× the intended margin.
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
            // 1. Prefer the contact collider: it's the exact surface the slime can touch,
            //    so the paintable rect matches the physically reachable region.
            if (contactCollider != null)
            {
                FillCorners(contactCollider.bounds, out corners);
                return true;
            }

            // 2. Mesh bounds in object space, transformed to world via the RENDERER's
            //    transform (not the receiver's — they can differ). Avoids
            //    SkinnedMeshRenderer.bounds' bone-sweep inflation.
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

            // 3. Fallback: renderer.bounds. On SkinnedMeshRenderer this is bone-sweep;
            //    warn so the user knows why the rect might overshoot.
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
            if (gizmoCorners == null) gizmoCorners = new Vector3[4];
            // Compute world-space corners of the local rect so Handles (which use world space) align.
            gizmoCorners[0] = transform.TransformPoint(new Vector3(localMin.x,               localMin.y,               0f));
            gizmoCorners[1] = transform.TransformPoint(new Vector3(localMin.x + localSize.x, localMin.y,               0f));
            gizmoCorners[2] = transform.TransformPoint(new Vector3(localMin.x + localSize.x, localMin.y + localSize.y, 0f));
            gizmoCorners[3] = transform.TransformPoint(new Vector3(localMin.x,               localMin.y + localSize.y, 0f));

            UnityEditor.Handles.DrawSolidRectangleWithOutline(gizmoCorners, gizmoFill, gizmoOutline);

            // Regenerate label text only when bounds change; Handles.Label is IMGUI-expensive
            // per call — at least avoid allocating the string every scene-view repaint.
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
    }
}
