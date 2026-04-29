using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Platforms.Snow
{
    /// <summary>
    /// One-way subscriber to SlimeController.OnLanded. Looks up the host platform's
    /// SnowField via SnowFieldRegistry (O(1)) and forwards the raw impact speed —
    /// depth/radius mapping happens inside SnowField using its SnowSettings asset, so
    /// every snow tuning knob lives in one place. Single component on the slime,
    /// mirrors the TrailStamper pattern but routes carve, not paint.
    ///
    /// Pure visual: never pushes back into the controller, never reads SnowField state.
    /// </summary>
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

            // Route only when the surface the slime actually attached to is a registered
            // SnowField host. Other surfaces (or the same surface with no SnowField on it)
            // get nothing.
            if (!SnowFieldRegistry.TryGet(slimeController.SurfaceCollider, out var field)) return;

            float impact = slimeController.ImpactNormalSpeed;
            if (logLandings)
                Debug.Log($"[Snow] Land: impact={impact:0.00} → {field.name}");

            field.HandleLanded(contactPoint, impact);
        }
    }
}
