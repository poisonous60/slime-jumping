using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Settings;

public enum EyesForceState { None, Open, Closed }

public class SlimeVisual : MonoBehaviour
{
    [SerializeField] private SlimeController slimeController;
    [SerializeField] private SlimeVisualSettings settings;
    [SerializeField] private SlimeSettings slimeSettings;
    // Slime-cut tuning (body-scale spring lives there with the rest of the cut feel).
    // Null-safe: without it the displayed scale snaps to the physics ratio (no slosh).
    [SerializeField] private SlimeSplitSettings splitSettings;
    [SerializeField] private CircleCollider2D slimeCollider;
    [SerializeField] private Transform body;
    // Sibling root of the 3D mesh subtree. Lives at Slime/SlimeVisual3d (next
    // to this SlimeVisual GO). Receives the same localScale/localPosition writes
    // body does, so bulk squash/stretch propagates to the mesh. May be null
    // during the migration window before the user wires it.
    [SerializeField] private Transform body3d;
    [SerializeField] private Transform face;
    [SerializeField] private SpriteRenderer eyesRenderer;
    [SerializeField] private Sprite eyesOpen;
    [SerializeField] private Sprite eyesClosed;
    [SerializeField] private Sprite eyesStun;

    // Face tracking
    private Vector2 currentFaceOffset;
    private Vector2 faceVelocity;
    private Vector2 lastInterest;
    private float nextBlinkTime;
    private float blinkEndTime;

    // State tracking
    private SlimeController.SlimeState prevState;
    private Vector2 cachedVelocity;
    private Vector2 lastNonZeroVelocity;

    // Deformation axis
    private Vector2 lastDeformAxis = Vector2.up;
    private Vector2 lastSurfaceNormal = Vector2.up;
    private Vector2 prevGravityUp = Vector2.up;

    // Scale pipeline
    private Vector2 smoothedScale = Vector2.one;
    private Vector2 scaleVelocity;

    // Parallel smoothing of just the velocity-stretch contribution so we can
    // subtract it from body3d's applied scale. The 3D mesh handles velocity
    // stretch in the shader (volume-preserving, rotated-ellipse along world
    // velocity) instead of through body.localScale's axis-aligned scale,
    // which can't express a diagonal rugby ball. body (archived 2D sprite)
    // still receives full scale including velStr — no regression there.
    private Vector2 smoothedVelStr;
    private Vector2 velStrSmoothVel;

    // Offset pipeline
    private Vector2 smoothedOffset;
    private Vector2 offsetVelocity;

    // Body scale (slime-cut). The physics BodyScaleRatio steps down instantly on a cut and
    // ramps back up while healing; the displayed ratio chases it through an underdamped
    // spring (WaterZone_v2-style Hooke + damping) so the body visibly sloshes as it gathers
    // into a smaller round blob, and grows back without popping. 1 = rest, multiplies the
    // whole bulk scale/offset uniformly (face is a child of body, so its tuned proportion
    // to the body survives every size).
    private float displayedBodyScale = 1f;
    private float bodyScaleSpringVel;
    private Vector3 faceRestScale = Vector3.one;

    // Pulled by SlimeSplitSequence to keep the transient cut-face clip glued to the
    // sloshing displayed size (the flat face closes exactly as the body rounds up).
    public float DisplayedBodyScale => displayedBodyScale;

    // Cut-transition layer 2 (asymmetric gather): SlimeSplitSequence pushes a WORLD-space
    // offset each frame during CLOSE — the body visually migrates toward the cut face while
    // the collider stays centered, so the far side reads as flowing in to cover the wound.
    // Zeroed by the sequence when the transition ends.
    private Vector2 cutGatherWorld;
    public void SetCutGatherOffset(Vector2 worldOffset) => cutGatherWorld = worldOffset;

    // One-shot kick to the body-scale spring (e.g. the regrow-complete "pop"). Positive =
    // outward overshoot.
    public void AddBodyScaleImpulse(float velocity) => bodyScaleSpringVel += velocity;

    // Impact (C-2)
    private float impactTimer = -1f;
    private float impactMagnitude;

    // Pong overlay (Phase 3) — driven by SlimeController.OnFrictionDetach.
    // pongTimer < 0 = idle. Set to 0 on event, advances each LateUpdate, returns to -1 when finished.
    private float pongTimer = -1f;
    private float pongIntensity;

    // Coyote squash recovery phase (runs after EnergyCoyoteTimer reaches 0)
    private float coyoteRecoveryTimer = -1f;
    private float coyoteRecoveryStartMagnitude;
    private float prevCoyoteTimer;

    // Breathing (C-6)
    private float breathPhase;

    // Charging curve (0~1, computed per frame)
    private float chargingCurve;

    // Shader asymmetry
    private SpriteRenderer bodyRenderer;
    // 3D mesh renderer mirror — dual-pushed alongside the SpriteRenderer through
    // the migration's step-5 comparison window. After the SpriteRenderer is
    // disabled at step 6, the dual-push collapses to the mesh path only.
    private MeshRenderer bodyMeshRenderer;
    private MeshFilter bodyMeshFilter;
    private MaterialPropertyBlock mpb;
    private static readonly int AsymmetryID = Shader.PropertyToID("_Asymmetry");
    private static readonly int ChargeDipID = Shader.PropertyToID("_ChargeDip");
    private static readonly int DipCenterUID = Shader.PropertyToID("_DipCenterU");
    // _SurfaceNormalWS / _DipDirWS feed SlimeDeform.hlsl. The "WS" suffix is
    // the contract with the HLSL signature — both vectors are world-space.
    private static readonly int SurfaceNormalWSID = Shader.PropertyToID("_SurfaceNormalWS");
    private static readonly int DipDirWSID = Shader.PropertyToID("_DipDirWS");
    // xyz = normalized world-space velocity, w = speed normalized to velocityStretchSpeedRef.
    // Drives the teardrop front-hemisphere compression in SlimeDeform.hlsl.
    private static readonly int VelocityDirWSID = Shader.PropertyToID("_VelocityDirWS");
    // Stretch amount pre-evaluated from velocityStretch3DCurve every frame.
    // Pushing the result (instead of raw speed + curve) keeps the shader
    // simple: just one scalar, no piecewise math in HLSL.
    private static readonly int VelocityStretchAmountID = Shader.PropertyToID("_VelocityStretchAmount");
    // Generalized pinch squash: per-contact face normals + compression, consumed by SlimeDeform.hlsl
    // section 3.5. Arrays are fixed-length PinchMax (must match the shader's PINCH_MAX and stay
    // constant across SetVectorArray calls).
    private const int PinchMax = 3;
    private static readonly int PinchCountID = Shader.PropertyToID("_PinchCount");
    private static readonly int PinchNormalWSID = Shader.PropertyToID("_PinchNormalWS");
    private static readonly int PinchCompID = Shader.PropertyToID("_PinchComp");
    private static readonly int PinchDisplaceScaleID = Shader.PropertyToID("_PinchDisplaceScale");
    // World-space half-gap per contact (= distance from slime center to that wall's face). The shader
    // plane-clamps every vertex past it back onto the plane → a genuine flat face exactly at the gap.
    private static readonly int PinchHalfGapID = Shader.PropertyToID("_PinchHalfGap");
    // World gap-center the plane-clamp measures from (the true midpoint between the plates, NOT the
    // drifting mesh origin) so the carved slab aligns to both real plate faces.
    private static readonly int PinchCenterWSID = Shader.PropertyToID("_PinchCenterWS");
    private readonly Vector4[] pinchNormalBuf = new Vector4[PinchMax];
    private readonly float[] pinchCompBuf = new float[PinchMax];
    private readonly float[] pinchHalfGapBuf = new float[PinchMax];
    private float smoothedSquashStrength;
    private float squashStrengthVel;

    // body3d rest transform captured in Start. Per-frame deformation values from
    // the bulk pipeline (assumed in a "1.0 = rest" convention) are multiplied
    // onto these baselines so the user's hand-tuned SlimeVisual3d transform in
    // the Scene survives Play. body itself doesn't need this treatment because
    // its rest scale has always been (1,1,1) by construction.
    private Vector3 body3dRestScale = Vector3.one;
    private Vector3 body3dRestPosition;
    private Quaternion body3dRestRotation = Quaternion.identity;
    // Rest-only cached mesh height (world units) and SlimeVisual's rest lossyScale.y.
    // BodyNativeHeight divides these to produce a SlimeVisual-local value without
    // reading live-deformed state, avoiding a per-frame feedback loop with PushDeform.
    private float body3dNativeHeightRest;
    private float transformRestLossyScaleY = 1f;

    // Sit-pose depth shift bookkeeping. World-space Z of SlimeVisual + body3d at
    // shift time; restored on release. Assumes Slime root's world Z is invariant
    // during the sit (project design: root stays at Z=0, only visual Z moves).
    private bool sitDepthShiftActive;
    private float sitDepthOriginalVisualZ;
    private float sitDepthOriginalBody3dZ;

    // External overrides applied while IsExternallyAnimated (BoxSleepAnimator etc.).
    // All cleared on the gate toggle so stale state can't leak between sessions.
    private EyesForceState _eyesForceState = EyesForceState.None;
    private Quaternion? _externalRotationOverride;
    // Face offset is a DELTA from the inspector-set rest position (captured in Start).
    // When set, replaces UpdateFaceOffset's per-frame write entirely — drift/saccade
    // pause while the override is active.
    private Vector3? _externalFaceOffset;
    private Vector3 faceRestLocalPosition;

    // Additive scale overlay that external systems push in via SetExternalCompression
    // (e.g. SlimeBurnVisual when the slime is being squeezed by a sustained granite
    // beam against a wall). Added on top of the normal squash/stretch pipeline; not
    // gated by IsExternallyAnimated. Default zero = no contribution.
    private Vector2 externalCompression;

    // External animation gate — when true, body is externally driven; face keeps its own pipeline.
    // Used by IExternalController implementers (sit-pose, firework recoil, pipe transit, etc.).
    private bool isExternallyAnimated;
    public bool IsExternallyAnimated
    {
        get => isExternallyAnimated;
        set
        {
            if (isExternallyAnimated == value) return;
            isExternallyAnimated = value;
            ResetDeformationState();
        }
    }

    // Visual clearance
    private readonly RaycastHit2D[] clearanceHits = new RaycastHit2D[8];

    private void OnDestroy()
    {
        if (slimeController != null)
            slimeController.OnFrictionDetach -= HandleFrictionDetach;
    }

    private void Start()
    {
        smoothedScale = Vector2.one;
        lastDeformAxis = Vector2.up;
        lastSurfaceNormal = Vector2.up;
        prevState = slimeController.CurrentState;
        nextBlinkTime = Time.time + Random.Range(2f, 5f);
        mpb = new MaterialPropertyBlock();
        if (slimeController != null)
            slimeController.OnFrictionDetach += HandleFrictionDetach;
        bodyRenderer = body.GetComponent<SpriteRenderer>();
        transformRestLossyScaleY = transform.lossyScale.y;
        // Capture the inspector-set face position as the rest baseline. Done in Start
        // (before any LateUpdate) so UpdateFaceOffset's per-frame writes don't taint it.
        if (face != null)
        {
            faceRestLocalPosition = face.localPosition;
            faceRestScale = face.localScale;
        }
        // The 3D mesh now lives on a sibling subtree (Slime/SlimeVisual3d/...)
        // rather than under the 2D body. Walk that subtree to cache the
        // renderer + filter. Stays null if body3d isn't wired, in which case
        // the mesh-side dual-push short-circuits.
        if (body3d != null)
        {
            bodyMeshRenderer = body3d.GetComponentInChildren<MeshRenderer>();
            bodyMeshFilter = body3d.GetComponentInChildren<MeshFilter>();
            body3dRestScale = body3d.localScale;
            body3dRestPosition = body3d.localPosition;
            body3dRestRotation = body3d.localRotation;
            if (bodyMeshFilter != null && bodyMeshFilter.sharedMesh != null)
            {
                body3dNativeHeightRest = bodyMeshFilter.sharedMesh.bounds.size.y
                                       * bodyMeshFilter.transform.lossyScale.y;
            }
        }
    }

    private void LateUpdate()
    {
        if (isExternallyAnimated)
        {
            // External animator drives body (scale/position/shader). Face animation
            // (drift + blink) keeps running so the slime feels alive while sitting.
            CacheVelocity();
            UpdateFaceOffset();
            ApplyExternalFaceOffset();
            UpdateBlink();
            return;
        }

        CacheVelocity();
        UpdateDeformAxis();
        UpdateSquashStrength();
        DetectImpact();
        UpdateChargingCurve();

        // Scale: slow layer
        Vector2 surfSq = ComputeSurfaceSquash();
        Vector2 velStr = ComputeVelocityStretch();
        Vector2 chargeSq = ComputeChargingSquash();
        // Pinch is NO LONGER a whole-body localScale term — it is a genuine per-contact, local
        // vertex deform pushed to the GPU (mesh MPB block below → SlimeDeform.hlsl section 3.5).
        Vector2 targetScale = Vector2.one + surfSq + velStr + chargeSq;
        smoothedScale = SmoothDampV2(smoothedScale, targetScale, ref scaleVelocity, settings.deformSmoothTime);
        // Parallel-smooth the velStr contribution alone at the same rate; used
        // to subtract velStr from body3d's applied scale below. Linear because
        // SmoothDampV2 passes maxSpeed=Infinity internally.
        smoothedVelStr = SmoothDampV2(smoothedVelStr, velStr, ref velStrSmoothVel, settings.deformSmoothTime);

        // Scale: fast layer
        Vector2 impact = ComputeImpactOverlay();
        Vector2 breath = ComputeBreathing();
        Vector2 chargeTremor = ComputeChargingTremor();
        Vector2 coyoteSq = ComputeCoyoteSquash();
        Vector2 pong = ComputePongOverlay();
        Vector2 finalScale = smoothedScale + impact + breath + chargeTremor + coyoteSq + externalCompression + pong;
        finalScale.x = Mathf.Clamp(finalScale.x, settings.deformScaleMin, settings.deformScaleMax);
        finalScale.y = Mathf.Clamp(finalScale.y, settings.deformScaleMin, settings.deformScaleMax);

        // Offset
        Vector2 sag = ComputeGravitySagOffset();
        Vector2 lean = ComputeVelocityLeanOffset();
        Vector2 chargePush = ComputeChargingOffset();
        Vector2 targetOff = sag + lean + chargePush;
        smoothedOffset = SmoothDampV2(smoothedOffset, targetOff, ref offsetVelocity, settings.offsetSmoothTime);

        // Body scale (slime-cut): an underdamped spring chases the physics ratio (semi-implicit
        // Euler, dt clamped so an editor hitch can't blow it up). During CLOSE the physics ramps
        // along closeBodyCurve, so the visible shrink is that curve with goo lag; the natural
        // overshoot at the ramp's end is the settle slosh. springK/damping tune both.
        {
            float physScale = slimeController.BodyScaleRatio;
            if (splitSettings == null)
            {
                displayedBodyScale = physScale;
                bodyScaleSpringVel = 0f;
            }
            else
            {
                float sdt = Mathf.Min(Time.deltaTime, 0.05f);
                bodyScaleSpringVel += (-splitSettings.bodyScaleSpringK * (displayedBodyScale - physScale)
                                       - splitSettings.bodyScaleSpringDamping * bodyScaleSpringVel) * sdt;
                displayedBodyScale += bodyScaleSpringVel * sdt;
                displayedBodyScale = Mathf.Clamp(displayedBodyScale, 0.1f, 1.5f);
            }
        }

        // Visual clearance clamp (prevent penetration at corners)
        Vector2 appliedOffset = smoothedOffset;
        Vector2 appliedScale = finalScale;

        if (slimeCollider != null && slimeSettings != null && appliedOffset.sqrMagnitude > 1e-6f)
        {
            Vector2 worldOffset = (Vector2)transform.TransformVector(appliedOffset);
            Vector2 sagDir = worldOffset.normalized;
            float offsetMag = worldOffset.magnitude;

            Vector3 colScale = slimeCollider.transform.lossyScale;
            float colliderWorldRadius = slimeCollider.radius * Mathf.Max(colScale.x, colScale.y);

            // appliedScale.x = tangent-direction scale (body.localX ≈ tangent on surfaces)
            float scaleExt = Mathf.Max(0f, (appliedScale.x - 1f) * colliderWorldRadius);
            float desiredReach = offsetMag + scaleExt;

            // Use slimeController.Position (rb.position, physics-step coords) instead of
            // the interpolated transform.position. Physics2D.Raycast queries the platform's
            // collider in physics-step space; if the origin is interpolated, the clearance
            // distance drifts between physics steps on moving platforms and chatters the clamp.
            float clearance = QueryClearance(
                slimeController.Position, sagDir,
                colliderWorldRadius + desiredReach + settings.visualClearanceSkin);

            float maxReach = Mathf.Max(0f, clearance - colliderWorldRadius - settings.visualClearanceSkin);

            if (desiredReach > maxReach && desiredReach > 0.001f)
            {
                float ratio = maxReach / desiredReach;
                appliedOffset *= ratio;

                float deltaX = appliedScale.x - 1f;
                if (deltaX > 0f)
                    appliedScale.x = 1f + deltaX * ratio;
            }
        }

        // Ground compensation — body.localScale.y < 1 shrinks the visual symmetrically
        // around body center, lifting the surface-touching edge off the ground. Push
        // body toward the attached surface by (1-scale.y) * strength so the edge
        // stays planted. Applied AFTER visualClearance clamp because it compensates
        // a mathematical artifact of symmetric scaling, not a corner penetration —
        // the clamp would otherwise zero this out on flat ground (clearance = collider
        // radius, maxReach = 0, ratio = 0).
        if (slimeController.CurrentState != SlimeController.SlimeState.Airborne)
        {
            float yCompress = Mathf.Max(0f, 1f - appliedScale.y);
            if (yCompress > 0f)
            {
                appliedOffset += -lastSurfaceNormal * yCompress * settings.groundCompensationStrength;
            }
        }

        appliedScale *= displayedBodyScale;
        appliedOffset *= displayedBodyScale;

        // Cut gather (world units, already shaped by the sequence's curve) — applied after
        // the body-scale multiply so its magnitude is exactly what the sequence computed.
        if (cutGatherWorld.sqrMagnitude > 1e-10f)
            appliedOffset += (Vector2)transform.InverseTransformVector(cutGatherWorld);

        var bulkScale    = new Vector3(appliedScale.x, appliedScale.y, 1f);
        var bulkPosition = new Vector3(appliedOffset.x, appliedOffset.y, 0f);
        body.localScale    = bulkScale;
        body.localPosition = bulkPosition;
        if (body3d != null)
        {
            // bulkPosition is authored in body's parent-frame (SlimeVisual's
            // local space). body3d lives directly under the Slime root, a
            // frame scaled differently from SlimeVisual. Convert by this
            // transform's localScale so the world-space offset matches body's.
            // Scale already multiplies onto body3dRestScale so no conversion
            // needed — lossyScale equivalence is preserved when rest matches.
            // Rotation: body rotates so local Y = surface normal (UpdateDeformAxis).
            // Mirror that so bulkScale.y compresses along surface normal on the
            // mesh too — otherwise the squash always points along world Y and
            // looks wrong on walls/ceilings.
            //
            // Subtract smoothedVelStr so the velocity-stretch contribution is
            // NOT applied here; the shader's volume-preserving stretch (5b in
            // SlimeDeform.hlsl) handles it along world velocity direction.
            // This avoids the diagonal-ball issue caused by axis-aligned scale.
            // velStr was summed into appliedScale before the body-scale multiply, so its
            // subtraction here must scale by the same ratio. Z (depth) shrinks uniformly
            // with the body-scale ratio — bulkScale.z stays 1 for the 2D sprite.
            var bulkScale3D = new Vector3(
                bulkScale.x - smoothedVelStr.x * displayedBodyScale,
                bulkScale.y - smoothedVelStr.y * displayedBodyScale,
                bulkScale.z * displayedBodyScale);
            // Compose surface alignment (body.localRotation) over designer's rest
            // rotation (body3dRestRotation). For p in mesh-local:
            //   p_final = surfaceRot * (restRot * p_mesh) = (surfaceRot * restRot) * p_mesh.
            // Assumes SlimeVisual.transform.localRotation == identity (C-1 tenet).
            body3d.localRotation = body.localRotation * body3dRestRotation;
            body3d.localScale    = Vector3.Scale(body3dRestScale, bulkScale3D);
            // Depth shift lives on world Z; bulkPosition contributes only X/Y.
            body3d.localPosition = body3dRestPosition + Vector3.Scale(transform.localScale, bulkPosition);
        }

        // Shader properties — computed once, dual-pushed below to both the
        // SpriteRenderer (legacy 2D shader) and the MeshRenderer (new 3D
        // shader) so the two visuals stay in lock-step during the migration's
        // comparison window.
        float asymmetry = -smoothedSquashStrength * settings.surfaceSquashAsymmetry;
        float chargeDip = chargingCurve * settings.chargingDipMax;

        float dipCenterU = 0f;
        if (slimeController.CurrentState == SlimeController.SlimeState.Charging)
        {
            Vector2 aim = slimeController.AimDirection;
            Vector2 tangent = new Vector2(-lastSurfaceNormal.y, lastSurfaceNormal.x);
            float aimTangent = Vector2.Dot(aim, tangent);
            dipCenterU = aimTangent * settings.chargingDipLateralShift;
        }

        if (bodyRenderer != null)
        {
            bodyRenderer.GetPropertyBlock(mpb);
            mpb.SetFloat(AsymmetryID, asymmetry);
            mpb.SetFloat(ChargeDipID, chargeDip);
            mpb.SetFloat(DipCenterUID, dipCenterU);
            bodyRenderer.SetPropertyBlock(mpb);
        }

        // Mesh dual-push: same scalars + new world-space vectors for SlimeDeform.hlsl.
        // _DipDirWS reuses the dipCenterU scalar (= aimTangent * lateralShift)
        // as the lateral bias along the surface tangent.
        if (bodyMeshRenderer != null && mpb != null)
        {
            Vector3 surfaceNormalWS = new Vector3(lastSurfaceNormal.x, lastSurfaceNormal.y, 0f);
            // Dip lands on the slime's outward face (TOP, away from the
            // surface) — same convention as the 2D shader's V=top dip.
            // Lateral term biases it along the surface tangent based on aim.
            Vector3 dipDirWS = surfaceNormalWS;
            if (dipCenterU != 0f)
            {
                Vector2 tangent = new Vector2(-lastSurfaceNormal.y, lastSurfaceNormal.x);
                dipDirWS += new Vector3(tangent.x, tangent.y, 0f) * dipCenterU;
            }
            if (dipDirWS.sqrMagnitude > 1e-6f) dipDirWS.Normalize();

            // Teardrop front-compression needs world velocity direction + speed norm.
            // Reuse velocityStretchSpeedRef as the "full speed" normalizer so this
            // ramps up on the same curve as the body.localScale velocity stretch.
            float rawSpeed = cachedVelocity.magnitude;
            float speedNorm = Mathf.Clamp01(rawSpeed / Mathf.Max(0.01f, settings.velocityStretchSpeedRef));
            Vector2 velDir = cachedVelocity.sqrMagnitude > 0.01f ? cachedVelocity.normalized : Vector2.zero;
            Vector4 velocityDirWS = new Vector4(velDir.x, velDir.y, 0f, speedNorm);

            // 3D velocity-stretch amount: remap rawSpeed so speedMin→0 and
            // speedMax→1 on the AnimationCurve's X axis, then sample.
            // speedMin/Max define the *reference* anchors, NOT hard clamps —
            // speeds beyond them still sample the curve at extrapolated x, so
            // designers can draw keys past x=1 to make stretch keep growing
            // at high speeds. Unity's default ClampForever WrapMode takes
            // over at the curve's actual leftmost/rightmost keys.
            float stretchDenom = Mathf.Max(0.01f, settings.velocityStretch3DSpeedMax - settings.velocityStretch3DSpeedMin);
            float stretchT = (rawSpeed - settings.velocityStretch3DSpeedMin) / stretchDenom;
            float velocityStretchAmount = settings.velocityStretch3DCurve != null
                ? settings.velocityStretch3DCurve.Evaluate(stretchT)
                : 0f;

            bodyMeshRenderer.GetPropertyBlock(mpb);
            mpb.SetFloat(AsymmetryID, asymmetry);
            mpb.SetFloat(ChargeDipID, chargeDip);
            mpb.SetVector(SurfaceNormalWSID, surfaceNormalWS);
            mpb.SetVector(DipDirWSID, dipDirWS);
            mpb.SetVector(VelocityDirWSID, velocityDirWS);
            mpb.SetFloat(VelocityStretchAmountID, velocityStretchAmount);

            // Generalized pinch squash — per-contact local flatten against each closing wall.
            int pinchCount = BuildPinchDeform();
            mpb.SetFloat(PinchCountID, pinchCount);
            mpb.SetVectorArray(PinchNormalWSID, pinchNormalBuf);
            mpb.SetFloatArray(PinchCompID, pinchCompBuf);
            mpb.SetFloatArray(PinchHalfGapID, pinchHalfGapBuf);
            Vector2 pinchCenter = slimeController.PinchCenter;
            mpb.SetVector(PinchCenterWSID, new Vector4(pinchCenter.x, pinchCenter.y, 0f, 0f));
            mpb.SetFloat(PinchDisplaceScaleID, settings.pinchDisplaceScale);

            bodyMeshRenderer.SetPropertyBlock(mpb);
        }

        UpdateFaceOffset();
        ApplyExternalFaceOffset();
        UpdateBlink();

        prevState = slimeController.CurrentState;
    }

    // Native body height in SlimeVisual-local units. Used by external animators to
    // compute pivot-compensation offsets for body.localPosition.y (body is a child of
    // SlimeVisual). Rest-only values — never reads live-deformed bounds/lossyScale,
    // which would create a feedback loop with per-frame PushDeform.
    public float BodyNativeHeight
    {
        get
        {
            if (bodyMeshFilter != null)
            {
                return body3dNativeHeightRest / Mathf.Max(1e-6f, transformRestLossyScaleY);
            }
            if (bodyRenderer == null) bodyRenderer = body != null ? body.GetComponent<SpriteRenderer>() : null;
            if (bodyRenderer == null || bodyRenderer.sprite == null) return 1f;
            return bodyRenderer.sprite.bounds.size.y;
        }
    }

    // Shifts the visual (face/eyes/mouth via SlimeVisual transform AND the sibling
    // body3d subtree) to a target world Z, so both 2D-sorted face sprites and the
    // 3D mesh stay depth-aligned relative to each other during a sit pose.
    // World-space Z is used (not localPosition.z) because SetExternalDeform rewrites
    // body3d.localPosition every frame. Assumes Slime root world Z is invariant.
    public void ApplySitDepthShift(float worldZ)
    {
        if (sitDepthShiftActive) return;
        sitDepthShiftActive = true;

        Vector3 p = transform.position;
        sitDepthOriginalVisualZ = p.z;
        p.z = worldZ;
        transform.position = p;

        if (body3d != null)
        {
            Vector3 bp = body3d.position;
            sitDepthOriginalBody3dZ = bp.z;
            bp.z = worldZ;
            body3d.position = bp;
        }
    }

    public void RestoreSitDepthShift()
    {
        if (!sitDepthShiftActive) return;
        sitDepthShiftActive = false;

        Vector3 p = transform.position;
        p.z = sitDepthOriginalVisualZ;
        transform.position = p;

        if (body3d != null)
        {
            Vector3 bp = body3d.position;
            bp.z = sitDepthOriginalBody3dZ;
            body3d.position = bp;
        }
    }

    // Explicit per-feature API for external controllers that need a specific surface
    // normal for the shader (sit pose wants world-up). Other IExternalController
    // users (firework recoil, pipe transit) are unaffected unless they call this.
    public void SetExternalSurfaceNormal(Vector2 normal)
    {
        if (normal.sqrMagnitude > 1e-6f)
            lastSurfaceNormal = normal.normalized;
    }

    // Force the eye sprite regardless of blink scheduler. Stun still wins.
    // Pass EyesForceState.None to release back to normal blink behavior.
    public void SetEyesForced(EyesForceState state)
    {
        _eyesForceState = state;
    }

    // Composes a full XYZ rotation (Euler-driven on the caller side) onto body3d's
    // rest rotation inside SetExternalDeform. Pass null to clear. Caller is responsible
    // for clearing once the animation completes — leaving an override active will keep
    // body3d rotated even after the gate closes (ResetDeformationState clears it as a
    // safety net on IsExternallyAnimated toggle).
    public void SetExternalRotation(Quaternion? rotation)
    {
        _externalRotationOverride = rotation;
    }

    // Sets a delta from the inspector-set rest position of the face transform
    // (captured in Start). When set, REPLACES UpdateFaceOffset's per-frame write —
    // natural drift/saccade pause while the override is active. Pass null to clear
    // and let UpdateFaceOffset resume control.
    public void SetExternalFaceOffset(Vector3? offset)
    {
        _externalFaceOffset = offset;
    }

    // Additive overlay onto the body scale pipeline. extraScale is added to the
    // composed (smoothedScale + impact + breath + tremor + coyote) before clamping,
    // so e.g. (0.15, -0.15) squashes Y by 15% on top of whatever is already happening.
    // Set to Vector2.zero to release. Caller is responsible for smoothing the input.
    public void SetExternalCompression(Vector2 extraScale)
    {
        externalCompression = extraScale;
    }

    // Face subtree root (eyes + mouth sprites). SlimeSplitSequence reads its child
    // SpriteRenderers to clip the face along the same wound planes as the body.
    public Transform FaceTransform => face;

    // Public refs for systems that need to attach decals/effects on the slime visual
    // (e.g. SlimeBurnVisual placing burn marks). body is the legacy 2D sprite root,
    // body3d is the 3D mesh root. Either can be null depending on visual setup;
    // SlimeBurnVisual prefers body3d if present.
    public Transform Body => body;
    public Transform Body3d => body3d;
    // body3d's rest localScale captured in Start. SlimeSplitSequence pulls the world cut
    // plane back through (live rotation × REST scale) so the rendered cut counter-rotates
    // with the body (world-fixed orientation) but rides the live squash/stretch scale.
    public Vector3 Body3dRestScale => body3dRestScale;

    private void ApplyExternalFaceOffset()
    {
        if (!_externalFaceOffset.HasValue || face == null) return;
        face.localPosition = faceRestLocalPosition + _externalFaceOffset.Value;
    }

    // Called by external animators (e.g. SitPoseAnimator) while IsExternallyAnimated is true.
    // Silently ignored when the gate is off, so runaway calls can't corrupt the normal pipeline.
    public void SetExternalDeform(Vector3 localScale, Vector3 localPosition, float asymmetry, float chargeDip)
    {
        if (!isExternallyAnimated) return;
        if (body != null)
        {
            body.localScale = localScale;
            body.localPosition = localPosition;
        }
        if (body3d != null)
        {
            // Sit pose skips surface alignment — apply rest rotation only, unless an
            // external animator (e.g. BoxSleepAnimator) provided an explicit override.
            // Override is composed on the LEFT so it acts as a world-space pre-rotation
            // (project tenet C-1: SlimeVisual.transform.localRotation == identity, so
            // body3d local axes equal world axes).
            body3d.localRotation = _externalRotationOverride.HasValue
                ? _externalRotationOverride.Value * body3dRestRotation
                : body3dRestRotation;
            body3d.localScale = Vector3.Scale(body3dRestScale, localScale);
            // Preserve existing localPosition.z so ApplySitDepthShift's world-Z
            // shift isn't wiped every frame. Only X/Y come from localPosition.
            Vector3 target = body3dRestPosition + Vector3.Scale(transform.localScale, localPosition);
            body3d.localPosition = new Vector3(target.x, target.y, body3d.localPosition.z);
        }
        // Face position is left to UpdateFaceOffset (still running under the external gate).
        if (bodyRenderer != null && mpb != null)
        {
            bodyRenderer.GetPropertyBlock(mpb);
            mpb.SetFloat(AsymmetryID, asymmetry);
            mpb.SetFloat(ChargeDipID, chargeDip);
            mpb.SetFloat(DipCenterUID, 0f);
            bodyRenderer.SetPropertyBlock(mpb);
        }
        // Mesh dual-push under the external-animation gate. Surface normal
        // tracks the most recent grounded value; dipDir defaults to the
        // anti-normal direction (no lateral aim under sit pose / firework
        // recoil / pipe transit).
        if (bodyMeshRenderer != null && mpb != null)
        {
            Vector3 surfaceNormalWS = new Vector3(lastSurfaceNormal.x, lastSurfaceNormal.y, 0f);
            Vector3 dipDirWS = -surfaceNormalWS;
            if (dipDirWS.sqrMagnitude > 1e-6f) dipDirWS.Normalize();

            bodyMeshRenderer.GetPropertyBlock(mpb);
            mpb.SetFloat(AsymmetryID, asymmetry);
            mpb.SetFloat(ChargeDipID, chargeDip);
            mpb.SetVector(SurfaceNormalWSID, surfaceNormalWS);
            mpb.SetVector(DipDirWSID, dipDirWS);
            // External animators (sit pose, firework recoil, pipe transit) don't
            // have meaningful velocity — zero out so front-compress + stretch are inert.
            mpb.SetVector(VelocityDirWSID, Vector4.zero);
            mpb.SetFloat(VelocityStretchAmountID, 0f);
            bodyMeshRenderer.SetPropertyBlock(mpb);
        }
    }

    private void ResetDeformationState()
    {
        smoothedScale = Vector2.one;
        scaleVelocity = Vector2.zero;
        smoothedVelStr = Vector2.zero;
        velStrSmoothVel = Vector2.zero;
        smoothedOffset = Vector2.zero;
        offsetVelocity = Vector2.zero;
        impactTimer = -1f;
        impactMagnitude = 0f;
        chargingCurve = 0f;
        smoothedSquashStrength = 0f;
        squashStrengthVel = 0f;
        externalCompression = Vector2.zero;

        // Clear external overrides so stale state from a previous override session
        // (e.g. SitPose followed by BoxSleep) can't leak into the next.
        _eyesForceState = EyesForceState.None;
        _externalRotationOverride = null;
        _externalFaceOffset = null;

        if (body != null)
        {
            body.localScale = Vector3.one;
            body.localPosition = Vector3.zero;
        }
        if (body3d != null)
        {
            body3d.localScale = body3dRestScale;
            body3d.localPosition = body3dRestPosition;
            body3d.localRotation = body3dRestRotation;
        }
        // prevGravityUp freezes during the external gate (updated only inside
        // UpdateDeformAxis). Use the controller's live GravityUp as the fresh
        // source of truth; normal LateUpdate overwrites this on the next frame.
        lastSurfaceNormal = slimeController != null ? slimeController.GravityUp : Vector2.up;

        if (bodyRenderer != null && mpb != null)
        {
            bodyRenderer.GetPropertyBlock(mpb);
            mpb.SetFloat(AsymmetryID, 0f);
            mpb.SetFloat(ChargeDipID, 0f);
            mpb.SetFloat(DipCenterUID, 0f);
            bodyRenderer.SetPropertyBlock(mpb);
        }
        if (bodyMeshRenderer != null && mpb != null)
        {
            bodyMeshRenderer.GetPropertyBlock(mpb);
            mpb.SetFloat(AsymmetryID, 0f);
            mpb.SetFloat(ChargeDipID, 0f);
            mpb.SetVector(SurfaceNormalWSID, Vector3.up);
            mpb.SetVector(DipDirWSID, Vector3.up);
            mpb.SetVector(VelocityDirWSID, Vector4.zero);
            mpb.SetFloat(VelocityStretchAmountID, 0f);
            bodyMeshRenderer.SetPropertyBlock(mpb);
        }
    }

    // --- Velocity caching ---

    private void CacheVelocity()
    {
        cachedVelocity = slimeController.Velocity;
        if (cachedVelocity.sqrMagnitude > 0.01f)
            lastNonZeroVelocity = cachedVelocity;
    }

    // --- Deformation axis (replaces old body rotation) ---

    private void UpdateDeformAxis()
    {
        // Detect gravity direction change: snap body.up and reset scale state
        // to prevent deformation along intermediate slerp angles.
        Vector2 currentGravityUp = slimeController.GravityUp;
        if (Vector2.Dot(prevGravityUp, currentGravityUp) < 0.7f)
        {
            body.up = (Vector3)currentGravityUp;
            scaleVelocity = Vector2.zero;
            smoothedScale = Vector2.one;
            offsetVelocity = Vector2.zero;
        }
        prevGravityUp = currentGravityUp;

        Vector2 normal = slimeController.SurfaceNormal;
        if (slimeController.CurrentState != SlimeController.SlimeState.Airborne
            && normal.sqrMagnitude > 0.001f)
        {
            lastSurfaceNormal = normal;
        }

        Vector2 targetAxis;
        if (slimeController.CurrentState == SlimeController.SlimeState.Airborne)
        {
            // Axis-decomposed stretch uses gravity axes, so body.up must be gravity up
            targetAxis = slimeController.GravityUp;
        }
        else
        {
            targetAxis = lastSurfaceNormal;
        }

        lastDeformAxis = targetAxis;
        body.up = Vector3.Slerp(body.up, (Vector3)targetAxis,
                                settings.deformAxisSlerpSpeed * Time.deltaTime);
    }

    // --- Squash strength (shared by scale + shader asymmetry, SmoothDamp-ed) ---

    private void UpdateSquashStrength()
    {
        float target = (slimeController.CurrentState == SlimeController.SlimeState.Airborne)
            ? 0f
            : Mathf.Clamp01(-Vector2.Dot(-slimeController.GravityUp, lastSurfaceNormal));
        smoothedSquashStrength = Mathf.SmoothDamp(smoothedSquashStrength, target,
                                                  ref squashStrengthVel, settings.deformSmoothTime);
    }

    // --- Surface squash (C-5) ---

    private Vector2 ComputeSurfaceSquash()
    {
        float squash = smoothedSquashStrength * settings.surfaceSquashIntensity;
        return new Vector2(squash, -squash);
    }

    // --- Pinch squash (squeezed between two opposing surfaces) ---

    private float pinchCompSmoothed;
    private float pinchCompVel;

    // Fills the pinch deform MPB buffers from the controller's reported contacts and returns the
    // active contact count (0 = no squeeze → shader section 3.5 is a no-op). The master compression
    // is SmoothDamped so the dents ease in on catch and out on release instead of popping. Each
    // active contact's world face normal + compression flatten the slime locally against that wall
    // in SlimeDeform.hlsl section 3.5 — genuine, per-contact, local; no whole-body scale.
    private int BuildPinchDeform()
    {
        float target = slimeController.PinchActive ? Mathf.Clamp01(slimeController.PinchCompression01) : 0f;
        pinchCompSmoothed = Mathf.SmoothDamp(pinchCompSmoothed, target, ref pinchCompVel, settings.pinchSquashSmoothTime);

        int reported = slimeController.PinchActive ? Mathf.Min(slimeController.PinchContactCount, PinchMax) : 0;
        int count = pinchCompSmoothed > 1e-3f ? reported : 0;
        // World half-gap from the smoothed compression: gap/2 = (1 − comp)·R. Derived from the same
        // smoothed comp the dent uses, so the plane-clamp eases in/out in lock-step (no pop), and R is
        // the slime's full (visual) radius — the mesh is full-size, the shader flattens it to this plane.
        float fullR = slimeSettings != null ? slimeSettings.slimeRadius : 0.4f;
        float halfGap = Mathf.Max(0f, (1f - pinchCompSmoothed) * fullR);
        for (int i = 0; i < PinchMax; i++)
        {
            if (i < count)
            {
                Vector2 n = slimeController.GetPinchContactNormal(i);
                pinchNormalBuf[i] = new Vector4(n.x, n.y, 0f, 0f);
                // All contacts currently share one compression (comp01), so the smoothed master is
                // the right per-contact value; revisit if walls ever report differing compression.
                pinchCompBuf[i] = pinchCompSmoothed;
                pinchHalfGapBuf[i] = halfGap;
            }
            else
            {
                pinchNormalBuf[i] = Vector4.zero;
                pinchCompBuf[i] = 0f;
                pinchHalfGapBuf[i] = 0f;
            }
        }
        return count;
    }

    // --- Gravity sag offset (C-5) ---

    private Vector2 ComputeGravitySagOffset()
    {
        if (slimeController.CurrentState == SlimeController.SlimeState.Airborne)
            return Vector2.zero;

        Vector2 tangent = new Vector2(-lastSurfaceNormal.y, lastSurfaceNormal.x);
        Vector2 gravity = slimeController.GravityVector;
        float sagStrength = Vector2.Dot(gravity, tangent) / Mathf.Abs(slimeController.Gravity);

        return tangent * sagStrength * settings.gravitySagDistance;
    }

    // --- Impact squash (C-2) ---

    private void DetectImpact()
    {
        SlimeController.SlimeState curState = slimeController.CurrentState;

        if (prevState == SlimeController.SlimeState.Airborne && curState != SlimeController.SlimeState.Airborne)
        {
            // Use normal-component speed, not total speed — a glancing hit shouldn't trigger full impact
            float normalSpeed = Mathf.Abs(Vector2.Dot(lastNonZeroVelocity, lastSurfaceNormal));
            float normalizedSpeed = Mathf.Clamp01(normalSpeed / settings.impactSpeedReference);

            if (normalizedSpeed > settings.impactMinThreshold)
            {
                impactTimer = -Time.deltaTime; // offset so first evaluation samples t=0 (peak)
                impactMagnitude = normalizedSpeed;
            }
        }
    }

    private Vector2 ComputeImpactOverlay()
    {
        if (impactTimer < 0f) return Vector2.zero;

        impactTimer += Time.deltaTime;

        if (impactTimer > settings.impactDuration)
        {
            impactTimer = -1f;
            return Vector2.zero;
        }

        float t = impactTimer / settings.impactDuration;
        float curve = Mathf.Exp(-settings.impactDecayRate * t)
                    * Mathf.Cos(settings.impactOscFreq * Mathf.PI * t);
        float squash = curve * impactMagnitude * settings.impactSquashIntensity;
        return new Vector2(squash, -squash);
    }

    // --- Friction detach pong (Phase 3) ---
    //
    // Subscribes to SlimeController.OnFrictionDetach. Phase 3 lives on the fast
    // (smoothed-bypass) scale layer so the release pop happens on the same frame
    // the controller transitions to Airborne instead of being eased through
    // SmoothDampV2 like the Phase 1 stress (which goes through the shader, not
    // body.localScale).
    private void HandleFrictionDetach(Vector2 detachVel, Vector2 sNormal, float overshoot)
    {
        pongTimer = 0f;
        pongIntensity = Mathf.Clamp01(overshoot / Mathf.Max(0.001f, settings.pongFullOvershoot));
    }

    private Vector2 ComputePongOverlay()
    {
        if (pongTimer < 0f) return Vector2.zero;
        pongTimer += Time.deltaTime;
        if (pongTimer >= settings.pongDuration)
        {
            pongTimer = -1f;
            pongIntensity = 0f;
            return Vector2.zero;
        }
        float t = pongTimer / Mathf.Max(0.0001f, settings.pongDuration);
        float curve = Mathf.Exp(-settings.pongDecay * t)
                    * Mathf.Cos(settings.pongOscFreq * Mathf.PI * t);
        float amt = curve * pongIntensity * settings.pongStretchMax;
        // Body local: X (tangent) slight pinch, Y (outward) bulge.
        return new Vector2(-amt * 0.4f, amt);
    }

    // --- Coyote squash (energy-conversion window) ---

    private Vector2 ComputeCoyoteSquash()
    {
        if (slimeSettings == null) return Vector2.zero;
        // Prefer the controller's active duration (matches the per-surface override
        // SurfaceMaterial may have applied at landing); fall back to the global
        // setting when nothing is currently armed.
        float armedDuration = slimeController.EnergyCoyoteDuration;
        float duration = armedDuration > 0f ? armedDuration : slimeSettings.energyConversionCoyoteTime;
        if (duration <= 0f)
        {
            coyoteRecoveryTimer = -1f;
            prevCoyoteTimer = 0f;
            return Vector2.zero;
        }

        float timer = slimeController.EnergyCoyoteTimer;
        float speedRef = Mathf.Max(0.01f, settings.coyoteSquashSpeedRef);
        float normalizedSpeed = Mathf.Clamp01(slimeController.ImpactNormalSpeed / speedRef);
        float intensity = settings.coyoteSquashSpeedCurve.Evaluate(normalizedSpeed);
        float magnitude = 0f;

        if (timer > 0f)
        {
            // Compression phase: peak reached when coyote window expires (progress = 1).
            coyoteRecoveryTimer = -1f;
            float progress = Mathf.Clamp01(1f - timer / duration);
            magnitude = settings.coyoteSquashCurve.Evaluate(progress) * intensity;
            coyoteRecoveryStartMagnitude = magnitude;
        }
        else
        {
            if (prevCoyoteTimer > 0f)
            {
                // Rising edge of recovery: coyote window just ended this frame.
                coyoteRecoveryTimer = 0f;
            }

            if (coyoteRecoveryTimer >= 0f)
            {
                float recoveryDuration = settings.coyoteSquashRecoveryTime;
                if (recoveryDuration <= 0f)
                {
                    coyoteRecoveryTimer = -1f;
                }
                else
                {
                    coyoteRecoveryTimer += Time.deltaTime;
                    float p = coyoteRecoveryTimer / recoveryDuration;
                    if (p >= 1f)
                    {
                        coyoteRecoveryTimer = -1f;
                    }
                    else
                    {
                        magnitude = settings.coyoteSquashRecoveryCurve.Evaluate(p) * coyoteRecoveryStartMagnitude;
                    }
                }
            }
        }

        prevCoyoteTimer = timer;
        return new Vector2(magnitude, -magnitude);
    }

    // --- Velocity stretch (C-4) ---

    private Vector2 ComputeVelocityStretch()
    {
        if (slimeController.CurrentState == SlimeController.SlimeState.Airborne)
        {
            // Decompose along body.up (current visual orientation), not GravityUp directly.
            // body.up slerps toward GravityUp; using it here avoids axis mismatch during transitions.
            Vector2 bodyUp = (Vector2)body.up;
            Vector2 gravityDir = -bodyUp;
            Vector2 horizontalDir = new Vector2(bodyUp.y, -bodyUp.x);

            float verticalSpeed = Mathf.Abs(Vector2.Dot(cachedVelocity, gravityDir));
            float horizontalSpeed = Mathf.Abs(Vector2.Dot(cachedVelocity, horizontalDir));

            float vNorm = Mathf.Clamp01(verticalSpeed / settings.velocityStretchSpeedRef);
            float hNorm = Mathf.Clamp01(horizontalSpeed / settings.velocityStretchSpeedRef);

            float vCurved = Mathf.Pow(vNorm, settings.velocityStretchCurvePower);
            float hCurved = Mathf.Pow(hNorm, settings.velocityStretchCurvePower);

            float vStretch = Mathf.Min(vCurved * settings.velocityStretchIntensityY, settings.velocityStretchMaxY);
            float hStretch = Mathf.Min(hCurved * settings.velocityStretchIntensityX, settings.velocityStretchMaxX);

            float damp = settings.velocityStretchCrossDamping;
            return new Vector2(hStretch - vStretch * damp, vStretch - hStretch * damp);
        }

        // Surface: body.up = surfaceNormal, velocity along tangent = local X → (+stretch, -stretch)
        float speed = cachedVelocity.magnitude;
        if (speed < 0.1f)
            return Vector2.zero;

        float normalizedSpeed = Mathf.Clamp01(speed / settings.velocityStretchSpeedRef);
        float stretch = Mathf.Min(normalizedSpeed * settings.velocityStretchIntensityX, settings.velocityStretchMaxX);
        return new Vector2(stretch, -stretch);
    }

    // --- Velocity lean offset (C-4) ---

    private Vector2 ComputeVelocityLeanOffset()
    {
        float speed = cachedVelocity.magnitude;
        if (speed < 0.1f)
            return Vector2.zero;

        float normalizedSpeed = Mathf.Clamp01(speed / settings.velocityStretchSpeedRef);
        Vector2 leanOffset = -cachedVelocity.normalized * normalizedSpeed * settings.velocityLeanDistance;
        return Vector2.ClampMagnitude(leanOffset, settings.velocityLeanMax);
    }

    // --- Charging curve ---

    private void UpdateChargingCurve()
    {
        if (slimeController.CurrentState != SlimeController.SlimeState.Charging
            || slimeController.ChargingProgress < 0.001f)
        {
            chargingCurve = 0f;
            return;
        }
        float speed = slimeController.ChargingLaunchSpeed;
        float range = settings.chargingSpeedMax - settings.chargingSpeedMin;
        float t = Mathf.Clamp01((speed - settings.chargingSpeedMin) / Mathf.Max(0.01f, range));
        chargingCurve = Mathf.Pow(t, settings.chargingCurvePower);
    }

    // --- Charging squash (slow layer) ---

    private Vector2 ComputeChargingSquash()
    {
        float x = chargingCurve;

        if (slimeController.CurrentState == SlimeController.SlimeState.Charging
            && slimeSettings != null && slimeSettings.chargeRadius > 0.0001f)
        {
            float excess = slimeController.ActiveChargeRadius / slimeSettings.chargeRadius - 1f;
            if (excess > 0f)
                x += excess;
        }

        float squash = settings.chargingSquashCurve.Evaluate(x);
        return new Vector2(squash, -squash);
    }

    // --- Charging tremor (fast layer) ---

    private Vector2 ComputeChargingTremor()
    {
        Vector2 result = Vector2.zero;

        if (chargingCurve >= 0.001f)
        {
            float speed = slimeController.ChargingLaunchSpeed;
            float range = settings.chargingSpeedMax - settings.chargingTremorThreshold;
            float t = Mathf.Clamp01((speed - settings.chargingTremorThreshold) / Mathf.Max(0.01f, range));
            float tremor = Mathf.Sin(Time.time * settings.chargingTremorFrequency * 2f * Mathf.PI)
                         * t * t
                         * settings.chargingTremorMax;
            result = new Vector2(tremor, -tremor);
        }

        if (slimeController.CurrentState == SlimeController.SlimeState.Charging
            && slimeController.IsAtChargeFullAim
            && settings.fullAimTremorAmplitude > 0f)
        {
            float fa = Mathf.Sin(Time.time * settings.fullAimTremorFrequency * 2f * Mathf.PI)
                     * settings.fullAimTremorAmplitude;
            result += new Vector2(fa, -fa);
        }

        return result;
    }

    // --- Charging offset (surface push — slow layer) ---

    private Vector2 ComputeChargingOffset()
    {
        Vector2 surfacePush = -lastSurfaceNormal * chargingCurve * settings.chargingPushMax;
        Vector2 aimPull = -slimeController.AimDirection * settings.bodyChargingAimPull;
        return surfacePush + aimPull;
    }

    // --- Breathing (C-6) ---

    private Vector2 ComputeBreathing()
    {
        breathPhase += Time.deltaTime * settings.breathFrequency;
        if (breathPhase > 1f) breathPhase -= 1f;
        float value = Mathf.Sin(breathPhase * 2f * Mathf.PI) * settings.breathAmplitude;
        return new Vector2(value, -value);
    }

    // --- Face offset (unchanged from C-1) ---

    private void UpdateFaceOffset()
    {
        Vector2 interest;
        if (slimeController.CurrentState == SlimeController.SlimeState.Charging)
        {
            interest = -slimeController.AimDirection * settings.faceChargingScale;
            lastInterest = interest;
        }
        else
        {
            Vector2 vel = cachedVelocity;
            if (vel.sqrMagnitude > settings.faceLingerThreshold)
                lastInterest = vel * settings.faceOffsetScale;
            else
                lastInterest = Vector2.Lerp(lastInterest, Vector2.zero,
                                            settings.faceLingerDecay * Time.deltaTime);
            interest = lastInterest;
        }

        Vector2 idleDrift = Vector2.zero;
        if (slimeController.CurrentState != SlimeController.SlimeState.Charging
            && cachedVelocity.sqrMagnitude <= settings.faceLingerThreshold)
        {
            float rawX = Mathf.Sin(Time.time * settings.faceDriftFreqX);
            float saccadeX = (float)System.Math.Tanh(rawX * settings.faceDriftSharpness);
            idleDrift = new Vector2(
                saccadeX * settings.faceDriftAmplitude,
                Mathf.Sin(Time.time * settings.faceDriftFreqY) * settings.faceDriftAmplitude * 0.8f
            );
        }

        Vector2 target = Vector2.ClampMagnitude(interest + idleDrift, settings.faceOffsetMax);
        currentFaceOffset = Vector2.SmoothDamp(
            currentFaceOffset, target, ref faceVelocity, settings.faceSmoothTime);

        // Face is a SIBLING of body (Slime/SlimeVisual/Face), so the body-scale multiply on
        // body.localScale never reaches it — scale it explicitly so the tuned face-to-body
        // proportion survives every size, and shrink the drift offset with it.
        Vector2 finalFacePos = (Vector2)body.localPosition + currentFaceOffset * displayedBodyScale;
        face.localPosition = new Vector3(finalFacePos.x, finalFacePos.y, face.localPosition.z);
        face.localRotation = Quaternion.identity;
        face.localScale = faceRestScale * displayedBodyScale;
    }

    // --- Blink (unchanged from C-1) ---

    private void UpdateBlink()
    {
        if (eyesRenderer == null || eyesClosed == null) return;

        if (slimeController.IsStunned)
        {
            if (eyesStun != null) eyesRenderer.sprite = eyesStun;
            // Keep deferring nextBlinkTime so blink doesn't fire the instant stun ends
            nextBlinkTime = Time.time + settings.blinkDuration
                          + Random.Range(settings.blinkIntervalMin, settings.blinkIntervalMax);
            return;
        }

        // External force override (BoxSleepAnimator etc.). Stun above still wins.
        // Defers nextBlinkTime so the natural blink doesn't fire the instant force releases.
        if (_eyesForceState != EyesForceState.None)
        {
            if (eyesRenderer.sprite == eyesStun) eyesRenderer.sprite = eyesOpen;
            eyesRenderer.sprite = _eyesForceState == EyesForceState.Closed ? eyesClosed : eyesOpen;
            nextBlinkTime = Time.time + settings.blinkDuration
                          + Random.Range(settings.blinkIntervalMin, settings.blinkIntervalMax);
            return;
        }

        // Recovery: stun branch leaves sprite on eyesStun; restore eyesOpen immediately
        // rather than waiting for the next blink cycle (would otherwise linger 3-7s).
        if (eyesRenderer.sprite == eyesStun)
            eyesRenderer.sprite = eyesOpen;

        float time = Time.time;
        if (time >= blinkEndTime && eyesRenderer.sprite == eyesClosed)
        {
            eyesRenderer.sprite = eyesOpen;
        }

        if (time >= nextBlinkTime)
        {
            eyesRenderer.sprite = eyesClosed;
            blinkEndTime = time + settings.blinkDuration;
            nextBlinkTime = time + settings.blinkDuration
                          + Random.Range(settings.blinkIntervalMin, settings.blinkIntervalMax);
        }
    }

    // --- Visual clearance ---
    // Limitation: ray from inside a deeply-penetrated collider won't detect it (Physics2D semantics).
    // If clamp fails at deep penetration, expand to 3-ray fan (see plan fallback #2).

    private float QueryClearance(Vector2 origin, Vector2 direction, float maxDist)
    {
        int count = Physics2D.RaycastNonAlloc(
            origin, direction, clearanceHits, maxDist, slimeSettings.groundLayer);
        float closest = maxDist;
        for (int i = 0; i < count; i++)
        {
            if (clearanceHits[i].collider == slimeCollider) continue;
            // Skip trigger colliders: the clearance clamp only guards the visual against
            // penetrating SOLID surfaces. One-way / passable triggers (TautLine ropes,
            // hazard zones, water, etc. — all isTrigger on the ground layer) are not solid;
            // letting them register collapses the body offset to ~0 the instant the slime
            // nears them, snapping the visual even though rb.position never moves. (Confirmed:
            // a rope trigger hit at distance 0 forced ratio→0 and jumped body.localPosition ~1m.)
            if (clearanceHits[i].collider.isTrigger) continue;
            if (clearanceHits[i].distance < closest)
                closest = clearanceHits[i].distance;
        }
        return closest;
    }

    // --- Utility ---

    private static Vector2 SmoothDampV2(Vector2 current, Vector2 target, ref Vector2 velocity, float smoothTime)
    {
        return new Vector2(
            Mathf.SmoothDamp(current.x, target.x, ref velocity.x, smoothTime),
            Mathf.SmoothDamp(current.y, target.y, ref velocity.y, smoothTime)
        );
    }
}
