using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Platforms;

namespace SlimeJumping.Mechanisms
{
    /// <summary>Start / moving-loop / stop sounds for a WaypointPlatformV2 elevator. Polls the deck's
    /// IsMoving instead of taking events, so the platform stays unaware of audio and removing this
    /// component removes the whole feature. A leg that begins with nobody aboard — the empty trip
    /// home — runs silent end to end, rather than cutting out the moment the rider steps off.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WaypointPlatformV2))]
    public class ElevatorAudio : MonoBehaviour
    {
        [SerializeField] SoundDef startSound;
        [SerializeField] SoundDef movingLoopSound;
        [SerializeField] SoundDef stopSound;
        [Range(0f, 2f)] [SerializeField] float loopVolumeScale = 1f;
        [Tooltip("Ramp on the moving loop when the deck halts, so it does not cut off under the stop hit.")]
        [Min(0f)] [SerializeField] float loopFadeOutSeconds = 0.15f;

        WaypointPlatformV2 platform;
        SoundHandle loop;
        bool wasMoving;
        bool ridden;   // this leg started with a rider aboard, so it owes a stop sound

        void Awake() => platform = GetComponent<WaypointPlatformV2>();

        void OnDisable()
        {
            SoundService.StopLoop(loop);
            loop = default;
            wasMoving = false;
            ridden = false;
        }

        void Update()
        {
            bool moving = platform.IsMoving;
            if (moving == wasMoving) return;
            wasMoving = moving;

            if (moving)
            {
                if (!platform.HasRiderOnTop) return;
                ridden = true;
                SoundService.Play(startSound, transform.position);
                loop = SoundService.PlayLooping(movingLoopSound, transform, loopVolumeScale);
            }
            else
            {
                if (!ridden) return;
                ridden = false;
                SoundService.StopLoop(loop, loopFadeOutSeconds);
                loop = default;
                SoundService.Play(stopSound, transform.position);
            }
        }
    }
}
