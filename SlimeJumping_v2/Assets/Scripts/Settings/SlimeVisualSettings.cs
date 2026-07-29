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

        [Header("Pinch Squash (squeezed between two platforms — gap-driven, no intensity)")]
        // Scale along the pinch axis = thickness ratio (1 − PinchCompression01), perpendicular bulges
        // to preserve area. No intensity knob — the squash IS the measured compression.
        [Tooltip("Floor on the thickness ratio so the body never collapses to a degenerate sliver. 0.15 = at most flattened to 15% of diameter.")]
        [Range(0.02f, 1f)] public float pinchMinThickness = 0.15f;
        [Tooltip("SmoothDamp time (s) for the squash, for soft press/release. Responsiveness only, not magnitude.")]
        [Min(0f)] public float pinchSquashSmoothTime = 0.05f;
        [Tooltip("Vertex displacement scale for the per-contact pinch dent (SlimeDeform.hlsl 3.5). Decoupled from the material's global _VertexDisplaceScale (0 here) so the pinch shows without re-enabling the other mesh deforms. ~0.3 ≈ 30% local flatten at full compression.")]
        [Min(0f)] public float pinchDisplaceScale = 0.3f;

        [Header("Impact Squash (C-2)")]
        public float impactSquashIntensity = 0.25f;
        public float impactDuration = 0.3f;
        public float impactDecayRate = 8f;
        public float impactOscFreq = 2.5f;
        public float impactSpeedReference = 20f;
        public float impactMinThreshold = 0.05f;

        [Header("Coyote Squash (energy-conversion window)")]
        [Tooltip("Compression phase. X axis: elapsed fraction of the coyote window (0 = landing instant, 1 = coyote expiry / peak). Y axis: squash magnitude multiplier (0 = rest, 1 = peak). Actual peak intensity is further scaled by coyoteSquashSpeedCurve. Default: linear 0→1 so the slime is most compressed right when the coyote window ends.")]
        public AnimationCurve coyoteSquashCurve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(1f, 1f));
        [Tooltip("Recovery phase duration (seconds) after the coyote window ends. Squash eases from peak back to rest over this time, independent of energyConversionCoyoteTime.")]
        [Min(0f)] public float coyoteSquashRecoveryTime = 0.2f;
        [Tooltip("Recovery phase. X axis: elapsed fraction of coyoteSquashRecoveryTime (0 = coyote just ended, 1 = recovery complete). Y axis: squash magnitude multiplier (1 = peak still held, 0 = rest). Default: linear 1→0.")]
        public AnimationCurve coyoteSquashRecoveryCurve = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(1f, 0f));
        [Tooltip("X axis: impact normal speed normalized by coyoteSquashSpeedRef (0 = no speed, 1 = reference speed). Y axis: peak squash intensity. Low falls → barely squash; high falls → full squash.")]
        public AnimationCurve coyoteSquashSpeedCurve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.4f, 0.06f),
            new Keyframe(1f, 0.18f));
        [Tooltip("Impact normal speed (m/s) that maps to x=1 on coyoteSquashSpeedCurve.")]
        public float coyoteSquashSpeedRef = 20f;

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

        [Header("Corner Stress (Phase 1)")]
        [Tooltip("이 ratio 미만이면 stretch 0.")]
        [Range(0f, 1f)] public float cornerStressMinRatio = 0.5f;
        [Tooltip("ratio∈[min,1] → curve 입력. shader 의 cornerStressAmount 로 전달.")]
        public AnimationCurve cornerStressCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("Tangent asymmetric stretch 최대 강도 (vertex displacement, slimeRadius 단위).")]
        [Min(0f)] public float cornerStressTangentMax = 0.35f;
        [Tooltip("Outward bulge 최대 강도 (slimeRadius 단위).")]
        [Min(0f)] public float cornerStressOutwardMax = 0.20f;

        [Header("Pong (Phase 3)")]
        [Tooltip("퐁 오버레이 지속시간 (s).")]
        [Min(0f)] public float pongDuration = 0.18f;
        [Tooltip("감쇠율 (1/s). 클수록 빠르게 사그라듬.")]
        [Min(0f)] public float pongDecay = 4f;
        [Tooltip("진동 cycle 수 / pongDuration. 1.2 ≈ 1.2 cycle.")]
        [Min(0f)] public float pongOscFreq = 1.2f;
        [Tooltip("최대 stretch (outward 방향).")]
        [Min(0f)] public float pongStretchMax = 0.4f;
        [Tooltip("이 overshoot 값에서 pongIntensity = 1. overshoot = ratio - 1.")]
        [Min(0.001f)] public float pongFullOvershoot = 1.5f;

        private void OnValidate()
        {
            if (chargingSpeedMax <= chargingSpeedMin)
                Debug.LogWarning($"[SlimeVisualSettings] chargingSpeedMax ({chargingSpeedMax}) must be > chargingSpeedMin ({chargingSpeedMin})");
            if (chargingTremorThreshold >= chargingSpeedMax)
                Debug.LogWarning($"[SlimeVisualSettings] chargingTremorThreshold ({chargingTremorThreshold}) must be < chargingSpeedMax ({chargingSpeedMax})");
        }
    }
}
