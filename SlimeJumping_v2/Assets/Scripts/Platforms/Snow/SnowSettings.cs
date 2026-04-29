using UnityEngine;

namespace SlimeJumping.Platforms.Snow
{
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

        [Header("Render")]
        [Tooltip("Material assigned to every spawned SnowStrip renderer. Must use Ground_Quibli_Snow.shader (or compatible). Per-strip heightTex pushed via MPB so this asset is read-only — Phase 3 textures/keywords below are forwarded onto a runtime material instance, never mutating the asset.")]
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

        [Header("Carve (impact → dip)")]
        [Tooltip("Below this normal impact speed (m/s) no carve is emitted — soft landings leave the snow untouched.")]
        [Min(0f)] public float minImpactSpeed = 1.5f;
        [Tooltip("Carve depth per unit of normal impact speed. Final depth = clamp(impactSpeed * depthPerImpact, 0, depthMax).")]
        [Min(0f)] public float depthPerImpact = 0.012f;
        [Min(0f)] public float depthMax = 0.20f;
        [Tooltip("Carve radius per unit of normal impact speed. Final radius = clamp(impactSpeed * radiusPerImpact, 0, radiusMax).")]
        [Min(0f)] public float radiusPerImpact = 0.05f;
        [Min(0.001f)] public float radiusMax = 0.8f;

        [Header("Recovery")]
        [Tooltip("Snow re-fill rate while a carve recovers, in metres per second. Per-vertex heightCurrent lerps toward heightRest at this rate.")]
        [Min(0f)] public float accumulationRate = 0.05f;
    }
}
