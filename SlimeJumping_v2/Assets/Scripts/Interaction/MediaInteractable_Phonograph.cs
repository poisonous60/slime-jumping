using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Interaction
{
    public class MediaInteractable_Phonograph : InteractableBase
    {
        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip[] tracks;

        [Header("Disc")]
        [SerializeField] private Transform discTransform;
        [SerializeField] private float discRPM = 33.3f;
        [SerializeField] private Vector3 rotationAxis = Vector3.forward;

        private int trackIndex;
        private bool isPlaying;

        protected override void Awake()
        {
            base.Awake();
            if (tracks == null) return;
            for (int i = 0; i < tracks.Length; i++)
            {
                var clip = tracks[i];
                if (clip != null && clip.loadState == AudioDataLoadState.Unloaded)
                    clip.LoadAudioData();
            }
        }

        protected override void OnInteract(SlimeController slime)
        {
            if (tracks == null || tracks.Length == 0) return;

            if (isPlaying)
            {
                if (audioSource != null) audioSource.Stop();
                isPlaying = false;
                return;
            }

            if (audioSource == null) return;

            audioSource.clip = tracks[trackIndex % tracks.Length];
            audioSource.Play();
            trackIndex++;
            isPlaying = true;
        }

        private void Update()
        {
            if (!isPlaying || discTransform == null) return;

            if (audioSource != null && !audioSource.isPlaying && !audioSource.loop)
            {
                isPlaying = false;
                return;
            }

            discTransform.Rotate(rotationAxis, discRPM * 6f * Time.deltaTime, Space.Self);
        }
    }
}
