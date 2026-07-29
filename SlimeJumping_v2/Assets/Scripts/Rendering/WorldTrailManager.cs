using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlimeJumping.Rendering
{
    /// Owns the world-space trail RTs, publishes them + bounds as shader globals, hosts TrailPersistence. One per scene.
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("Rendering/Trail/World Trail Manager")]
    public class WorldTrailManager : MonoBehaviour, ITrailTarget
    {
        public static WorldTrailManager Instance { get; private set; }

        // With "Enter Play Mode → Reload Domain" disabled, statics persist across Play sessions.
        // Reset so the previous session's fake-null reference can't shadow the new manager.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        [Header("World bounds")]
        [Tooltip("Bottom-left XY corner of the world trail rectangle.")]
        [SerializeField] private Vector2 worldMin = new Vector2(-50f, -50f);
        [Tooltip("Extent of the world trail rectangle. Pad ~1 unit beyond playable area.")]
        [SerializeField] private Vector2 worldSize = new Vector2(100f, 200f);
        [Tooltip("Pixels per world unit. Higher = crisper trail, more memory (R8).")]
        [Range(4, 80)]
        [SerializeField] private int pixelsPerUnit = 20;

        [Header("Appearance — Trail Color (A: by intensity)")]
        [Tooltip("Color sampled by trail intensity. Mask=0 → leftmost stop (light/new), mask=1 → rightmost stop " +
                 "(heavy/saturated). Per-stop alpha drives blend strength so the start can fade in.")]
        [GradientUsage(true)]
        [SerializeField] private Gradient trailGradient;
        [Tooltip("Visual saturation cap. Brush writes additively and the RT value can reach 1.0; " +
                 "this caps the lerp factor at render time so the ground stays legible even after many passes. " +
                 "1.0 = no cap (full tint at saturation); 0.6 = max 60% tint visible.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float trailMaxMask = 0.95f;

        [Header("Appearance — Position Gradient (B: by world position, optional)")]
        [Tooltip("If on, multiplies the trail color (A) by a second gradient sampled by world XY position. " +
                 "Use this to vary trail hue across the level (e.g. cool at the bottom, warm at the top).")]
        [SerializeField] private bool positionGradientEnabled = false;
        [Tooltip("Color tint sampled by world position along the configured axis.")]
        [GradientUsage(true)]
        [SerializeField] private Gradient positionGradient;
        [Tooltip("World-space midpoint of the gradient (t = 0.5 here).")]
        [SerializeField] private Vector2 positionGradientCenter = Vector2.zero;
        [Tooltip("Width in world units across which the gradient spans from 0 to 1.")]
        [Min(0.01f)]
        [SerializeField] private float positionGradientSize = 20f;
        [Tooltip("Rotation in degrees. 0 = vertical (gradient runs along world Y). 90 = horizontal (along X).")]
        [Range(0f, 360f)]
        [SerializeField] private float positionGradientAngle = 0f;

        [Header("Appearance — Angle Gradient (C: by surface slope, optional)")]
        [Tooltip("If on, multiplies the trail color by a gradient sampled by the surface's slope relative to world up. " +
                 "Lets you give floors, walls, and ceilings distinct trail hues. Layers with (B) position gradient.")]
        [SerializeField] private bool angleGradientEnabled = false;
        [Tooltip("Color tint sampled by surface slope. t=0 is a perfectly horizontal floor (normal pointing up), " +
                 "t=0.5 is a vertical wall (normal horizontal), t=1 is an overhang/ceiling (normal pointing down).")]
        [GradientUsage(true)]
        [SerializeField] private Gradient angleGradient;

        [Header("Appearance — Cold Trail (D: split routing while slime is frozen)")]
        [Tooltip("Second gradient applied to the cold-route RT. Stamps split between warm and cold by SlimeFrostController.FreezeT — " +
                 "warm receives strength*(1-T), cold receives strength*T. Ground shader composes alpha-over (cold over warm).")]
        [GradientUsage(true)]
        [SerializeField] private Gradient trailGradientCold;

        [Header("Brush")]
        [Tooltip("Material instantiated from Hidden/SlimeJumping/TrailBrush. Required.")]
        [SerializeField] private Material brushMaterial;

        [Header("Z-plane fade (avoid tinting background/foreground geometry)")]
        [Tooltip("World Z of the gameplay plane. Trail is 2D-projected; this defines the plane around which the fade is centered.")]
        [SerializeField] private float trailZCenter = 0f;
        [Tooltip("Surfaces within this Z distance of the center receive the full trail. Raise if ground has noticeable Z thickness.")]
        [SerializeField] private float trailZNear = 0.5f;
        [Tooltip("Surfaces beyond this Z distance receive no trail. Fade is linear between Near and Far.")]
        [SerializeField] private float trailZFar = 2.0f;

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
        // Cold-route RT (same dim/format as warm); stamps split here when freezeT > 0.
        private RenderTexture rtCold;
        private TrailPersistence persistence;
        private Vector2Int resolution;
        private bool dirty;
        private float currentFreezeT;
        private ColdTrailTarget coldTarget;
        // Runtime clone: SetFloat/SetPass on the shared asset marks it dirty and makes Unity
        // re-serialize the .mat on every Play-exit.
        private Material brushInstance;

        // 1D ramps baked from the inspector Gradients. RGBAHalf so HDR stops (intensity > 1) survive.
        private Texture2D bakedTrailGradient;
        private Texture2D bakedPositionGradient;
        private Texture2D bakedAngleGradient;
        private Texture2D bakedTrailGradientCold;
        private const int GradientResolution = 256;

        // -------------------- Inspector-facing accessors (read-only) --------------------

        public Vector2 WorldMin => worldMin;
        public Vector2 WorldSize => worldSize;
        public int PixelsPerUnit => pixelsPerUnit;
        public LayerMask GroundLayers => groundLayers;
        public bool ShowBoundsGizmo => showBoundsGizmo;
        public TrailPersistence Persistence => persistence;

        // Single source of truth for stamp-time freezeT, so consumers (LocalTrailReceiver) don't
        // each poll SlimeFrostController. Single-slime stage.
        public float CurrentFreezeT => currentFreezeT;
        public RenderTexture RtCold => rtCold;

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
            // Linear readWrite: R8 defaults to sRGB, which isn't a supported GPU format and silently
            // falls back to RGBA32 sRGB (4× the budgeted memory).
            rt = new RenderTexture(resolution.x, resolution.y, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
                autoGenerateMips = false,
                name = "WorldTrail_" + SaveKey,
            };
            rt.Create();
            Graphics.Blit(Texture2D.blackTexture, rt);

            // Explicit black blit: a fresh RT holds undefined GPU memory and would otherwise show
            // garbage tint through the ground shader's cold sampler.
            rtCold = new RenderTexture(resolution.x, resolution.y, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
                autoGenerateMips = false,
                name = "WorldTrailCold_" + SaveKey,
            };
            rtCold.Create();
            Graphics.Blit(Texture2D.blackTexture, rtCold);

            if (brushMaterial != null)
                brushInstance = new Material(brushMaterial) { name = brushMaterial.name + " (Instance)", hideFlags = HideFlags.HideAndDontSave };

            persistence = new TrailPersistence(autoSaveInterval);
            persistence.Register(this);
            persistence.TryLoad(this);

            // Distinct save key keeps warm-only stage1 saves loadable.
            coldTarget = new ColdTrailTarget(this);
            persistence.Register(coldTarget);
            persistence.TryLoad(coldTarget);

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
            if (rtCold != null) { rtCold.Release(); Object.Destroy(rtCold); rtCold = null; }
            if (brushInstance != null) { Object.Destroy(brushInstance); brushInstance = null; }
            if (bakedTrailGradient != null) { Object.Destroy(bakedTrailGradient); bakedTrailGradient = null; }
            if (bakedPositionGradient != null) { Object.Destroy(bakedPositionGradient); bakedPositionGradient = null; }
            if (bakedAngleGradient != null) { Object.Destroy(bakedAngleGradient); bakedAngleGradient = null; }
            if (bakedTrailGradientCold != null) { Object.Destroy(bakedTrailGradientCold); bakedTrailGradientCold = null; }

            // Neutralize trail globals: a freed RT falls back to Unity's default white texture,
            // which would leave every trail-receiver material tinted.
            Shader.SetGlobalTexture("_TrailMask",          Texture2D.blackTexture);
            Shader.SetGlobalTexture("_TrailGradient",      Texture2D.blackTexture);
            Shader.SetGlobalTexture("_TrailPosGradient",   Texture2D.whiteTexture);
            Shader.SetGlobalTexture("_TrailAngleGradient", Texture2D.whiteTexture);
            Shader.SetGlobalTexture("_TrailMaskCold",      Texture2D.blackTexture);
            Shader.SetGlobalTexture("_TrailGradientCold",  Texture2D.whiteTexture);
            Shader.SetGlobalVector ("_TrailWorldMin",      Vector4.zero);
            Shader.SetGlobalVector ("_TrailWorldSize",     Vector4.zero);
            Shader.SetGlobalVector ("_TrailPosGradientParams", Vector4.zero);
            Shader.SetGlobalFloat  ("_TrailPosGradientEnabled",   0f);
            Shader.SetGlobalFloat  ("_TrailAngleGradientEnabled", 0f);
            Shader.SetGlobalFloat  ("_TrailMaxMask",       0f);
        }

        private void PublishGlobals()
        {
            BakeGradients();

            Shader.SetGlobalTexture("_TrailMask",          rt);
            Shader.SetGlobalTexture("_TrailGradient",      bakedTrailGradient    != null ? (Texture)bakedTrailGradient    : Texture2D.whiteTexture);
            Shader.SetGlobalTexture("_TrailPosGradient",   bakedPositionGradient != null ? (Texture)bakedPositionGradient : Texture2D.whiteTexture);
            Shader.SetGlobalTexture("_TrailAngleGradient", bakedAngleGradient    != null ? (Texture)bakedAngleGradient    : Texture2D.whiteTexture);
            Shader.SetGlobalVector ("_TrailWorldMin",      new Vector4(worldMin.x,  worldMin.y,  0f, 0f));
            Shader.SetGlobalVector ("_TrailWorldSize",     new Vector4(worldSize.x, worldSize.y, 0f, 0f));
            Shader.SetGlobalVector ("_TrailPosGradientParams", new Vector4(
                positionGradientCenter.x,
                positionGradientCenter.y,
                Mathf.Max(0.01f, positionGradientSize),
                positionGradientAngle));
            Shader.SetGlobalFloat  ("_TrailPosGradientEnabled",   positionGradientEnabled ? 1f : 0f);
            Shader.SetGlobalFloat  ("_TrailAngleGradientEnabled", angleGradientEnabled    ? 1f : 0f);
            Shader.SetGlobalFloat  ("_TrailMaxMask",       Mathf.Clamp(trailMaxMask, 0.01f, 1f));
            Shader.SetGlobalFloat  ("_TrailZCenter",     trailZCenter);
            Shader.SetGlobalFloat  ("_TrailZNear",       Mathf.Max(0f, trailZNear));
            Shader.SetGlobalFloat  ("_TrailZFar",        Mathf.Max(trailZNear + 0.01f, trailZFar));

            // Explicit != null ternary (never `??` on UnityEngine.Object). White fallback keeps the
            // shader's cold-mask×alpha lerp a no-op when the cold gradient isn't authored.
            Shader.SetGlobalTexture("_TrailMaskCold", rtCold);
            Shader.SetGlobalTexture("_TrailGradientCold",
                bakedTrailGradientCold != null ? (Texture)bakedTrailGradientCold : Texture2D.whiteTexture);
        }

        private void BakeGradients()
        {
            BakeOne(ref bakedTrailGradient,     trailGradient,     "TrailGradient");
            BakeOne(ref bakedPositionGradient,  positionGradient,  "TrailPosGradient");
            BakeOne(ref bakedAngleGradient,     angleGradient,     "TrailAngleGradient");
            BakeOne(ref bakedTrailGradientCold, trailGradientCold, "TrailGradientCold");
        }

        private static void BakeOne(ref Texture2D tex, Gradient g, string label)
        {
            if (g == null) return;
            if (tex == null)
            {
                tex = new Texture2D(GradientResolution, 1, TextureFormat.RGBAHalf, false, true)
                {
                    wrapMode   = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    name       = "Baked_" + label,
                    hideFlags  = HideFlags.HideAndDontSave,
                };
            }
            for (int i = 0; i < GradientResolution; i++)
            {
                float t = (float)i / (GradientResolution - 1);
                tex.SetPixel(i, 0, g.Evaluate(t));
            }
            tex.Apply(false, false);
        }

        private void OnValidate()
        {
            // Live preview of inspector tweaks; globals only exist in Play.
            if (Application.isPlaying && rt != null) PublishGlobals();
        }

        private void Reset()
        {
            trailGradient = new Gradient();
            trailGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.55f, 1.00f, 0.55f), 0f),
                    new GradientColorKey(new Color(0.20f, 0.55f, 0.20f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0.50f, 0f),
                    new GradientAlphaKey(1.00f, 1f),
                });
            positionGradient = new Gradient();
            positionGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f),
                });
            // White = identity multiplier, so enabling the toggle without tuning leaves the trail unchanged.
            angleGradient = new Gradient();
            angleGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f),
                });
            trailGradientCold = new Gradient();
            trailGradientCold.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.78f, 0.92f, 1.00f), 0f),
                    new GradientColorKey(new Color(0.32f, 0.62f, 0.85f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0.50f, 0f),
                    new GradientAlphaKey(1.00f, 1f),
                });
        }

        // -------------------- Public stamp API --------------------

        /// Stamp into the world RT; strength splits warm ×(1-T) / cold ×T by currentFreezeT.
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

            float warmS = strength * (1f - currentFreezeT);
            float coldS = strength * currentFreezeT;
            // Skip near-zero contributions so a fully-frozen slime doesn't waste a GL draw on warm.
            if (warmS > 0.001f) BlitToTarget(rt,     uv, radiusUV, warmS);
            if (coldS > 0.001f && rtCold != null) BlitToTarget(rtCold, uv, radiusUV, coldS);
            dirty = true;
        }

        /// Updates the freezeT used for stamp split-routing. Single-slime stage: last writer wins.
        public void SetFreezeT(float t) => currentFreezeT = Mathf.Clamp01(t);

        /// Shared GL.QUADS brush draw reused by LocalTrailReceiver.
        public void BlitToTarget(RenderTexture target, Vector2 centerUV, Vector2 radiusUV, float strength)
        {
            if (target == null || brushInstance == null) return;

            brushInstance.SetFloat("_Strength", strength);

            var prev = RenderTexture.active;
            RenderTexture.active = target;
            GL.PushMatrix();
            GL.LoadOrtho();

            brushInstance.SetPass(0);

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

        /// Clears the world RT (warm + cold) immediately. Play-mode only — no-ops otherwise.
        public void ClearWorldTrail()
        {
            if (!Application.isPlaying) return;
            if (rt != null) Graphics.Blit(Texture2D.blackTexture, rt);
            if (rtCold != null) Graphics.Blit(Texture2D.blackTexture, rtCold);
            dirty = true;
        }

        /// Clears the world RT and every LocalTrailReceiver in the scene. Play-mode only.
        public void ClearAllTrails()
        {
            if (!Application.isPlaying) return;
            ClearWorldTrail();
            var all = FindObjectsByType<LocalTrailReceiver>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++) all[i].ClearTrail();
        }

        [ContextMenu("Trail / Clear World")]
        private void DebugClearWorld()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[Trail] Play Mode only."); return; }
            ClearWorldTrail();
        }

        [ContextMenu("Trail / Clear All (World + Locals)")]
        private void DebugClearAll()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[Trail] Play Mode only."); return; }
            ClearAllTrails();
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
            if (gizmoCorners == null || gizmoCorners.Length != 4) gizmoCorners = new Vector3[4];
            gizmoCorners[0] = new Vector3(worldMin.x,               worldMin.y,               0f);
            gizmoCorners[1] = new Vector3(worldMin.x + worldSize.x, worldMin.y,               0f);
            gizmoCorners[2] = new Vector3(worldMin.x + worldSize.x, worldMin.y + worldSize.y, 0f);
            gizmoCorners[3] = new Vector3(worldMin.x,               worldMin.y + worldSize.y, 0f);

            Color fill    = selected ? gizmoFillSelected    : gizmoFillUnselected;
            Color outline = selected ? gizmoOutlineSelected : gizmoOutlineUnselected;

            UnityEditor.Handles.DrawSolidRectangleWithOutline(gizmoCorners, fill, outline);

            // Selected only: Handles.Label is an IMGUI path that allocates and renders text every
            // call — the most expensive thing here under continuous Scene-view repaint.
            if (selected)
            {
                if (gizmoLabelStyle == null)
                {
                    gizmoLabelStyle = new GUIStyle(UnityEditor.EditorStyles.helpBox) { alignment = TextAnchor.MiddleCenter, fontSize = 11 };
                    gizmoLabelStyle.normal.textColor = Color.white;
                }
                // Rebuild label text only when its inputs change, not once per repaint.
                var res = PlannedResolution;
                if (res != gizmoLabelCachedRes || !Mathf.Approximately(worldSize.x, gizmoLabelCachedSize.x) || !Mathf.Approximately(worldSize.y, gizmoLabelCachedSize.y))
                {
                    long bytes = PlannedMemoryBytes;
                    string mem = bytes < 1024 * 1024 ? (bytes / 1024f).ToString("0.0") + " KB" : (bytes / 1024f / 1024f).ToString("0.0") + " MB";
                    gizmoLabelText = $"Trail World Bounds\n{worldSize.x:0.#} × {worldSize.y:0.#}  ·  RT {res.x}×{res.y}  ·  {mem}";
                    gizmoLabelCachedRes = res;
                    gizmoLabelCachedSize = worldSize;
                }
                UnityEditor.Handles.Label((gizmoCorners[0] + gizmoCorners[2]) * 0.5f, gizmoLabelText, gizmoLabelStyle);
            }
#endif
        }

#if UNITY_EDITOR
        private static GUIStyle gizmoLabelStyle;
        private static readonly Color gizmoFillSelected    = new Color(0.25f, 1f, 0.3f, 0.12f);
        private static readonly Color gizmoFillUnselected  = new Color(0.25f, 1f, 0.3f, 0.05f);
        private static readonly Color gizmoOutlineSelected = new Color(0.25f, 1f, 0.3f, 1f);
        private static readonly Color gizmoOutlineUnselected = new Color(0.25f, 1f, 0.3f, 0.6f);
        private Vector3[] gizmoCorners;
        private string gizmoLabelText;
        private Vector2Int gizmoLabelCachedRes;
        private Vector2 gizmoLabelCachedSize;
#endif

        // -------------------- Cold trail save adapter --------------------
        // Distinct save key keeps existing warm-only stage1 saves compatible.
        private sealed class ColdTrailTarget : ITrailTarget
        {
            private readonly WorldTrailManager owner;
            public ColdTrailTarget(WorldTrailManager o) { owner = o; }

            public RenderTexture Rt => owner.rtCold;

            public string SaveKey
            {
                get
                {
                    string sceneKey = string.IsNullOrEmpty(owner.sceneKeyOverride)
                        ? UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex.ToString()
                        : owner.sceneKeyOverride;
                    return "world_cold_" + sceneKey;
                }
            }

            public Vector2Int Resolution => owner.resolution;

            // Shares warm's dirty flag — both RTs save together.
            public bool Dirty => owner.dirty;
            public void MarkDirty() => owner.dirty = true;
            public void ClearDirty() { /* warm clears for both */ }

            public TrailTargetMeta BuildMeta() => owner.BuildMeta();

            public void OnLoadedInto() { /* no post-load hook needed */ }
        }
    }
}
