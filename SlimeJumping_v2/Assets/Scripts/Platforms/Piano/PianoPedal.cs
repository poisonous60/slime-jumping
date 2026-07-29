using UnityEngine;

namespace SlimeJumping.Platforms.Piano
{
    /// A steppable piano pedal. Does NOT affect notes/sound — physical/visual prop only.
    /// PlatformMotor so the PolygonCollider2D sinks straight down on a damped spring and the normal
    /// carrier path rides the slime with it; the mesh tilts under a separate pivot child so the
    /// collider only ever translates in Y.
    [RequireComponent(typeof(Rigidbody2D))]
    public class PianoPedal : PlatformMotor
    {
        [SerializeField] private PianoConfig config;      // shared piano tuning (pedal section)
        [SerializeField] private Transform visualPivot;   // cosmetic X-tilt hinge (mesh parented under it)
        [SerializeField] private AudioSource source;      // per-pedal press-sound voice

        private Vector2 restPos;
        private float restRot;
        private Quaternion restPivotRot;
        private bool hasRider, wasRider;
        private float press, vel;
        private float pressSpeed;   // slime impact speed captured on first contact (for the volume curve)

        public void Init(PianoConfig config, Transform visualPivot, AudioSource source)
        {
            this.config = config;
            this.visualPivot = visualPivot;
            this.source = source;
        }

        protected override void Awake()
        {
            base.Awake();   // makes the Rigidbody2D kinematic + interpolated
            restPos = rb.position;
            restRot = rb.rotation;
            if (visualPivot != null) restPivotRot = visualPivot.localRotation;
        }

        // Only the first call of a press carries the impact velocity — the slime zeroes it on landing.
        public override void ApplyRiderInfluence(Vector2 riderWorldPos, float riderMass, Vector2 riderVelocity, bool isCharging)
        {
            if (!hasRider) pressSpeed = riderVelocity.magnitude;
            hasRider = true;
        }

        public override void RemoveRider() => hasRider = false;

        /// Is the slime standing on this pedal? PianoKeyboard reads it to lengthen note tails.
        public bool IsPressed => hasRider;

        protected override (Vector2 position, float rotation) ComputeMotion(float dt)
        {
            if (config == null) return (restPos, restRot);

            if (hasRider && !wasRider && source != null && config.pedalPressSound != null)
            {
                float vol = Mathf.Clamp01(config.pedalVelocityToVolume.Evaluate(config.PedalNormalizedImpact(pressSpeed)))
                            * config.pedalSoundVolume;
                source.PlayOneShot(config.pedalPressSound, vol);
            }
            wasRider = hasRider;

            float target = hasRider ? 1f : 0f;
            float accel = -config.pedalSpringStiffness * (press - target) - config.pedalSpringDamping * vel;
            vel += accel * dt;
            press += vel * dt;
            return (restPos + new Vector2(0f, -config.pedalMaxDip * press), restRot);
        }

        protected override void OnAfterMotion(float dt)
        {
            if (visualPivot != null && config != null)
                visualPivot.localRotation = restPivotRot * Quaternion.Euler(-config.pedalMaxAngle * press, 0f, 0f);
        }
    }
}
