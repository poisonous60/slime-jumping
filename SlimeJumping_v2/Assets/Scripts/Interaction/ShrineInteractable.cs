using System.Collections;
using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;
using SlimeJumping.Interfaces;
using SlimeJumping.UI;

namespace SlimeJumping.Interaction
{
    /// On interact: spawns a wisp at wispAnchor, holds, then runs the reverse iris transition
    /// to EndScene. Takes slime override so movement is frozen for the whole sequence.
    public class ShrineInteractable : InteractableBase, IExternalController
    {
        [Header("Wisp")]
        [SerializeField] private GameObject wispPrefab;
        [SerializeField] private Transform wispAnchor;
        [Tooltip("Uniform scale applied to the spawned wisp root. Lets you tune size per scene without editing the prefab.")]
        [SerializeField] private float wispScale = 1f;

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

            GameObject wispInstance = null;
            if (wispPrefab != null)
            {
                wispInstance = Instantiate(wispPrefab, spawnPos, Quaternion.identity);
                if (wispScale > 0f && !Mathf.Approximately(wispScale, 1f))
                    wispInstance.transform.localScale = Vector3.one * wispScale;
            }

            // Face look-toward-wisp (damped). Runs in parallel; scene unload ends it.
            if (faceLookOffset > 0f && wispInstance != null)
            {
                var visual = slime.GetComponentInChildren<SlimeVisual>();
                if (visual != null)
                    StartCoroutine(FaceTrackRoutine(visual, slime.transform, wispInstance.transform));
            }

            SoundService.Play(wispAppearSound, transform.position);

            if (appearHold > 0f)
                yield return new WaitForSeconds(appearHold);

            Vector3 transitionCenter = wispInstance != null ? wispInstance.transform.position : spawnPos;
            SceneTransitionController.PlayReverse(endSceneName, transitionCenter);
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
        }
    }
}
