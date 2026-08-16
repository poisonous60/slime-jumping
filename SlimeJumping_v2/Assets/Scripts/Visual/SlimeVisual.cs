using UnityEngine;
using Unity.Cinemachine;
using SlimeJumping.Controller;
using SlimeJumping.Settings;

public enum EyesForceState { None, Open, Closed }

public class SlimeVisual : MonoBehaviour
{
    [SerializeField] private SlimeController slimeController;
    [SerializeField] private SlimeVisualSettings settings;
    [SerializeField] private SlimeSettings slimeSettings;
    // Null-safe: without it the displayed body scale snaps to the physics ratio (no slosh).
    [SerializeField] private SlimeSplitSettings splitSettings;
    [SerializeField] private CircleCollider2D slimeCollider;
    [SerializeField] private Transform body;
    // Sibling root of the 3D mesh subtree (Slime/SlimeVisual3d), fed the same
    // localScale/localPosition writes as body. May be null if unwired.
    [SerializeField] private Transform body3d;
    [SerializeField] private Transform face;
    [SerializeField] private SpriteRenderer eyesRenderer;
    [SerializeField] private Sprite eyesOpen;
    [SerializeField] private Sprite eyesClosed;
    [SerializeField] private Sprite eyesStun;

    // Face tracking
    private UnityEngine.Camera faceCam;
    private bool faceCamMissingLogged;
    // Face XY before the camera correction. The brain's LateUpdate order against this one is
    // undefined, so the correction is re-applied from this base once the brain has moved the camera.
    private Vector2 faceBaseLocalXY;
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

    // Velocity-stretch contribution smoothed in parallel so it can be subtracted from
    // body3d's scale: the shader stretches along world velocity, which axis-aligned
    // localScale can't express (diagonal rugby ball). body keeps the full scale.
    private Vector2 smoothedVelStr;
    private Vector2 velStrSmoothVel;

    // Offset pipeline
    private Vector2 smoothedOffset;
    private Vector2 offsetVelocity;

    // Displayed cut ratio chases the physics BodyScaleRatio through an underdamped spring so
    // the body sloshes instead of popping. 1 = rest, multiplies bulk scale/offset uniformly.
    private float displayedBodyScale = 1f;
    private float bodyScaleSpringVel;
    private Vector3 faceRestScale = Vector3.one;

    // Read by SlimeSplitSequence to keep the cut-face clip glued to the sloshing size.
    public float DisplayedBodyScale => displayedBodyScale;

    // WORLD-space offset pushed by SlimeSplitSequence during CLOSE; the collider stays
    // centered, so the far side reads as flowing in. Sequence zeroes it when done.
    private Vector2 cutGatherWorld;
    public void SetCutGatherOffset(Vector2 worldOffset) => cutGatherWorld = worldOffset;

    // One-shot kick to the body-scale spring. Positive = outward overshoot.
    public void AddBodyScaleImpulse(float velocity) => bodyScaleSpringVel += velocity;

    // Impact (C-2)
    private float impactTimer = -1f;
    private float impactMagnitude;

    // Pong overlay — pongTimer < 0 = idle, 0 on OnFrictionDetach, back to -1 when finished.
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
    private MeshRenderer bodyMeshRenderer;
    private MeshFilter bodyMeshFilter;
    private MaterialPropertyBlock mpb;
    private static readonly int AsymmetryID = Shader.PropertyToID("_Asymmetry");
    private static readonly int ChargeDipID = Shader.PropertyToID("_ChargeDip");
    private static readonly int DipCenterUID = Shader.PropertyToID("_DipCenterU");
    // Feed SlimeDeform.hlsl; the WS suffix is the HLSL contract — both are world-space.
    private static readonly int SurfaceNormalWSID = Shader.PropertyToID("_SurfaceNormalWS");
    private static readonly int DipDirWSID = Shader.PropertyToID("_DipDirWS");
    // xyz = normalized world-space velocity, w = speed normalized to velocityStretchSpeedRef.
    private static readonly int VelocityDirWSID = Shader.PropertyToID("_VelocityDirWS");
    // Curve pre-evaluated CPU-side so the shader gets one scalar, no piecewise math in HLSL.
    private static readonly int VelocityStretchAmountID = Shader.PropertyToID("_VelocityStretchAmount");
    // Per-contact face normals + compression for SlimeDeform.hlsl 3.5. Fixed-length PinchMax:
    // must match the shader's PINCH_MAX and stay constant across SetVectorArray calls.
    private const int PinchMax = 3;
    private static readonly int PinchCountID = Shader.PropertyToID("_PinchCount");
    private static readonly int PinchNormalWSID = Shader.PropertyToID("_PinchNormalWS");
    private static readonly int PinchCompID = Shader.PropertyToID("_PinchComp");
    private static readonly int PinchDisplaceScaleID = Shader.PropertyToID("_PinchDisplaceScale");
    // World half-gap per contact; the shader plane-clamps every vertex past it back onto the plane.
    private static readonly int PinchHalfGapID = Shader.PropertyToID("_PinchHalfGap");
    // World gap-center the plane-clamp measures from — the true midpoint between the plates,
    // NOT the drifting mesh origin.
    private static readonly int PinchCenterWSID = Shader.PropertyToID("_PinchCenterWS");
    private readonly Vector4[] pinchNormalBuf = new Vector4[PinchMax];
    private readonly float[] pinchCompBuf = new float[PinchMax];
    private readonly float[] pinchHalfGapBuf = new float[PinchMax];
    private float smoothedSquashStrength;
    private float squashStrengthVel;

    // Rest transform captured in Start; per-frame "1.0 = rest" deform values multiply onto
    // these so the hand-tuned SlimeVisual3d transform in the Scene survives Play.
    private Vector3 body3dRestScale = Vector3.one;
    private Vector3 body3dRestPosition;
    private Quaternion body3dRestRotation = Quaternion.identity;
    // body3d's child, carrying the inverse surface-alignment rotation (composed R·S·R⁻¹).
    // Without it the whole body visibly spins 180° when the slime sticks to a ceiling.
    private Transform meshBaseline;
    private Quaternion meshBaselineRestRotation = Quaternion.identity;
    // Rest-only; reading live-deformed bounds/lossyScale here would create a per-frame
    // feedback loop with PushDeform.
    private float body3dNativeHeightRest;
    private float transformRestLossyScaleY = 1f;

    // Sit-pose depth shift bookkeeping. Assumes the Slime root's world Z is invariant during
    // the sit (root stays at Z=0, only visual Z moves).
    private bool sitDepthShiftActive;
    private float sitDepthOriginalVisualZ;
    private float sitDepthOriginalBody3dZ;

    // External overrides applied while IsExternallyAnimated. All cleared on the gate toggle
    // so stale state can't leak between sessions.
    private EyesForceState _eyesForceState = EyesForceState.None;
    private Quaternion? _externalRotationOverride;
    // DELTA from the inspector-set rest position. When set, replaces UpdateFaceOffset's
    // per-frame write entirely — drift/saccade pause.
    private Vector3? _externalFaceOffset;
    private Vector3 faceRestLocalPosition;

    // Additive scale overlay pushed in via SetExternalCompression; NOT gated by
    // IsExternallyAnimated.
    private Vector2 externalCompression;

    // External animation gate — body is externally driven; face keeps its own pipeline.
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
        CinemachineCore.CameraUpdatedEvent.RemoveListener(OnBrainCameraUpdated);
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
        // Captured in Start, before any LateUpdate, so UpdateFaceOffset's writes don't taint it.
        if (face != null)
        {
            faceRestLocalPosition = face.localPosition;
            faceRestScale = face.localScale;
            faceBaseLocalXY = faceRestLocalPosition;
            ResolveFaceCamera();
            CinemachineCore.CameraUpdatedEvent.AddListener(OnBrainCameraUpdated);
        }
        // Stays null if body3d isn't wired, in which case the mesh-side push short-circuits.
        if (body3d != null)
        {
            bodyMeshRenderer = body3d.GetComponentInChildren<MeshRenderer>();
            bodyMeshFilter = body3d.GetComponentInChildren<MeshFilter>();
            body3dRestScale = body3d.localScale;
            body3dRestPosition = body3d.localPosition;
            body3dRestRotation = body3d.localRotation;
            if (body3d.childCount > 0)
            {
                meshBaseline = body3d.GetChild(0);
                meshBaselineRestRotation = meshBaseline.localRotation;
            }
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
            // Face animation (drift + blink) keeps running so the slime feels alive while sitting.
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
        // Pinch is not a whole-body scale term — per-contact vertex deform on the GPU below.
        Vector2 targetScale = Vector2.one + surfSq + velStr + chargeSq;
        smoothedScale = SmoothDampV2(smoothedScale, targetScale, ref scaleVelocity, settings.deformSmoothTime);
        // Smoothed alone at the same rate so it can be subtracted from body3d's scale below.
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

        // Body scale (slime-cut): underdamped spring chasing the physics ratio, semi-implicit
        // Euler with dt clamped so an editor hitch can't blow it up.
        {
            float physScale = slimeController.BodyScaleRatio;
            if (splitSettings == null)
            {
                displayedBodyScale = physScale;
                bodyScaleSpringVel = 0f;
            }
            else
            {
                float sdt = Mathf.Min(Time.deltaTime, 0.05f * Time.timeScale);
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

            // Body size, not the base collider's radius — while cut that collider is one lobe of a
            // union and reads far smaller than the slime actually is.
            float colliderWorldRadius = slimeController != null
                ? slimeController.SlimeRadius
                : slimeCollider.radius * Mathf.Max(slimeCollider.transform.lossyScale.x,
                                                   slimeCollider.transform.lossyScale.y);

            // appliedScale.x = tangent-direction scale (body.localX ≈ tangent on surfaces)
            float scaleExt = Mathf.Max(0f, (appliedScale.x - 1f) * colliderWorldRadius);
            float desiredReach = offsetMag + scaleExt;

            // rb.position, not the interpolated transform.position — Physics2D queries in
            // physics-step space; an interpolated origin chatters the clamp on moving platforms.
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

        // Symmetric y-scale < 1 lifts the surface-touching edge off the ground; push toward the
        // surface to keep it planted. Must run AFTER the clearance clamp — on flat ground
        // clearance = collider radius, so maxReach = 0 and the clamp would zero this out.
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

        // Applied after the body-scale multiply so the magnitude is exactly what the sequence computed.
        if (cutGatherWorld.sqrMagnitude > 1e-10f)
            appliedOffset += (Vector2)transform.InverseTransformVector(cutGatherWorld);

        var bulkScale    = new Vector3(appliedScale.x, appliedScale.y, 1f);
        var bulkPosition = new Vector3(appliedOffset.x, appliedOffset.y, 0f);
        body.localScale    = bulkScale;
        body.localPosition = bulkPosition;
        if (body3d != null)
        {
            // bulkPosition is in SlimeVisual's local frame while body3d sits under the Slime root,
            // so convert by this transform's localScale. velStr is subtracted (same body-scale
            // ratio) — the shader stretches along world velocity; localScale can't do diagonal.
            var bulkScale3D = new Vector3(
                bulkScale.x - smoothedVelStr.x * displayedBodyScale,
                bulkScale.y - smoothedVelStr.y * displayedBodyScale,
                bulkScale.z * displayedBodyScale);
            // R·S·R⁻¹: squash still acts along the surface normal while the mesh keeps its world
            // orientation. Assumes SlimeVisual.transform.localRotation == identity (C-1 tenet).
            body3d.localRotation = body.localRotation * body3dRestRotation;
            if (meshBaseline != null)
                meshBaseline.localRotation = Quaternion.Inverse(body.localRotation) * meshBaselineRestRotation;
            body3d.localScale    = Vector3.Scale(body3dRestScale, bulkScale3D);
            // Depth shift lives on world Z; bulkPosition contributes only X/Y.
            body3d.localPosition = body3dRestPosition + Vector3.Scale(transform.localScale, bulkPosition);
        }

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

        // _DipDirWS reuses dipCenterU (= aimTangent * lateralShift) as lateral bias along the tangent.
        if (bodyMeshRenderer != null && mpb != null)
        {
            Vector3 surfaceNormalWS = new Vector3(lastSurfaceNormal.x, lastSurfaceNormal.y, 0f);
            // Dip lands on the outward face, away from the surface — same convention as the 2D shader.
            Vector3 dipDirWS = surfaceNormalWS;
            if (dipCenterU != 0f)
            {
                Vector2 tangent = new Vector2(-lastSurfaceNormal.y, lastSurfaceNormal.x);
                dipDirWS += new Vector3(tangent.x, tangent.y, 0f) * dipCenterU;
            }
            if (dipDirWS.sqrMagnitude > 1e-6f) dipDirWS.Normalize();

            // Reuses velocityStretchSpeedRef so this ramps on the same curve as the localScale stretch.
            float rawSpeed = cachedVelocity.magnitude;
            float speedNorm = Mathf.Clamp01(rawSpeed / Mathf.Max(0.01f, settings.velocityStretchSpeedRef));
            Vector2 velDir = cachedVelocity.sqrMagnitude > 0.01f ? cachedVelocity.normalized : Vector2.zero;
            Vector4 velocityDirWS = new Vector4(velDir.x, velDir.y, 0f, speedNorm);

            // speedMin/Max are curve-X anchors, NOT clamps — faster speeds sample extrapolated x,
            // so keys past x=1 keep growing; Unity's ClampForever takes over past the outer keys.
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

    // Native body height in SlimeVisual-local units, from rest-only values — reading live
    // deformed bounds would feed back into the per-frame PushDeform.
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

    // Shifts the visual + sibling body3d subtree to a target world Z so face sprites and mesh stay
    // depth-aligned. World Z, not localPosition.z, which SetExternalDeform rewrites every frame.
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

    // Lets an external controller pin the shader's surface normal (sit pose wants world-up).
    public void SetExternalSurfaceNormal(Vector2 normal)
    {
        if (normal.sqrMagnitude > 1e-6f)
            lastSurfaceNormal = normal.normalized;
    }

    // Force the eye sprite regardless of blink scheduler. Stun still wins; None releases.
    public void SetEyesForced(EyesForceState state)
    {
        _eyesForceState = state;
    }

    // Composed onto body3d's rest rotation in SetExternalDeform. Caller must clear it — a stale
    // override keeps body3d rotated after the gate closes (ResetDeformationState is the net).
    public void SetExternalRotation(Quaternion? rotation)
    {
        _externalRotationOverride = rotation;
    }

    // Delta from the face's rest position. While set it REPLACES UpdateFaceOffset's write —
    // natural drift/saccade pause. null clears.
    public void SetExternalFaceOffset(Vector3? offset)
    {
        _externalFaceOffset = offset;
    }

    // Added onto the composed body scale before clamping. Vector2.zero releases; caller smooths.
    public void SetExternalCompression(Vector2 extraScale)
    {
        externalCompression = extraScale;
    }

    // SlimeSplitSequence reads its child SpriteRenderers to clip the face along the wound planes.
    public Transform FaceTransform => face;

    // Attach points for decals/effects. Either can be null depending on visual setup.
    public Transform Body => body;
    public Transform Body3d => body3d;
    // SlimeSplitSequence pulls the world cut plane back through (live rotation × REST scale) so
    // the cut stays world-fixed in orientation but rides the live squash/stretch.
    public Vector3 Body3dRestScale => body3dRestScale;

    private void ApplyExternalFaceOffset()
    {
        if (!_externalFaceOffset.HasValue || face == null) return;
        Vector3 p = faceRestLocalPosition + _externalFaceOffset.Value;
        faceBaseLocalXY = new Vector2(p.x, p.y);
        face.localPosition = p;
        ApplyFaceCameraOffset();
    }

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
            // Override is composed on the LEFT so it acts as a world-space pre-rotation
            // (C-1 tenet: SlimeVisual.transform.localRotation == identity).
            body3d.localRotation = _externalRotationOverride.HasValue
                ? _externalRotationOverride.Value * body3dRestRotation
                : body3dRestRotation;
            // External animators want their rotation SEEN, so no counter-rotation under the gate.
            if (meshBaseline != null) meshBaseline.localRotation = meshBaselineRestRotation;
            body3d.localScale = Vector3.Scale(body3dRestScale, localScale);
            // Preserve localPosition.z so ApplySitDepthShift's world-Z shift isn't wiped each frame.
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
        // Surface normal tracks the last grounded value; no lateral aim under the external gate.
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
            // External animators have no meaningful velocity — zero so front-compress/stretch are inert.
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

        // Clear external overrides so a previous session (SitPose → BoxSleep) can't leak in.
        _eyesForceState = EyesForceState.None;
        _externalRotationOverride = null;
        _externalFaceOffset = null;
        // Only the external path writes face Z, so without this a shrine's z=0 sticks for good.
        if (face != null)
            face.localPosition = new Vector3(face.localPosition.x, face.localPosition.y, faceRestLocalPosition.z);

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
            if (meshBaseline != null) meshBaseline.localRotation = meshBaselineRestRotation;
        }
        // lastSurfaceNormal goes stale under the external gate; reseed from the controller's
        // live GravityUp (normal LateUpdate overwrites it next frame).
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

    // --- Deformation axis ---

    private void UpdateDeformAxis()
    {
        // Gravity flip: snap body.up and reset scale state, else the blob deforms along the
        // intermediate slerp angles.
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

        // Squash is (a, -a) about local Y, so the axis is a line, not a direction — take the sign
        // nearest body.up, else a floor->ceiling flip sweeps the stretch axis a visible 180.
        // lastSurfaceNormal keeps the signed normal for the shader.
        if (Vector2.Dot((Vector2)body.up, targetAxis) < 0f) targetAxis = -targetAxis;

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

    // Fills the pinch MPB buffers from the controller's contacts, returns the active count
    // (0 = shader 3.5 no-op). Compression is SmoothDamped so dents ease in/out instead of popping.
    private int BuildPinchDeform()
    {
        float target = slimeController.PinchActive ? Mathf.Clamp01(slimeController.PinchCompression01) : 0f;
        pinchCompSmoothed = Mathf.SmoothDamp(pinchCompSmoothed, target, ref pinchCompVel, settings.pinchSquashSmoothTime);

        int reported = slimeController.PinchActive ? Mathf.Min(slimeController.PinchContactCount, PinchMax) : 0;
        int count = pinchCompSmoothed > 1e-3f ? reported : 0;
        // gap/2 = (1 − comp)·R off the same smoothed comp the dent uses, so the plane-clamp eases
        // in lock-step (no pop); R is the slime's full visual radius.
        float fullR = slimeSettings != null ? slimeSettings.slimeRadius : 0.4f;
        float halfGap = Mathf.Max(0f, (1f - pinchCompSmoothed) * fullR);
        for (int i = 0; i < PinchMax; i++)
        {
            if (i < count)
            {
                Vector2 n = slimeController.GetPinchContactNormal(i);
                pinchNormalBuf[i] = new Vector4(n.x, n.y, 0f, 0f);
                // All contacts share one comp01 today; revisit if walls ever report differing values.
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
        // 분모는 baseline. 저중력존에서 sag 가 배율만큼 줄어드는 게 의도다.
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
    // On the fast (smoothed-bypass) scale layer so the release pop lands the same frame the
    // controller goes Airborne, instead of being eased through SmoothDampV2.
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
        // The controller's armed duration carries SurfaceMaterial's per-surface override;
        // fall back to the global setting when nothing is armed.
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
            // Decompose along body.up, not GravityUp — body.up slerps, so this avoids an axis
            // mismatch during transitions.
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

    // --- Face offset ---

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

        // Face is a SIBLING of body, so the body-scale multiply on body.localScale never reaches
        // it — scale it explicitly, and shrink the drift offset with it.
        faceBaseLocalXY = (Vector2)body.localPosition + currentFaceOffset * displayedBodyScale;
        ApplyFaceCameraOffset();
        face.localRotation = Quaternion.identity;
        face.localScale = faceRestScale * displayedBodyScale;
    }

    // --- Face camera correction (XY only; rotating shears the sprites 10:1 under lossyScale 0.2/0.2/2) ---

    private void ResolveFaceCamera()
    {
        var brain = FindAnyObjectByType<CinemachineBrain>();
        if (brain != null && brain.OutputCamera != null) faceCam = brain.OutputCamera;
        if (faceCam == null) faceCam = UnityEngine.Camera.main;
        if (faceCam == null && !faceCamMissingLogged)
        {
            faceCamMissingLogged = true;
            Debug.LogError("SlimeVisual: no CinemachineBrain output camera and no Camera.main — "
                         + "face camera correction is off, the face will drift off the body off-center.", this);
        }
    }

    private void OnBrainCameraUpdated(CinemachineBrain brain)
    {
        if (!isActiveAndEnabled || brain == null || brain.OutputCamera == null || face == null) return;
        faceCam = brain.OutputCamera;
        ApplyFaceCameraOffset();
    }

    private void ApplyFaceCameraOffset()
    {
        Vector2 p = faceBaseLocalXY + ComputeFaceCameraOffset();
        face.localPosition = new Vector3(p.x, p.y, face.localPosition.z);
    }

    private Vector2 ComputeFaceCameraOffset()
    {
        if (faceCam == null)
        {
            ResolveFaceCamera();
            if (faceCam == null) return Vector2.zero;
        }

        Vector3 camPos = faceCam.transform.position;
        float dz = transform.position.z - camPos.z;
        if (Mathf.Abs(dz) < 1e-3f) return Vector2.zero;

        // m33 runs 0 (perspective) to 1 (ortho), so CameraProjectionToggle's matrix lerp is the fade.
        float persp = 1f - Mathf.Clamp01(faceCam.projectionMatrix.m33);
        if (persp <= 0f) return Vector2.zero;

        Vector2 off = (Vector2)(transform.position - camPos);
        float depth = Mathf.Abs(face.localPosition.z * transform.lossyScale.z);

        // Back onto the camera-to-body ray, then leaned the same way so the slime looks where the camera went.
        Vector2 parallax = -off * (depth / dz);
        Vector2 gaze = Vector2.ClampMagnitude(-off / dz * settings.faceCameraGaze,
                                              settings.faceCameraGazeMax);
        return transform.InverseTransformVector((parallax + gaze) * persp);
    }

    // --- Blink ---

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

        // Defers nextBlinkTime so the natural blink doesn't fire the instant the force releases.
        if (_eyesForceState != EyesForceState.None)
        {
            if (eyesRenderer.sprite == eyesStun) eyesRenderer.sprite = eyesOpen;
            eyesRenderer.sprite = _eyesForceState == EyesForceState.Closed ? eyesClosed : eyesOpen;
            nextBlinkTime = Time.time + settings.blinkDuration
                          + Random.Range(settings.blinkIntervalMin, settings.blinkIntervalMax);
            return;
        }

        // Stun branch leaves the sprite on eyesStun; restore now or it lingers 3-7s to the next blink.
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
    // Limitation: a ray from inside a deeply-penetrated collider won't detect it (Physics2D).

    private float QueryClearance(Vector2 origin, Vector2 direction, float maxDist)
    {
        int count = Physics2D.RaycastNonAlloc(
            origin, direction, clearanceHits, maxDist, slimeSettings.groundLayer);
        float closest = maxDist;
        for (int i = 0; i < count; i++)
        {
            if (clearanceHits[i].collider == slimeCollider) continue;
            // Skip triggers: one-way/passable colliders on the ground layer (TautLine ropes,
            // hazards, water) are not solid — a rope trigger hits at distance 0, forcing ratio→0
            // and jumping body.localPosition ~1m while rb.position never moves.
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
