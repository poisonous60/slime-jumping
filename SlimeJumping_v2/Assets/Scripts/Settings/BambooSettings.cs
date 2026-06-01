using UnityEngine;
using UnityEngine.Serialization;
using SlimeJumping.Platforms;

namespace SlimeJumping.Settings
{
    public enum BambooInteractionMode
    {
        TipGrab = 0,
        SideReboundV2 = 1
    }

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

        [Header("Interaction Mode")]
        [Tooltip("TipGrab keeps the original top hanging behavior. SideReboundV2 disables top grab and turns side impacts into delayed rebound jumps.")]
        public BambooInteractionMode interactionMode = BambooInteractionMode.TipGrab;

        [Header("Side-Rebound V2 - Bend")]
        [Tooltip("How V2 bend and launch values are resolved. Additive uses the constant fields; SpeedCurve evaluates the curves by impact speed or charge power.")]
        public BambooV2ResponseMode v2ResponseMode = BambooV2ResponseMode.Additive;
        [Tooltip("Multiplier on riderPumpGain while SideReboundV2 is active. Higher = side hits bend farther.")]
        [Min(0f)] public float v2BendPumpGain = 1.4f;
        [Tooltip("Curve version of v2BendPumpGain, evaluated by side-impact speed.")]
        public AnimationCurve v2BendPumpGainByImpactSpeed = AnimationCurve.Linear(0f, 0.6f, 18f, 2.2f);
        [Tooltip("Spring frequency while V2 side rebound is loaded. Lower values make the bend/release take longer.")]
        [Range(0.1f, 5f)] public float v2SpringFreqHz = 0.75f;
        [Tooltip("Damping while V2 side rebound is loaded. Lower values preserve more rebound energy.")]
        [Range(0f, 1f)] public float v2DampingRatio = 0.12f;
        [Tooltip("Maximum side bend angle while V2 side rebound is loaded.")]
        [Range(5f, 360f)] public float v2MaxDeflectionDeg = 120f;
        [Tooltip("Minimum normalized contact height (0 base, 1 tip) that can arm V2 side rebound. Lower contacts still collide, but do not trigger the big bend/rebound behavior.")]
        [Range(0f, 1f)] public float v2ReboundMinContactHeight01 = 0.25f;
        [Tooltip("Minimum bend pump scale at the base for V2. 0 means base hits add almost no bend.")]
        [Range(0f, 1f)] public float v2BendMinHeightScale = 0f;
        [Tooltip("Contact-height exponent for V2 bend pump. Higher values make low contacts affect the stalk much less.")]
        [Min(0.1f)] public float v2BendHeightPower = 1.75f;
        [Header("Side-Rebound V2 - Launch")]
        [Tooltip("Extra speed added to a charged launch from a V2 bamboo in Additive mode.")]
        [Min(0f)] public float v2ChargedLaunchBonus = 3f;
        [Tooltip("Extra speed added to a charged launch in SpeedCurve mode, evaluated by the charge launch power.")]
        public AnimationCurve v2ChargedLaunchBonusByChargePower = AnimationCurve.Linear(0f, 0f, 18f, 7f);
        [Tooltip("Extra multiplier on inherited bamboo velocity for V2 rebound launches.")]
        [Min(0f)] public float v2CarrierScale = 1f;
        [Tooltip("Launch-time recoil kick for Bamboo V2. Converts the slime's tangential launch speed at the contact height into bamboo angular velocity. 1 = bamboo surface tries to move at the same tangential speed as the slime launch.")]
        [Min(0f)] public float v2LaunchReleaseVelocityScale = 0.85f;
        [Tooltip("If true, launch-release recoil is scaled by the slime's contact height. Disable for old behavior where low and high contacts kick the bamboo equally by tangential launch speed.")]
        public bool v2LaunchReleaseUseHeightScale = true;
        [Tooltip("Minimum launch-release recoil scale at the base for V2. 0 means base launches add almost no release kick.")]
        [Range(0f, 1f)] public float v2LaunchReleaseMinHeightScale = 0f;
        [Tooltip("Contact-height exponent for V2 launch-release recoil. Higher values make low launches shake the stalk much less.")]
        [Min(0.1f)] public float v2LaunchReleaseHeightPower = 1.75f;
        [Tooltip("Maximum bamboo tip speed after the V2 launch-release kick.")]
        [Min(0f)] public float v2LaunchReleaseMaxTipSpeed = 24f;
        [Tooltip("A contact is considered a V2 side hit when |dot(surfaceNormal, gravityUp)| is at or below this value.")]
        [Range(0f, 1f)] public float v2SideNormalMaxUpDot = 0.45f;

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

        [Tooltip("Soft cap on |currentAngle| (degrees, bend-tangent at tip — NOT rigid rotation angle) while a slime is tip-grabbing. The bamboo is a curving cantilever, not a rotating rod: this is the tangent angle at the tip end of the bent curve. Reference: 90° = quarter-arc (tip at ~(-0.64L, 0.64L), looks diagonally up-left); 180° = half-circle (tip at ~(-0.64L, 0), looks horizontally left); 270° = 3/4 arc (tip curls below base). For 'tip looks roughly horizontal', set this around 180°. Set MUCH larger than grabHoldBendDeg (4–6×) so a well-timed pump has visible amplitude headroom. Independent from maxDeflectionDeg which still applies for side attaches. NOTE: in grab mode this is a soft cap enforced by grabCapPenaltyGain stiffening, not the hard ω=0 clamp used for side attaches.")]
        [Range(5f, 360f)] public float grabMaxDeflectionDeg = 90f;
        [Tooltip("Multiplier on grabHoldBendDeg lean torque while the slime is Charging. Default 0 = lean is disabled during Charging so the player's aim+release isn't fighting bamboo tilt input on the same axis. Paired with grabPumpChargingScale for the anti-damping boost channel.")]
        [Range(0f, 2f)] public float chargingTiltScale = 0f;

        [Tooltip("Anti-damping boost gain (1/s). Fires when held input direction matches current tip motion direction (swing pump). Added on top of the lean torque so the bamboo's amplitude grows over multiple matched swings instead of settling at the lean equilibrium. Effective net damping = 2ζω - grabPumpBoostGain when matched; values above the damping coefficient make amplitude grow exponentially until the soft cap.")]
        [Range(0f, 30f)] public float grabPumpBoostGain = 4f;
        [Tooltip("Multiplier on grabPumpBoostGain while the slime is Charging. Default 0 = pump is disabled during Charging so the player's aim+release isn't fighting bamboo tilt input on the same axis.")]
        [Range(0f, 2f)] public float grabPumpChargingScale = 0f;
        [Tooltip("Soft-cap stiffening gain. Adds a quartic restoring torque -sign(θ)·gain·(|θ|/grabMaxDeflectionDeg)^4·stiffness·grabMaxDeflectionDeg(rad) so the bamboo asymptotes to grabMaxDeflectionDeg without the ω=0 hard clamp. Higher = stiffer wall near the cap. Grab mode only — side-attach saturation still uses clampRestitution.")]
        [Range(0f, 30f)] public float grabCapPenaltyGain = 5f;

        [Tooltip("Extra multiplier on VelocityTransferScale while a bamboo is the slime's grab target at launch. 1 = same boost as side attach; >1 = grab launches inherit more tip velocity than ordinary launches.")]
        [Min(0f)] public float grabModeCarrierScale = 1f;
        [Tooltip("Multiplier on the charge-power jump component when launching from tip-grab. The base launch direction is the tip-up axis (jump off the top of the bent stalk); this scales how strong that jump is. The bamboo's swing velocity is added separately as momentum via grabModeCarrierScale, so this and grabModeCarrierScale tune the up-jump vs the sideways swing-throw independently.")]
        [Min(0f)] public float grabLaunchPowerScale = 1f;
        [Tooltip("Multiplier on the charge auto-drift speed while tip-grabbed. While grabbed, A/D is routed to bamboo tilt (not aim), so the charge fills only via the auto-outward drift — this scales how fast that fills relative to a flat-ground charge. >1 = the charge reaches full faster on a bamboo than on flat ground, so the player doesn't have to hold charge as long mid-swing.")]
        [Min(0f)] public float grabChargeFillScale = 1f;
        [Tooltip("Override for SlimeSettings.chargeFullAimTime while tip-grabbed — the grace/hold window after the charge reaches the edge before it auto-launches. -1 = use the slime's default (same as flat ground). >=0 = use this value instead: 0 launches the instant the charge fills, larger gives more hold time to time the release mid-swing. Surface-tag-style per-bamboo override.")]
        public float grabChargeFullAimTime = -1f;
    }
}
