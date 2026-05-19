using UnityEngine;
using UnityEngine.Serialization;

namespace SlimeJumping.Settings
{
    /// <summary>
    /// Single source of truth for bamboo gameplay tuning. BambooPlatform references one of
    /// these assets; every per-stalk tweak that affects feel (spring frequency, damping,
    /// pumping gain, tilt torque, grab-mode carrier boost, re-entry guards) lives here so
    /// designers tune one file and every bamboo that points at it updates together.
    ///
    /// Per-stalk fields that stay on BambooPlatform itself: geometry (length, segment count,
    /// segment radius, collider shape), the backgroundOnly mode toggle, the surface-material
    /// override, and everything under "Visual" (material, texture, colors, node geometry,
    /// toon lighting, mesh tessellation) — those are intentionally per-stalk so different
    /// bamboos can look visually distinct without forking a settings asset.
    ///
    /// BambooPlatform falls back to a runtime-created BambooSettings instance with the
    /// class-default values below if no asset is wired, so existing scenes that haven't
    /// been migrated still get the same numbers they had before this refactor.
    /// </summary>
    [CreateAssetMenu(fileName = "BambooSettings", menuName = "SlimeJumping/Bamboo Settings", order = 210)]
    public sealed class BambooSettings : ScriptableObject
    {
        [Header("Spring Shape")]
        [Range(0.3f, 3f)] public float naturalFreqHz = 1.2f;
        [Range(0f, 1f)] public float dampingRatio = 0.45f;
        [Range(5f, 90f)] public float maxDeflectionDeg = 30f;
        [Tooltip("Bounce factor when |currentAngle| hits maxDeflectionDeg. 0 = full absorb (no cap bounce, no ringing). >0 = elastic rebound off the cap.")]
        [Range(0f, 0.9f)] public float clampRestitution = 0f;

        [Header("Pumping")]
        [Tooltip("Multiplier on angular impulse from rider Δp. Lowering this makes the bamboo less sensitive to hit position (high impulses won't snap the cap).")]
        [Range(0f, 5f)] public float riderPumpGain = 0.4f;
        [Range(0f, 2f)] public float chargePumpScale = 1f;
        [Range(0f, 2f)] public float riderTorqueScale = 1f;

        [Header("Decay")]
        [Tooltip("Extra exponential decay (1/s) applied to angularVel when no rider attached. Higher = faster return to rest after rider hops off.")]
        [Range(0f, 5f)] public float passiveDecayRate = 1.2f;
        [Range(0f, 5f)] public float riderDecayRate = 0f;

        [Header("Ceiling")]
        [Min(0f)] public float maxTipSpeed = 18f;

        [Header("Carrier (launch inheritance)")]
        [Tooltip("Overall scale on inherited platform velocity at launch. 0 = pure visual, no boost.")]
        [Range(0f, 2f)] public float velocityTransferScale = 0.2f;
        [Tooltip("Minimum inheritance fraction even at the base (t=0). Slime attached at base inherits this fraction of tip speed.")]
        [Range(0f, 1f)] public float carrierMinContribution = 0.3f;
        [Tooltip("Scales the height-dependent inheritance term (carrierMin + carrierDistanceScale * t).")]
        [Range(0f, 2f)] public float carrierDistanceScale = 1f;

        [Header("Re-entry Guards")]
        [Min(0f)] public float slamMinHorizontalSpeed = 3f;
        [Min(0f)] public float slamReattachLockout = 0.15f;

        [Header("Tip-Grab")]
        [Tooltip("dot(surfaceNormal, gravityUp) entry threshold for tip-grab. Slime entering Attached on a bamboo with at least this much upward-facing normal flips into grab mode. Grab is sticky after entry — only Launch (or losing Attached/Charging state) clears it.")]
        [Range(0f, 1f)] public float grabEnterDot = 0.9f;
        [Tooltip("Tarzan swing model — steady-state bend-tangent angle (degrees, same definition as grabMaxDeflectionDeg: 90° = quarter-arc bend, not 90° rigid rotation) when A or D is held. KEEP WELL BELOW grabMaxDeflectionDeg. Holding alone settles bamboo at this lean. Switching A↔D at the swing extremes redirects the steady-state target across rest, amplifying amplitude over multiple cycles (driven oscillation). Recommended ~10–25° for subtle lean + dramatic pump amplification.")]
        [FormerlySerializedAs("grabPumpReferenceBendDeg")]
        [FormerlySerializedAs("grabTargetBendDeg")]
        [Range(0f, 180f)] public float grabHoldBendDeg = 15f;

        [Tooltip("Spring stiffness (= elasticity) used by the bamboo spring ONLY while a slime is tip-grabbing. Higher = snappier return / faster swing period (the torque per lean auto-scales with stiffness, so steady-state lean stays at grabHoldBendDeg regardless). When no slime is grabbed, naturalFreqHz applies.")]
        [Range(0.1f, 5f)] public float grabSpringFreqHz = 1.2f;

        [Tooltip("Damping ratio used by the bamboo spring ONLY while a slime is tip-grabbing. Low (0.02–0.10) lets amplitude accumulate over multiple A↔D switches timed at the swing extremes (Tarzan pump). When no slime is grabbed, dampingRatio applies, so a slime that hops off doesn't leave the bamboo ringing.")]
        [Range(0f, 1f)] public float grabDampingRatio = 0.05f;

        [Tooltip("Hard cap on |currentAngle| (degrees, bend-tangent at tip — NOT rigid rotation angle) while a slime is tip-grabbing. The bamboo is a curving cantilever, not a rotating rod: this is the tangent angle at the tip end of the bent curve. Reference: 90° = quarter-arc (tip at ~(-0.64L, 0.64L), looks diagonally up-left); 180° = half-circle (tip at ~(-0.64L, 0), looks horizontally left); 270° = 3/4 arc (tip curls below base). For 'tip looks roughly horizontal', set this around 180°. Set MUCH larger than grabHoldBendDeg (4–6×) so a well-timed pump has visible amplitude headroom. Independent from maxDeflectionDeg which still applies for side attaches.")]
        [Range(5f, 360f)] public float grabMaxDeflectionDeg = 90f;
        [Tooltip("Multiplier on grabHoldBendDeg lean torque while the slime is Charging — pairs with chargePumpScale.")]
        [Range(0f, 2f)] public float chargingTiltScale = 1f;
        [Tooltip("Extra multiplier on VelocityTransferScale while a bamboo is the slime's grab target at launch. 1 = same boost as side attach; >1 = grab launches inherit more tip velocity than ordinary launches.")]
        [Min(0f)] public float grabModeCarrierScale = 1f;
    }
}
