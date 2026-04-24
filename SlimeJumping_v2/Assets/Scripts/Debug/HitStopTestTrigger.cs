using UnityEngine;
using SlimeJumping.Effects;

namespace SlimeJumping.Debugging
{
    /// Test helper: press the configured key in Play mode to invoke HitStopService.Freeze
    /// with the Inspector-tuned duration and frozen scale. Use this to preview the B&amp;W
    /// impact frame visual and tune defaults before wiring real call sites.
    ///
    /// Note: HitStopGuards still applies — if slime is charging or options panel is open,
    /// the press is a no-op. A Debug.Log reports each outcome so the blocked case is obvious.
    public class HitStopTestTrigger : MonoBehaviour
    {
        [SerializeField] private KeyCode triggerKey = KeyCode.G;

        [Header("Freeze Parameters")]
        [Tooltip("Realtime seconds of the hitstop. Anime-style impact: 0.04-0.15.")]
        [SerializeField, Range(0.01f, 1f)] private float duration = 0.1f;

        [Tooltip("Time.timeScale during the freeze. 0 = full stop, 0.05 = slow-mo feel.")]
        [SerializeField, Range(0f, 0.5f)] private float frozenScale = 0f;

        [Header("Optional Shake")]
        [SerializeField] private ImpulseEmitter shakeEmitter;
        [SerializeField, Range(0f, 3f)] private float shakeForce = 0f;

        private void Update()
        {
            if (!Input.GetKeyDown(triggerKey)) return;

            if (shakeEmitter != null && shakeForce > 0f)
                HitStopService.Freeze(duration, shakeEmitter, shakeForce, frozenScale);
            else
                HitStopService.Freeze(duration, frozenScale);

            Debug.Log($"[HitStopTestTrigger] Freeze requested: duration={duration:0.000}s, frozenScale={frozenScale:0.00}. " +
                      $"If no visual change, a guard blocked it (charging / options open) or ImpactFrameVisual/FullScreenPass feature is not wired.");
        }
    }
}
