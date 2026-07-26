using UnityEngine;

namespace SlimeJumping.Settings
{
    /// <summary>
    /// Gameplay tuning for BambooPlatformV3. Eleven knobs, nothing else — every other
    /// quantity the stalk uses (bend from an impact, sag under the rider's weight) is
    /// derived from the cantilever ODE and the rider's own mass/position, not from a
    /// multiplier.
    ///
    /// Contrast with BambooSettings (v1), which carries 26 knobs because seven layers of
    /// non-physical correction sit on top of the same ODE: impact-speed curves, contact
    /// height shaping applied on top of the moment arm that already encodes height, a
    /// second spring set that swaps in the frame the rider leaves, and two decay rates
    /// outside the damping ratio. v1 is unchanged and still used by the existing stalks;
    /// V3 is the model for new gameplay bamboo.
    ///
    /// Per-stalk fields (length, segment count, radius, colours, node geometry, toon
    /// lighting, tessellation, backgroundOnly) live on BambooPlatformV3 itself so stalks
    /// sharing this asset can still look different.
    /// </summary>
    [CreateAssetMenu(fileName = "BambooV3Settings", menuName = "SlimeJumping/Bamboo V3 Settings", order = 211)]
    public sealed class BambooV3Settings : ScriptableObject
    {
        [Header("Stalk material")]
        [Tooltip("Spring frequency of the stalk (Hz). This is what 'stiff' means for a bamboo — not a small bend, but a FAST return. 2 Hz = a 0.5 s swing period. Low values (v1 shipped 0.7 Hz = 1.43 s) read as rubber no matter how the bend is tuned.")]
        [Range(0.3f, 6f)] public float naturalFreqHz = 2f;

        [Tooltip("Damping ratio. Controls how many times the stalk swings before settling: ~0.3 gives two or three visible swings, 0.1 rings like jelly, 0.7 is nearly dead on the first return. This is the ONLY energy sink — there is no separate decay rate, so what you set here is what you see.")]
        [Range(0f, 1f)] public float dampingRatio = 0.3f;

        [Header("Limits")]
        [Tooltip("Hard cap on |bend angle| (degrees, tangent angle at the tip of the arc — 90 deg is a quarter-arc, not a 90 deg rigid rotation). At the cap the angle clamps and outward angular velocity is absorbed. Applies at all times, rider or no rider — v1's cap changed the instant the rider left, which teleported the stalk and deleted the recoil.")]
        [Range(5f, 180f)] public float maxDeflectionDeg = 60f;

        [Tooltip("Hard cap on tip linear speed (m/s), i.e. |angularVel| * bambooLength. Also the ceiling on the launch boost a slime can inherit from this stalk, since carrier inheritance is unscaled (omega x arm at 1.0).")]
        [Min(0f)] public float maxTipSpeed = 18f;

        [Header("Throw")]
        [Tooltip("Peak bend (degrees) a swing must reach before the stalk throws the slime off. The sequence is contact -> bend -> spring back -> released at maximum tip speed as it whips through toward the other side. A hard hit bends past this and launches with no input; a soft landing never gets there and the stalk stays an ordinary platform. The knob is an ANGLE because that is the thing the player can see on screen, which is what makes the outcome predictable. Raise it if soft landings are being thrown, lower it if hard hits refuse to launch — no other knob should need touching. 0 disables the throw entirely (pure springy platform).")]
        [Range(0f, 180f)] public float ejectBendDeg = 12f;

        [Header("Landing")]
        [Tooltip("Soft cap (m/s) on the relative impact speed the landing impulse sees, applied BEFORE the tip-bonus gain: v_eff = cap * ln(1 + v/cap). Slope is 1 at low speed, so gentle landings are untouched; a fast dive is compressed logarithmically (cap 8: 3->2.6, 10->6.5, 22->10.6, 40->14.4). Without this a fast arrival slams the stalk to its deflection cap and the violent whip-back shakes the slime off before it can even attach. 0 disables.")]
        [Range(0f, 30f)] public float impactSoftCap = 8f;

        [Tooltip("Landing bonus at the TIP of the stalk. The landing impulse is scaled by lerp(1, this, heightT^2) where heightT = contact height / length — a base landing stays purely physical (short arm, tip barely moves) and the bonus ramps in quadratically toward the tip, so hitting the sweet spot near the top is what earns the extra bend / throw / boost. At 2, a slow landing on the tip bends twice as far as physics alone. Hard hits still compress into the maxTipSpeed clamp, and matching the surface still injects zero (no free energy).")]
        [Range(0.5f, 4f)] public float landingImpulseGain = 2f;

        [Header("Launch")]
        [Tooltip("Multiplier on the swing velocity (omega x arm) a player launch inherits from this stalk — PlatformMotor.VelocityTransferScale. 1 = physical inheritance only. Above 1 the stalk hands the jump a bonus proportional to how hard it is swinging, so launching off a loaded stalk clears the whip-back instead of being caught by it. Ceiling is still maxTipSpeed x this.")]
        [Range(0f, 3f)] public float launchBoostScale = 1.5f;

        [Tooltip("Below this pre-bonus launch speed (m/s) the stalk transfers full launchBoostScale. " +
                 "Pre-bonus speed = slime.Velocity.magnitude when carrier.GetLaunchVelocity is called — " +
                 "already includes EnergyConversion power and hyperdash inheritance, but NOT the stalk's " +
                 "own carrier contribution (that's what's being scaled). Lets the stalk reward normal " +
                 "launches without stacking extra tip-speed on top of a high-fall EnergyConversion launch.")]
        [Min(0f)] public float boostFalloffStartSpeed = 12f;

        [Tooltip("Above this pre-bonus launch speed (m/s) the stalk transfers zero carrier velocity. " +
                 "Linear falloff between boostFalloffStartSpeed and boostFalloffEndSpeed. " +
                 "Set <= boostFalloffStartSpeed for hard cutoff. The slime's own (EnergyConversion + " +
                 "hyperdash) speed passes through unchanged — only the stalk's carrier inheritance is " +
                 "removed, so a high-fall landing still launches with its full converted energy.")]
        [Min(0f)] public float boostFalloffEndSpeed = 18f;

        [Tooltip("Seconds after the rider leaves this stalk during which it cannot re-attach: SurfaceProbe skips the stalk's colliders for the full window (timer-only, no separation early-out) while physics stays ON — the whip-back shoves the slime away instead of catching it, and never passes through it. Needed because the springing stalk can move faster than a weak jump and chase the slime down mid-flight. 0 disables.")]
        [Range(0f, 1f)] public float relandLockoutTime = 0.3f;
    }
}
