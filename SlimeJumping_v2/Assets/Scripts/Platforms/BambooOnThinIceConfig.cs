using UnityEngine;

namespace SlimeJumping.Platforms
{
    /// Shared BambooOnThinIce tuning; canonical asset <c>Assets/Resources/BambooOnThinIceConfig.asset</c> (auto-loaded).
    /// Per-link variants go through the component's Config Override field.
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

        [Tooltip("Seconds without any contact on the stalk before the partial cracks heal back " +
                 "to none (the count AND the shader progress). Leaving the respawn zone still " +
                 "resets them immediately, whichever comes first. 0 = never heal on its own.")]
        [Min(0f)] public float crackHealSeconds = 2f;

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

        [Tooltip("Extra mass a supporting stalk feels while the SLIME rides the fallen pole. " +
                 "One number drives all three ways the slime reaches the stalk through the pole: " +
                 "weight bow while it stands (m·g), landing kick when it touches down (m·v), and " +
                 "recoil when it launches off. Split between supports — a pole bridging two stalks " +
                 "loads each with half. 0 = the stalk only ever feels the pole itself (bambooMass).")]
        [Min(0f)] public float ridingSlimeMass = 1f;

        [Tooltip("Seconds after the stalk topples before it stands back up on its own (the ice " +
                 "regenerates with it — same path the respawn zone uses). The zone-exit route " +
                 "still works and wins if the slime leaves the area first. 0 = no timer, zone " +
                 "exit only.")]
        [Min(0f)] public float respawnDelaySeconds = 2f;

        [Tooltip("Seconds the fallen pole may exist before it poofs, counted from the topple. " +
                 "Independent of the respawn above — a shorter respawn simply means the new stalk " +
                 "and the old pole coexist for a while. 0 = no age limit.")]
        [Min(0f)] public float corpseMaxLifeSeconds = 4f;

        [Tooltip("Seconds the fallen pole must stay at rest (asleep or settled Static) before it " +
                 "poofs. 0 = never poof on rest, age limit and zone exit only.")]
        [Min(0f)] public float corpseRestPoofSeconds = 2f;

        [Tooltip("Dust-puff prefab spawned along the pole when the corpse is removed (out of " +
                 "zone / ice regen). playOnAwake must be OFF; Play() is called explicitly. " +
                 "Null = the corpse just disappears with no effect.")]
        public GameObject corpsePoofPrefab;

        const string ResourcesPath = "BambooOnThinIceConfig";

        /// Override if assigned, else the Resources asset, else null — callers fall back to class defaults.
        public static BambooOnThinIceConfig Resolve(BambooOnThinIceConfig @override)
        {
            if (@override != null) return @override;
            return Resources.Load<BambooOnThinIceConfig>(ResourcesPath);
        }
    }
}
