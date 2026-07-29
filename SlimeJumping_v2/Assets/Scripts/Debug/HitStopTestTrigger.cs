using UnityEngine;
using SlimeJumping.Effects;

namespace SlimeJumping.Debugging
{
    /// Test helper: press the key in Play mode to invoke HitStopService, one mode per channel.
    /// HitStopGuards still applies — charging or an open options panel makes the press a no-op.
    public class HitStopTestTrigger : MonoBehaviour
    {
        public enum TriggerMode { HitStopOnly, ImpactFrameOnly, Both }

        [SerializeField] private KeyCode triggerKey = KeyCode.G;
        [SerializeField] private TriggerMode mode = TriggerMode.Both;

        [Header("Parameters")]
        [Tooltip("Realtime seconds. Anime-style impact: 0.04-0.15.")]
        [SerializeField, Range(0.01f, 1f)] private float duration = 0.1f;

        [Tooltip("Time.timeScale during hitstop. 0 = full stop, 0.05 = slow-mo feel. Ignored for ImpactFrameOnly mode.")]
        [SerializeField, Range(0f, 0.5f)] private float frozenScale = 0f;

        [Header("Optional Shake (hitstop modes only)")]
        [SerializeField] private ImpulseEmitter shakeEmitter;
        [SerializeField, Range(0f, 3f)] private float shakeForce = 0f;

        private void Update()
        {
            if (!Input.GetKeyDown(triggerKey)) return;

            switch (mode)
            {
                case TriggerMode.HitStopOnly:
                    if (shakeEmitter != null && shakeForce > 0f)
                        HitStopService.Freeze(duration, shakeEmitter, shakeForce, frozenScale);
                    else
                        HitStopService.Freeze(duration, frozenScale);
                    break;

                case TriggerMode.ImpactFrameOnly:
                    HitStopService.PlayImpactFrame(duration);
                    break;

                case TriggerMode.Both:
                default:
                    if (shakeEmitter != null && shakeForce > 0f) shakeEmitter.Emit(shakeForce);
                    HitStopService.ImpactHit(duration, frozenScale);
                    break;
            }

            Debug.Log($"[HitStopTestTrigger] mode={mode}, duration={duration:0.000}s, frozenScale={frozenScale:0.00}. " +
                      $"If no effect visible, a guard blocked it (charging / options open) or the visual/feature is not wired.");
        }
    }
}
