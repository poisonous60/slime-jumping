using System.Collections;
using UnityEngine;
using Unity.Cinemachine;
using SlimeJumping.Audio;
using SlimeJumping.Background;
using SlimeJumping.Controller;
using SlimeJumping.Interfaces;
using SlimeJumping.UI;

namespace SlimeJumping.Interaction
{
    /// On interact: the camera leaves the slime for the background planet (VCam priority plus a
    /// Brain blend, the BellRevealCamera mechanism), the planet transitions to its luminous phase,
    /// then a wisp appears at wispAnchor (on the planet's rim) and the reverse iris transition
    /// closes on it into EndScene. Takes slime override so movement is frozen for the whole
    /// sequence; the camera never comes back — the scene ends first.
    public class ShrineInteractable : InteractableBase, IExternalController
    {
        [Header("Wisp")]
        [SerializeField] private GameObject wispPrefab;
        [SerializeField] private Transform wispAnchor;
        [Tooltip("Uniform scale applied to the spawned wisp root. Lets you tune size per scene without editing the prefab.")]
        [SerializeField] private float wispScale = 1f;

        [Header("Planet Reveal")]
        [Tooltip("Camera framing the planet. Its priority is raised for the rest of the sequence.")]
        [SerializeField] private CinemachineCamera planetVCam;
        [Tooltip("Box drawn over the region to reveal. The VCam is pulled back to fit it. Left empty, the VCam's own transform is the shot.")]
        [SerializeField] private BoxCollider2D revealRegion;
        [Tooltip("World margin added around the region box so its edges are not flush with the screen.")]
        [SerializeField] private float regionPadding = 1f;
        [Tooltip("Brain whose default blend carries the move. Empty = first one in the scene.")]
        [SerializeField] private CinemachineBrain brain;
        [Tooltip("Seconds for the camera to travel from the slime to the planet.")]
        [SerializeField] private float cameraBlendSeconds = 2.5f;
        [Tooltip("Extra seconds parked on the planet after the blend, before the wisp may appear.")]
        [SerializeField] private float arriveHold = 0.5f;
        [Tooltip("Planet look transition played once the camera has arrived.")]
        [SerializeField] private PlanetPhaseTransition planetTransition;
        [Tooltip("Priority while revealing. Must beat every other VCam (RainWorldZone's active room uses 100).")]
        [SerializeField] private int revealPriority = 200;

        [Header("Timing")]
        [Tooltip("Seconds the wisp is visible before the reverse transition starts.")]
        [SerializeField] private float appearHold = 1.2f;

        [Header("Target Scene")]
        [SerializeField] private string endSceneName = "EndScene";

        [Header("Face")]
        [Tooltip("Face offset magnitude (local units) toward the wisp. 0 disables.")]
        [SerializeField] private float faceLookOffset = 0.12f;
        [Tooltip("SmoothDamp time for the face easing toward the target offset. Higher = slower, lazier glance.")]
        [SerializeField] private float faceLookSmoothTime = 0.25f;

        [Header("Audio")]
        [SerializeField] private SlimeJumping.Audio.SoundDef wispAppearSound;

        private bool isFiring;

        protected override void OnInteract(SlimeController slime)
        {
            if (isFiring) return;
            isFiring = true;
            IsActive = false;

            slime.SetOverride(this);
            StartCoroutine(Sequence(slime));
        }

        private IEnumerator Sequence(SlimeController slime)
        {
            Vector3 spawnPos = wispAnchor != null ? wispAnchor.position : transform.position;

            // Face look-toward-anchor (damped). Visible while the camera is still leaving the slime.
            if (faceLookOffset > 0f && wispAnchor != null)
            {
                var visual = slime.GetComponentInChildren<SlimeVisual>();
                if (visual != null)
                    StartCoroutine(FaceTrackRoutine(visual, slime.transform, wispAnchor));
            }

            // 행성 전환은 카메라 이동과 동시에 시작 — 이동 중에도 행성이 변해 간다.
            BeginPlanetReveal();
            float wait = planetVCam != null ? cameraBlendSeconds + arriveHold : 0f;
            if (planetTransition != null)
            {
                planetTransition.Play();
                wait = Mathf.Max(wait, planetTransition.Duration);
            }
            if (wait > 0f)
                yield return new WaitForSeconds(wait);

            GameObject wispInstance = null;
            if (wispPrefab != null)
            {
                wispInstance = Instantiate(wispPrefab, spawnPos, Quaternion.identity);
                if (wispScale > 0f && !Mathf.Approximately(wispScale, 1f))
                    wispInstance.transform.localScale = Vector3.one * wispScale;
            }

            // 리스너는 카메라를 타고 행성까지 가 있다 — 3D 재생은 거리감쇠로 묻힌다.
            SoundService.Play2D(wispAppearSound);

            if (appearHold > 0f)
                yield return new WaitForSeconds(appearHold);

            // 엔딩 진입 = 진행 종료. 세이브를 남기면 Load 가 신전 앞으로 복원한다.
            LoadSaveManager.DeleteSave();

            Vector3 transitionCenter = wispInstance != null ? wispInstance.transform.position : spawnPos;
            SceneTransitionController.PlayReverse(endSceneName, transitionCenter);
        }

        // 복귀가 없는 편도 이동이라 Brain 블렌드 스냅샷 복원이 필요 없다 — 씬이 먼저 끝난다.
        private void BeginPlanetReveal()
        {
            if (planetVCam == null) return;
            if (brain == null) brain = FindAnyObjectByType<CinemachineBrain>();
            FitVCamToRegion();
            if (brain != null)
                brain.DefaultBlend = new CinemachineBlendDefinition(
                    CinemachineBlendDefinition.Styles.EaseInOut, cameraBlendSeconds);
            planetVCam.Priority = revealPriority;
        }

        /// Frames revealRegion: VCam to the box center, pulled back until the box fits the screen
        /// (or ortho size grown to it). Same fit as BellRevealCamera. Re-run after moving the box.
        [ContextMenu("Fit VCam To Region")]
        public void FitVCamToRegion()
        {
            if (revealRegion == null || planetVCam == null) return;

            Bounds b = revealRegion.bounds;
            float halfW = b.extents.x + regionPadding;
            float halfH = b.extents.y + regionPadding;

            UnityEngine.Camera output = brain != null ? brain.OutputCamera : UnityEngine.Camera.main;
            float aspect = output != null ? output.aspect : 16f / 9f;

            var lens = planetVCam.Lens;
            if (lens.Orthographic)
            {
                lens.OrthographicSize = Mathf.Max(halfH, halfW / aspect);
                planetVCam.Lens = lens;
                planetVCam.transform.position = new Vector3(b.center.x, b.center.y, planetVCam.transform.position.z);
                return;
            }

            float tanHalfFov = Mathf.Tan(lens.FieldOfView * 0.5f * Mathf.Deg2Rad);
            float distance = Mathf.Max(halfH / tanHalfFov, halfW / (tanHalfFov * aspect));
            planetVCam.transform.position = new Vector3(b.center.x, b.center.y, b.center.z - distance);
        }

        private IEnumerator FaceTrackRoutine(SlimeVisual visual, Transform slimeTr, Transform target)
        {
            Vector3 current = Vector3.zero;
            Vector3 velocity = Vector3.zero;
            visual.SetExternalFaceOffset(current);
            while (visual != null && slimeTr != null && target != null)
            {
                Vector3 worldDir = target.position - slimeTr.position;
                worldDir.z = 0f;
                Vector3 desired = Vector3.zero;
                if (worldDir.sqrMagnitude > 0.0001f)
                {
                    Vector3 localDir = visual.transform.InverseTransformDirection(worldDir.normalized);
                    desired = new Vector3(localDir.x, localDir.y, 0f) * faceLookOffset;
                }
                current = Vector3.SmoothDamp(current, desired, ref velocity, faceLookSmoothTime, Mathf.Infinity, Time.deltaTime);
                visual.SetExternalFaceOffset(current);
                yield return null;
            }
        }

        // IExternalController: freeze slime until scene unloads.
        public void OnTakeControl(SlimeController slime)
        {
            slime.OverrideVelocity(Vector2.zero);
        }

        public void OnReleaseControl(SlimeController slime) { }

        public void ExternalUpdate(SlimeController slime) { }

        public void ExternalFixedUpdate(SlimeController slime, float dt)
        {
            slime.OverrideVelocity(Vector2.zero);
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 p = wispAnchor != null ? wispAnchor.position : transform.position;
            Gizmos.color = new Color(0.3f, 1f, 0.7f, 0.9f);
            Gizmos.DrawSphere(p, 0.25f);

            if (revealRegion == null) return;
            Bounds b = revealRegion.bounds;
            Vector3 size = new Vector3(b.size.x + regionPadding * 2f, b.size.y + regionPadding * 2f, 0.1f);
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.10f);
            Gizmos.DrawCube(b.center, size);
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.9f);
            Gizmos.DrawWireCube(b.center, size);
        }
    }
}
