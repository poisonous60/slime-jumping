using UnityEngine;

namespace SlimeJumping.Physics
{
    public enum LaunchInertiaBlendMode
    {
        // Pure vector add: velocity = launchVec + inheritedVec. Legacy / current behavior.
        Sum,
        // Magnitude lerps between max(|L|,|I|) and |L+I|; direction follows (L+I).
        // launchInertiaLerpFactor 0 = max, 1 = sum.
        LerpMaxToSum,
        // Magnitude is the p-norm of the two magnitudes; direction follows (L+I).
        // launchInertiaPNorm 1 = sum, 2 = energy-style, large p -> max.
        PNorm
    }

    [CreateAssetMenu(menuName = "SlimeJumping/Surface Material")]
    public class SurfaceMaterial : ScriptableObject
    {
        [Header("Carrier — moving platform friction")]
        [Tooltip("0 = ice (platform moves, slime stays), 1 = full follow")]
        public AngleScaledValue carrierFriction = AngleScaledValue.Constant(1f);

        [Header("Crawl")]
        [Tooltip("Multiplier on the decay rate of residual landing-tangent momentum on this surface (lower = slides longer after a hard landing).")]
        public AngleScaledValue crawlOverspeedDecelerationMultiplier = AngleScaledValue.Constant(1f);

        [Header("Slide")]
        [Tooltip("0 = ice (no resistance), 2+ = mud (high resistance)")]
        public AngleScaledValue slideResistanceMultiplier = AngleScaledValue.Constant(1f);
        public AngleScaledValue slideGravityMultiplier = AngleScaledValue.Constant(1f);
        [Tooltip("Time (seconds) for slide to ramp from 0 to full after landing. 0 = instant.")]
        public AngleScaledValue slideRampUpTime = AngleScaledValue.Constant(0f);
        [Tooltip("Inertial-slide only. Linear drag coefficient (1/s) applied as slideVelocity *= (1 - k*dt). 0 = none. 0.5–1.5 ≈ mild ice drag (climb shorter than drop).")]
        public AngleScaledValue slideLinearDrag = AngleScaledValue.Constant(0f);
        [Tooltip("Inertial-slide only. Multiplier on tangent gravity when it accelerates motion (slideAccel sign matches slideVelocity). 1 = neutral, >1 = sharper descent speed-up.")]
        public AngleScaledValue slideDownhillGain = AngleScaledValue.Constant(1f);
        [Tooltip("Inertial-slide only. Multiplier on tangent gravity when it opposes motion (slideAccel sign opposes slideVelocity). 1 = neutral, >1 = sharper ascent slow-down.")]
        public AngleScaledValue slideUphillBrake = AngleScaledValue.Constant(1f);

        [Header("Landing")]
        [Tooltip("0 = instant stop (default), 1 = full tangent velocity preserved")]
        public AngleScaledValue landingVelocityRetention = AngleScaledValue.Constant(0f);
        [Tooltip("Charged-launch landings only. Scales the EXCESS of post-retention landing tangent speed over the slime's pre-launch slide speed (= |slideVelocity + crawlVelocity| at the moment Launch fired).\n" +
                 "1 (default) = current behavior, full excess preserved → launch+land cycles can accumulate speed.\n" +
                 "0.5 = half of the cycle excess is preserved.\n" +
                 "0 = caps landed tangent at pre-launch slide speed → launches don't accelerate slide speed beyond what it was pre-launch.\n" +
                 ">1 = intentional accelerator surface.\n" +
                 "Does NOT affect Hop landings, external-arrival impacts, or natural slide. Per-angle curve allows different gain on shallow vs steep landings.")]
        public AngleScaledValue landingExcessGain = AngleScaledValue.Constant(1f);
        [Tooltip("Toggle the landing tangent speed remap curve below. OFF (default) = curve ignored, current behavior. ON = curve evaluated. Lets you author a curve and toggle without losing the keys.")]
        public bool enableLandingTangentSpeedRemap = false;
        [Tooltip("When enableLandingTangentSpeedRemap is ON: non-linear remap of the final landed tangent speed (after retention + impactRedirect + landingExcessGain). X (m/s) = |landed|, Y (m/s) = remapped magnitude. Use a saturating curve on ice/halfpipes to compress high-speed landings (e.g. (0,0)→(10,10)→(25,14)) so vertical hops don't translate to runaway slide speed.")]
        public AnimationCurve landingTangentSpeedRemapCurve = new AnimationCurve();
        public AngleScaledValue stickDurationMultiplier = AngleScaledValue.Constant(1f);
        [Tooltip("0 = absorb normal impact (default), 0.5 = redirect 50% of normal speed to tangent")]
        public AngleScaledValue impactRedirectRatio = AngleScaledValue.Constant(0f);

        [Header("Wall Attach")]
        [Tooltip("Speed multiplier when attaching to surface. 1 = no dampen, 0.5 = half speed")]
        public AngleScaledValue wallAttachSpeedDampen = AngleScaledValue.Constant(1f);

        [Header("Slide Physics")]
        [Tooltip("ON = inertial slide (gravity-along-tangent integrated as acceleration; slideResistance is friction in m/s²; landed tangent velocity flows into the inertial channel). OFF (default, untagged surfaces) = legacy steady-state slide where slideSpeed is recomputed each frame from current tangent gravity. Use ON for ice / halfpipes where kinetic energy must convert back to height.")]
        public bool useInertialSlide = false;

        [Header("Launch Inertia Blend")]
        [Tooltip("How the inherited tangent vector (from preLaunchCrawl × tangentInheritanceFactor, after hyperDashDeduction) combines with the aim-launch vector.\n" +
                 "Sum (default) = legacy pure vector add (velocity += inherited).\n" +
                 "LerpMaxToSum = magnitude is Lerp(max(|L|,|I|), |L+I|, launchInertiaLerpFactor); direction follows (L+I).\n" +
                 "PNorm = magnitude is (|L|^p + |I|^p)^(1/p) with p = launchInertiaPNorm; direction follows (L+I).")]
        public LaunchInertiaBlendMode launchInertiaBlendMode = LaunchInertiaBlendMode.Sum;
        [Range(0f, 1f)]
        [Tooltip("LerpMaxToSum only. 0 = magnitude clamps at max(|launch|,|inherited|) (still slightly biased in direction). 1 = full vector sum (== legacy Sum mode).")]
        public float launchInertiaLerpFactor = 0.5f;
        [Range(1f, 8f)]
        [Tooltip("PNorm only. 1 = sum, 2 = pythagorean / energy-style, large values approach max. Typical tuning range 1.5–3.")]
        public float launchInertiaPNorm = 2f;

        [Header("Audio")]
        [Tooltip("Hard landing clip override. Null → SlimeLocomotionAudio's default landHardClip plays.")]
        public AudioClip landHardClip;
        [Tooltip("Soft landing clip override. Null → SlimeLocomotionAudio's default landSoftClip plays.")]
        public AudioClip landSoftClip;
        [Tooltip("Charged Launch clip override. Null → SlimeLocomotionAudio's default launchClip plays.")]
        public AudioClip launchClip;
        [Tooltip("Hop clip override. Null → SlimeLocomotionAudio's default hopClip plays.")]
        public AudioClip hopClip;
        [Tooltip("Volume for this surface's landHardClip override. 0 = inherit SlimeLocomotionAudio.landHardVolume. Only used when landHardClip is also set.")]
        [Range(0f, 1f)] public float landHardVolume = 0f;
        [Tooltip("Volume for this surface's landSoftClip override. 0 = inherit SlimeLocomotionAudio.landSoftVolume. Only used when landSoftClip is also set.")]
        [Range(0f, 1f)] public float landSoftVolume = 0f;
        [Tooltip("Volume for this surface's launchClip override. 0 = inherit SlimeLocomotionAudio.launchVolume. Only used when launchClip is also set.")]
        [Range(0f, 1f)] public float launchVolume = 0f;
        [Tooltip("Volume for this surface's hopClip override. 0 = inherit SlimeLocomotionAudio.hopVolume. Only used when hopClip is also set.")]
        [Range(0f, 1f)] public float hopVolume = 0f;

        [Header("Ceiling")]
        [Tooltip("Override the global ceiling thresholds with the values below. ON lets sticky surfaces extend the attachable arc past 107° (e.g. 180° = slime climbs full overhangs) or slippery surfaces tighten it.")]
        public bool overrideCeilingAngle = false;
        [Range(90f, 180f)]
        [Tooltip("Detach / probe-filter angle. surfaceUp below cos(this) → contact treated as ceiling.")]
        public float ceilingAngle = 107f;
        [Range(90f, 180f)]
        [Tooltip("Re-attach angle (Airborne landing). Hysteresis: must be ≤ ceilingAngle.")]
        public float ceilingReattachAngle = 101f;

        [Header("Energy Conversion Coyote")]
        [Tooltip("Override the SlimeSettings energyConversionCoyoteTime with the value below. OFF = use global SlimeSettings (default). Surfaces like SpringyBranch can extend the post-landing window for late-Space energy conversion.")]
        public bool overrideEnergyConversionCoyoteTime = false;
        [Min(0f)]
        [Tooltip("Seconds after landing during which a Space press still grants the same expanded charge radius a Space-held landing would have. 0 = disabled on this surface.")]
        public float energyConversionCoyoteTime = 0.12f;

        public SurfaceModifiers ToModifiers() => new SurfaceModifiers
        {
            crawlOverspeedDecel = crawlOverspeedDecelerationMultiplier,
            slideResistance = slideResistanceMultiplier,
            slideGravity = slideGravityMultiplier,
            slideRampUp = slideRampUpTime,
            slideLinearDrag = slideLinearDrag,
            slideDownhillGain = slideDownhillGain,
            slideUphillBrake = slideUphillBrake,
            carrierFriction = carrierFriction,
            landingVelocityRetention = landingVelocityRetention,
            landingExcessGain = landingExcessGain,
            enableLandingTangentSpeedRemap = enableLandingTangentSpeedRemap,
            landingTangentSpeedRemapCurve = landingTangentSpeedRemapCurve,
            stickDuration = stickDurationMultiplier,
            impactRedirect = impactRedirectRatio,
            wallAttachDampen = wallAttachSpeedDampen,
            useInertialSlide = useInertialSlide,
            launchInertiaBlendMode = launchInertiaBlendMode,
            launchInertiaLerpFactor = launchInertiaLerpFactor,
            launchInertiaPNorm = launchInertiaPNorm,
            overrideCeilingAngle = overrideCeilingAngle,
            ceilingThresholdOverride = Mathf.Cos(ceilingAngle * Mathf.Deg2Rad),
            ceilingReattachThresholdOverride = Mathf.Cos(ceilingReattachAngle * Mathf.Deg2Rad),
            overrideEnergyConversionCoyoteTime = overrideEnergyConversionCoyoteTime,
            energyConversionCoyoteTimeOverride = energyConversionCoyoteTime
        };
    }
}
