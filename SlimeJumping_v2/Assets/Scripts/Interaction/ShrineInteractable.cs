using System.Collections;
using UnityEngine;
using Unity.Cinemachine;
using SlimeJumping.Audio;
using SlimeJumping.Background;
using SlimeJumping.Controller;
using SlimeJumping.Interfaces;
using SlimeJumping.Platforms;
using SlimeJumping.UI;

namespace SlimeJumping.Interaction
{
    /// On interact: a wisp appears beside the shrine, blinks, then rides the authored flight
    /// path around the background planet while the camera cuts to the planet shot (VCam
    /// priority plus a Brain blend, the BellRevealCamera mechanism). Route progress drives the
    /// planet's transition to the luminous phase and the camera depart/return points; the
    /// reverse iris then closes on the wisp into EndScene. Takes slime override so movement is
    /// frozen for the whole sequence.
    public class ShrineInteractable : InteractableBase, IExternalController
    {
        [Header("Wisp")]
        [SerializeField] private GameObject wispPrefab;
        [Tooltip("Where the wisp spawns — beside the shrine. Empty = this transform.")]
        [SerializeField] private Transform spawnAnchor;
        [Tooltip("Uniform scale applied to the spawned wisp root.")]
        [SerializeField] private float wispScale = 1f;
        [Tooltip("How much distance shrink is compensated: 0 = pure perspective, 1 = on-screen size held at spawn size, between = partial.")]
        [Range(0f, 1f)]
        [SerializeField] private float sizeCompensation = 0.4f;
        [Tooltip("Seconds the wisp sits at the shrine before the camera and flight begin.")]
        [SerializeField] private float spawnHold = 0.8f;
        [Tooltip("Seconds of the double light-blink right before departure. 0 = skip.")]
        [SerializeField] private float departBlinkSeconds = 1.3f;
        [Tooltip("Seconds of stillness between the blink and take-off.")]
        [SerializeField] private float postBlinkHold = 0.7f;
        [Tooltip("Up-down bob of the traveling wisp, world units. 0 = off.")]
        [SerializeField] private float bobAmplitude = 0.5f;
        [Tooltip("Bob cycles per second.")]
        [SerializeField] private float bobFrequency = 1.2f;

        [Header("Flight")]
        [Tooltip("Authored full route (WaypointSnakePath): shrine, around the planet, down to the final rest point. Route progress drives the planet transition.")]
        [SerializeField] private WaypointSnakePath flightPath;
        [Tooltip("Top travel speed (world units/s). The speed curve scales it.")]
        [SerializeField] private float pathSpeed = 110f;
        [Tooltip("Speed over the whole route: x = route progress 0~1, y = multiplier of pathSpeed.")]
        [SerializeField] private AnimationCurve speedCurve = new AnimationCurve(
            new Keyframe(0f, 0.15f), new Keyframe(0.25f, 1f), new Keyframe(0.85f, 1f), new Keyframe(1f, 0.3f));
        [Tooltip("Route progress where the camera departs for the planet shot. 0 = as the flight begins.")]
        [Range(0f, 1f)]
        [SerializeField] private float cameraDepartProgress = 0f;
        [Tooltip("Route progress where the camera blends back to the gameplay camera. 1 = right at the route's end.")]
        [Range(0f, 1f)]
        [SerializeField] private float cameraReturnProgress = 1f;
        [Tooltip("Route progress where the planet phase transition starts.")]
        [Range(0f, 1f)]
        [SerializeField] private float transitionStart = 0.25f;
        [Tooltip("Route progress where the planet phase transition completes.")]
        [Range(0f, 1f)]
        [SerializeField] private float transitionEnd = 0.9f;

        [Header("Planet Reveal")]
        [Tooltip("Camera framing the planet. Its priority is raised for the rest of the sequence.")]
        [SerializeField] private CinemachineCamera planetVCam;
        [Tooltip("Box drawn over the region to reveal. The VCam is pulled back to fit it. Left empty, the VCam's own transform is the shot.")]
        [SerializeField] private BoxCollider2D revealRegion;
        [Tooltip("World margin added around the region box so its edges are not flush with the screen.")]
        [SerializeField] private float regionPadding = 1f;
        [Tooltip("Brain whose default blend carries the move. Empty = first one in the scene.")]
        [SerializeField] private CinemachineBrain brain;
        [Tooltip("Seconds for the camera to travel to the planet shot.")]
        [SerializeField] private float cameraBlendSeconds = 2.5f;
        [Tooltip("Planet look transition, driven by the wisp's orbit progress.")]
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
        [Tooltip("Played when the blink light turns on.")]
        [SerializeField] private SlimeJumping.Audio.SoundDef blinkOnSound;
        [Tooltip("Played when the blink light turns off.")]
        [SerializeField] private SlimeJumping.Audio.SoundDef blinkOffSound;
        [Tooltip("Played once when the planet phase transition starts.")]
        [SerializeField] private SlimeJumping.Audio.SoundDef planetTransitionSound;

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
            Vector3 startPos = spawnAnchor != null ? spawnAnchor.position : transform.position;

            GameObject wispInstance = null;
            if (wispPrefab != null)
            {
                wispInstance = Instantiate(wispPrefab, startPos, Quaternion.identity);
                if (wispScale > 0f && !Mathf.Approximately(wispScale, 1f))
                    wispInstance.transform.localScale = Vector3.one * wispScale;
                foreach (var light in wispInstance.GetComponentsInChildren<Light>())
                    light.enabled = false;
            }

            // 보정 기준 거리 = 스폰 순간의 카메라 거리.
            UnityEngine.Camera cam0 = brain != null ? brain.OutputCamera : UnityEngine.Camera.main;
            float spawnDist = cam0 != null ? Vector3.Distance(cam0.transform.position, startPos) : 0f;

            // 리스너는 곧 카메라를 타고 행성으로 떠난다 — 3D 재생은 거리감쇠로 묻힌다.
            SoundService.Play2D(wispAppearSound);

            // Face look-toward-wisp (damped). Visible while the camera is still on the slime.
            if (faceLookOffset > 0f && wispInstance != null)
            {
                var visual = slime.GetComponentInChildren<SlimeVisual>();
                if (visual != null)
                    StartCoroutine(FaceTrackRoutine(visual, slime.transform, wispInstance.transform));
            }

            if (spawnHold > 0f)
                yield return new WaitForSeconds(spawnHold);

            if (wispInstance != null && departBlinkSeconds > 0f)
                yield return BlinkRoutine(wispInstance);

            if (postBlinkHold > 0f)
                yield return new WaitForSeconds(postBlinkHold);

            bool pathMode = flightPath != null && flightPath.TotalArcLen > 0.01f;
            if (!pathMode || cameraDepartProgress <= 0f)
                BeginPlanetReveal();

            if (wispInstance != null)
            {
                yield return FlightRoutine(wispInstance.transform, spawnDist);
            }
            else
            {
                // 딸기가 없어도 카메라와 씬 전환은 진행한다.
                if (planetVCam != null && cameraBlendSeconds > 0f)
                    yield return new WaitForSeconds(cameraBlendSeconds);
                if (planetTransition != null) planetTransition.ApplyProgress(1f);
            }

            if (appearHold > 0f)
                yield return new WaitForSeconds(appearHold);

            // 엔딩 진입 = 진행 종료. 세이브를 남기면 Load 가 신전 앞으로 복원한다.
            LoadSaveManager.DeleteSave();

            Vector3 transitionCenter = wispInstance != null ? wispInstance.transform.position : startPos;
            SceneTransitionController.PlayReverse(endSceneName, transitionCenter);
        }

        // 경로가 비행·선회·정착 전부. 속도 = pathSpeed × speedCurve(진행도), 거리 비율 = 전환 진행도.
        private IEnumerator FlightRoutine(Transform wisp, float spawnDist)
        {
            if (flightPath == null || flightPath.TotalArcLen <= 0.01f)
                yield break;

            float len = flightPath.TotalArcLen;
            bool cameraDeparted = cameraDepartProgress <= 0f;
            bool cameraReturned = false;
            bool transitionSoundPlayed = false;
            for (float arc = 0f; arc < len; )
            {
                float speed = pathSpeed * Mathf.Max(0.02f, speedCurve.Evaluate(arc / len));
                arc = Mathf.Min(len, arc + speed * Time.deltaTime);
                float u = arc / len;
                flightPath.SampleWorld(arc, out Vector3 pos, out _);
                wisp.position = pos + BobOffset(Mathf.Clamp01(u * 10f) * Mathf.Clamp01((1f - u) * 10f));
                ApplySizeCompensation(wisp, spawnDist);
                if (planetTransition != null) planetTransition.ApplyProgress(MapTransition(u));
                if (!transitionSoundPlayed && MapTransition(u) > 0f)
                {
                    transitionSoundPlayed = true;
                    SoundService.Play2D(planetTransitionSound);
                }
                if (!cameraDeparted && u >= cameraDepartProgress)
                {
                    cameraDeparted = true;
                    BeginPlanetReveal();
                }
                if (cameraDeparted && !cameraReturned && u >= cameraReturnProgress)
                {
                    cameraReturned = true;
                    ReturnCameraToGameplay();
                }
                yield return null;
            }
            if (planetTransition != null) planetTransition.ApplyProgress(1f);
            if (!cameraReturned) ReturnCameraToGameplay();
        }

        // 출발 신호: 꺼둔 픽업 라이트를 2회 점멸.
        private IEnumerator BlinkRoutine(GameObject wisp)
        {
            var lights = wisp.GetComponentsInChildren<Light>(true);
            float quarter = departBlinkSeconds * 0.25f;
            for (int i = 0; i < 2; i++)
            {
                foreach (var l in lights) l.enabled = true;
                SoundService.Play2D(blinkOnSound);
                yield return new WaitForSeconds(quarter);
                foreach (var l in lights) l.enabled = false;
                SoundService.Play2D(blinkOffSound);
                yield return new WaitForSeconds(quarter);
            }
        }

        private Vector3 BobOffset(float envelope)
        {
            if (bobAmplitude <= 0f || envelope <= 0f) return Vector3.zero;
            return Vector3.up * (bobAmplitude * envelope
                * Mathf.Sin(Time.time * bobFrequency * 2f * Mathf.PI));
        }

        private void ReturnCameraToGameplay()
        {
            if (planetVCam == null) return;
            if (brain != null)
                brain.DefaultBlend = new CinemachineBlendDefinition(
                    CinemachineBlendDefinition.Styles.EaseInOut, cameraBlendSeconds);
            planetVCam.Priority = -100;
        }

        // 루트 진행도를 [transitionStart, transitionEnd] 창으로 재매핑 — 창 밖은 0/1 로 고정.
        private float MapTransition(float u)
        {
            if (u >= transitionEnd) return 1f;
            return Mathf.InverseLerp(transitionStart, Mathf.Max(transitionStart + 0.0001f, transitionEnd), u);
        }

        // 원근 축소를 sizeCompensation 지수만큼 상쇄: scale = wispScale × (d/스폰거리)^k.
        private void ApplySizeCompensation(Transform wisp, float spawnDist)
        {
            if (sizeCompensation <= 0f || wispScale <= 0f || spawnDist <= 0f) return;
            UnityEngine.Camera cam = brain != null ? brain.OutputCamera : UnityEngine.Camera.main;
            if (cam == null) return;
            float d = Vector3.Distance(cam.transform.position, wisp.position);
            wisp.localScale = Vector3.one * (wispScale * Mathf.Pow(d / spawnDist, sizeCompensation));
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
            Vector3 p = spawnAnchor != null ? spawnAnchor.position : transform.position;
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
