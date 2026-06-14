using UnityEngine;

namespace SlimeJumping.Platforms.Piano
{
    /// A steppable piano pedal. It does NOT affect the notes/sound — purely a physical/visual prop.
    ///
    /// Implemented as a PlatformMotor so the slime is carried properly: while the slime rests on it
    /// the pedal (and its PolygonCollider2D) sinks straight DOWN with a damped spring, and the
    /// standard PlatformCarrier path rides the slime down with it; it springs back up when the slime
    /// leaves. On top of that the visual mesh tilts about X (cosmetic hinge tilt that goes into the
    /// screen — it does NOT move the 2D collider) for a natural pedal-press look.
    ///
    /// The collider that carries the rider is the PolygonCollider2D on this GameObject (reshape it to
    /// the pedal outline). The tilting mesh lives under a separate pivot child, so the collider only
    /// translates in Y while the visual tilts — the rider rides the clean vertical sink.
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

        // PlatformCarrier calls this every frame the slime is attached, and RemoveRider on detach.
        // The first call of a press carries the impact velocity (before the slime zeroes it on landing).
        public override void ApplyRiderInfluence(Vector2 riderWorldPos, float riderMass, Vector2 riderVelocity, bool isCharging)
        {
            if (!hasRider) pressSpeed = riderVelocity.magnitude;
            hasRider = true;
        }

        public override void RemoveRider() => hasRider = false;

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
