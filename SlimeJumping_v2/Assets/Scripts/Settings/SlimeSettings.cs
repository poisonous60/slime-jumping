using UnityEngine;
using SlimeJumping.Physics;

namespace SlimeJumping.Settings
{
    public enum HyperDashInertiaSource { ChargeStart, LaunchMoment }

    // Selects how the friction-detach predicate combines tangent speed and ω.
    public enum FrictionDetachFormula
    {
        // required = |v_tangent| · ω (= v²/R). Detach scales with v².
        VelocityTimesOmega = 0,
        // required = v_threshold · ω. Speed-independent, purely curvature-driven.
        ConstantTimesOmega = 1,
        // VelocityTimesOmega gated by |Δθ| > cornerMinAngleDeg — ignores barely-turning frames.
        VelocityTimesOmegaWithMinAngle = 2,
        // ω > cornerOmegaThreshold. Pure angular-rate gate; v drops out entirely.
        AngularRateThreshold = 3,
    }

    [CreateAssetMenu(fileName = "SlimeSettings", menuName = "SlimeJumping/Slime Physical Settings")]
    public class SlimeSettings : ScriptableObject
    {
        [Header("Surface Movement")]
        [Tooltip("Decay rate of residual landing-tangent momentum on the crawl channel (slime has no self-powered surface movement; slide gravity + the charge slingshot are the only movers).")]
        public float crawlOverspeedDeceleration = 15f;
        public float slideGravityScale = 1f;
        public float slideThreshold = 1f;
        public float slideResistance = 2f;

        [Header("Hyper Dash")]
        [Tooltip("ChargeStart: capture tangent speed at the moment charging begins (legacy). LaunchMoment: read tangent speed at the launch frame so any deceleration during the charge actually reduces the inherited momentum.")]
        public HyperDashInertiaSource hyperDashInertiaSource = HyperDashInertiaSource.ChargeStart;
        [Range(0f, 1f)] public float tangentInheritanceFactor = 0.7f;
        [Tooltip("Crawl speed below this threshold is ignored during tangent inheritance. 0 = inherit full crawl speed.")]
        [Min(0f)] public float hyperDashDeduction = 5f;
        [Tooltip("Energy conversion ratio below which hyper dash is fully retained.")]
        public float hyperDashConversionDampingMin = 1.2f;
        [Tooltip("Energy conversion ratio at which hyper dash is fully dampened.")]
        public float hyperDashConversionDampingMax = 2f;

        [Header("Charge Circle")]
        [Min(0.01f)] public float chargeRadius = 1.5f;
        public float autoDriftSpeed = 1.5f;
        public float inputAcceleration = 3.0f;
        [Min(0.01f)] public float baseLaunchPower = 15f;
        public float minLaunchPower = 3f;
        public float chargeCurveExponent = 1f;
        [Tooltip("Grace period at full charge to adjust aim direction without affecting power.")]
        [Min(0f)] public float chargeFullAimTime = 0.1f;
        public float maxAimAngularSpeed = 180f;
        public float chargeNormalFollowSpeed = 5f;
        public float chargingCoyoteTime = 0.2f;
        public float coyoteGraceTime = 0.1f;
        public int chargingSurfaceProbeGraceFrames = 3;
        [Tooltip("Jump press-to-land buffer (seconds). Pressing Jump this long before landing still triggers charging.")]
        public float chargeInputBuffer = 0.1f;
        [Tooltip("If true, merely holding Jump in the air also triggers charging on landing (legacy behavior). If false, only a press within chargeInputBuffer before landing triggers charging — matches hop/ground-dash.")]
        public bool allowChargeAirHold = false;

        [Header("Attach Debounce")]
        public float attachToggleWindow = 0.1f;
        public int attachToggleLockoutThreshold = 10;
        public float attachLockoutDuration = 0.1f;

        [Header("Energy Conversion")]
        [Min(0f)] public float initialConversionRate = 2.25f;
        [Min(0f)] public float subsequentConversionRate = 0.9f;
        public float notJumpConversionBonus = 1.3f;
        public float aimDriftRadiusScale = 0.7f;
        [Tooltip("Grace window (seconds) after a no-Space landing during which pressing Space still grants energy conversion, as if Space had been held on the landing frame. 0 disables.")]
        [Min(0f)] public float energyConversionCoyoteTime = 0.12f;

        [Header("Stick")]
        public float minStickDuration = 0.3f;
        public float maxStickDuration = 0.6f;
        public float stickSpeedScale = 0.1f;
        public float stickDecelerationTime = 0.05f;
        public float stickSlopeThreshold = 0.7f; // surfaceUp above this = floor-like, no stick
        public float stickSlideTransitionRampTime = 0.2f;

        [Header("Charge Entry Grace")]
        public float chargeEntryStickBonus = 0.2f;
        public float chargingSlideGravityScale = 0.5f;

        [Header("Ceiling")]
        [Range(90f, 180f)]
        public float ceilingAngle = 107f;
        [Range(90f, 180f)]
        public float ceilingReattachAngle = 101f;
        public float ceilingThreshold => Mathf.Cos(ceilingAngle * Mathf.Deg2Rad);
        public float ceilingReattachThreshold => Mathf.Cos(ceilingReattachAngle * Mathf.Deg2Rad);

        [Header("Air Control")]
        public float airMaxHorizontalSpeed = 5f;
        public float airAcceleration = 20f;
        public float airDeceleration = 15f;
        [Tooltip("W-hold gravity scale while rising (velocity along gravityUp > 0).")]
        public float floatGravityScaleRising = 1f;
        [Tooltip("W-hold gravity scale while falling.")]
        public float floatGravityScaleFalling = 0.5f;
        [Tooltip("S-hold gravity scale while rising.")]
        public float diveGravityScaleRising = 1f;
        [Tooltip("S-hold gravity scale while falling.")]
        public float diveGravityScaleFalling = 2.0f;
        public float risingGravityScale = 1f;
        public float fallingGravityScale = 1f;
        public float apexThreshold = 0f;
        [Tooltip("Seconds after a charged launch during which air control is disabled.")]
        public float postLaunchAirControlLockTime = 0.15f;

        [Header("Air Control — In Fluid")]
        [Tooltip("W-hold gravity scale while submerged. Applied regardless of vertical direction — buoyancy-driven motion has no clean apex.")]
        public float floatGravityScaleInFluid = 0.5f;
        [Tooltip("S-hold gravity scale while submerged. Applied regardless of vertical direction — buoyancy-driven motion has no clean apex.")]
        public float diveGravityScaleInFluid = 2.0f;

        [Header("Physics")]
        public float gravity = -20f;
        public float slimeRadius = 0.4f;
        public float surfaceCheckDistance = 0.05f;
        public float contactSkin = 0.02f;
        public float depenetrationSkin = 0.01f;
        [Tooltip("Min penetration advantage another contact needs to steal primary from the previous primary. Damps frame-to-frame flipping at concave corners.")]
        public float primaryContactHysteresis = 0.005f;
        public float attachedCheckMultiplier = 3f;
        public float airborneCheckMultiplier = 1f;
        public LayerMask groundLayer;

        [Header("Pinch (caught between two platforms — explicit hold/extrude, driven by WaypointPlatformV2)")]
        [Tooltip("Compression (0..1 of diameter) the press phase waits for before holding. The platform's pinchPressDepth should reach at least this.")]
        public float pinchPressTargetComp = 0.5f;
        [Tooltip("Safety cap (s) on the press phase if the target compression is never reached.")]
        public float pinchPressMaxTime = 1.2f;
        [Tooltip("How long (s) the slime stays fully squashed before it starts extruding out.")]
        public float pinchHoldTime = 0.35f;
        [Tooltip("Squeeze-out speed gain (m/s). Lateral speed = pinchExtrudeSpeed * comp/(1−comp), so the more compressed the gap, the faster the slime shoots out (slow→fast, like a pinched seed). Pure tangent — no axial drive into the walls.")]
        public float pinchExtrudeSpeed = 1.5f;
        [Tooltip("Hard cap (m/s) on the squeeze-out speed so a near-closed gap can't fling the slime.")]
        public float pinchMaxExtrudeSpeed = 4f;
        [Tooltip("(DEAD — kept for .asset compat) The collider now shrinks to FIT the gap directly (radius = (gap−fitMargin)/2), floored by a code const MinPinchColliderScale in SlimeController.Pinch.cs, decoupled from the visual. This min-scale knob is no longer read.")]
        [Range(0.25f, 1f)] public float pinchSqueezeMinScale = 0.4f;
        [Tooltip("Extra clearance (units) the collider shrinks below the raw gap fit, so minor drift never re-overlaps a plate. ~0.04.")]
        public float pinchSqueezeFitMargin = 0.04f;
        // DEAD (kept for .asset compat): pinchMaxLateralStep / pinchPressTargetComp /
        // pinchPressMaxTime / pinchHoldTime. Pinch is now the collider-shrink squeeze model.
        public float pinchMaxLateralStep = 0.04f;

        [Header("Surface Probe")]
        [Tooltip("Contact detector. Single = per-collider ClosestPoint 1회 (기존). Multi = N-probe 샘플링 (multi-path polygon 대응).")]
        public SurfaceProbeMode surfaceProbeMode = SurfaceProbeMode.Single;
        [Tooltip("(Multi) Per-collider probe samples. 8은 multi-path polygon 간격에 충분.")]
        [Range(1, 16)] public int surfaceProbeSamples = 8;
        [Tooltip("(Multi) Probe ring radius as fraction of slimeRadius.")]
        [Range(0f, 1f)] public float surfaceProbeRingScale = 0.8f;
        [Tooltip("(Multi) Dedupe: dot(n1,n2) > 이 값이면 같은 면으로 머지.\n" +
                 "낮출수록 인접 facet contact 가 더 적극적으로 머지됨 → multi-contact churn 감소.\n" +
                 "곡면 (halfpipe / 둥근 ice) 에서 saw-tooth dPos 줄이려면 0.93 ~ 0.88 권장.\n" +
                 "너무 낮추면 sharp corner (90°) 에서도 한 contact 로 머지되어 corner 회피가 부정확.")]
        [Range(0.85f, 1f)] public float surfaceProbeDedupeDot = 0.97f;
        [Tooltip("EffectiveNormal weight ramp: smoothstep ramp covers this fraction of the soft band.\n" +
                 "0 = OFF (legacy step weight). 1 = ramp covers entire soft band. Recommended: 1.0.")]
        [Range(0f, 1f)] public float effectiveNormalWeightRampBand = 1f;
        [Tooltip("Soft-band multiplier for EffectiveNormal & ProjectVelocityAgainstContacts.\n" +
                 "Contacts within (radius + this × contactSkin) contribute, weighted by smoothstep ramp.\n" +
                 "1 = legacy hard threshold (active = within radius+contactSkin only).\n" +
                 "Higher = wider window, smoother active-set transitions on fast curve slides at the cost of looser into-surface clipping.\n" +
                 "Recommended: 6~10. At slime speed v, gap per frame ≈ v × dt; band must exceed expected gap for continuity.")]
        [Range(1f, 15f)] public float effectiveNormalSoftBandMultiplier = 8f;
        [Tooltip("Phase A depenetration aggregation:\n" +
                 "ON (default): per-collider net push = soft-banded weighted average of unique-face contacts (smooth bisector direction × weighted average depth). Eliminates chord-junction transverse kick on Bezier-sampled curves at high tangent speed.\n" +
                 "OFF (legacy): per-collider net push = Σ n[p]·depth[p]. May produce jitter at chord junctions on concave curves but pushes harder at sharp V-notches.")]
        public bool depenetrationUseSmoothedAggregation = true;
        [Tooltip("Velocity midpoint arc compensation (Phase 2A):\n" +
                 "ON (default): in Attached / Charging, the rewritten `velocity = surfaceTangent × speed` is rotated by ω·dt/2 (ω estimated from prev-frame Δθ). Aims each fixed-step chord at the midpoint of the predicted arc, halving chord-arc sagitta visible through Rigidbody2D interpolation on tight curves at high tangent speed.\n" +
                 "OFF: velocity uses the leading-edge tangent unmodified (legacy).")]
        public bool useSurfaceTangentArcCompensation = true;
        [Tooltip("ProjectVelocityAgainstContacts: contacts whose normal aligns with fallbackNormal (smoothed surfaceNormal) by more than this dot threshold are treated as 'same surface' samples and skipped from multi-clip.\n" +
                 "Same-direction multi-probe samples are noisy; clipping against all of them produces vel jitter as the active set toggles.\n" +
                 "0.7 ≈ 45° tolerance (default). Lower = stricter, more contacts skipped (smoother but less corner safety). Higher = original behavior with more multi-clip.\n" +
                 "fallbackNormal itself always provides a primary-axis clip — corners with truly opposing walls (dot < threshold) still get multi-clipped.")]
        [Range(0f, 1f)] public float projectVelocitySameDirectionDot = 0.7f;

        [Tooltip("여러 collider 가 겹쳤을 때, 다른 collider 내부에 묻힌 edge contact 를 reject. " +
                 "한 collider 의 일부 edge 만 묻힌 경우 (slope 에 박힌 spike 의 base 등) 그 일부만 reject. " +
                 "한 collider 의 모든 edge 가 reject 되면 collider 별 가장 가까운 1개를 fallback 으로 부활 (Airborne 박힘 회귀 방지). " +
                 "Multi probe mode 전용. opt-in.")]
        public bool surfaceProbeRejectBuriedContacts = false;
        [Tooltip("Buried 판정용 test point bias (unit). closest point 에서 collider 안쪽으로 이만큼 밀어보고 " +
                 "다른 collider 의 OverlapPoint 가 true 면 buried 로 판단. " +
                 "실제 사용값 = min(이 값, contactDistance × 0.4) — 슬라임이 표면에 매우 가까우면 자동 축소.")]
        [Range(0.001f, 0.05f)] public float surfaceProbeBuriedTestBias = 0.01f;

        [Tooltip("작은 턱 부드럽게 넘어가기 (opt-in). 한 contact 가 (1) 폴리곤 vertex 에 clamp 됐고 " +
                 "(2) 그 vertex 가 슬라임 발(center − gravityUp×slimeRadius)에서 0~smallLedgeStepHeight 사이이고 " +
                 "(3) vertex 바로 위 지점이 그 콜라이더 밖(= 턱 위가 빈 공간)이면 '작은 턱'으로 분류한다.\n" +
                 "ProjectVelocityAgainstContacts 가 그 contact 의 속도 클립을 (실제로 막던 경우에 한해) skip → " +
                 "forward 슬라이드 보존 → Phase A 의 tangent-strip radial push 가 슬라임을 턱 위로 들어올림. " +
                 "SlimeController 의 teleport CCD 도 그 콜라이더를 제외해서 슬라이드가 턱 안으로 들어가게 둔다.\n" +
                 "큰 벽: closest point 가 edge interior(non-clamped)라 (1)에서 제외. " +
                 "tessellate 된 큰 벽: 짧은 edge 의 top vertex 가 stepHeight 안에 들어와도 위에 벽이 계속됨 → (3)에서 제외.")]
        public bool surfaceProbeSmoothSmallLedges = false;
        [Tooltip("작은 턱 판정 기준 높이 (unit). clamped vertex 가 슬라임 발에서 이 값 이내여야 작은 턱. " +
                 "0.05~0.3 권장. 너무 크면 진짜 벽도 넘어가버림 (단 '위가 빈 공간' 조건이 키 큰 벽은 걸러줌).")]
        [Min(0f)] public float smallLedgeStepHeight = 0.15f;

        [Header("Surface Normal Smoothing")]
        [Tooltip("surfaceNormal 저역통과 필터 컷오프(rad/s). 0 = OFF.\n" +
                 "예: 20 → 매 50Hz 프레임당 ~33% 갱신 (90° 튐이 한 프레임에 ~30°로 감쇠, 약 5프레임에 90% 수렴).\n" +
                 "정적 곡면에서도 EffectiveNormal active-set churn (aC=1↔3 swing) 으로 발생하는 normal jitter 를 억제하기 위해 항상 적용됨.")]
        [Min(0f)] public float surfaceNormalSmoothRate = 20f;
        [Tooltip("회전 플랫폼(이 각속도 deg/s 이상) 에서 추가로 적용할 smoothing rate 가중치.\n" +
                 "회전 플랫폼은 기본보다 강하게 smoothing 하고 싶을 때만 사용. 1 = 동일 (기본 rate 만 적용). 2 = 회전 시 2배 rate.")]
        [Min(1f)] public float surfaceNormalSmoothRotatingMultiplier = 1f;
        [Tooltip("이 각속도(deg/s) 이상의 플랫폼에 붙어있을 때 surfaceNormalSmoothRotatingMultiplier 적용.")]
        [Min(0f)] public float surfaceNormalSmoothAngularThreshold = 10f;

        public float postLaunchIgnoreTime = 0.05f;
        public float launchClearanceThreshold = 0.05f;
        public float maxIgnoreTime = 0.15f;

        public float Gravity => gravity;

        [Header("Stunned")]
        [Tooltip("Default stun duration if hazard doesn't specify.")]
        public float defaultStunDuration = 0.5f;
        [Tooltip("Default knockback force if hazard doesn't specify.")]
        public float defaultKnockbackForce = 8f;

        [Header("Friction-Based Detachment")]
        [Tooltip("Master toggle. true: 원심/관성 기반 detach 활성. false: 기존 ceiling-각도 분기만.")]
        public bool useFrictionBasedDetachment = true;
        [Tooltip("점착 가속도 한계 (m/s²). v·ω 가 (gravityHelp + 이 값) 을 넘으면 detach.")]
        [Min(0f)] public float cornerAdhesionAccel = 60f;
        [Tooltip("|v_tangent| 이 이 값 미만이면 검사 skip. 정지 false-positive 방지.")]
        [Min(0f)] public float cornerDetachMinSpeed = 1.5f;
        [Tooltip("CornerStressRatio LP filter rate (1/s). visual SmoothDamp 앞단 한 단계.")]
        [Min(0.001f)] public float cornerStressRatioSmoothRate = 25f;
        [Tooltip("Horizon lookahead 곡선. X = |v| (m/s), Y = lookahead 거리 (m).\n" +
                 "그래프 에디터로 자유롭게 모양 조절. 빠른 슬라임에서 lookahead 를 가파르게 / 완만하게 하거나, 특정 속도대에 평탄 구간을 두는 등.\n" +
                 "기본값은 legacy 식 max(0.15, |v|·0.08) 의 두 끝점 키프레임 (linear).")]
        public AnimationCurve cornerDetachHorizonLookaheadCurve =
            new AnimationCurve(new Keyframe(0f, 0.15f), new Keyframe(20f, 1.6f));
        [Tooltip("Horizon raycast 의 발 아래 검사 깊이 skin (m). 깊이 = slimeRadius + 이 값. 작으면 작은 굴곡에도 detach, 크면 보수적.")]
        [Min(0f)] public float cornerDetachHorizonProbeSkin = 0.25f;
        [Tooltip("Detach 식 모드. 인스펙터 하단 heatmap 에 각 모드의 detach 영역이 시각화됨.")]
        public FrictionDetachFormula frictionDetachFormula = FrictionDetachFormula.VelocityTimesOmega;
        [Tooltip("ConstantTimesOmega 모드용 가상 속도 (m/s). 실제 속도 대신 이 값을 ω 와 곱해 required 계산.")]
        [Min(0.01f)] public float cornerVelocityThreshold = 8f;
        [Tooltip("VelocityTimesOmegaWithMinAngle 모드용 최소 각도 게이트 (deg). |Δθ| 이 값 미만이면 detach 검사 skip.")]
        [Min(0f)] public float cornerMinAngleDeg = 5f;
        [Tooltip("AngularRateThreshold 모드용 ω 임계 (rad/s). 이 값 초과 시 detach 통과 (centrifugal 게이트). 5 rad/s ≈ 0.1s 만에 90° 회전.")]
        [Min(0.01f)] public float cornerOmegaThreshold = 5f;

        [Header("Default Surface Modifiers (used when no SurfaceTag is present, or as the override-off fallback for tagged surfaces)")]
        public AngleScaledValue carrierFriction = AngleScaledValue.Constant(1f);
        [Tooltip("Multiplier on residual landing-tangent momentum decay rate.")]
        public AngleScaledValue crawlOverspeedDecelerationMultiplier = AngleScaledValue.Constant(1f);
        public AngleScaledValue slideResistanceMultiplier = AngleScaledValue.Constant(1f);
        public AngleScaledValue slideGravityMultiplier = AngleScaledValue.Constant(1f);
        public AngleScaledValue slideRampUpTime = AngleScaledValue.Constant(0f);
        public AngleScaledValue slideLinearDrag = AngleScaledValue.Constant(0f);
        public AngleScaledValue slideDownhillGain = AngleScaledValue.Constant(1f);
        public AngleScaledValue slideUphillBrake = AngleScaledValue.Constant(1f);
        public AngleScaledValue landingVelocityRetention = AngleScaledValue.Constant(0f);
        [Tooltip("Charged-launch landings only. Scales the excess of post-retention landing tangent speed over the slime's pre-launch slide speed. 1 = current behavior. 0 = launch+land cycle does not accelerate slide speed past pre-launch. See SurfaceMaterial.landingExcessGain for details.")]
        public AngleScaledValue landingExcessGain = AngleScaledValue.Constant(1f);
        [Tooltip("Toggle the landing tangent speed remap curve for untagged surfaces. OFF (default) = curve ignored.")]
        public bool enableLandingTangentSpeedRemap = false;
        [Tooltip("Default landing tangent speed remap curve for untagged surfaces. Only evaluated when enableLandingTangentSpeedRemap is ON. See SurfaceMaterial.landingTangentSpeedRemapCurve.")]
        public AnimationCurve landingTangentSpeedRemapCurve = new AnimationCurve();
        public AngleScaledValue stickDurationMultiplier = AngleScaledValue.Constant(1f);
        public AngleScaledValue impactRedirectRatio = AngleScaledValue.Constant(0f);
        public AngleScaledValue wallAttachSpeedDampen = AngleScaledValue.Constant(1f);
        public bool useInertialSlide = false;

        public SurfaceModifiers BuildDefaultModifiers() => new SurfaceModifiers
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
            overrideCeilingAngle = false,
            ceilingThresholdOverride = 0f,
            ceilingReattachThresholdOverride = 0f
        };
    }
}
