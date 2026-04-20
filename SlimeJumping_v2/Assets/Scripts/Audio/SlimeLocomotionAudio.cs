using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Audio
{
    /// Observes SlimeController events (OnLanded, OnLaunch) and plays SFX.
    /// Pure consumer — does not push into the controller. Mirrors the LandingDustSpawner
    /// pattern so audio stays off the controller.
    [AddComponentMenu("SlimeJumping/Audio/Slime Locomotion Audio")]
    public class SlimeLocomotionAudio : MonoBehaviour
    {
        [SerializeField] SlimeController slimeController;

        [Header("Landing")]
        [Tooltip("Played when impact speed (normal component) >= Land Hard Threshold.")]
        [SerializeField] AudioClip landHardClip;
        [Tooltip("Played on softer landings below Land Hard Threshold.")]
        [SerializeField] AudioClip landSoftClip;
        [Tooltip("ImpactNormalSpeed at or above which the hard land clip is used.")]
        [Min(0f)] [SerializeField] float landHardThreshold = 8f;
        [Tooltip("Impact speed that maps to full volume; below scales linearly toward zero.")]
        [Min(0.1f)] [SerializeField] float landRefSpeed = 12f;
        [Range(0f, 1f)] [SerializeField] float landVolume = 0.9f;

        [Header("Launch / Hop")]
        [Tooltip("Played on charged Launch().")]
        [SerializeField] AudioClip launchClip;
        [Tooltip("Played on Hop() (quick jump without full charge).")]
        [SerializeField] AudioClip hopClip;
        [Tooltip("Launch speed that maps to full volume.")]
        [Min(0.1f)] [SerializeField] float launchRefSpeed = 20f;
        [Range(0f, 1f)] [SerializeField] float launchVolume = 0.9f;
        [Range(0f, 1f)] [SerializeField] float hopVolume = 0.7f;

        [Header("Common")]
        [Tooltip("±random pitch jitter applied to each playback.")]
        [Range(0f, 0.3f)] [SerializeField] float pitchJitter = 0.06f;

        void Reset()
        {
            if (slimeController == null)
                slimeController = GetComponentInParent<SlimeController>();
        }

        void OnEnable()
        {
            if (slimeController == null) return;
            slimeController.OnLanded += HandleLanded;
            slimeController.OnLaunch += HandleLaunch;
        }

        void OnDisable()
        {
            if (slimeController == null) return;
            slimeController.OnLanded -= HandleLanded;
            slimeController.OnLaunch -= HandleLaunch;
        }

        void HandleLanded(Vector2 position, Vector2 surfaceNormal)
        {
            float speed = slimeController.ImpactNormalSpeed;
            AudioClip clip = speed >= landHardThreshold ? landHardClip : landSoftClip;
            Play(clip, position, speed, landRefSpeed, landVolume);
        }

        void HandleLaunch(Vector2 position, Vector2 velocity, bool wasHop)
        {
            AudioClip clip = wasHop ? hopClip : launchClip;
            float baseVol = wasHop ? hopVolume : launchVolume;
            Play(clip, position, velocity.magnitude, launchRefSpeed, baseVol);
        }

        void Play(AudioClip clip, Vector2 position, float speed, float refSpeed, float baseVolume)
        {
            if (clip == null) return;
            float vol = baseVolume * Mathf.Clamp01(speed / refSpeed);
            if (vol <= 0.001f) return;
            float pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            SoundService.PlayAt(clip, position, SoundCategory.SFX_Slime, vol, pitch);
        }
    }
}
