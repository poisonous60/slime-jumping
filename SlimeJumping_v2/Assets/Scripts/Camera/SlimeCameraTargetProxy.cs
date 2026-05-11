using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Camera
{
    /// <summary>
    /// Follow-target proxy for Cinemachine. Position = low-pass filtered slime position
    /// during normal motion, but snaps instantly on intentional bursts (Launch). Set the
    /// CinemachineCamera's Follow target to this object's transform instead of the slime
    /// directly.
    ///
    /// Why: with the camera following slime directly and Cinemachine Damping=0, frame-
    /// to-frame multi-probe normal jitter on Ice curves transfers into visible camera
    /// shake. Adding Cinemachine Damping smooths it but the slime feels "left behind"
    /// when launching (intentional bursts get the same lag as jitter).
    ///
    /// This proxy decouples the two:
    ///   - Continuous motion (slide / walk / fall): low-pass at smoothRateHz, slime
    ///     micro-jitter is averaged out, lag ≈ 1 / (2π × rate). At 30 Hz that's ~5 ms,
    ///     well under one fixed step — visually imperceptible.
    ///   - Launch: SlimeController fires OnLaunch, proxy snaps to slime in the next
    ///     LateUpdate. Camera follows the burst with zero lag, exactly as if
    ///     Cinemachine Damping = 0.
    ///
    /// Setup:
    ///   1. Create empty GameObject "SlimeCameraTarget" in the scene (anywhere).
    ///   2. Add this component, drag the Slime into the slime field.
    ///   3. On the SlimeVCam, set Tracking Target / Follow to "SlimeCameraTarget".
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

            // Catch large unannounced jumps (teleport / external force) — keeps
            // camera from spending several frames lerping across the level.
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
