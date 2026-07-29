using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    /// <summary>Per-mode tuning for GraniteBlastEmitter Burst mode (multi-beam fan).</summary>
    [CreateAssetMenu(fileName = "GraniteBlastBurstConfig",
        menuName = "SlimeJumping/Granite Blast/Burst Config", order = 2)]
    public class GraniteBlastBurstConfig : ScriptableObject
    {
        [Tooltip("Number of beam-flashes in the fan.")]
        public int burstFlashCount = 6;
        [Tooltip("Total cone spread in degrees (split half above / half below the aim line).")]
        public float burstSpreadDegrees = 32f;
        [Tooltip("Each flash's lifetime — long enough that the whole fan is visible at once.")]
        public float burstFlashDuration = 0.7f;
        public float burstFlashFadeTime = 0.25f;
        [Tooltip("Core line width for each burst flash. Thinner than the main Beam mode.")]
        public float burstFlashCoreWidth = 0.22f;
        [Tooltip("Fraction of flashes fired at FireStart. Only used when burstFlashInterval == 0.")]
        [Range(0f, 1f)]
        public float burstFireStartFraction = 1f;
        [Tooltip("Seconds between consecutive flash spawns. > 0 enables sequential firing (overrides burstFireStartFraction). 0 = batch mode using fraction.")]
        public float burstFlashInterval = 0f;
        [Tooltip("Burst flash tip travel speed in world units per second. 0 = instant full-length flash.")]
        public float burstFlashTravelSpeed = 150f;
    }
}
