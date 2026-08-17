using UnityEngine;
using Unity.Cinemachine;
using SlimeJumping.Controller;
using SlimeJumping.InputHandling;
using SlimeJumping.Platforms.Bell;

namespace SlimeJumping.Camera
{
    /// <summary>Rings the bell, the camera leaves the slime to show a fixed region (the temple maze
    /// above) for a few seconds, then hands back. Same mechanism as RainWorldZone — VCam priority
    /// plus a Brain blend — but triggered by an event instead of the slime's position, since the
    /// revealed region is one the slime is not in.
    ///
    /// The region is authored as a box: resize the BoxCollider2D over what should be on screen and
    /// the VCam is pulled back until it fits. Timing and blends come from BellRevealCameraConfig,
    /// and the blend times are pushed onto the Brain only for the duration of a transition, so
    /// retuning this reveal cannot retime RainWorldZone's zone enter/exit. The same box doubles as the
    /// entry line: the revealed platforms hold until the slime rises into it, so their fade measures
    /// time spent in the region rather than the trip there.
    ///
    /// Everything here fails loudly: a missing ref, a strike that never reaches the camera, or a
    /// reveal that loses the priority arbitration each logs once rather than doing nothing.</summary>
    public class BellRevealCamera : MonoBehaviour
    {
        [Tooltip("Bell whose ring triggers the reveal. Silent strikes (LastRingResult not PLAYED) are ignored.")]
        [SerializeField] private SwingingBell bell;

        [Tooltip("Camera framing the region to reveal. Needs no Follow target — a static framing is the point.")]
        [SerializeField] private CinemachineCamera revealVCam;

        [Tooltip("Box drawn over the region to reveal. The VCam is pulled back to fit it. Left empty, the VCam's own transform is the shot.")]
        [SerializeField] private BoxCollider2D revealRegion;

        [Tooltip("Brain that arbitrates. Left empty, the first one in the scene is used.")]
        [SerializeField] private CinemachineBrain brain;

        [Tooltip("What the listening point follows while the camera is away — the slime, or its camera proxy. Empty = parked where the camera stood at the strike.")]
        [SerializeField] private Transform audioAnchor;

        [Tooltip("Read to tell whether the player is hands-off. Empty = found in the scene.")]
        [SerializeField] private SlimeInputReader inputReader;

        [Tooltip("ON: the reveal keeps holding past the hand-back until the slime crosses the entry line, so the fade is time spent inside the region rather than the trip there. Only this bell's platforms hold.")]
        [SerializeField] private bool freezeRevealUntilSlimeEnters = true;

        [Tooltip("Entry line as world Y above the region box's bottom edge — the yellow gizmo line. Negative starts the fade before the slime is level with the box.")]
        [SerializeField] private float slimeEntryYOffset;

        [Tooltip("Slime the entry line is tested against. Empty = found in the scene.")]
        [SerializeField] private SlimeController slime;

        [Tooltip("Optional per-instance config; empty = Resources/BellRevealCameraConfig.")]
        [SerializeField] private BellRevealCameraConfig configOverride;

        private BellRevealCameraConfig cfg;

        // Reveal runs in phases so the hold can be measured from arrival rather than from the strike.
        private enum Phase { Idle, BlendingIn, Holding, BlendingOut }
        private Phase phase = Phase.Idle;
        private float phaseTimer;

        private float liveCheckDeadline;
        private bool notLiveLogged;

        private bool brainBlendSnapshotted;
        private CinemachineBlendDefinition originalDefaultBlend;

        private bool inputTapped;
        private bool noInputReaderLogged;
        private bool wavePaused;
        private bool waitingForSlime;
        private bool noSlimeLogged;
        private BellSoundWave bellWave;

        private AudioListener cameraListener;
        private AudioListener revealListener;
        private Vector3 listenerParkedPos;
        private Vector3 listenerAnchorAtHandover;
        private bool audioHandedOver;
        private bool noListenerLogged;

        private void OnEnable()
        {
            if (bell == null || revealVCam == null)
            {
                Debug.LogError($"BellRevealCamera on '{name}': "
                             + $"{(bell == null ? "bell" : "revealVCam")} is not assigned — disabling.", this);
                enabled = false;
                return;
            }

            cfg = BellRevealCameraConfig.Resolve(configOverride);

            // Group is read lazily — BellSoundWave.Awake may not have run when this enables.
            bellWave = bell.GetComponentInChildren<BellSoundWave>(true);
            if (bellWave == null)
                Debug.LogError($"BellRevealCamera on '{name}': bell '{bell.name}' has no BellSoundWave — "
                             + "the reveal cannot hold its platforms while the camera is on the region.", this);

            if (brain == null) brain = FindAnyObjectByType<CinemachineBrain>();
            if (brain == null)
                Debug.LogError($"BellRevealCamera on '{name}': no CinemachineBrain in the scene — "
                             + "the reveal cannot be verified and probably will not show.", this);

            revealVCam.Priority = cfg.idlePriority;
            phase = Phase.Idle;
            FitVCamToRegion();
            bell.Struck += OnStruck;

            if (slime == null) slime = FindFirstObjectByType<SlimeController>();
            if (inputReader == null) inputReader = FindAnyObjectByType<SlimeInputReader>();
            if (inputReader != null)
            {
                inputReader.OnJumpPressed += OnPlayerTapped;
                inputReader.OnInteractPressed += OnPlayerTapped;
            }
        }

        private void OnPlayerTapped() => inputTapped = true;

        // Held state misses a tap that starts and ends inside one frame, hence the event flag.
        private bool PlayerIsHandsOff()
        {
            if (inputReader == null)
            {
                if (!noInputReaderLogged)
                {
                    noInputReaderLogged = true;
                    Debug.LogError($"BellRevealCamera on '{name}': no SlimeInputReader found — cannot tell "
                                 + "whether the player is idle, so the reveal leaves on its timer.", this);
                }
                return false;
            }
            return !inputTapped
                && !inputReader.IsJumpHeld
                && !inputReader.VerticalUpHeld
                && !inputReader.VerticalDownHeld
                && inputReader.LastInputMove.sqrMagnitude < 0.0001f;
        }

        // Addressed by bundle so a long hold here cannot freeze the platforms another bell revealed.
        private void SetWavePause(bool paused)
        {
            if (paused == wavePaused) return;
            wavePaused = paused;
            if (bellWave == null) return;
            BellSoundWave.PausedGroup = paused ? bellWave.Group : 0f;
        }

        // Sole owner of the pause flag, so the entry-line hold outlives the camera's own phases.
        private void UpdateWavePause()
        {
            if (cfg == null) return;
            if (waitingForSlime)
            {
                if (!SlimeReachedEntryLine()) { SetWavePause(true); return; }
                waitingForSlime = false;
            }
            SetWavePause(cfg.freezeRevealWhileShown
                         && (phase == Phase.BlendingIn || phase == Phase.Holding));
        }

        private bool SlimeReachedEntryLine()
        {
            if (slime == null)
            {
                if (!noSlimeLogged)
                {
                    noSlimeLogged = true;
                    Debug.LogError($"BellRevealCamera on '{name}': no SlimeController found — cannot tell when the "
                                 + "slime enters the region, so the reveal fades on the camera's timing instead.", this);
                }
                return true;
            }
            return slime.Position.y >= EntryLineY();
        }

        private float EntryLineY()
        {
            float baseY = revealRegion != null ? revealRegion.bounds.min.y : transform.position.y;
            return baseY + slimeEntryYOffset;
        }

        private void OnDisable()
        {
            if (bell != null) bell.Struck -= OnStruck;
            if (inputReader != null)
            {
                inputReader.OnJumpPressed -= OnPlayerTapped;
                inputReader.OnInteractPressed -= OnPlayerTapped;
            }
            if (revealVCam != null && cfg != null) revealVCam.Priority = cfg.idlePriority;
            RestoreBrainBlend();
            ReturnAudioToCamera();
            waitingForSlime = false;
            SetWavePause(false);
            phase = Phase.Idle;
        }

        // The listener rides the Main Camera, so a reveal would drag the ears 40 units off the bell
        // and every 3D sound would attenuate and pan with it. Hand over to a listener that stays put.
        private void TakeAudioFromCamera()
        {
            if (!cfg.keepAudioAtSlime || audioHandedOver) return;

            if (cameraListener == null)
            {
                UnityEngine.Camera output = brain != null ? brain.OutputCamera : UnityEngine.Camera.main;
                if (output != null) cameraListener = output.GetComponent<AudioListener>();
                if (cameraListener == null) cameraListener = FindAnyObjectByType<AudioListener>();
            }
            if (cameraListener == null)
            {
                if (!noListenerLogged)
                {
                    noListenerLogged = true;
                    Debug.LogError($"BellRevealCamera on '{name}': no AudioListener found — "
                                 + "3D sound will follow the camera away from the slime during the reveal.", this);
                }
                return;
            }

            if (revealListener == null)
            {
                var go = new GameObject("RevealAudioListener");
                go.transform.SetParent(transform, worldPositionStays: false);
                revealListener = go.AddComponent<AudioListener>();
            }

            listenerParkedPos = cameraListener.transform.position;
            listenerAnchorAtHandover = audioAnchor != null ? audioAnchor.position : listenerParkedPos;
            revealListener.transform.position = listenerParkedPos;

            // Disable first: two enabled listeners make Unity warn and pick one arbitrarily.
            cameraListener.enabled = false;
            revealListener.enabled = true;
            audioHandedOver = true;
        }

        private void ReturnAudioToCamera()
        {
            if (!audioHandedOver) return;
            if (revealListener != null) revealListener.enabled = false;
            if (cameraListener != null) cameraListener.enabled = true;
            audioHandedOver = false;
        }

        private void UpdateRevealListener()
        {
            if (!audioHandedOver || revealListener == null) return;
            revealListener.transform.position = audioAnchor != null
                ? audioAnchor.position + (listenerParkedPos - listenerAnchorAtHandover)
                : listenerParkedPos;
        }

        /// <summary>Frames revealRegion: VCam to the box center, pulled back until the box fits the
        /// screen (or ortho size grown to it). Re-run after moving or resizing the box.</summary>
        [ContextMenu("Fit VCam To Region")]
        public void FitVCamToRegion()
        {
            if (revealRegion == null || revealVCam == null) return;
            var c = cfg != null ? cfg : BellRevealCameraConfig.Resolve(configOverride);

            Bounds b = revealRegion.bounds;
            float halfW = b.extents.x + c.regionPadding;
            float halfH = b.extents.y + c.regionPadding;

            UnityEngine.Camera output = brain != null ? brain.OutputCamera : UnityEngine.Camera.main;
            float aspect = output != null ? output.aspect : 16f / 9f;

            var lens = revealVCam.Lens;
            if (lens.Orthographic)
            {
                lens.OrthographicSize = Mathf.Max(halfH, halfW / aspect);
                revealVCam.Lens = lens;
                revealVCam.transform.position = new Vector3(b.center.x, b.center.y, revealVCam.transform.position.z);
                return;
            }

            float tanHalfFov = Mathf.Tan(lens.FieldOfView * 0.5f * Mathf.Deg2Rad);
            float distance = Mathf.Max(halfH / tanHalfFov, halfW / (tanHalfFov * aspect));
            revealVCam.transform.position = new Vector3(b.center.x, b.center.y, b.center.z - distance);
        }

        private void OnStruck(float closingSpeed)
        {
            // Same gate BellSoundWave uses: Struck fires on every clapper hit, ringing or not.
            if (!bell.LastRingResult.StartsWith("PLAYED")) return;

            // Re-fit per strike so moving the region box takes effect without a restart.
            FitVCamToRegion();

            if (freezeRevealUntilSlimeEnters) waitingForSlime = true;

            bool wasIdle = phase == Phase.Idle;
            if (wasIdle || phase == Phase.BlendingOut)
            {
                TakeAudioFromCamera();
                PushBrainBlend(cfg.blendInSeconds);
                phase = Phase.BlendingIn;
                phaseTimer = cfg.blendInSeconds;
                revealVCam.Priority = cfg.activePriority;
                if (wasIdle) liveCheckDeadline = Time.time + Mathf.Max(0.5f, cfg.blendInSeconds + 0.25f);
            }
            else
            {
                // Already there or on the way: extend the stay instead of restarting the trip.
                phase = Phase.Holding;
                phaseTimer = cfg.holdSeconds;
            }
        }

        private void Update()
        {
            UpdateWavePause();
            if (phase == Phase.Idle) return;
            UpdateRevealListener();

            if (liveCheckDeadline > 0f && Time.time >= liveCheckDeadline)
            {
                liveCheckDeadline = 0f;
                if (brain != null && !brain.IsLiveChild(revealVCam) && !notLiveLogged)
                {
                    notLiveLogged = true;
                    Debug.LogError($"BellRevealCamera on '{name}': revealVCam '{revealVCam.name}' is still "
                                 + $"not live at priority {cfg.activePriority} — another VCam outranks it, or it "
                                 + "is on a channel the Brain does not render.", this);
                }
            }

            phaseTimer -= Time.deltaTime;

            // The hold outstays its timer while the player is hands-off, so a still player keeps
            // reading the maze; their first move is what sends the camera back.
            if (phase == Phase.Holding && phaseTimer <= 0f)
            {
                phaseTimer = 0f;
                if (cfg.holdUntilIdleInput && PlayerIsHandsOff()) return;
                LeaveHold();
                return;
            }

            if (phaseTimer > 0f) return;

            switch (phase)
            {
                case Phase.BlendingIn:
                    RestoreBrainBlend();
                    phase = Phase.Holding;
                    inputTapped = false;
                    // OFF means the hold was already running during the blend in, so it may be spent.
                    phaseTimer = cfg.holdStartsOnArrival
                        ? cfg.holdSeconds
                        : Mathf.Max(0f, cfg.holdSeconds - cfg.blendInSeconds);
                    break;

                case Phase.BlendingOut:
                    RestoreBrainBlend();
                    ReturnAudioToCamera();
                    phase = Phase.Idle;
                    break;
            }
        }

        private void LeaveHold()
        {
            PushBrainBlend(cfg.blendOutSeconds);
            phase = Phase.BlendingOut;
            phaseTimer = cfg.blendOutSeconds;
            revealVCam.Priority = cfg.idlePriority;
        }

        // Snapshot-and-restore, the same trick RainWorldZone uses for its hard cuts.
        private void PushBrainBlend(float seconds)
        {
            if (brain == null) return;
            if (!brainBlendSnapshotted)
            {
                originalDefaultBlend = brain.DefaultBlend;
                brainBlendSnapshotted = true;
            }
            var style = seconds <= 0f ? CinemachineBlendDefinition.Styles.Cut : cfg.blendStyle;
            brain.DefaultBlend = new CinemachineBlendDefinition(style, seconds);
        }

        private void RestoreBrainBlend()
        {
            if (brain == null || !brainBlendSnapshotted) return;
            brain.DefaultBlend = originalDefaultBlend;
            brainBlendSnapshotted = false;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            var c = cfg != null ? cfg : BellRevealCameraConfig.Resolve(configOverride);
            float halfWidth = 10f;
            float centerX = transform.position.x;
            float z = transform.position.z;

            if (revealRegion != null)
            {
                Bounds b = revealRegion.bounds;
                Vector3 size = new Vector3(b.size.x + c.regionPadding * 2f, b.size.y + c.regionPadding * 2f, 0.1f);
                Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.10f);
                Gizmos.DrawCube(b.center, size);
                Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.9f);
                Gizmos.DrawWireCube(b.center, size);
                halfWidth = b.extents.x + c.regionPadding;
                centerX = b.center.x;
                z = b.center.z;
            }

            if (!freezeRevealUntilSlimeEnters) return;

            // Crossing this line upward is what starts the revealed platforms fading.
            float y = EntryLineY();
            var left  = new Vector3(centerX - halfWidth, y, z);
            var right = new Vector3(centerX + halfWidth, y, z);
            Gizmos.color = new Color(1f, 0.85f, 0.25f, 0.95f);
            Gizmos.DrawLine(left, right);
            Gizmos.DrawLine(left, left + Vector3.up * 0.6f);
            Gizmos.DrawLine(right, right + Vector3.up * 0.6f);
            UnityEditor.Handles.color = Gizmos.color;
            UnityEditor.Handles.Label(new Vector3(centerX, y + 0.4f, z), $"entry Y {y:0.##}");
        }
#endif
    }
}
