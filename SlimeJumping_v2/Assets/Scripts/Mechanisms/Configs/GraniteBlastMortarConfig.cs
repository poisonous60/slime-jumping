using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    /// <summary>Per-mode tuning for GraniteBlastEmitter Mortar mode (delayed-tracking projectile).</summary>
    /// Projectile lifecycle: eject from muzzle → travel to orbit anchor → dwell → lock onto the slime and strike (fizzles at strikeMaxDuration).
    [CreateAssetMenu(fileName = "GraniteBlastMortarConfig",
        menuName = "SlimeJumping/Granite Blast/Mortar Config", order = 3)]
    public class GraniteBlastMortarConfig : ScriptableObject
    {
        [Header("Spawn Cadence")]
        [Tooltip("Seconds between consecutive mortar projectile spawns. Spawning runs across FireStart + Firing — set this together with the emitter's firingDuration to tune total count.")]
        public float spawnInterval = 0.18f;
        [Tooltip("Optional hard cap on total projectiles per blast. 0 = unlimited (only firingDuration / spawnInterval limits the count).")]
        public int maxProjectiles = 0;

        [Header("Orbit Phase (spawn → orbit anchor)")]
        [Tooltip("Distance from the muzzle to each projectile's hover anchor (world units).")]
        public float orbitDistance = 3.5f;
        [Tooltip("Random ejection direction is biased upward by this much. 0 = full 360° random around muzzle, 1 = straight up only. Values in between mix random + up bias.")]
        [Range(0f, 1f)]
        public float upwardBias = 0.5f;
        [Tooltip("Seconds the projectile takes to travel from muzzle to its orbit anchor.")]
        public float travelToOrbitDuration = 0.35f;
        [Tooltip("Speed multiplier curve along the spawn → orbit travel. X = normalized progress (0..1), Y = speed multiplier. Average area under curve over [0,1] should be ~1 so the projectile actually arrives at the anchor in travelToOrbitDuration seconds. Use a curve peaking high then easing to 0 to land softly.")]
        public AnimationCurve travelToOrbitSpeedCurve = AnimationCurve.EaseInOut(0f, 1.6f, 1f, 0.2f);

        [Header("Dwell")]
        [Tooltip("Seconds the projectile hovers at its orbit anchor before locking onto the slime and striking.")]
        public float dwellAtOrbitDuration = 0.4f;

        [Header("Strike Phase (orbit → slime)")]
        [Tooltip("Homing strength after lock-in. 0 = direction is locked at strike start (no tracking); 1 = continuously re-aim every frame at the live slime; 0.5 = blend (the projectile's heading lerps toward the slime each frame at this rate).")]
        [Range(0f, 1f)]
        public float homingStrength = 0.3f;
        [Tooltip("Peak/baseline strike speed (world units per second). Multiplied by strikeSpeedCurve each frame.")]
        public float strikeSpeed = 26f;
        [Tooltip("Speed multiplier curve along the strike. X = normalized progress over strikeMaxDuration (0..1), Y = multiplier on strikeSpeed. Use to ease-in (slow start, fast end) or slam-then-coast.")]
        public AnimationCurve strikeSpeedCurve = AnimationCurve.EaseInOut(0f, 0.2f, 1f, 1.5f);
        [Tooltip("Hard cap on strike duration (sec). If the projectile hasn't hit anything by then, it fizzles out.")]
        public float strikeMaxDuration = 1.5f;

        [Header("Visual")]
        [Tooltip("Sprite used for the projectile head. Leave null to use a generated solid disc.")]
        public Sprite headSprite;
        [Tooltip("Body radius (world units). Visual + collision both scale to this.")]
        public float bodyRadius = 0.35f;
        [Tooltip("Z offset applied to the projectile so it renders in front of 2.5D ground.")]
        public float zOffset = -0.5f;
        [Tooltip("Whether the projectile leaves a TrailRenderer behind it.")]
        public bool useTrail = true;
        [Tooltip("TrailRenderer time (seconds of history shown).")]
        public float trailTime = 0.25f;
        [Tooltip("TrailRenderer start width (multiplier on bodyRadius).")]
        [Range(0f, 2f)]
        public float trailStartWidth = 0.9f;
        [Tooltip("TrailRenderer end width (multiplier on bodyRadius).")]
        [Range(0f, 2f)]
        public float trailEndWidth = 0.05f;
    }
}
