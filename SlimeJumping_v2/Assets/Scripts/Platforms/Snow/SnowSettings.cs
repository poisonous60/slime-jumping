using UnityEngine;
using SlimeJumping.Physics;

namespace SlimeJumping.Platforms.Snow
{
    /// <summary>Carve depth falloff profile. See SnowSettings.carveProfile tooltip for shape details.</summary>
    public enum CarveProfileType
    {
        Gaussian = 0,
        RaisedCosine = 1,
        SphericalCap = 2,
        Parabola = 3,
    }

    /// <summary>
    /// Single source of truth for snow tuning. SnowField references one of these assets;
    /// every per-platform tweak (deposit slope, mesh density, carve depth/radius, recovery
    /// rate) lives here so designers tune one file and every platform that points at it
    /// updates together. Fall back to inspector-shipped defaults when no asset is wired.
    ///
    /// Material is intentionally part of the settings — same shader is used everywhere; if
    /// a level wants a visually different snow, point that level's SnowField at a different
    /// settings asset that holds the variant material.
    /// </summary>
    [CreateAssetMenu(fileName = "SnowSettings", menuName = "SlimeJumping/Snow/Snow Settings", order = 200)]
    public sealed class SnowSettings : ScriptableObject
    {
        [Header("Deposit")]
        [Tooltip("Triangles whose face normal is steeper than this (degrees from horizontal) stay bare.")]
        [Range(0f, 89f)] public float maxSnowSlopeDeg = 45f;
        [Tooltip("Drop connected components below this triangle count — single-tri patches look like noise.")]
        [Min(1)] public int minPatchTris = 3;

        [Header("Mesh")]
        [Tooltip("Perpendicular thickness applied along each patch vertex's averaged normal. Drives heightRest after edge taper.")]
        [Min(0.01f)] public float restThickness = 0.25f;
        [Tooltip("How many graph hops inland the snow ramps from 0 (boundary) to full restThickness. 0 = sharp edge. 1 = boundary verts at 0, neighbors at full. Larger = wider rounded bevel.")]
        [Range(0, 8)] public int edgeTaperHops = 1;
        [Tooltip("Barycentric subdivision applied per patch triangle before BFS taper / mesh emit. 0 = host triangulation as-is, 1 = 4× verts (default), 2 = 16×, 3 = 64×.")]
        [Range(0, 3)] public int subdivLevels = 1;

        [Tooltip("Per-vertex heightRest multiplier as a function of host-surface angle. X = angle from horizontal (0° = flat ground, 90° = vertical wall) measured at the vertex via averaged normal. Y = multiplier on the taper-shaped restThickness (1 = full, 0 = bare). Default fades from full at 18° to zero at 45° so steep parts thin out gracefully instead of vanishing at the maxSnowSlopeDeg cutoff. WARNING: averaged vertex normals on jagged meshes can read non-zero angles even on \"flat\" geometry — flatten the low-angle portion of the curve if you see thinning on visually level surfaces.")]
        public AnimationCurve slopeToHeightScale = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(18f, 1f),
            new Keyframe(45f, 0f),
            new Keyframe(90f, 0f)
        );

        [Header("Render")]
        [Tooltip("Material assigned to every spawned SnowStrip renderer. Must use Ground_Quibli_Snow.shader (or compatible). Per-strip heightBuf (StructuredBuffer<float>) pushed via MPB so this asset is read-only — Phase 3 textures/keywords below are forwarded onto a runtime material instance, never mutating the asset.")]
        public Material snowMaterial;

        [Tooltip("Quibli toon ramp (NdotL → color). Assign a horizontal blue→white gradient to drive the cool snow shadow tone. Pushed onto the runtime material's _GradientRamp slot at rebuild. Leave null to fall back to whatever the snowMaterial asset has.")]
        public Texture2D shadowRamp;

        [Tooltip("Tile texture multiplied into the base color where snow is carved (mask = 1 - heightCurrent/heightRest). Requires footprintEnabled below. Pushed onto the runtime material's _FootprintAlbedo slot.")]
        public Texture2D footprintAlbedo;

        [Tooltip("Noise texture sampled in fragment to drive glitter sparkle locations (host-local UV). A Perlin noise tile works well. Requires glitterEnabled below. Pushed onto the runtime material's _GlitterNoise slot.")]
        public Texture2D glitterNoise;

        [Header("Phase 3 effects (keyword toggles)")]
        [Tooltip("Enable footprint detail blend on carved snow. Drives the _SNOW_FOOTPRINT shader keyword on the runtime material instance.")]
        public bool footprintEnabled = false;

        [Tooltip("Enable Fresnel rim highlight at grazing camera angles. Drives the _SNOW_FRESNEL shader keyword on the runtime material instance.")]
        public bool fresnelEnabled = false;

        [Tooltip("Enable glitter sparkle overlay. Drives the _SNOW_GLITTER shader keyword on the runtime material instance.")]
        public bool glitterEnabled = false;

        [Header("Shadow Ramp Generation (editor only)")]
        [Tooltip("Color at NdotL = -1 (fully self-shadowed back-face). Deepest cool tone of the toon ramp.")]
        [ColorUsage(showAlpha: false, hdr: false)] public Color rampShadowDeep = new Color(96f/255f, 134f/255f, 178f/255f);
        [Tooltip("Color just before the toon step (still in shadow but lighter).")]
        [ColorUsage(showAlpha: false, hdr: false)] public Color rampShadowEdge = new Color(160f/255f, 192f/255f, 222f/255f);
        [Tooltip("Color just after the toon step (still in light but slightly off-white).")]
        [ColorUsage(showAlpha: false, hdr: false)] public Color rampLitEdge = new Color(245f/255f, 248f/255f, 252f/255f);
        [Tooltip("Color at NdotL = 1 (fully lit). Brightest snow color.")]
        [ColorUsage(showAlpha: false, hdr: false)] public Color rampLitPeak = Color.white;
        [Tooltip("Where on the gradient (0..1) the toon step sits. 0.5 = NdotL ≈ 0 (perpendicular). Lower = more area in shadow tone, higher = more area lit.")]
        [Range(0f, 1f)] public float rampStepCenter = 0.5f;
        [Tooltip("Width of the smoothstep transition at the toon step. Smaller = harder edge (more toon), larger = softer transition.")]
        [Range(0.001f, 0.5f)] public float rampStepWidth = 0.06f;
        [Tooltip("Output PNG width in pixels. 256 is plenty for a 1D gradient.")]
        [Range(64, 1024)] public int rampWidth = 256;

        [Header("Carve (impact → dip)")]
        [Tooltip("Below this normal impact speed (m/s) no carve is emitted — soft landings leave the snow untouched.")]
        [Min(0f)] public float minImpactSpeed = 1.5f;
        [Tooltip("Carve depth per unit of normal impact speed. Final depth = clamp(impactSpeed * depthPerImpact, 0, depthMax).")]
        [Min(0f)] public float depthPerImpact = 0.012f;
        [Min(0f)] public float depthMax = 0.20f;
        [Tooltip("Carve radius per unit of normal impact speed. Final radius = clamp(impactSpeed * radiusPerImpact, 0, radiusMax).")]
        [Min(0f)] public float radiusPerImpact = 0.05f;
        [Min(0.001f)] public float radiusMax = 0.8f;

        [Tooltip("Falloff profile for carve depth as a function of distance d from impact (0..r).\n" +
                 "Gaussian: exp(-4·(d/r)²) — narrow bell, looks pointy/V-shaped at low mesh density.\n" +
                 "RaisedCosine: 0.5·(1+cos(π·d/r)) — smooth bowl, zero slope at center AND at edge.\n" +
                 "SphericalCap: √(1-(d/r)²) — literal ball-press indent, widest/roundest bottom (sharper transition at d=r).\n" +
                 "Parabola: 1-(d/r)² — round bottom, linear taper.")]
        public CarveProfileType carveProfile = CarveProfileType.RaisedCosine;

        [Header("Recovery")]
        [Tooltip("Snow re-fill rate while a carve recovers, in metres per second. Per-vertex heightCurrent lerps toward heightRest at this rate.")]
        [Min(0f)] public float accumulationRate = 0.05f;

        [Header("Gameplay coupling")]
        [Tooltip("SurfaceMaterial blended over the host's tag while standing on snow-covered ground. Typical wiring: Assets/Settings/SurfaceMaterials/Snow.asset (Mud-derived, slow slide). Leave null to disable blending — host SurfaceTag stays in effect everywhere.")]
        public SurfaceMaterial snowSurfaceMaterial;

        [Tooltip("X = remaining snow coverage (0 = bare / fully carved, 1 = full untouched thickness). Y = blend weight toward snowSurfaceMaterial (0 = pure host, 1 = pure snow). Default linear (0,0)→(1,1) gives smooth ramp; flatten the left half for a step (binary covered/bare); shape it freely for graded feel.")]
        public AnimationCurve depthToSnowBlend = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("X = remaining snow coverage (0..1) at the slime's contact when launching. Y = multiplier on launch power. Default flat 1.0 (no effect). Pull the right side down (e.g. (1, 0.6)) so deeply-buried slime jumps weaker.")]
        public AnimationCurve depthToLaunchScale = AnimationCurve.Constant(0f, 1f, 1f);

        [Header("Plow drag (slime pushes through snow)")]
        [Tooltip("Exponential drag rate (1/s) applied to slime velocity at full snow burial (depthRatio = 1). Higher = stops faster. Set 0 to disable plow drag entirely. With 4: ~63% energy loss per second at full burial, ~95% in 0.75s.")]
        [Min(0f)] public float plowDragK = 4f;

        [Tooltip("Per-second carve depth automatically applied at the slime's position while it moves through snow. The slime visibly digs a tunnel as it pushes through. 0 = no continuous carve (only landing impacts carve).")]
        [Min(0f)] public float plowCarveDepthPerSecond = 0.3f;

        [Tooltip("Minimum slime speed (m/s) for the continuous plow carve to fire. Below this the slime can sit / wiggle in snow without digging deeper. Drag itself still applies. 0 = always carve while moving (legacy behaviour).")]
        [Min(0f)] public float plowCarveMinSpeed = 1.5f;

        [Tooltip("Below this remaining snow coverage, plow drag is skipped — keeps the slime free to slide on naturally bare regions (carved-out tracks, patch edges).")]
        [Range(0f, 1f)] public float plowMinDepthRatio = 0.1f;

        [Tooltip("Below this speed (m/s) the plow snaps the slime to a full stop instead of continuing exponential decay — prevents the slime from twitching forever as drag asymptotes toward zero.")]
        [Min(0f)] public float plowStopSpeed = 0.5f;
    }
}
