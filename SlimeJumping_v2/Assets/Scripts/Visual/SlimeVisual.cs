using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Settings;

public class SlimeVisual : MonoBehaviour
{
    [SerializeField] private SlimeController slimeController;
    [SerializeField] private SlimeSettings settings;
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

    // Shader asymmetry
    private SpriteRenderer bodyRenderer;
    private MaterialPropertyBlock mpb;
    private static readonly int AsymmetryID = Shader.PropertyToID("_Asymmetry");
    private float smoothedSquashStrength;
    private float squashStrengthVel;

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

        // Scale: slow layer
        Vector2 surfSq = ComputeSurfaceSquash();
        Vector2 velStr = ComputeVelocityStretch();
        Vector2 targetScale = Vector2.one + surfSq + velStr;
        smoothedScale = SmoothDampV2(smoothedScale, targetScale, ref scaleVelocity, settings.deformSmoothTime);

        // Scale: fast layer
        Vector2 impact = ComputeImpactOverlay();
        Vector2 breath = ComputeBreathing();
        Vector2 finalScale = smoothedScale + impact + breath;
        finalScale.x = Mathf.Clamp(finalScale.x, settings.deformScaleMin, settings.deformScaleMax);
        finalScale.y = Mathf.Clamp(finalScale.y, settings.deformScaleMin, settings.deformScaleMax);

        // Offset
        Vector2 sag = ComputeGravitySagOffset();
        Vector2 lean = ComputeVelocityLeanOffset();
        Vector2 targetOff = sag + lean;
        smoothedOffset = SmoothDampV2(smoothedOffset, targetOff, ref offsetVelocity, settings.offsetSmoothTime);

        // Apply
        body.localScale = new Vector3(finalScale.x, finalScale.y, 1f);
        body.localPosition = new Vector3(smoothedOffset.x, smoothedOffset.y, 0f);

        // Shader asymmetry (same SmoothDamp curve as scale — no desync)
        if (bodyRenderer != null)
        {
            float asymmetry = -smoothedSquashStrength * settings.surfaceSquashAsymmetry;
            bodyRenderer.GetPropertyBlock(mpb);
            mpb.SetFloat(AsymmetryID, asymmetry);
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
            Vector2 vel = cachedVelocity;
            if (vel.sqrMagnitude > settings.velocityAxisThreshold * settings.velocityAxisThreshold)
                targetAxis = vel.normalized;
            else
                targetAxis = lastDeformAxis;
        }
        else
        {
            targetAxis = lastSurfaceNormal;
        }

        // Airborne only: prevent >90-degree Slerp. Scale along +axis and -axis produce
        // the same ellipse on a symmetric circle, so take the shorter rotation path.
        // On surfaces, direction matters for shader asymmetry (which side is "bottom"),
        // so we must follow the exact surfaceNormal even if it's a large rotation.
        if (slimeController.CurrentState == SlimeController.SlimeState.Airborne
            && Vector2.Dot(targetAxis, (Vector2)body.up) < 0f)
            targetAxis = -targetAxis;

        lastDeformAxis = targetAxis;
        body.up = Vector3.Slerp(body.up, (Vector3)targetAxis,
                                settings.deformAxisSlerpSpeed * Time.deltaTime);
    }

    // --- Squash strength (shared by scale + shader asymmetry, SmoothDamp-ed) ---

    private void UpdateSquashStrength()
    {
        float target = (slimeController.CurrentState == SlimeController.SlimeState.Airborne)
            ? 0f
            : Mathf.Clamp01(-Vector2.Dot(new Vector2(0f, settings.gravity).normalized, lastSurfaceNormal));
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
        Vector2 gravity = new Vector2(0f, settings.gravity);
        float sagStrength = Vector2.Dot(gravity, tangent) / Mathf.Abs(settings.gravity);

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
        float speed = cachedVelocity.magnitude;
        if (speed < 0.1f)
            return Vector2.zero;

        float normalizedSpeed = Mathf.Clamp01(speed / settings.velocityStretchSpeedRef);
        float stretch = Mathf.Min(normalizedSpeed * settings.velocityStretchIntensity, settings.velocityStretchMax);

        // On surface: body.up = surfaceNormal, velocity along tangent = local X → (+stretch, -stretch)
        // Airborne: body.up = velocity direction, velocity along local Y → (-stretch, +stretch)
        if (slimeController.CurrentState == SlimeController.SlimeState.Airborne)
            return new Vector2(-stretch, stretch);
        else
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
            interest = slimeController.AimDirection
                     * slimeController.ChargingProgress
                     * settings.faceChargingScale;
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

        face.localPosition = (Vector3)currentFaceOffset;
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

    // --- Utility ---

    private static Vector2 SmoothDampV2(Vector2 current, Vector2 target, ref Vector2 velocity, float smoothTime)
    {
        return new Vector2(
            Mathf.SmoothDamp(current.x, target.x, ref velocity.x, smoothTime),
            Mathf.SmoothDamp(current.y, target.y, ref velocity.y, smoothTime)
        );
    }
}
