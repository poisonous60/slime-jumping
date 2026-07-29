using UnityEngine;

namespace SlimeJumping.Platforms.Piano
{
    /// Cosmetic "this key is sounding" dip for all 88 keys. Writes only its own localPosition, so
    /// colliders and rigidbodies stay untouched. Driven by PianoVoicePool.NotePlayed via
    /// PianoKeyboard (one press per sounded note); attached at runtime by SetupHarmonize.
    public class PianoKeyVisual : MonoBehaviour
    {
        private Vector3 restLocalPos;
        private bool restCaptured;

        private float depth;      // current dip magnitude (local units)
        private float timer;      // seconds until fully back up
        private float rise;       // length of the final rise
        private bool active;

        // Re-articulation: a key struck while still down must visibly come back up first, else an
        // Alberti figure's repeated top note just holds the key down flat.
        private float lift;       // 0..1 of the dip that is momentarily released
        private float liftTimer, liftFall;
        private int lastPressFrame = -1;

        private PianoKey owner;   // white keys only: the motor that physically sinks this same mesh

        /// The motor that already sinks this mesh; the dip composes against it, else double travel.
        public void SetOwner(PianoKey key) => owner = key;

        void Awake() => CaptureRest();

        private void CaptureRest()
        {
            if (restCaptured) return;
            restLocalPos = transform.localPosition;
            restCaptured = true;
        }

        /// Press for `hold` seconds at `depth`, then rise over `rise`. Overlapping presses ACCUMULATE
        /// (a quiet ornament must not cut short a note still ringing on the same key); striking a key
        /// that is still down lifts it by `reartLift` over `reartSeconds` so the repeat reads.
        public void Press(float depth, float hold, float rise, float reartLift, float reartSeconds)
        {
            CaptureRest();

            // Two notes on the same key in the SAME frame are one onset, not a re-strike — lifting
            // here would shallow out a dip that hasn't rendered yet.
            bool sameFrame = lastPressFrame == Time.frameCount;
            lastPressFrame = Time.frameCount;

            if (!sameFrame && active && CurrentDipFactor() > 0.05f)
            {
                lift = Mathf.Clamp01(reartLift);
                liftFall = Mathf.Max(0.0001f, reartSeconds);
                liftTimer = liftFall;
            }

            this.rise = Mathf.Max(0.0001f, rise);
            this.depth = Mathf.Max(this.depth, depth);
            timer = Mathf.Max(timer, Mathf.Max(0f, hold) + this.rise);
            active = true;
        }

        private float CurrentDipFactor() => DipFactor(timer, rise);

        /// Snap back to rest immediately (harmonize turned off / teardown).
        public void ResetNow()
        {
            if (!restCaptured) return;
            transform.localPosition = restLocalPos;
            active = false;
            timer = 0f;
            depth = 0f;
            lift = 0f;
            liftTimer = 0f;
        }

        // LateUpdate so the offset composes on the motor's interpolated FixedUpdate move and animates
        // at frame rate, not the 50 Hz physics step.
        void LateUpdate()
        {
            if (!active) return;

            timer -= Time.deltaTime;
            if (liftTimer > 0f) liftTimer -= Time.deltaTime;

            if (timer <= 0f)
            {
                transform.localPosition = restLocalPos;
                active = false;
                depth = 0f;
                lift = 0f;
                liftTimer = 0f;
                return;
            }

            // `depth` is a WORLD distance, so it must come back into parent space — see ToLocal.
            float targetDip = ToLocal(depth) * DipFactor(timer, rise) * (1f - LiftFactor(liftTimer, liftFall, lift));

            // Subtracting the motor's physical sink prevents double travel AND lets a re-strike lift
            // the mesh above the sunk position — the only way a repeat shows under the slime's weight.
            transform.localPosition = restLocalPos + new Vector3(0f, MeshLocalY(targetDip, PhysicalSinkLocal()), 0f);
        }

        /// World distance -> this mesh's parent space. White key meshes hang off a KeyMotor (scale 1),
        /// black ones off the piano root (scale 2), so a raw value moves black keys twice as far.
        private float ToLocal(float world)
        {
            float scale = transform.parent != null ? Mathf.Abs(transform.parent.lossyScale.y) : 1f;
            return world / Mathf.Max(1e-4f, scale);
        }

        private float PhysicalSinkLocal()
        {
            if (owner == null) return 0f;
            return ToLocal(owner.PhysicalSink);
        }

        /// Local Y so the key reads `targetDip` below rest given the motor's `physicalSink`;
        /// positive = the mesh rides UP relative to the motor (a re-strike popping it back open).
        public static float MeshLocalY(float targetDip, float physicalSink) => physicalSink - targetDip;

        /// Fraction of the dip released by a re-strike: full `lift` at the strike, linear to 0 over `fall`.
        public static float LiftFactor(float liftTimer, float fall, float lift)
        {
            if (liftTimer <= 0f || fall <= 0.0001f) return 0f;
            return lift * Mathf.Clamp01(liftTimer / fall);
        }

        /// 1 while the note is held down, then a linear rise to 0 over the last `rise` seconds.
        public static float DipFactor(float timer, float rise)
        {
            if (timer <= 0f) return 0f;
            if (rise <= 0.0001f) return 1f;
            return timer > rise ? 1f : timer / rise;
        }

        /// "PianoKey.037" -> 37. -1 when the name isn't a numbered key mesh.
        public static int ParseKeyIndex(string name)
        {
            if (string.IsNullOrEmpty(name) || !name.StartsWith("PianoKey")) return -1;
            int i = name.Length;
            while (i > 0 && char.IsDigit(name[i - 1])) i--;
            if (i == name.Length) return -1;
            return int.TryParse(name.Substring(i), out int n) ? n : -1;
        }

        /// MIDI note for a key mesh name, mirroring PianoKeyboardBuilder: 001 = 21 (A0) .. 088 = 108 (C8).
        public static int MidiFromName(string name)
        {
            int idx = ParseKeyIndex(name);
            if (idx < 1 || idx > 88) return -1;
            return 20 + idx;
        }
    }
}
