using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using SlimeJumping.Controller;

namespace SlimeJumping.Rendering
{
    /// Owns the world-space trail RTs, publishes the slime's current region + bounds as shader globals,
    /// hosts TrailPersistence. One per scene.
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("Rendering/Trail/World Trail Manager")]
    public class WorldTrailManager : MonoBehaviour
    {
        /// One authored world rect and its RT pair. Rects must not overlap — the ground shader binds
        /// a single rect at a time (the one holding the slime), so an overlap would just hide one of them.
        [Serializable]
        public class Region
        {
            [Tooltip("Save-key suffix and gizmo label. Must be unique. Leave empty on the rect that already " +
                     "has save files from the pre-region-list version, so those saves keep loading.")]
            public string id = "";
            [Tooltip("Bottom-left XY corner of this rect.")]
            public Vector2 worldMin = new Vector2(-50f, -50f);
            [Tooltip("Extent of this rect. Pad ~1 unit beyond the playable area.")]
            public Vector2 worldSize = new Vector2(100f, 100f);
            [Tooltip("Pixels per world unit. Higher = crisper trail, more memory (R8).")]
            [Range(4, 80)] public int pixelsPerUnit = 20;

            [NonSerialized] public RenderTexture rt;
            [NonSerialized] public RenderTexture rtCold;
            [NonSerialized] public Vector2Int resolution;
            [NonSerialized] public bool dirty;

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

            // R8 = 1 byte / pixel, allocated twice (warm + cold).
            public long PlannedMemoryBytes { get { var r = PlannedResolution; return (long)r.x * r.y; } }

            public bool Contains(Vector2 p)
            {
                return p.x >= worldMin.x && p.x <= worldMin.x + worldSize.x
                    && p.y >= worldMin.y && p.y <= worldMin.y + worldSize.y;
            }

            public string Label => string.IsNullOrEmpty(id) ? "(default)" : id;

#if UNITY_EDITOR
            [NonSerialized] public string gizmoLabel;
            [NonSerialized] public Vector2Int gizmoCachedRes;
            [NonSerialized] public Vector2 gizmoCachedSize;
#endif
        }

        public static WorldTrailManager Instance { get; private set; }

        // With "Enter Play Mode → Reload Domain" disabled, statics persist across Play sessions.
        // Reset so the previous session's fake-null reference can't shadow the new manager.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        [Header("Regions")]
        [Tooltip("Non-overlapping world rects, each with its own RT pair and save files. The rect holding the " +
                 "slime is the one the ground shader draws; stamps go to the rect holding the stamp. Split a tall " +
                 "level into rects so no single RT exceeds 8192 px per side, and keep rects tight around playable " +
                 "areas so empty space costs nothing.")]
        [SerializeField] private List<Region> regions = new List<Region>();
        [Tooltip("Polled to pick the active rect. Auto-resolved on Awake when left empty.")]
        [SerializeField] private SlimeController slime;

        // Legacy single-rect fields. Seed regions[0] once so existing scene/prefab bounds and their
        // save files survive; unused afterwards.
        [HideInInspector] [SerializeField] private Vector2 worldMin = Vector2.zero;
        [HideInInspector] [SerializeField] private Vector2 worldSize = Vector2.zero;
        [HideInInspector] [SerializeField] private int pixelsPerUnit = 20;

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

        private TrailPersistence persistence;
        private float currentFreezeT;
        private int activeIndex;
        private readonly List<RegionTarget> warmTargets = new List<RegionTarget>();
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

        public IReadOnlyList<Region> Regions => regions;
        public int ActiveRegionIndex => activeIndex;
        public LayerMask GroundLayers => groundLayers;
        public bool ShowBoundsGizmo => showBoundsGizmo;
        public TrailPersistence Persistence => persistence;

        // Single source of truth for stamp-time freezeT, so consumers (LocalTrailReceiver) don't
        // each poll SlimeFrostController. Single-slime stage.
        public float CurrentFreezeT => currentFreezeT;

        private string SceneKey => string.IsNullOrEmpty(sceneKeyOverride)
            ? SceneManager.GetActiveScene().buildIndex.ToString()
            : sceneKeyOverride;

        /// Empty id keeps the pre-region-list key, so saves made before the split still load.
        private string SaveKeyFor(Region r, bool cold)
        {
            string key = (cold ? "world_cold_" : "world_") + SceneKey;
            return string.IsNullOrEmpty(r.id) ? key : key + "_" + r.id;
        }

        /// Every key this manager writes (warm + cold per region). Editor tooling deletes by these.
        public string[] AllSaveKeys()
        {
            SeedRegionsFromLegacy();
            var keys = new string[regions.Count * 2];
            for (int i = 0; i < regions.Count; i++)
            {
                keys[i * 2]     = SaveKeyFor(regions[i], false);
                keys[i * 2 + 1] = SaveKeyFor(regions[i], true);
            }
            return keys;
        }

        private static TrailTargetMeta BuildMetaFor(Region r)
        {
            return new TrailTargetMeta
            {
                version = TrailPersistence.SaveVersion,
                resolutionX = r.resolution.x,
                resolutionY = r.resolution.y,
                originX = r.worldMin.x,
                originY = r.worldMin.y,
                sizeX = r.worldSize.x,
                sizeY = r.worldSize.y,
                format = TrailPersistence.Format,
            };
        }

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

            SeedRegionsFromLegacy();
            WarnOnDuplicateIds();

            for (int i = 0; i < regions.Count; i++)
            {
                var r = regions[i];
                r.resolution = r.PlannedResolution;
                r.rt     = CreateTrailRt(r.resolution, "WorldTrail_"     + SaveKeyFor(r, false));
                r.rtCold = CreateTrailRt(r.resolution, "WorldTrailCold_" + SaveKeyFor(r, true));
            }

            if (brushMaterial != null)
                brushInstance = new Material(brushMaterial) { name = brushMaterial.name + " (Instance)", hideFlags = HideFlags.HideAndDontSave };

            persistence = new TrailPersistence(autoSaveInterval);
            for (int i = 0; i < regions.Count; i++)
            {
                // Warm and cold have distinct keys, so warm-only saves from before the cold route stay loadable.
                var warm = new RegionTarget(regions[i], this, false);
                var cold = new RegionTarget(regions[i], this, true);
                warmTargets.Add(warm);
                persistence.Register(warm);
                persistence.TryLoad(warm);
                persistence.Register(cold);
                persistence.TryLoad(cold);
            }

            if (slime == null) slime = FindFirstObjectByType<SlimeController>();
            activeIndex = slime != null ? Mathf.Max(0, IndexContaining(slime.Position)) : 0;

            PublishGlobals();
        }

        /// Pre-region-list scenes carry bounds in the legacy fields; move them into regions[0] once.
        private void SeedRegionsFromLegacy()
        {
            if (regions == null) regions = new List<Region>();
            if (regions.Count > 0 || worldSize.x <= 0f || worldSize.y <= 0f) return;
            regions.Add(new Region { id = "", worldMin = worldMin, worldSize = worldSize, pixelsPerUnit = pixelsPerUnit });
        }

        private void WarnOnDuplicateIds()
        {
            for (int i = 0; i < regions.Count; i++)
                for (int j = i + 1; j < regions.Count; j++)
                    if (regions[i].id == regions[j].id)
                        Debug.LogError($"[Trail] Regions {i} and {j} share the id '{regions[i].Label}' — their save files clobber each other. Give each region a unique id.", this);
        }

        // Linear readWrite: R8 defaults to sRGB, which isn't a supported GPU format and silently
        // falls back to RGBA32 sRGB (4× the budgeted memory). The black blit is mandatory — a fresh
        // RT holds undefined GPU memory and would show garbage tint through the ground shader.
        private static RenderTexture CreateTrailRt(Vector2Int res, string rtName)
        {
            var t = new RenderTexture(res.x, res.y, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
                autoGenerateMips = false,
                name = rtName,
            };
            t.Create();
            Graphics.Blit(Texture2D.blackTexture, t);
            return t;
        }

        private void Start()
        {
            if (regions.Count == 0)
                Debug.LogWarning("[Trail] No regions configured — nothing will receive a world trail.", this);

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
            UpdateActiveRegion();
        }

        /// Binds the rect the slime stands in. Rects are non-overlapping, so the first hit wins; a slime
        /// in the gap between rects keeps the previous binding instead of flickering to nothing.
        private void UpdateActiveRegion()
        {
            if (slime == null || regions.Count < 2) return;
            int idx = IndexContaining(slime.Position);
            if (idx < 0 || idx == activeIndex) return;
            activeIndex = idx;
            PublishActiveRegion();
        }

        private int IndexContaining(Vector2 p)
        {
            for (int i = 0; i < regions.Count; i++)
                if (regions[i].Contains(p)) return i;
            return -1;
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
            warmTargets.Clear();
            for (int i = 0; i < regions.Count; i++)
            {
                var r = regions[i];
                if (r.rt != null) { r.rt.Release(); UnityEngine.Object.Destroy(r.rt); r.rt = null; }
                if (r.rtCold != null) { r.rtCold.Release(); UnityEngine.Object.Destroy(r.rtCold); r.rtCold = null; }
            }
            if (brushInstance != null) { UnityEngine.Object.Destroy(brushInstance); brushInstance = null; }
            if (bakedTrailGradient != null) { UnityEngine.Object.Destroy(bakedTrailGradient); bakedTrailGradient = null; }
            if (bakedPositionGradient != null) { UnityEngine.Object.Destroy(bakedPositionGradient); bakedPositionGradient = null; }
            if (bakedAngleGradient != null) { UnityEngine.Object.Destroy(bakedAngleGradient); bakedAngleGradient = null; }
            if (bakedTrailGradientCold != null) { UnityEngine.Object.Destroy(bakedTrailGradientCold); bakedTrailGradientCold = null; }

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

            Shader.SetGlobalTexture("_TrailGradient",      bakedTrailGradient    != null ? (Texture)bakedTrailGradient    : Texture2D.whiteTexture);
            Shader.SetGlobalTexture("_TrailPosGradient",   bakedPositionGradient != null ? (Texture)bakedPositionGradient : Texture2D.whiteTexture);
            Shader.SetGlobalTexture("_TrailAngleGradient", bakedAngleGradient    != null ? (Texture)bakedAngleGradient    : Texture2D.whiteTexture);
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
            Shader.SetGlobalTexture("_TrailGradientCold",
                bakedTrailGradientCold != null ? (Texture)bakedTrailGradientCold : Texture2D.whiteTexture);

            PublishActiveRegion();
        }

        /// Only the active rect's RTs and bounds reach the shader. Size zero means "no region",
        /// which the ground shader treats as "skip the trail entirely".
        private void PublishActiveRegion()
        {
            Region r = activeIndex >= 0 && activeIndex < regions.Count ? regions[activeIndex] : null;

            Shader.SetGlobalTexture("_TrailMask",     r != null && r.rt     != null ? (Texture)r.rt     : Texture2D.blackTexture);
            Shader.SetGlobalTexture("_TrailMaskCold", r != null && r.rtCold != null ? (Texture)r.rtCold : Texture2D.blackTexture);
            Shader.SetGlobalVector ("_TrailWorldMin",  r != null ? new Vector4(r.worldMin.x,  r.worldMin.y,  0f, 0f) : Vector4.zero);
            Shader.SetGlobalVector ("_TrailWorldSize", r != null ? new Vector4(r.worldSize.x, r.worldSize.y, 0f, 0f) : Vector4.zero);
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
            SeedRegionsFromLegacy();
            // Live preview of inspector tweaks; globals only exist in Play.
            if (Application.isPlaying) PublishGlobals();
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

        /// Stamp into the rect holding the point; strength splits warm ×(1-T) / cold ×T by currentFreezeT.
        public void Stamp(Vector2 worldPos, float worldRadius, float strength)
        {
            if (brushMaterial == null) return;

            int idx = IndexContaining(worldPos);
            if (idx < 0) return;
            var r = regions[idx];
            if (r.rt == null || r.worldSize.x <= 0f || r.worldSize.y <= 0f) return;

            // World → UV; per-axis because the rect is generally non-square.
            Vector2 uv = new Vector2(
                (worldPos.x - r.worldMin.x) / r.worldSize.x,
                (worldPos.y - r.worldMin.y) / r.worldSize.y);
            Vector2 radiusUV = new Vector2(worldRadius / r.worldSize.x, worldRadius / r.worldSize.y);

            float warmS = strength * (1f - currentFreezeT);
            float coldS = strength * currentFreezeT;
            // Skip near-zero contributions so a fully-frozen slime doesn't waste a GL draw on warm.
            if (warmS > 0.001f) BlitToTarget(r.rt, uv, radiusUV, warmS);
            if (coldS > 0.001f && r.rtCold != null) BlitToTarget(r.rtCold, uv, radiusUV, coldS);
            r.dirty = true;
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

        /// Clears every region's RTs (warm + cold) immediately. Play-mode only — no-ops otherwise.
        public void ClearWorldTrail()
        {
            if (!Application.isPlaying) return;
            for (int i = 0; i < regions.Count; i++)
            {
                var r = regions[i];
                if (r.rt != null) Graphics.Blit(Texture2D.blackTexture, r.rt);
                if (r.rtCold != null) Graphics.Blit(Texture2D.blackTexture, r.rtCold);
                r.dirty = true;
            }
        }

        /// Clears every region and every LocalTrailReceiver in the scene. Play-mode only.
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
            var t = ActiveWarmTarget();
            if (t == null) return;
            persistence.SaveNamedSlotSync(t, "A");
            Debug.Log($"[Trail] Snapshot 'A' saved for region '{regions[activeIndex].Label}'.");
        }

        [ContextMenu("Trail / Load Snapshot \"A\"")]
        private void DebugLoadSnapshotA()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[Trail] Play Mode only."); return; }
            var t = ActiveWarmTarget();
            bool ok = t != null && persistence.LoadNamedSlotSync(t, "A");
            Debug.Log(ok ? "[Trail] Snapshot 'A' loaded." : "[Trail] Snapshot 'A' not found or metadata mismatch.");
        }

        private RegionTarget ActiveWarmTarget()
        {
            if (persistence == null || activeIndex < 0 || activeIndex >= warmTargets.Count) return null;
            return warmTargets[activeIndex];
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
            for (int i = 0; i < regions.Count; i++) DrawRegionGizmo(i, regions[i], selected);
#endif
        }

#if UNITY_EDITOR
        private void DrawRegionGizmo(int index, Region r, bool selected)
        {
            if (r.worldSize.x <= 0f || r.worldSize.y <= 0f) return;
            if (gizmoCorners == null || gizmoCorners.Length != 4) gizmoCorners = new Vector3[4];
            gizmoCorners[0] = new Vector3(r.worldMin.x,                r.worldMin.y,                0f);
            gizmoCorners[1] = new Vector3(r.worldMin.x + r.worldSize.x, r.worldMin.y,               0f);
            gizmoCorners[2] = new Vector3(r.worldMin.x + r.worldSize.x, r.worldMin.y + r.worldSize.y, 0f);
            gizmoCorners[3] = new Vector3(r.worldMin.x,                r.worldMin.y + r.worldSize.y, 0f);

            Color fill    = gizmoPalette[index % gizmoPalette.Length];
            Color outline = fill;
            fill.a = 0.12f;
            outline.a = 1f;
            if (!selected) { fill.a = 0.05f; outline.a = 0.6f; }
            // In Play the bound rect is the only one the ground shader draws — make it obvious which.
            if (Application.isPlaying && index != activeIndex) { fill.a *= 0.3f; outline.a *= 0.4f; }

            UnityEditor.Handles.DrawSolidRectangleWithOutline(gizmoCorners, fill, outline);

            // Selected only: Handles.Label is an IMGUI path that allocates and renders text every
            // call — the most expensive thing here under continuous Scene-view repaint.
            if (!selected) return;

            if (gizmoLabelStyle == null)
            {
                gizmoLabelStyle = new GUIStyle(UnityEditor.EditorStyles.helpBox) { alignment = TextAnchor.MiddleCenter, fontSize = 11 };
                gizmoLabelStyle.normal.textColor = Color.white;
            }
            // Rebuild label text only when its inputs change, not once per repaint.
            var res = r.PlannedResolution;
            if (r.gizmoLabel == null || res != r.gizmoCachedRes ||
                !Mathf.Approximately(r.worldSize.x, r.gizmoCachedSize.x) ||
                !Mathf.Approximately(r.worldSize.y, r.gizmoCachedSize.y))
            {
                long bytes = r.PlannedMemoryBytes;
                string mem = bytes < 1024 * 1024 ? (bytes / 1024f).ToString("0.0") + " KB" : (bytes / 1024f / 1024f).ToString("0.0") + " MB";
                r.gizmoLabel = $"Trail Region {index}: {r.Label}\n{r.worldSize.x:0.#} × {r.worldSize.y:0.#}  ·  RT {res.x}×{res.y}  ·  {mem}";
                r.gizmoCachedRes = res;
                r.gizmoCachedSize = r.worldSize;
            }
            UnityEditor.Handles.Label((gizmoCorners[0] + gizmoCorners[2]) * 0.5f, r.gizmoLabel, gizmoLabelStyle);
        }

        private static GUIStyle gizmoLabelStyle;
        private static readonly Color[] gizmoPalette =
        {
            new Color(0.25f, 1.00f, 0.30f),
            new Color(1.00f, 0.75f, 0.20f),
            new Color(0.35f, 0.70f, 1.00f),
            new Color(1.00f, 0.40f, 0.75f),
            new Color(0.80f, 0.55f, 1.00f),
            new Color(1.00f, 1.00f, 0.45f),
        };
        private Vector3[] gizmoCorners;
#endif

        // -------------------- Save adapter --------------------
        // One per region per route. Cold has its own key so warm-only saves stay loadable, and the
        // dirty flag is per region so an unvisited region never triggers a readback.
        private sealed class RegionTarget : ITrailTarget
        {
            private readonly Region region;
            private readonly WorldTrailManager owner;
            private readonly bool cold;
            public RegionTarget(Region region, WorldTrailManager owner, bool cold)
            {
                this.region = region; this.owner = owner; this.cold = cold;
            }

            public RenderTexture Rt => cold ? region.rtCold : region.rt;

            public string SaveKey => owner.SaveKeyFor(region, cold);

            public Vector2Int Resolution => region.resolution;

            // Warm clears for both — round-robin visits them in turn and they always save together.
            public bool Dirty => region.dirty;
            public void MarkDirty() => region.dirty = true;
            public void ClearDirty() { if (!cold) region.dirty = false; }

            public TrailTargetMeta BuildMeta() => BuildMetaFor(region);

            public void OnLoadedInto() { /* no post-load hook needed for world targets */ }
        }
    }
}
