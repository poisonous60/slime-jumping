using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlimeJumping.Rendering
{
    /// Owns the persistent world-space trail RenderTexture, publishes it + bounds as global
    /// shader properties, and hosts the TrailPersistence subsystem. One per scene.
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("Rendering/Trail/World Trail Manager")]
    public class WorldTrailManager : MonoBehaviour, ITrailTarget
    {
        public static WorldTrailManager Instance { get; private set; }

        [Header("World bounds")]
        [Tooltip("Bottom-left XY corner of the world trail rectangle.")]
        [SerializeField] private Vector2 worldMin = new Vector2(-50f, -50f);
        [Tooltip("Extent of the world trail rectangle. Pad ~1 unit beyond playable area.")]
        [SerializeField] private Vector2 worldSize = new Vector2(100f, 200f);
        [Tooltip("Pixels per world unit. Higher = crisper trail, more memory (R8).")]
        [Range(4, 80)]
        [SerializeField] private int pixelsPerUnit = 20;

        [Header("Appearance")]
        [SerializeField] private Color trailColor = new Color(0.3f, 0.9f, 0.3f, 1f);
        [Tooltip("Visual saturation cap. Brush writes additively and the RT value can reach 1.0; " +
                 "this caps the lerp factor at render time so the ground stays legible even after many passes. " +
                 "1.0 = no cap (full tint at saturation); 0.6 = max 60% tint visible.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float trailMaxMask = 0.75f;
        [Tooltip("Material instantiated from Hidden/SlimeJumping/TrailBrush. Required.")]
        [SerializeField] private Material brushMaterial;

        [Header("Scene tooling")]
        [Tooltip("Used by Tools > Trail > Fit Bounds to Scene Ground to pick which renderers count as ground.")]
        [SerializeField] private LayerMask groundLayers = 0;
        [Tooltip("Show the bounds gizmo in the scene view when this object is NOT selected.")]
        [SerializeField] private bool showBoundsGizmo = true;

        [Header("Persistence")]
        [Tooltip("Seconds between auto-save ticks. Dirty-only: idle or paused slime writes zero bytes.")]
        [SerializeField] private float autoSaveInterval = 15f;
        [Tooltip("Overrides the scene build index in the save path if set. Useful when build indices shuffle.")]
        [SerializeField] private string sceneKeyOverride = "";

        // Hard cap on RT dimensions to prevent inspector-driven GPU allocation disasters.
        public const int MaxResolutionPerSide = 8192;

        private RenderTexture rt;
        private TrailPersistence persistence;
        private Vector2Int resolution;
        private bool dirty;

        // -------------------- Inspector-facing accessors (read-only) --------------------

        public Vector2 WorldMin => worldMin;
        public Vector2 WorldSize => worldSize;
        public int PixelsPerUnit => pixelsPerUnit;
        public Color TrailColor => trailColor;
        public LayerMask GroundLayers => groundLayers;
        public bool ShowBoundsGizmo => showBoundsGizmo;
        public TrailPersistence Persistence => persistence;

        public Vector2Int PlannedResolution
        {
            get
            {
                int w = Mathf.CeilToInt(worldSize.x * pixelsPerUnit);
                int h = Mathf.CeilToInt(worldSize.y * pixelsPerUnit);
                return new Vector2Int(Mathf.Clamp(w, 1, MaxResolutionPerSide),
                                      Mathf.Clamp(h, 1, MaxResolutionPerSide));
            }
        }

        public long PlannedMemoryBytes
        {
            get { var r = PlannedResolution; return (long)r.x * r.y; }  // R8 = 1 byte / pixel
        }

        // -------------------- ITrailTarget --------------------

        public RenderTexture Rt => rt;

        public string SaveKey
        {
            get
            {
                string sceneKey = string.IsNullOrEmpty(sceneKeyOverride)
                    ? SceneManager.GetActiveScene().buildIndex.ToString()
                    : sceneKeyOverride;
                return "world_" + sceneKey;
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
                originX = worldMin.x,
                originY = worldMin.y,
                sizeX = worldSize.x,
                sizeY = worldSize.y,
                format = TrailPersistence.Format,
            };
        }

        public void OnLoadedInto() { /* no post-load hook needed for world target */ }

        // -------------------- Lifecycle --------------------

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError($"[Trail] Two WorldTrailManager instances in scene: '{Instance.name}' and '{name}'. Disabling the second one.", this);
                enabled = false;
                return;
            }
            Instance = this;

            resolution = PlannedResolution;
            rt = new RenderTexture(resolution.x, resolution.y, 0, RenderTextureFormat.R8)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
                autoGenerateMips = false,
                name = "WorldTrail_" + SaveKey,
            };
            rt.Create();
            Graphics.Blit(Texture2D.blackTexture, rt);

            persistence = new TrailPersistence(autoSaveInterval);
            persistence.Register(this);
            persistence.TryLoad(this);

            PublishGlobals();
        }

        private void Start()
        {
            // Warn if any renderer on groundLayers is still on stock StylizedLit.
            if (groundLayers.value == 0) return;
            var stockShader = Shader.Find("Quibli/Stylized Lit");
            if (stockShader == null) return;

            var renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            int strays = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (((1 << r.gameObject.layer) & groundLayers.value) == 0) continue;
                var mat = r.sharedMaterial;
                if (mat == null || mat.shader == null) continue;
                if (mat.shader == stockShader)
                {
                    Debug.LogWarning($"[Trail] Renderer '{r.name}' is on a ground layer but uses stock Quibli StylizedLit. " +
                                     "Migrate via Tools > Trail > Migrate Selected Materials.", r);
                    strays++;
                }
            }
            if (strays > 0)
                Debug.LogWarning($"[Trail] {strays} ground renderer(s) still on stock StylizedLit — trail will not appear on them.");
        }

        private void Update()
        {
            if (persistence != null) persistence.Tick();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && persistence != null) persistence.FlushSync();
        }

        private void OnApplicationQuit()
        {
            if (persistence != null) persistence.FlushSync();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (persistence != null) { persistence.Cancel(); persistence = null; }
            if (rt != null) { rt.Release(); Object.Destroy(rt); rt = null; }
        }

        private void PublishGlobals()
        {
            Shader.SetGlobalTexture("_TrailMask", rt);
            Shader.SetGlobalVector("_TrailWorldMin",  new Vector4(worldMin.x,  worldMin.y,  0f, 0f));
            Shader.SetGlobalVector("_TrailWorldSize", new Vector4(worldSize.x, worldSize.y, 0f, 0f));
            Shader.SetGlobalColor ("_TrailColor", trailColor);
            Shader.SetGlobalFloat ("_TrailMaxMask", Mathf.Clamp(trailMaxMask, 0.01f, 1f));
        }

        private void OnValidate()
        {
            // Re-publish in Edit Mode so inspector tweaks preview live (only meaningful in Play).
            if (Application.isPlaying && rt != null) PublishGlobals();
        }

        // -------------------- Public stamp API --------------------

        /// Stamp into the world RT. worldPos is the slime contact point; worldRadius in world units.
        public void Stamp(Vector2 worldPos, float worldRadius, float strength)
        {
            if (rt == null || brushMaterial == null) return;

            // World → UV; per-axis because worldSize is generally non-square.
            Vector2 uv = new Vector2(
                (worldPos.x - worldMin.x) / worldSize.x,
                (worldPos.y - worldMin.y) / worldSize.y);
            Vector2 radiusUV = new Vector2(worldRadius / worldSize.x, worldRadius / worldSize.y);

            // Early-out if the stamp is entirely outside the rect.
            if (uv.x + radiusUV.x < 0f || uv.x - radiusUV.x > 1f ||
                uv.y + radiusUV.y < 0f || uv.y - radiusUV.y > 1f) return;

            BlitToTarget(rt, uv, radiusUV, strength);
            dirty = true;
        }

        /// Shared GL.QUADS brush draw reused by LocalTrailReceiver.
        public void BlitToTarget(RenderTexture target, Vector2 centerUV, Vector2 radiusUV, float strength)
        {
            if (target == null || brushMaterial == null) return;

            brushMaterial.SetFloat("_Strength", strength);

            var prev = RenderTexture.active;
            RenderTexture.active = target;
            GL.PushMatrix();
            GL.LoadOrtho();

            brushMaterial.SetPass(0);

            float x0 = centerUV.x - radiusUV.x;
            float x1 = centerUV.x + radiusUV.x;
            float y0 = centerUV.y - radiusUV.y;
            float y1 = centerUV.y + radiusUV.y;

            GL.Begin(GL.QUADS);
            // TEXCOORD0 = local coord in [-1, 1] for radial falloff in the brush shader.
            GL.TexCoord2(-1f, -1f); GL.Vertex3(x0, y0, 0f);
            GL.TexCoord2( 1f, -1f); GL.Vertex3(x1, y0, 0f);
            GL.TexCoord2( 1f,  1f); GL.Vertex3(x1, y1, 0f);
            GL.TexCoord2(-1f,  1f); GL.Vertex3(x0, y1, 0f);
            GL.End();

            GL.PopMatrix();
            RenderTexture.active = prev;
        }

        // -------------------- Debug ContextMenu --------------------

        [ContextMenu("Trail / Clear World")]
        private void DebugClearWorld()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[Trail] Play Mode only."); return; }
            if (rt != null) Graphics.Blit(Texture2D.blackTexture, rt);
            dirty = true;
        }

        [ContextMenu("Trail / Clear All (World + Locals)")]
        private void DebugClearAll()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[Trail] Play Mode only."); return; }
            DebugClearWorld();
            var all = FindObjectsByType<LocalTrailReceiver>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++) all[i].ClearTrail();
        }

        [ContextMenu("Trail / Save Snapshot \"A\"")]
        private void DebugSaveSnapshotA()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[Trail] Play Mode only."); return; }
            if (persistence != null) persistence.SaveNamedSlotSync(this, "A");
            Debug.Log("[Trail] Snapshot 'A' saved.");
        }

        [ContextMenu("Trail / Load Snapshot \"A\"")]
        private void DebugLoadSnapshotA()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[Trail] Play Mode only."); return; }
            bool ok = persistence != null && persistence.LoadNamedSlotSync(this, "A");
            Debug.Log(ok ? "[Trail] Snapshot 'A' loaded." : "[Trail] Snapshot 'A' not found or metadata mismatch.");
        }

        // -------------------- Gizmos --------------------

        private void OnDrawGizmos()
        {
            if (!showBoundsGizmo) return;
            DrawBoundsGizmo(false);
        }

        private void OnDrawGizmosSelected()
        {
            DrawBoundsGizmo(true);
        }

        private void DrawBoundsGizmo(bool selected)
        {
#if UNITY_EDITOR
            Vector3 bl = new Vector3(worldMin.x,               worldMin.y,               0f);
            Vector3 br = new Vector3(worldMin.x + worldSize.x, worldMin.y,               0f);
            Vector3 tr = new Vector3(worldMin.x + worldSize.x, worldMin.y + worldSize.y, 0f);
            Vector3 tl = new Vector3(worldMin.x,               worldMin.y + worldSize.y, 0f);

            Color fill    = selected ? new Color(0.25f, 1f, 0.3f, 0.12f) : new Color(0.25f, 1f, 0.3f, 0.05f);
            Color outline = selected ? new Color(0.25f, 1f, 0.3f, 1f)    : new Color(0.25f, 1f, 0.3f, 0.6f);

            // Filled translucent quad so the paintable region reads at any zoom.
            UnityEditor.Handles.DrawSolidRectangleWithOutline(new[] { bl, br, tr, tl }, fill, outline);

            // Corner ticks for precise alignment against the play area.
            float tick = Mathf.Max(worldSize.x, worldSize.y) * 0.01f;
            UnityEditor.Handles.color = outline;
            UnityEditor.Handles.DrawLine(bl, bl + new Vector3( tick,  tick, 0));
            UnityEditor.Handles.DrawLine(br, br + new Vector3(-tick,  tick, 0));
            UnityEditor.Handles.DrawLine(tr, tr + new Vector3(-tick, -tick, 0));
            UnityEditor.Handles.DrawLine(tl, tl + new Vector3( tick, -tick, 0));

            // Label with size + RT resolution + memory; only when selected to avoid clutter.
            if (selected)
            {
                var res = PlannedResolution;
                long bytes = PlannedMemoryBytes;
                string mem = bytes < 1024 * 1024 ? (bytes / 1024f).ToString("0.0") + " KB" : (bytes / 1024f / 1024f).ToString("0.0") + " MB";
                string txt = $"Trail World Bounds\n{worldSize.x:0.#} × {worldSize.y:0.#}  ·  RT {res.x}×{res.y}  ·  {mem}";
                if (gizmoLabelStyle == null)
                {
                    gizmoLabelStyle = new GUIStyle(UnityEditor.EditorStyles.helpBox) { alignment = TextAnchor.MiddleCenter, fontSize = 11 };
                    gizmoLabelStyle.normal.textColor = Color.white;
                }
                UnityEditor.Handles.Label((bl + tr) * 0.5f, txt, gizmoLabelStyle);
            }
#endif
        }

#if UNITY_EDITOR
        private static GUIStyle gizmoLabelStyle;
#endif
    }
}
