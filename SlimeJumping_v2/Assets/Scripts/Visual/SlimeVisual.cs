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

    private Vector2 lastSurfaceNormal = Vector2.up;
    private Vector2 currentFaceOffset;
    private Vector2 faceVelocity;
    private Vector2 lastInterest;
    private float nextBlinkTime;
    private float blinkEndTime;

    private void Start()
    {
        nextBlinkTime = Time.time + Random.Range(2f, 5f);
    }

    private void LateUpdate()
    {
        UpdateBodyRotation();
        UpdateFaceOffset();
        UpdateBlink();
    }

    private void UpdateBodyRotation()
    {
        Vector2 normal = slimeController.SurfaceNormal;
        if (slimeController.CurrentState != SlimeController.SlimeState.Airborne
            && normal.sqrMagnitude > 0.001f)
        {
            lastSurfaceNormal = normal;
        }

        body.up = Vector3.Slerp(body.up, lastSurfaceNormal,
                                settings.visualSlerpSpeed * Time.deltaTime);
    }

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
            Vector2 vel = slimeController.Velocity;
            if (vel.sqrMagnitude > settings.faceLingerThreshold)
                lastInterest = vel * settings.faceOffsetScale;
            else
                lastInterest = Vector2.Lerp(lastInterest, Vector2.zero,
                                            settings.faceLingerDecay * Time.deltaTime);
            interest = lastInterest;
        }

        // Idle drift: only when velocity is near zero and not charging
        Vector2 idleDrift = Vector2.zero;
        if (slimeController.CurrentState != SlimeController.SlimeState.Charging
            && slimeController.Velocity.sqrMagnitude <= settings.faceLingerThreshold)
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
}
