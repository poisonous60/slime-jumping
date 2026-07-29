using System.Collections;
using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;
using SlimeJumping.UI.Framework;
using SlimeJumping.UI.Panels;

namespace SlimeJumping.Interaction
{
    /// v2 gramophone: opens GramophonePanel, then plays the chosen track through SoundService
    /// and tweens needle/disc. Rotation touches discTransform only — the collider stays put.
    public class MediaInteractable_PhonographV2 : InteractableBase
    {
        [Header("Playlist")]
        [SerializeField] private PhonographPlaylist playlist;

        [Header("Playback")]
        [SerializeField, Min(0f)] private float bgmCrossfadeSeconds = 1.5f;
        [SerializeField, Min(0f)] private float bgmStopFadeSeconds = 0.8f;

        [Header("Disc")]
        [SerializeField] private Transform discTransform;
        [SerializeField] private float discRPM = 33.3f;
        [SerializeField] private Vector3 rotationAxis = Vector3.forward;
        [SerializeField, Min(0f)] private float discSpinUpSeconds = 0.6f;
        [SerializeField, Min(0f)] private float discSpinDownSeconds = 0.5f;

        [Header("Needle (optional)")]
        [SerializeField] private Transform needleArm;
        [SerializeField] private Vector3 needleRestEuler = new Vector3(0f, 0f, -20f);
        [SerializeField] private Vector3 needlePlayEuler = Vector3.zero;
        [SerializeField, Min(0f)] private float needleTweenSeconds = 0.4f;

        private int currentTrackIndex = -1;
        private float spinFactor;        // 0..1 — drives visible disc speed
        private Coroutine spinRoutine;
        private Coroutine needleRoutine;

        public PhonographPlaylist Playlist => playlist;
        public int CurrentTrackIndex => currentTrackIndex;
        public bool IsPlaying => currentTrackIndex >= 0;

        protected override void Awake()
        {
            base.Awake();
            ApplyNeedleRotation(needleRestEuler);
        }

        protected override void OnInteract(SlimeController slime)
        {
            var panel = UIManager.Instance != null ? UIManager.Instance.Get<GramophonePanel>() : null;
            if (panel == null)
            {
                Debug.LogWarning($"[{nameof(MediaInteractable_PhonographV2)}] No GramophonePanel registered with UIManager.", this);
                return;
            }
            panel.OpenFor(this);
        }

        /// Called by GramophonePanel after the player picks a track.
        public void PlayTrack(int index)
        {
            if (playlist == null || index < 0 || index >= playlist.Count) return;
            var entry = playlist.GetTrack(index);
            if (entry == null || entry.clip == null) return;

            currentTrackIndex = index;
            SoundService.PlayBgm(entry.clip, bgmCrossfadeSeconds, entry.volume);
            StartNeedle(toPlay: true);
            StartSpin(targetFactor: 1f, discSpinUpSeconds);
        }

        /// Called by GramophonePanel when the player picks "Stop music".
        public void StopMusic()
        {
            if (!IsPlaying) return;
            currentTrackIndex = -1;
            SoundService.StopBgm(bgmStopFadeSeconds);
            StartNeedle(toPlay: false);
            StartSpin(targetFactor: 0f, discSpinDownSeconds);
        }

        private void Update()
        {
            if (discTransform == null || spinFactor <= 0f) return;
            discTransform.Rotate(rotationAxis, discRPM * 6f * spinFactor * Time.deltaTime, Space.Self);
        }

        private void StartSpin(float targetFactor, float duration)
        {
            if (spinRoutine != null) StopCoroutine(spinRoutine);
            spinRoutine = StartCoroutine(SpinTween(targetFactor, duration));
        }

        private IEnumerator SpinTween(float target, float duration)
        {
            float start = spinFactor;
            if (duration <= 0f) { spinFactor = target; yield break; }
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                spinFactor = Mathf.Lerp(start, target, Mathf.Clamp01(t / duration));
                yield return null;
            }
            spinFactor = target;
            spinRoutine = null;
        }

        private void StartNeedle(bool toPlay)
        {
            if (needleArm == null) return;
            if (needleRoutine != null) StopCoroutine(needleRoutine);
            needleRoutine = StartCoroutine(NeedleTween(toPlay ? needlePlayEuler : needleRestEuler));
        }

        private IEnumerator NeedleTween(Vector3 targetEuler)
        {
            Vector3 startEuler = needleArm.localEulerAngles;
            // Unwrap to avoid 359 → 0 wrap glitches
            startEuler = new Vector3(NormalizeAngle(startEuler.x), NormalizeAngle(startEuler.y), NormalizeAngle(startEuler.z));
            if (needleTweenSeconds <= 0f) { ApplyNeedleRotation(targetEuler); yield break; }
            float t = 0f;
            while (t < needleTweenSeconds)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / needleTweenSeconds);
                ApplyNeedleRotation(Vector3.Lerp(startEuler, targetEuler, u));
                yield return null;
            }
            ApplyNeedleRotation(targetEuler);
            needleRoutine = null;
        }

        private void ApplyNeedleRotation(Vector3 euler)
        {
            if (needleArm == null) return;
            needleArm.localEulerAngles = euler;
        }

        private static float NormalizeAngle(float a)
        {
            a %= 360f;
            if (a > 180f) a -= 360f;
            return a;
        }
    }
}
