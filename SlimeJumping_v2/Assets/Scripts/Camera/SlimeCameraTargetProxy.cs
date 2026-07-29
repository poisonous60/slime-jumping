using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Camera
{
    /// <summary>
    /// Cinemachine follow target: low-pass filtered slime position, snapped on Launch.
    /// Cinemachine Damping alone was rejected — it hides probe-normal jitter but makes
    /// launches feel left behind. Point the vcam's Follow at this transform, not the slime.
    /// </summary>
    [DefaultExecutionOrder(1500)]
    public class SlimeCameraTargetProxy : MonoBehaviour
    {
        [SerializeField] private SlimeController slime;
        [Tooltip("Low-pass cutoff in Hz for continuous motion. Higher = less smoothing, more responsive.\n" +
                 "20 Hz: noticeable jitter masking, slight visible lag on slow turns.\n" +
                 "30 Hz: balanced (recommended).\n" +
                 "50+ Hz: minimal smoothing, mostly passthrough.")]
        [SerializeField] [Min(0f)] private float smoothRateHz = 30f;
        public float SmoothRateHz { get => smoothRateHz; set => smoothRateHz = Mathf.Max(0f, value); }

        [Tooltip("Snap instantly on Launch (player-intentional burst). Recommended ON.")]
        [SerializeField] private bool snapOnLaunch = true;
        [Tooltip("Snap if slime moves more than this in one LateUpdate (catches teleports / external forces / stun knockback that don't fire OnLaunch). 0 = disabled.")]
        [SerializeField] [Min(0f)] private float snapDistanceThreshold = 2f;

        private Vector3 smoothedPos;
        private bool initialized;
        private bool snapPending = true;

        private void OnEnable()
        {
            if (slime != null)
                slime.OnLaunch += HandleLaunch;
        }

        private void OnDisable()
        {
            if (slime != null)
                slime.OnLaunch -= HandleLaunch;
        }

        private void HandleLaunch(Vector2 pos, Vector2 vel, bool wasHop)
        {
            if (snapOnLaunch) snapPending = true;
        }

        public void RequestSnap() { snapPending = true; }

        private void LateUpdate()
        {
            if (slime == null) return;

            Vector3 slimePos = slime.transform.position;
            slimePos.z = transform.position.z;

            if (!initialized)
            {
                smoothedPos = slimePos;
                initialized = true;
                snapPending = false;
                transform.position = smoothedPos;
                return;
            }

            // Teleport / external force fires no event — snap instead of lerping across the level.
            if (snapDistanceThreshold > 0f
                && (slimePos - smoothedPos).sqrMagnitude > snapDistanceThreshold * snapDistanceThreshold)
            {
                snapPending = true;
            }

            if (snapPending || smoothRateHz <= 0.001f)
            {
                smoothedPos = slimePos;
                snapPending = false;
            }
            else
            {
                float k = 1f - Mathf.Exp(-smoothRateHz * Time.deltaTime);
                smoothedPos = Vector3.Lerp(smoothedPos, slimePos, k);
            }

            transform.position = smoothedPos;
        }
    }
}
