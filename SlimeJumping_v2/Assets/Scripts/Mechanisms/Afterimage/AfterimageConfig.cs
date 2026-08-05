using UnityEngine;
using UnityEngine.Serialization;

namespace SlimeJumping.Mechanisms.Afterimage
{
    /// <summary>Shared tuning for the afterimage zone; canonical asset auto-loaded from
    /// <c>Assets/Resources/AfterimageConfig.asset</c>, per-instance override optional.</summary>
    [CreateAssetMenu(fileName = "AfterimageConfig", menuName = "SlimeJumping/Afterimage/Config", order = 1)]
    public sealed class AfterimageConfig : ScriptableObject
    {
        [Header("Recording — when a stamp is taken")]
        [Tooltip("Seconds between stamps while the slime moves inside the zone.")]
        [FormerlySerializedAs("stampInterval")]
        [Min(0.02f)] public float stampIntervalSeconds = 0.05f;
        [Tooltip("Minimum spacing between stamps. Unit: slime bodies (slime size = 1). " +
                 "1 = trail circles barely touch, below 1 = they overlap (gapless trail). " +
                 "Also blocks stamping while charging/idling in place.")]
        [Min(0f)] public float minStampSpacing = 0.45f;

        [Header("Stamp lifetime")]
        [Tooltip("Seconds after the slime passes before the afterimage becomes visible. " +
                 "0 = appears instantly.")]
        [Min(0f)] public float appearDelaySeconds = 0.3f;
        [Tooltip("Seconds from creation to disappearance. The timer never pauses, even while " +
                 "the slime stands on the stamp.")]
        [FormerlySerializedAs("lifetime")]
        [Min(0.5f)] public float stampLifetimeSeconds = 5f;
        [Tooltip("Seconds a fresh stamp stays ghost (no collider) before it can harden.")]
        [FormerlySerializedAs("hardenDelay")]
        [Min(0f)] public float hardenDelaySeconds = 0.4f;
        [Tooltip("Width (world u) of the harden-block box around the slime. A stamp touching " +
                 "the box stays ghost; it hardens once outside. Already-hardened stamps never " +
                 "revert.")]
        [Min(0f)] public float hardenBlockBoxWidth = 4f;
        [Tooltip("How far (world u) the box reaches ABOVE the slime center.")]
        [Min(0f)] public float hardenBlockBoxAbove = 3f;
        [Tooltip("How far (world u) the box reaches BELOW the slime center.")]
        [Min(0f)] public float hardenBlockBoxBelow = 1.5f;

        [Header("Collision")]
        [Tooltip("Stamp collider size relative to the slime. 1 = exactly the slime's size, " +
                 "0.5 = half, 2 = double.")]
        [Min(0.1f)] public float stampSizeScale = 1f;
        [Tooltip("Layer hardened stamps live on. Must be the slime's attachable ground layer.")]
        public LayerMask stampLayer = 1 << 7;

        [Header("Shockwave — launching off a stamp")]
        [Tooltip("Master switch. Off = launching from a stamp erases nothing (no ripple, " +
                 "no silence) — stamps only die by lifetime.")]
        public bool shockwaveEnabled = true;
        [Tooltip("Radius (world u) in which stamps are erased and recording is silenced. " +
                 "Tune >= the height a launch off a stamp can reach, so stamp-ladders can't climb.")]
        [Min(0.5f)] public float shockwaveRadius = 7f;
        [Tooltip("Seconds the erased area refuses new stamps.")]
        [FormerlySerializedAs("silenceSeconds")]
        [Min(0f)] public float silenceDurationSeconds = 2f;

        [Header("Visual")]
        [Tooltip("Alpha of a hardened stamp at birth age; fades to 0 over the lifetime.")]
        [Range(0f, 1f)] public float ghostAlpha = 0.35f;
        [Tooltip("Brightness of the ghost (not yet hardened) phase relative to hardened. " +
                 "Hardened stamps are always this much brighter — a steady step, no blink.")]
        [Range(0f, 1f)] public float ghostAlphaFactor = 0.55f;
        [Tooltip("Seconds the brightness eases up when a stamp hardens (one-way rise).")]
        [Min(0.01f)] public float hardenFadeInSeconds = 0.25f;
        [Tooltip("Ghost tint: unhardened stamps are pushed toward greyscale × this color. " +
                 "Cool blue-grey by default; hardening restores true colors.")]
        [ColorUsage(false, true)] public Color ghostTint = new Color(0.75f, 0.9f, 1.15f);
        [Tooltip("How fully the ghost phase desaturates toward the tint. 0 = no color shift.")]
        [Range(0f, 1f)] public float ghostDesaturation = 1f;
        [Tooltip("Transparent material using SlimeJumping/SlimeAfterimage. The test rig " +
                 "creates and wires Assets/Art/Shader/Mat_SlimeAfterimage.mat.")]
        public Material ghostMaterial;

        [Header("Ripple")]
        [Tooltip("Peak screen-UV distortion of the shockwave ripple. Subtle values read best.")]
        [Range(0f, 0.4f)] public float rippleStrength = 0.08f;
        [Tooltip("Seconds the ripple takes to sweep the shockwave radius.")]
        [FormerlySerializedAs("rippleDuration")]
        [Min(0.05f)] public float rippleDurationSeconds = 0.45f;

        // ---------------------------------------------------------------------

        const string ResourcesPath = "AfterimageConfig";
        static AfterimageConfig fallback;

        /// <summary>Override, else the Resources asset, else a shared in-memory defaults instance.</summary>
        public static AfterimageConfig Resolve(AfterimageConfig @override)
        {
            if (@override != null) return @override;

            var asset = Resources.Load<AfterimageConfig>(ResourcesPath);
            if (asset != null) return asset;

            if (fallback == null)
            {
                fallback = CreateInstance<AfterimageConfig>();
                Debug.LogWarning("AfterimageConfig: no Assets/Resources/AfterimageConfig.asset found — " +
                                 "running on built-in defaults. Create one via Assets > Create > SlimeJumping > " +
                                 "Afterimage > Config.");
            }
            return fallback;
        }
    }
}
