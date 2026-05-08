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
        [Tooltip("How many graph hops inland the snow ramps from 0 (boundary) to full restThickness. 0 = sharp edge. 1 = boundary verts at 0, neighbors at full. Larger = wider rounded bevel. Applies to ALL boundary edges. Use slopeCutTaperHops below to taper ONLY slope-cut boundaries (where the host surface continues but exceeds maxSnowSlopeDeg) — they combine: each vert takes the most restrictive ramp.")]
        [Range(0, 8)] public int edgeTaperHops = 1;

        [Tooltip("Additional taper depth applied ONLY to slope-cut boundary verts — boundary edges that border a too-steep host triangle (where the host surface continues past the slope filter cutoff). 0 = no extra taper. >0 = the snow fades over this many hops at slope-cut endings, separately from edgeTaperHops. Use this to soften the abrupt cliff where snow meets a too-steep face without softening true mesh edges.")]
        [Range(0, 8)] public int slopeCutTaperHops = 0;
        [Tooltip("Barycentric subdivision applied per patch triangle before BFS taper / mesh emit. 0 = host triangulation as-is, 1 = 4× verts, 2 = 16×, 3 = 64×, N = 4^N×. CPU cost of CarveLocal / TickRecovery / SampleDepthRatio01 scales linearly with vertex count — past ~5 the per-FixedUpdate sample loop can become noticeable on large hosts.")]
        [Range(1, 5)] public int subdivLevels = 1;

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

        [Tooltip("Grayscale pattern (.r channel) sampled in carved areas. White = inner snow fully exposed, black = original surface. Color comes from footprintTint below — texture provides shape only. Requires footprintEnabled. Pushed onto the runtime material's _FootprintAlbedo slot.")]
        public Texture2D footprintAlbedo;

        [Tooltip("Color of the 'inner snow' revealed inside carves. Multiplied with base lighting so toon-ramp shading is preserved. Typical: cool blue-white (e.g. RGB 0.65, 0.78, 0.95) for snow-on-snow look. Pushed onto the runtime material's _FootprintTint slot.")]
        [ColorUsage(showAlpha: false, hdr: false)] public Color footprintTint = new Color(0.65f, 0.78f, 0.95f);

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

        [Tooltip("Settle rate for snow piled ABOVE heightRest (m/s). When heightCurrent > heightRest (after a Splash raise), it walks down toward heightRest at this rate. 0 = raise stays permanent — let the field accumulate persistent mounds. >0 = mounds slowly collapse back to natural rest.")]
        [Min(0f)] public float settleRate = 0f;

        [Header("Raise (impact → mound)")]
        [Tooltip("Cap multiplier on heightRest for snow piled above rest. heightMax = heightRest × this. Splash / RaiseLocal cannot push heightCurrent past heightMax. 1.0 = no raise allowed (clamps at rest), 1.5 = 50% extra above natural thickness, etc.")]
        [Min(1f)] public float heightMaxMultiplier = 1.5f;

        [Header("Gameplay coupling")]
        [Tooltip("SurfaceMaterial used as the slime's surface tag while standing on a snow-covered region of the host. Binary: any meaningful snow at the contact → this material; otherwise the host's own tag. Typical wiring: Assets/Settings/SurfaceMaterials/Snow.asset.")]
        public SurfaceMaterial snowSurfaceMaterial;

        [Tooltip("Exponential drag rate (1/s) applied to slime velocity at full snow coverage (depthRatio = 1). Higher = stops faster. Set 0 to disable. With 4: ~63% energy loss per second at full coverage. Drag-only: no continuous carve, no airborne probe — applies while attached/charging on a snow patch.")]
        [Min(0f)] public float plowDragK = 4f;
    }
}
