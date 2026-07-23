using UnityEngine;

namespace SlimeJumping.Platforms.Piano
{
    /// One white piano key, implemented as a PlatformMotor so the slime is carried: when the slime
    /// rests on it the key (its collider AND mesh, which is parented under this motor) sinks DOWN
    /// with a damped spring, and the standard PlatformCarrier path rides the slime down with it; it
    /// springs back up when the slime leaves. The note plays on the press (velocity-scaled volume
    /// and depth) and damper-fades out on release.
    ///
    /// The keyboard drives this every FixedUpdate via Drive(contacting, impactSpeed, settings) — it
    /// owns the (multi-key) contact detection and the shared tuning. The motor just springs + sounds.
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

        // Cosmetic chord-key press: sinks the MESH only (collider/rb untouched) so it never carries a
        // rider or fires a note — visual/collider stay divided. Driven in OnAfterMotion, not ComputeMotion.
        private Transform meshVisual;
        private Vector3 restMeshLocalPos;
        private bool visualActive;
        private float visualDepth, visualHold, visualTimer;

        public int MidiNote => midiNote;
        public Collider2D Contact => contact;

        // Pool access (harmonize voice pool builds a midi->clip map from the keys).
        public AudioClip Clip => source != null ? source.clip : null;
        public float Pitch => pitch;
        public UnityEngine.Audio.AudioMixerGroup OutputGroup => source != null ? source.outputAudioMixerGroup : null;

        /// Silence this key's own strike/damper (harmonize mode plays the voiced chord via the pool).
        public void SetMuted(bool m) => muted = m;

        /// Cosmetic press for a chord's constituent key: dip the MESH for `hold` seconds — no collider
        /// movement, no rider carry, no sound. Decays in OnAfterMotion.
        public void VisualPress(float depth, float hold)
        {
            if (meshVisual == null) return;
            visualDepth = depth;
            visualHold = Mathf.Max(0.0001f, hold);
            visualTimer = visualHold;
            visualActive = true;
        }

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
            // the key mesh is parented under this motor (see PianoKeyboardBuilder) — dip it for cosmetics
            if (transform.childCount > 0)
            {
                meshVisual = transform.GetChild(0);
                restMeshLocalPos = meshVisual.localPosition;
            }
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

        // Cosmetic mesh dip only (collider stays put) — runs after the base rigidbody move. Zero cost
        // unless a VisualPress is active, so the OFF path (no harmonize) never touches the mesh.
        protected override void OnAfterMotion(float dt)
        {
            if (!visualActive) return;
            visualTimer -= dt;
            float k = Mathf.Clamp01(visualTimer / visualHold);
            meshVisual.localPosition = restMeshLocalPos + new Vector3(0f, -visualDepth * k, 0f);
            if (visualTimer <= 0f)
            {
                meshVisual.localPosition = restMeshLocalPos;
                visualActive = false;
            }
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
