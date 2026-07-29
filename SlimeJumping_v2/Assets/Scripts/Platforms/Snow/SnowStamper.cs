using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Platforms.Snow
{
    /// <summary>Forwards OnLanded impact speed to the landed surface's SnowField (via SnowFieldRegistry); depth/radius mapping stays in SnowField.</summary>
    /// Pure visual: never pushes back into the controller, never reads SnowField state.
    [DisallowMultipleComponent]
    [AddComponentMenu("SlimeJumping/Platforms/Snow/Snow Stamper")]
    public sealed class SnowStamper : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private SlimeController slimeController;

        [Header("Debug")]
        [SerializeField] private bool logLandings = false;

        void Reset()
        {
            slimeController = GetComponentInParent<SlimeController>();
        }

        void OnEnable()
        {
            if (slimeController != null) slimeController.OnLanded += HandleLanded;
        }

        void OnDisable()
        {
            if (slimeController != null) slimeController.OnLanded -= HandleLanded;
        }

        void HandleLanded(Vector2 contactPoint, Vector2 surfaceNormal)
        {
            if (slimeController == null) return;

            if (!SnowFieldRegistry.TryGet(slimeController.SurfaceCollider, out var field)) return;

            float impact = slimeController.ImpactNormalSpeed;
            if (logLandings)
                Debug.Log($"[Snow] Land: impact={impact:0.00} → {field.name}");

            field.HandleLanded(contactPoint, impact);
        }
    }
}
