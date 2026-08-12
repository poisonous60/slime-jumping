using UnityEngine;
using SlimeJumping.Platforms;

namespace SlimeJumping.Audio
{
    /// Metal groan driven by how fast a platform is turning, not by landings: a hinge squeaks by
    /// its own sliding speed. Re-arming waits for the speed to fall back below rearmSpeed, so a
    /// swing groans once and the slow-down at each end of the arc re-arms it — the spacing comes
    /// from the platform's motion instead of a cooldown timer unrelated to what is on screen.
    /// Poll-only against PlatformMotor.AngularVelocity (order -100, so it is this frame's value).
    /// All tuning lives on the shared PlatformCreakConfig.
    [AddComponentMenu("SlimeJumping/Audio/Platform Creak Audio")]
    public class PlatformCreakAudio : MonoBehaviour
    {
        [Tooltip("Platform whose rotation is watched. Null = the PlatformMotor on this GameObject.")]
        [SerializeField] PlatformMotor motor;

        [Tooltip("Optional override of the shared PlatformCreakConfig. Leave null to auto-load " +
                 "Assets/Resources/PlatformCreakConfig.asset.")]
        [SerializeField] PlatformCreakConfig configOverride;

        PlatformCreakConfig config;
        bool armed = true;

        void Reset()
        {
            if (motor == null) motor = GetComponent<PlatformMotor>();
        }

        void Awake()
        {
            if (motor == null) motor = GetComponent<PlatformMotor>();
            if (motor == null)
            {
                Debug.LogError("[PlatformCreakAudio] No PlatformMotor assigned or on this GameObject.", this);
                enabled = false;
                return;
            }

            // Fallback instance so a config-less project needs no null guard on every read.
            config = PlatformCreakConfig.Resolve(configOverride);
            if (config == null) config = ScriptableObject.CreateInstance<PlatformCreakConfig>();
        }

        void FixedUpdate()
        {
            float speed = Mathf.Abs(motor.AngularVelocity);

            if (armed && speed >= config.triggerSpeed)
            {
                SoundService.Play(config.creakSound, motor.transform.position);
                armed = false;
            }
            else if (!armed && speed <= config.rearmSpeed)
            {
                armed = true;
            }
        }
    }
}
