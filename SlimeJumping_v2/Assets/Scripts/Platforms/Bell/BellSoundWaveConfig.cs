using UnityEngine;

namespace SlimeJumping.Platforms.Bell
{
    /// <summary>Shared tuning for <see cref="BellSoundWave"/>; canonical asset auto-loaded from
    /// <c>Assets/Resources/BellSoundWaveConfig.asset</c>, per-instance override optional.</summary>
    [CreateAssetMenu(fileName = "BellSoundWaveConfig", menuName = "SlimeJumping/Bell/Sound Wave Config", order = 1)]
    public sealed class BellSoundWaveConfig : ScriptableObject
    {
        [Header("Wave — how the ring of sound travels")]
        [Tooltip("How fast the wavefront expands (world u/s).")]
        [Min(0.1f)] public float waveSpeed = 7f;
        [Tooltip("Radius at which the wave dies (world u). Must reach the farthest hidden platform.")]
        [Min(0.1f)] public float maxRadius = 15f;
        [Tooltip("Thickness of the wavefront's leading edge (world u) — a pixel fades in over the " +
                 "time the front takes to cross this distance. Wider = softer arrival.")]
        [Min(0.01f)] public float frontWidth = 1.5f;

        [Header("Reveal — how long a passed-over pixel stays visible")]
        [Tooltip("Seconds a pixel stays fully visible after the front has passed it.")]
        [Min(0f)] public float holdSeconds = 1f;
        [Tooltip("Seconds a pixel takes to fade back to invisible after the hold.")]
        [Min(0.01f)] public float fadeSeconds = 1f;

        [Header("Trail on hidden platforms")]
        [Tooltip("Stamp strength multiplier for goo landing on the hidden platforms — their trail is " +
                 "the only always-visible hint, so it can be stronger than on normal ground. 1 = same.")]
        [Min(0f)] public float hiddenPlatformTrailStrength = 1f;

        [Header("Distortion ring")]
        [Tooltip("Peak screen-UV offset inside the ring band. Small values read best — 0.15 shifts " +
                 "15% of the screen and feels like the camera itself lurching.")]
        [Range(0f, 0.2f)] public float distortionStrength = 0.02f;
        [Tooltip("Thickness of the distortion ring (world u).")]
        [Min(0.05f)] public float distortionRingWidthWorld = 1.2f;
        [Tooltip("Blur radius inside the ring band (screen UV). Softens the ring into frosted " +
                 "glass instead of a hard zoom; 0 = pure refraction.")]
        [Range(0f, 0.02f)] public float distortionBlurSize = 0.004f;

        [Header("Editor")]
        [Tooltip("How visible the hidden platforms are in EDIT mode, so they can be placed and told " +
                 "apart from normal platforms. Play mode and builds always start fully invisible.")]
        [Range(0f, 1f)] public float editorPreviewAlpha = 0.4f;

        // ---------------------------------------------------------------------

        const string ResourcesPath = "BellSoundWaveConfig";
        static BellSoundWaveConfig fallback;

        /// <summary>Override, else the Resources asset, else a shared in-memory defaults instance.</summary>
        public static BellSoundWaveConfig Resolve(BellSoundWaveConfig @override)
        {
            if (@override != null) return @override;

            var asset = Resources.Load<BellSoundWaveConfig>(ResourcesPath);
            if (asset != null) return asset;

            if (fallback == null)
            {
                fallback = CreateInstance<BellSoundWaveConfig>();
                Debug.LogWarning("BellSoundWaveConfig: no Assets/Resources/BellSoundWaveConfig.asset found — " +
                                 "running on built-in defaults. Create one via Assets > Create > SlimeJumping > " +
                                 "Bell > Sound Wave Config.");
            }
            return fallback;
        }
    }
}
