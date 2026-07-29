using UnityEngine;
using UnityEngine.Serialization;
using SlimeJumping.Controller;

namespace SlimeJumping.Audio
{
    /// Plays SFX from SlimeController events, pull-only. Uses ActiveSurfaceMaterial, resolved by the
    /// time OnLanded fires; a raw SnowFieldRegistry lookup skips the heightRest footprint hit-test.
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
        [Tooltip("Constant volume for hard landings (>= Land Hard Threshold). Not impact-speed scaled.")]
        [Range(0f, 1f)] [FormerlySerializedAs("landVolume")] [SerializeField] float landHardVolume = 0.9f;
        [Tooltip("Constant volume for soft landings. Not impact-speed scaled.")]
        [Range(0f, 1f)] [SerializeField] float landSoftVolume = 0.9f;

        [Header("Launch / Hop")]
        [Tooltip("Played on charged Launch().")]
        [SerializeField] AudioClip launchClip;
        [Tooltip("Played on Hop() (quick jump without full charge).")]
        [SerializeField] AudioClip hopClip;
        [Range(0f, 1f)] [SerializeField] float launchVolume = 0.9f;
        [Range(0f, 1f)] [SerializeField] float hopVolume = 0.7f;

        [Tooltip("Suppress soft landing SFX if a hop fired within this many seconds. Prevents hop+land overlap on quick re-hops.")]
        [Min(0f)] [SerializeField] float softLandHopSuppressWindow = 0.4f;

        [Header("Common")]
        [Tooltip("±random pitch jitter applied to each playback.")]
        [Range(0f, 0.3f)] [SerializeField] float pitchJitter = 0.06f;

        float _lastHopTime = float.NegativeInfinity;

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
            bool hard = speed >= landHardThreshold;
            if (!hard && Time.time - _lastHopTime < softLandHopSuppressWindow) return;
            AudioClip clip = hard ? landHardClip : landSoftClip;
            float vol = hard ? landHardVolume : landSoftVolume;
            var mat = slimeController.ActiveSurfaceMaterial;
            if (mat != null)
            {
                AudioClip overrideClip = hard ? mat.landHardClip : mat.landSoftClip;
                if (overrideClip != null)
                {
                    clip = overrideClip;
                    float surfaceVol = hard ? mat.landHardVolume : mat.landSoftVolume;
                    if (surfaceVol > 0f) vol = surfaceVol;
                }
            }
            Play(clip, position, vol);
        }

        void HandleLaunch(Vector2 position, Vector2 velocity, bool wasHop)
        {
            if (wasHop) _lastHopTime = Time.time;
            AudioClip clip = wasHop ? hopClip : launchClip;
            float vol = wasHop ? hopVolume : launchVolume;
            var mat = slimeController.ActiveSurfaceMaterial;
            if (mat != null)
            {
                AudioClip overrideClip = wasHop ? mat.hopClip : mat.launchClip;
                if (overrideClip != null)
                {
                    clip = overrideClip;
                    float surfaceVol = wasHop ? mat.hopVolume : mat.launchVolume;
                    if (surfaceVol > 0f) vol = surfaceVol;
                }
            }
            Play(clip, position, vol);
        }

        void Play(AudioClip clip, Vector2 position, float volume)
        {
            if (clip == null) return;
            if (volume <= 0.001f) return;
            float pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            SoundService.PlayAt(clip, position, SoundCategory.SFX_Slime, volume, pitch);
        }
    }
}
