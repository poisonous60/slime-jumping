using UnityEngine;

namespace SlimeJumping.Settings
{
    [CreateAssetMenu(fileName = "SlimeVisualSettings", menuName = "SlimeJumping/Slime Visual Settings")]
    public class SlimeVisualSettings : ScriptableObject
    {
        [Header("Deformation General")]
        public float deformSmoothTime = 0.1f;
        public float offsetSmoothTime = 0.1f;
        public float deformAxisSlerpSpeed = 15f;
        public float deformScaleMin = 0.4f;
        public float deformScaleMax = 1.8f;

        [Header("Surface Squash (C-5)")]
        public float surfaceSquashIntensity = 0.12f;
        public float surfaceSquashAsymmetry = 0.3f;
        public float gravitySagDistance = 0.06f;

        [Header("Impact Squash (C-2)")]
        public float impactSquashIntensity = 0.25f;
        public float impactDuration = 0.3f;
        public float impactDecayRate = 8f;
        public float impactOscFreq = 2.5f;
        public float impactSpeedReference = 20f;
        public float impactMinThreshold = 0.05f;

        [Header("Velocity Stretch (C-4)")]
        public float velocityStretchIntensityX = 0.15f;
        public float velocityStretchIntensityY = 0.15f;
        public float velocityStretchSpeedRef = 15f;
        public float velocityStretchMaxX = 0.3f;
        public float velocityStretchMaxY = 0.3f;
        public float velocityLeanDistance = 0.05f;
        public float velocityLeanMax = 0.08f;
        public float velocityAxisThreshold = 1f;
        [Min(0.1f)] public float velocityStretchCurvePower = 2f;
        [Min(0f)] public float velocityStretchCrossDamping = 0.3f;

        [Header("Velocity Stretch 3D (shader vertex displacement)")]
        [Tooltip("Below this speed (m/s), no 3D shader stretch. Used to normalize the X axis of the curve.")]
        public float velocityStretch3DSpeedMin = 0f;
        [Tooltip("At this speed (m/s), the curve samples x=1. Speeds beyond clamp to the curve's rightmost value.")]
        public float velocityStretch3DSpeedMax = 15f;
        [Tooltip("X axis: normalized speed (0 at speedMin, 1 at speedMax). Y axis: stretch amount s (typical 0..0.3). s=0 no stretch, s=0.3 = 30% elongation with √(1/(1+s)) perpendicular compression. Graph editor lets you pick any shape.")]
        public AnimationCurve velocityStretch3DCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0.1f),
            new Keyframe(1f, 0.25f, 0.4f, 0f));

        [Header("Charging Curve")]
        public float chargingSpeedMin = 3f;
        public float chargingSpeedMax = 15f;
        [Min(0.1f)] public float chargingCurvePower = 1f;

        [Header("Charging Effects")]
        [Tooltip("Squash amount (body.localScale: +X / -Y) as a function of charging progress. X axis: chargingCurve during normal charging (0..1), plus (activeChargeRadius/chargeRadius - 1) during energy expansion so X goes beyond 1. Y axis: squash magnitude.")]
        public AnimationCurve chargingSquashCurve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(1f, 0.2f),
            new Keyframe(1.5f, 0.35f));
        public float chargingDipMax = 0.2f;
        public float chargingPushMax = 0.1f;
        public float bodyChargingAimPull = 0.15f;
        public float chargingDipLateralShift = 0.2f;
        public float chargingTremorFrequency = 25f;
        public float chargingTremorMax = 0.03f;
        public float chargingTremorThreshold = 9f;
        [Tooltip("Extra scale-tremor amplitude applied while aim is held at charge-radius edge (chargeFullAimTime grace window). Adds on top of chargingTremorMax.")]
        public float fullAimTremorAmplitude = 0.05f;
        public float fullAimTremorFrequency = 45f;

        [Header("Breathing (C-6)")]
        public float breathAmplitude = 0.02f;
        public float breathFrequency = 0.4f;

        [Header("Face Tracking")]
        public float faceOffsetScale = 0.03f;
        public float faceOffsetMax = 0.2f;
        public float faceSmoothTime = 0.1f;
        public float faceChargingScale = 0.05f;
        public float faceLingerThreshold = 0.1f;
        public float faceLingerDecay = 1.5f;

        [Header("Face Idle Drift")]
        public float faceDriftAmplitude = 0.01f;
        public float faceDriftFreqX = 0.7f;
        public float faceDriftFreqY = 0.9f;
        public float faceDriftSharpness = 4f;

        [Header("Blink")]
        public float blinkDuration = 0.1f;
        public float blinkIntervalMin = 3f;
        public float blinkIntervalMax = 7f;

        [Header("Visual Clearance")]
        public float visualClearanceSkin = 0.02f;

        [Header("Ground Compensation")]
        [Range(0f, 1f)]
        [Tooltip("Pushes body toward the attached surface to counteract body.localScale.y compression (charging, surface squash, impact, breath). Without this, symmetric Y scale around body center lifts the surface-touching edge off the ground. 0.5 = geometrically correct for a unit-radius body (surface edge stays exactly planted). 0 = disabled, surface edge lifts with scale. 1 = over-compensation (slime visually sinks into surface). Applied AFTER visualClearanceSkin clamp.")]
        public float groundCompensationStrength = 0.5f;

        [Header("Charge Ring (debug)")]
        [Tooltip("Debug-only floor-ellipse charge ring. Off by default — turn on to visualize charging progress/grace window during development.")]
        public bool chargeRingEnabled = false;
        [Tooltip("Multiplier on ActiveChargeRadius for the visual ring radius. 1.0 = same as physics judgment radius.")]
        public float chargeRingRadiusScale = 1.0f;
        [Tooltip("Ring line thickness in world units. Target ~slimeRadius/15 to /20 so it reads as a line, not a band.")]
        public float chargeRingThickness = 0.025f;
        [Tooltip("Base tint — keep close to map tone to minimize gaze pull. Accent color (fullAim) carries the grace-window signal.")]
        public Color chargeRingBaseColor = new Color(0.78f, 0.82f, 0.9f, 0.4f);
        [Tooltip("Warm-tone tick color shown during the chargeFullAimTime grace window.")]
        public Color chargeRingFullAimColor = new Color(1.0f, 0.88f, 0.7f, 0.85f);
        [Tooltip("Fallback foot offset (from slime center along -SurfaceNormal). Overridden at runtime by slimeSettings.slimeRadius when wired.")]
        public float chargeRingFootOffsetFallback = 0.4f;
        [Tooltip("Fade-in time — kept short so the ring appears punchy.")]
        public float chargeRingFadeInTime = 0.05f;
        [Tooltip("Fade-out time — kept short so consecutive charges don't leave a lingering trail.")]
        public float chargeRingFadeOutTime = 0.05f;
        [Tooltip("Thickness multiplier peak at grace-entry pulse. Kept mild (~0.2 = 1.2x) so color shift carries most of the signal.")]
        public float chargeRingFullAimPulseAmount = 0.2f;
        public float chargeRingFullAimPulseDuration = 0.08f;
        [Min(2)] public int chargeRingPosterizeSteps = 2;
        [Tooltip("Slerp rate (rad/s) for the ring's rotation to follow surface-normal changes while sliding. Prevents snap on angle breaks.")]
        public float chargeRingNormalFollowSpeed = 20f;
        [Tooltip("Alpha multiplier for the occluded-silhouette pass (ring drawn behind terrain).")]
        [Range(0f, 1f)] public float chargeRingOccludedAlpha = 0.35f;
        [Tooltip("Euler angles (degrees) applied as a LOCAL tilt before the surface-normal spin. X leans the ring front/back into the surface (foreshortening). Y skews left/right. Z rotates in the ring's face plane (mostly invisible since the ring is symmetric). Default (55, 0, 0) produces a strong floor-ellipse under perspective camera.")]
        public Vector3 chargeRingSurfaceTiltEuler = new Vector3(55f, 0f, 0f);
        [Tooltip("Soft-edge width for the ring annulus (UV units). Higher = softer, reads as energy imprint rather than a drawn line.")]
        [Range(0f, 0.05f)] public float chargeRingEdgeSoftness = 0.015f;

        private void OnValidate()
        {
            if (chargingSpeedMax <= chargingSpeedMin)
                Debug.LogWarning($"[SlimeVisualSettings] chargingSpeedMax ({chargingSpeedMax}) must be > chargingSpeedMin ({chargingSpeedMin})");
            if (chargingTremorThreshold >= chargingSpeedMax)
                Debug.LogWarning($"[SlimeVisualSettings] chargingTremorThreshold ({chargingTremorThreshold}) must be < chargingSpeedMax ({chargingSpeedMax})");
        }
    }
}
