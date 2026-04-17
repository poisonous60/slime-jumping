using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlimeJumping.Rendering
{
    /// One per moving/rotating platform. Owns a small object-space RT, pushes it into the
    /// renderer's MaterialPropertyBlock, and routes stamps from world space into object
    /// space so the trail follows the platform through translation/rotation/tilt.
    [RequireComponent(typeof(Renderer))]
    [AddComponentMenu("Rendering/Trail/Local Trail Receiver")]
    public class LocalTrailReceiver : MonoBehaviour, ITrailTarget
    {
        [Header("Object-space bounds (padded ~0.2 beyond visible platform)")]
        [SerializeField] private Vector2 localMin  = new Vector2(-1.5f, -0.5f);
        [SerializeField] private Vector2 localSize = new Vector2(3.0f, 1.0f);
        [Range(4, 80)]
        [SerializeField] private int pixelsPerUnit = 20;

        [Header("Routing")]
        [Tooltip("The platform's physics collider — the one SlimeController detects on contact. " +
                 "On split-GameObject platforms (visual rotates, collider static), drag it here. " +
                 "Left empty → auto-resolved via GetComponentInParent on Awake/OnValidate.")]
        [SerializeField] private Collider2D contactCollider;

        [Header("Persistence")]
        [Tooltip("Optional stable GUID. Empty = save key derived from hierarchy path (fragile across renames). " +
                 "Set via context menu 'Trail / Generate Stable ID' for shipping platforms.")]
        [SerializeField] private string overrideId = "";

        private RenderTexture rt;
        private Renderer rend;
        private MaterialPropertyBlock mpb;
        private Vector2Int resolution;
        private bool dirty;

        public Collider2D ContactCollider => contactCollider;
        public Vector2 LocalMin => localMin;
        public Vector2 LocalSize => localSize;
        public int PixelsPerUnit => pixelsPerUnit;
        public string OverrideId => overrideId;

        public Vector2Int PlannedResolution
        {
            get
            {
                int w = Mathf.CeilToInt(localSize.x * pixelsPerUnit);
                int h = Mathf.CeilToInt(localSize.y * pixelsPerUnit);
                return new Vector2Int(Mathf.Clamp(w, 1, 4096), Mathf.Clamp(h, 1, 4096));
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
            rend = GetComponent<Renderer>();
            if (contactCollider == null) contactCollider = GetComponentInParent<Collider2D>();
        }

        private void OnEnable()
        {
            if (rend == null) rend = GetComponent<Renderer>();

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
            rend.GetPropertyBlock(mpb);
            mpb.SetTexture("_LocalTrailMask", rt);
            mpb.SetVector("_LocalTrailMin",  new Vector4(localMin.x,  localMin.y,  0f, 0f));
            mpb.SetVector("_LocalTrailSize", new Vector4(localSize.x, localSize.y, 0f, 0f));
            rend.SetPropertyBlock(mpb);

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

        // -------------------- Stamp API --------------------

        public void StampAt(Vector2 worldPos, float worldRadius, float strength)
        {
            var mgr = WorldTrailManager.Instance;
            if (mgr == null || rt == null) return;

            // World → object-space XY. InverseTransformPoint handles rotation and scale.
            Vector3 lp = transform.InverseTransformPoint(new Vector3(worldPos.x, worldPos.y, 0f));
            Vector2 uv = new Vector2(
                (lp.x - localMin.x) / localSize.x,
                (lp.y - localMin.y) / localSize.y);

            // Skip stamps whose center is well outside this platform's local rect.
            if (uv.x < -0.1f || uv.x > 1.1f || uv.y < -0.1f || uv.y > 1.1f) return;

            Vector2 radiusUV = new Vector2(worldRadius / localSize.x, worldRadius / localSize.y);
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
            var planned = PlannedResolution;
            if (planned.x > 256 || planned.y > 256)
            {
                Debug.LogWarning($"[Trail] LocalTrailReceiver on '{name}' resolves to {planned.x}x{planned.y} — " +
                                 "that's unusually large for a platform. Consider the world RT for big static surfaces.", this);
            }
        }

        [ContextMenu("Trail / Auto-fit to Renderer Bounds")]
        private void AutoFitBounds()
        {
            var r = GetComponent<Renderer>();
            if (r == null) return;

            UnityEditor.Undo.RecordObject(this, "Trail: auto-fit bounds");

            // Renderer.bounds is world-space AABB. Transform corners back to local space and envelope.
            Bounds wb = r.bounds;
            Vector3[] corners = new Vector3[8];
            corners[0] = new Vector3(wb.min.x, wb.min.y, wb.min.z);
            corners[1] = new Vector3(wb.max.x, wb.min.y, wb.min.z);
            corners[2] = new Vector3(wb.min.x, wb.max.y, wb.min.z);
            corners[3] = new Vector3(wb.max.x, wb.max.y, wb.min.z);
            corners[4] = new Vector3(wb.min.x, wb.min.y, wb.max.z);
            corners[5] = new Vector3(wb.max.x, wb.min.y, wb.max.z);
            corners[6] = new Vector3(wb.min.x, wb.max.y, wb.max.z);
            corners[7] = new Vector3(wb.max.x, wb.max.y, wb.max.z);

            Vector2 lmin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 lmax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < 8; i++)
            {
                Vector3 lp = transform.InverseTransformPoint(corners[i]);
                if (lp.x < lmin.x) lmin.x = lp.x;
                if (lp.y < lmin.y) lmin.y = lp.y;
                if (lp.x > lmax.x) lmax.x = lp.x;
                if (lp.y > lmax.y) lmax.y = lp.y;
            }

            const float pad = 0.2f;
            lmin -= new Vector2(pad, pad);
            lmax += new Vector2(pad, pad);
            localMin = lmin;
            localSize = lmax - lmin;

            UnityEditor.EditorUtility.SetDirty(this);
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
