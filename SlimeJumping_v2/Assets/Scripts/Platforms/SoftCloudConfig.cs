using UnityEngine;

namespace SlimeJumping.Platforms
{
    /// <summary>
    /// Shared tuning for every <see cref="SoftCloudPlatform"/> in the game. The canonical asset
    /// lives at <c>Assets/Resources/SoftCloudConfig.asset</c> and is auto-loaded — clouds carry no
    /// per-instance numbers, only an optional override for a special variant (mirrors
    /// <see cref="ThinIceV2.ThinIceV2Config"/>).
    /// </summary>
    [CreateAssetMenu(fileName = "SoftCloudConfig",
        menuName = "SlimeJumping/Soft Cloud/Config", order = 0)]
    public sealed class SoftCloudConfig : ScriptableObject
    {
        [Header("Body recoil — how far the WHOLE cloud moves")]
        [Tooltip("Peak distance (world units) the whole cloud is pushed along the landing normal, " +
                 "for a landing at 'Ref Impact Speed'. Scales linearly with the actual landing speed. " +
                 "0 = the body never moves and only the surface dents.")]
        [Min(0f)] public float bodyRecoilDistance = 0.25f;
        [Tooltip("Landing ImpactNormalSpeed (world u/s) at which the recoil reaches exactly Body Recoil Distance.")]
        [Min(0.1f)] public float bodyRecoilRefSpeed = 8f;
        [Tooltip("How far the cloud stays pressed down while a slime rides it (world units). The landing " +
                 "wobbles around this and then settles ON it — it does not spring back to rest until the " +
                 "slime leaves. 0 = fully returns to rest.")]
        [Min(0f)] public float bodySagDistance = 0.12f;
        [Tooltip("Return-spring frequency (Hz) pulling the cloud back to its rest position. Higher = snappier return.")]
        [Min(0.05f)] public float bodyFrequency = 1.6f;
        [Tooltip("Return-spring damping ratio. <1 = the cloud overshoots rest and wobbles back, 1 = no overshoot.")]
        [Range(0.05f, 1.5f)] public float bodyDamping = 0.35f;
        [Tooltip("Hard cap on how far the body may leave rest (world units), whatever the landing speed.")]
        [Min(0f)] public float bodyMaxOffset = 0.4f;

        [Header("Dent timing (authored ease — the surface feel lives here)")]
        [Tooltip("The dent's shape over time. X = 0..1 normalized over 'Dent Duration'. Y = signed depth " +
                 "fraction: + = inward dent, − = outward rebound past the surface. Draw the whole feel — " +
                 "slow-in, dwell at the bottom, fast-out, overshoot up, hold at the apex, settle. Scaled by peak depth.")]
        public AnimationCurve dentEnvelope = DefaultDentEnvelope();
        [Tooltip("Seconds the dent envelope plays over on each landing.")]
        [Min(0.05f)] public float dentDuration = 0.4f;
        [Tooltip("Peak inward dent depth (world units) for a landing at zero speed — the constant every " +
                 "landing gets, however gently the slime touches down. Capped by Max Deflection.")]
        [Min(0f)] public float dentBaseDepth = 0.43f;
        [Tooltip("Extra peak dent depth (world units) per 1 world u/s of landing ImpactNormalSpeed. " +
                 "Total = Dent Base Depth + this * impact speed.")]
        [Min(0f)] public float dentDepthPerSpeed = 0.002f;

        [Header("Alive (resting + idle)")]
        [Tooltip("Small dent held under a rider while it rests / rolls along (world units). Gives a walking dimple + " +
                 "resting softness. 0 = only reacts on landing.")]
        [Min(0f)] public float restDentDepth = 0.015f;
        [Tooltip("Amplitude of a continuous idle breathing jiggle inside the rider's footprint (world units). " +
                 "Keeps the surface feeling alive at rest. 0 = still.")]
        [Min(0f)] public float idleAmplitude = 0.012f;
        [Tooltip("Idle breathing frequency (Hz).")]
        [Min(0f)] public float idleFrequency = 1.3f;

        [Header("Shape")]
        [Tooltip("Footprint radius = (SlimeRadius / BodyScaleRatio) * this. How wide the dent spreads. " +
                 "Lower = the dent stays local to where the slime landed.")]
        [Min(0.1f)] public float radiusMult = 5f;
        [Tooltip("0..1. Neighbor smoothing of the dent field — higher = the region moves as one coherent chunk " +
                 "(marshmallow), lower = more local/finely-divided.")]
        [Range(0f, 1f)] public float neighborCoupling = 0.4f;
        [Tooltip("Hard cap on inward dent depth (world units). Keep modest so the collider never swallows the slime.")]
        [Min(0.001f)] public float maxDeflection = 0.2f;

        [Header("Follow")]
        [Tooltip("How tightly the collider/mesh tracks the authored target (SmoothDamp time, s). Small = tight to the " +
                 "curve — the wobble comes from the CURVE, not this. Keep small (~0.02-0.05).")]
        [Min(0.001f)] public float followSmoothTime = 0.03f;
        [Tooltip("Max surface speed (world u/s). Caps per-tick dent motion so the slime's surface probe can always " +
                 "track it — no detach mid-bounce.")]
        [Min(0.1f)] public float maxSurfaceSpeed = 3f;

        [Header("Visual follower")]
        [Tooltip("Contour points within this XY radius (world) influence a visual vertex. ~ dent footprint.")]
        [Min(0.05f)] public float visualInfluenceRadius = 0.6f;

        [Header("Perf")]
        [Tooltip("Skip the collider SetPath re-cook when the largest contour delta is below this (world units).")]
        [Min(0f)] public float restEpsilon = 0.0008f;

        // ---------------------------------------------------------------------

        const string ResourcesPath = "SoftCloudConfig";
        static SoftCloudConfig fallback;

        /// <summary>
        /// Returns the override if assigned, otherwise the canonical asset at
        /// <c>Assets/Resources/SoftCloudConfig.asset</c>. If neither exists, returns a shared
        /// in-memory instance holding the defaults above so a fresh scene still behaves.
        /// </summary>
        public static SoftCloudConfig Resolve(SoftCloudConfig @override)
        {
            if (@override != null) return @override;

            var asset = Resources.Load<SoftCloudConfig>(ResourcesPath);
            if (asset != null) return asset;

            if (fallback == null)
            {
                fallback = CreateInstance<SoftCloudConfig>();
                Debug.LogWarning("SoftCloudConfig: no Assets/Resources/SoftCloudConfig.asset found — " +
                                 "running on built-in defaults. Create one via Assets > Create > SlimeJumping > Soft Cloud > Config.");
            }
            return fallback;
        }

        static AnimationCurve DefaultDentEnvelope() => new AnimationCurve(
            new Keyframe(0f,    0f),      // slow-in (flat start)
            new Keyframe(0.22f, 1f),      // reach the bottom
            new Keyframe(0.40f, 1f),      // dwell at the bottom (flat)
            new Keyframe(0.55f, -0.35f),  // fast-out, overshoot up past rest
            new Keyframe(0.70f, -0.35f),  // hold at the apex (flat)
            new Keyframe(0.86f, 0.06f),   // small secondary dip
            new Keyframe(1f,    0f));     // settle
    }
}
