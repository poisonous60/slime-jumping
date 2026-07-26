using UnityEngine;

namespace SlimeJumping.Platforms
{
    /// Shared tuning for BambooOnThinIce. Canonical asset lives at
    /// <c>Assets/Resources/BambooOnThinIceConfig.asset</c> (auto-loaded; the test
    /// setup menu creates it if missing). Per-link variants go through the
    /// component's Config Override field.
    [CreateAssetMenu(fileName = "BambooOnThinIceConfig",
        menuName = "SlimeJumping/Bamboo On ThinIce Config", order = 0)]
    public sealed class BambooOnThinIceConfig : ScriptableObject
    {
        [Header("Ice link")]
        [Tooltip("Number of stalk landings that fully crack the ice; the Nth landing shatters it.")]
        [Min(1)] public int jumpsToShatter = 3;

        [Tooltip("Seconds the slime must stay OFF the stalk before the next contact counts as " +
                 "a new landing — debounce for attach flicker double-counts.")]
        [Min(0f)] public float landingDebounceSeconds = 0.1f;

        [Header("Corpse fall")]
        [Tooltip("Mass the SLIME feels (rigidbody mass: shove resistance, launch recoil " +
                 "response). No effect on fall speed, no effect on stalk shake (bambooMass).")]
        [UnityEngine.Serialization.FormerlySerializedAs("corpseMass")]
        [Min(0.01f)] public float slimeMass = 8f;

        [Tooltip("Fall acceleration (real gravity multiplier). The corpse motor bypasses the " +
                 "base MovePosition tick, so unlike FallingTrain this is genuine accumulating " +
                 "gravity — keep it low (FallingTrain-style 80 would be a rocket drop).")]
        [Min(0f)] public float corpseGravityScale = 2.5f;

        [Tooltip("Higher = lower terminal velocity (floatier fall).")]
        [Min(0f)] public float corpseLinearDamping = 0.05f;

        [Tooltip("Higher = the tumble slows down faster after impact.")]
        [Min(0f)] public float corpseAngularDamping = 0.6f;

        [Tooltip("Launch recoil: impulse = -launchVelocity × this, applied to a Dynamic corpse " +
                 "at the launch point when the slime jumps off it. 0 = no recoil. A Static " +
                 "(ground-settled) corpse never recoils.")]
        [Min(0f)] public float corpseRecoilImpulse = 1f;

        [Tooltip("Mass a live BAMBOO stalk feels when the fallen pole rests on it — landing " +
                 "kick and weight bow both scale linearly in this. Independent of slimeMass; " +
                 "the slime's rider mass is ~1 for comparison.")]
        [Min(0f)] public float bambooMass = 2f;

        [Tooltip("Seconds the corpse keeps holding a live stalk after their contact drops. " +
                 "Resting on a swinging spring makes contacts blink out for a few frames at a " +
                 "time; releasing on every blink re-arms the stalk's landing kick AND its " +
                 "reland lockout, which keeps the slime Airborne on top of that stalk. Must be " +
                 "longer than a flicker gap, shorter than a real departure.")]
        [Min(0f)] public float stalkContactGraceSeconds = 0.3f;

        [Tooltip("Dust-puff prefab spawned along the pole when the corpse is removed (out of " +
                 "zone / ice regen). playOnAwake must be OFF; Play() is called explicitly. " +
                 "Null = the corpse just disappears with no effect.")]
        public GameObject corpsePoofPrefab;

        const string ResourcesPath = "BambooOnThinIceConfig";

        /// Override if assigned, else the canonical Resources asset, else null —
        /// BambooOnThinIce falls back to a runtime instance with class defaults.
        public static BambooOnThinIceConfig Resolve(BambooOnThinIceConfig @override)
        {
            if (@override != null) return @override;
            return Resources.Load<BambooOnThinIceConfig>(ResourcesPath);
        }
    }
}
