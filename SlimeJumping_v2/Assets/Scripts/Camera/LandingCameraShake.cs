using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Effects;

namespace SlimeJumping.Camera
{
    [DisallowMultipleComponent]
    public class LandingCameraShake : MonoBehaviour
    {
        [SerializeField] private SlimeController slimeController;
        [SerializeField] private ImpulseEmitter impulseEmitter;

        [Header("Impact Velocity Range (surface-perpendicular)")]
        [SerializeField] private float minImpactSpeed = 10f;
        [SerializeField] private float maxImpactSpeed = 40f;

        [Header("Shake Intensity")]
        [Tooltip("X: normalized impact speed (0 = min, 1 = max). Y: impulse force multiplier.")]
        [SerializeField]
        private AnimationCurve intensityCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        private void OnEnable()
        {
            if (slimeController != null)
                slimeController.OnLanded += HandleLanded;
        }

        private void OnDisable()
        {
            if (slimeController != null)
                slimeController.OnLanded -= HandleLanded;
        }

        private void HandleLanded(Vector2 contactPoint, Vector2 surfaceNormal)
        {
            if (impulseEmitter == null || slimeController == null) return;

            float speed = slimeController.ImpactNormalSpeed;
            if (speed < minImpactSpeed) return;

            float t = Mathf.InverseLerp(minImpactSpeed, maxImpactSpeed, speed);
            float intensity = intensityCurve.Evaluate(t);
            if (intensity <= 0f) return;

            impulseEmitter.Emit(intensity);
        }
    }
}
