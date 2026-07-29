using UnityEngine;

namespace SlimeJumping.Platforms.Piano
{
    /// One white key: a PlatformMotor so PlatformCarrier rides the slime down as the key springs.
    /// PianoKeyboard owns contact detection + tuning and calls Drive() each FixedUpdate; this only
    /// springs and sounds.
    [RequireComponent(typeof(Rigidbody2D))]
    public class PianoKey : PlatformMotor
    {
        [SerializeField] private int midiNote;
        [SerializeField] private AudioSource source;   // dedicated per-key voice
        [SerializeField] private float pitch = 1f;     // !=1 for pitch-shifted top keys
        [SerializeField] private Collider2D contact;   // own collider (used by the keyboard for detection)

        private Vector2 restPos;
        private float restRot;
        private float offset;          // current sink in world units (<= 0)
        private float vel;
        private bool contacting, wasContacting;
        private float curImpact;
        private float heldDepth;        // velocity-scaled sink target set on the last strike
        private float releaseTimer, fadeStartVol;
        private PianoSettings s;        // shared, supplied by the keyboard each frame

        private bool muted;            // harmonize mode: the voice pool owns the audio, keys stay silent

        public int MidiNote => midiNote;
        public Collider2D Contact => contact;

        /// Is the slime standing on this key right now?
        public bool IsPhysicallyPressed => contacting;

        /// Sink already applied (positive = down); PianoKeyVisual subtracts it so an authored dip
        /// composes with the slime's weight instead of doubling it.
        public float PhysicalSink => -offset;

        /// The key mesh (parented under this motor by PianoKeyboardBuilder). PianoKeyVisual rides it.
        public Transform Mesh => transform.childCount > 0 ? transform.GetChild(0) : null;

        // Pool access (harmonize voice pool builds a midi->clip map from the keys).
        public AudioClip Clip => source != null ? source.clip : null;
        public float Pitch => pitch;
        public UnityEngine.Audio.AudioMixerGroup OutputGroup => source != null ? source.outputAudioMixerGroup : null;

        /// Silence this key's own strike/damper (harmonize mode plays the voiced chord via the pool).
        public void SetMuted(bool m) => muted = m;

        public void Init(int midiNote, AudioSource source, float pitch, Collider2D contact)
        {
            this.midiNote = midiNote;
            this.source = source;
            this.pitch = pitch;
            this.contact = contact;
        }

        protected override void Awake()
        {
            base.Awake();   // kinematic + interpolated Rigidbody2D
            restPos = rb.position;
            restRot = rb.rotation;
            if (contact == null) contact = GetComponent<Collider2D>();
        }

        /// Called by PianoKeyboard every FixedUpdate (it runs earlier than this motor).
        public void Drive(bool contacting, float impactSpeed, PianoSettings settings)
        {
            this.contacting = contacting;
            curImpact = impactSpeed;
            s = settings;
        }

        protected override (Vector2 position, float rotation) ComputeMotion(float dt)
        {
            if (s == null) return (restPos, restRot);

            if (contacting && !wasContacting) Strike();
            else if (!contacting && wasContacting) Release();
            wasContacting = contacting;

            float target = contacting ? -heldDepth : 0f;
            float accel = -s.springStiffness * (offset - target) - s.springDamping * vel;
            vel += accel * dt;
            offset += vel * dt;

            // damper release: linear fade of the same note clip, then stop
            if (releaseTimer > 0f && source != null)
            {
                releaseTimer -= dt;
                float k = Mathf.Clamp01(releaseTimer / Mathf.Max(0.0001f, s.releaseFade));
                source.volume = fadeStartVol * k;
                if (releaseTimer <= 0f) source.Stop();
            }

            return (restPos + new Vector2(0f, offset), restRot);
        }

        private void Strike()
        {
            float t = s.NormalizedImpact(curImpact);
            heldDepth = Mathf.Max(s.holdDepth, s.velocityToDepth.Evaluate(t) * s.strikeDepth);

            if (!muted && source != null && source.clip != null)
            {
                source.pitch = pitch;
                source.volume = Mathf.Clamp01(s.velocityToVolume.Evaluate(t)) * s.masterVolume;
                source.time = 0f;
                source.Play();
            }
            releaseTimer = 0f;
        }

        private void Release()
        {
            if (muted) { releaseTimer = 0f; return; }
            if (source != null && source.isPlaying)
            {
                fadeStartVol = source.volume;
                releaseTimer = s.releaseFade;
            }
            else releaseTimer = 0f;
        }
    }
}
