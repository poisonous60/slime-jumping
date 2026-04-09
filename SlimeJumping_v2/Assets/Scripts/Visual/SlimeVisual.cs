using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Settings;

public class SlimeVisual : MonoBehaviour
{
    [SerializeField] private SlimeController slimeController;
    [SerializeField] private SlimeVisualSettings settings;
    [SerializeField] private SlimeSettings slimeSettings;
    [SerializeField] private CircleCollider2D slimeCollider;
    [SerializeField] private Transform body;
    [SerializeField] private Transform face;
    [SerializeField] private SpriteRenderer eyesRenderer;
    [SerializeField] private Sprite eyesOpen;
    [SerializeField] private Sprite eyesClosed;

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

    // Scale pipeline
    private Vector2 smoothedScale = Vector2.one;
    private Vector2 scaleVelocity;

    // Offset pipeline
    private Vector2 smoothedOffset;
    private Vector2 offsetVelocity;

    // Impact (C-2)
    private float impactTimer = -1f;
    private float impactMagnitude;

    // Breathing (C-6)
    private float breathPhase;

    // Charging curve (0~1, computed per frame)
    private float chargingCurve;

    // Shader asymmetry
    private SpriteRenderer bodyRenderer;
    private MaterialPropertyBlock mpb;
    private static readonly int AsymmetryID = Shader.PropertyToID("_Asymmetry");
    private static readonly int ChargeDipID = Shader.PropertyToID("_ChargeDip");
    private static readonly int DipCenterUID = Shader.PropertyToID("_DipCenterU");
    private float smoothedSquashStrength;
    private float squashStrengthVel;

    // Visual clearance
    private readonly RaycastHit2D[] clearanceHits = new RaycastHit2D[8];

    private void Start()
    {
        smoothedScale = Vector2.one;
        lastDeformAxis = Vector2.up;
        lastSurfaceNormal = Vector2.up;
        prevState = slimeController.CurrentState;
        nextBlinkTime = Time.time + Random.Range(2f, 5f);
        mpb = new MaterialPropertyBlock();
        bodyRenderer = body.GetComponent<SpriteRenderer>();
    }

    private void LateUpdate()
    {
        CacheVelocity();
        UpdateDeformAxis();
        UpdateSquashStrength();
        DetectImpact();
        UpdateChargingCurve();

        // Scale: slow layer
        Vector2 surfSq = ComputeSurfaceSquash();
        Vector2 velStr = ComputeVelocityStretch();
        Vector2 chargeSq = ComputeChargingSquash();
        Vector2 targetScale = Vector2.one + surfSq + velStr + chargeSq;
        smoothedScale = SmoothDampV2(smoothedScale, targetScale, ref scaleVelocity, settings.deformSmoothTime);

        // Scale: fast layer
        Vector2 impact = ComputeImpactOverlay();
        Vector2 breath = ComputeBreathing();
        Vector2 chargeTremor = ComputeChargingTremor();
        Vector2 finalScale = smoothedScale + impact + breath + chargeTremor;
        finalScale.x = Mathf.Clamp(finalScale.x, settings.deformScaleMin, settings.deformScaleMax);
        finalScale.y = Mathf.Clamp(finalScale.y, settings.deformScaleMin, settings.deformScaleMax);

        // Offset
        Vector2 sag = ComputeGravitySagOffset();
        Vector2 lean = ComputeVelocityLeanOffset();
        Vector2 chargePush = ComputeChargingOffset();
        Vector2 targetOff = sag + lean + chargePush;
        smoothedOffset = SmoothDampV2(smoothedOffset, targetOff, ref offsetVelocity, settings.offsetSmoothTime);

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

            // Uses transform.position (interpolated) intentionally: visual-only check in LateUpdate
            float clearance = QueryClearance(
                (Vector2)transform.position, sagDir,
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

        body.localScale = new Vector3(appliedScale.x, appliedScale.y, 1f);
        body.localPosition = new Vector3(appliedOffset.x, appliedOffset.y, 0f);

        // Shader properties
        if (bodyRenderer != null)
        {
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

            bodyRenderer.GetPropertyBlock(mpb);
            mpb.SetFloat(AsymmetryID, asymmetry);
            mpb.SetFloat(ChargeDipID, chargeDip);
            mpb.SetFloat(DipCenterUID, dipCenterU);
            bodyRenderer.SetPropertyBlock(mpb);
        }

        UpdateFaceOffset();
        UpdateBlink();

        prevState = slimeController.CurrentState;
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
        Vector2 normal = slimeController.SurfaceNormal;
        if (slimeController.CurrentState != SlimeController.SlimeState.Airborne
            && normal.sqrMagnitude > 0.001f)
        {
            lastSurfaceNormal = normal;
        }

        Vector2 targetAxis;
        if (slimeController.CurrentState == SlimeController.SlimeState.Airborne)
        {
            // Axis-decomposed stretch uses world axes, so body.up must be world up
            targetAxis = Vector2.up;
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
            : Mathf.Clamp01(-Vector2.Dot(new Vector2(0f, slimeController.Gravity).normalized, lastSurfaceNormal));
        smoothedSquashStrength = Mathf.SmoothDamp(smoothedSquashStrength, target,
                                                  ref squashStrengthVel, settings.deformSmoothTime);
    }

    // --- Surface squash (C-5) ---

    private Vector2 ComputeSurfaceSquash()
    {
        float squash = smoothedSquashStrength * settings.surfaceSquashIntensity;
        return new Vector2(squash, -squash);
    }

    // --- Gravity sag offset (C-5) ---

    private Vector2 ComputeGravitySagOffset()
    {
        if (slimeController.CurrentState == SlimeController.SlimeState.Airborne)
            return Vector2.zero;

        Vector2 tangent = new Vector2(-lastSurfaceNormal.y, lastSurfaceNormal.x);
        Vector2 gravity = new Vector2(0f, slimeController.Gravity);
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

    // --- Velocity stretch (C-4) ---

    private Vector2 ComputeVelocityStretch()
    {
        if (slimeController.CurrentState == SlimeController.SlimeState.Airborne)
        {
            Vector2 gravityDir = new Vector2(0f, slimeController.Gravity).normalized;
            Vector2 horizontalDir = new Vector2(-gravityDir.y, gravityDir.x);

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
        float squash = chargingCurve * settings.chargingSquashMax;
        return new Vector2(squash, -squash);
    }

    // --- Charging tremor (fast layer) ---

    private Vector2 ComputeChargingTremor()
    {
        if (chargingCurve < 0.001f)
            return Vector2.zero;

        float speed = slimeController.ChargingLaunchSpeed;
        float range = settings.chargingSpeedMax - settings.chargingTremorThreshold;
        float t = Mathf.Clamp01((speed - settings.chargingTremorThreshold) / Mathf.Max(0.01f, range));
        float tremor = Mathf.Sin(Time.time * settings.chargingTremorFrequency * 2f * Mathf.PI)
                     * t * t
                     * settings.chargingTremorMax;

        return new Vector2(tremor, -tremor);
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

        Vector2 finalFacePos = (Vector2)body.localPosition + currentFaceOffset;
        face.localPosition = new Vector3(finalFacePos.x, finalFacePos.y, face.localPosition.z);
        face.localRotation = Quaternion.identity;
    }

    // --- Blink (unchanged from C-1) ---

    private void UpdateBlink()
    {
        if (eyesRenderer == null || eyesClosed == null) return;

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
