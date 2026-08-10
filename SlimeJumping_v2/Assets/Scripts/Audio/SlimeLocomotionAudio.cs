using UnityEngine;
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
        [SerializeField] SoundDef landHardSound;
        [Tooltip("Played on softer landings below Land Hard Threshold.")]
        [SerializeField] SoundDef landSoftSound;
        [Tooltip("ImpactNormalSpeed at or above which the hard land sound is used.")]
        [Min(0f)] [SerializeField] float landHardThreshold = 8f;

        [Header("Launch / Hop")]
        [Tooltip("Played on charged Launch().")]
        [SerializeField] SoundDef launchSound;
        [Tooltip("Played on Hop() (quick jump without full charge).")]
        [SerializeField] SoundDef hopSound;

        [Tooltip("Suppress soft landing SFX if a hop fired within this many seconds. Prevents hop+land overlap on quick re-hops.")]
        [Min(0f)] [SerializeField] float softLandHopSuppressWindow = 0.4f;

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

            SoundDef def = hard ? landHardSound : landSoftSound;
            var mat = slimeController.ActiveSurfaceMaterial;
            if (mat != null)
            {
                SoundDef surfaceDef = hard ? mat.landHardSound : mat.landSoftSound;
                if (surfaceDef != null) def = surfaceDef;
            }
            SoundService.Play(def, position);
        }

        void HandleLaunch(Vector2 position, Vector2 velocity, bool wasHop)
        {
            if (wasHop) _lastHopTime = Time.time;

            SoundDef def = wasHop ? hopSound : launchSound;
            var mat = slimeController.ActiveSurfaceMaterial;
            if (mat != null)
            {
                SoundDef surfaceDef = wasHop ? mat.hopSound : mat.launchSound;
                if (surfaceDef != null) def = surfaceDef;
            }
            SoundService.Play(def, position);
        }
    }
}
