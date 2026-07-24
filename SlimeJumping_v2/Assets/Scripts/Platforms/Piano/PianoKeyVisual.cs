using UnityEngine;

namespace SlimeJumping.Platforms.Piano
{
    /// The cosmetic "this key is sounding" dip, for ALL 88 keys — white ones (whose mesh rides a
    /// KeyMotor) and black ones (inert meshes with no collider at all). It only writes its own
    /// localPosition, so colliders and rigidbodies are untouched: visual and collider stay divided.
    ///
    /// Driven entirely by PianoVoicePool.NotePlayed via PianoKeyboard, so every note — the landing
    /// melody, a rolled chord's third note, a chiptune burst, a suspension's resolution 180 ms later —
    /// presses its own key at the moment it actually sounds. Ornaments never touch visuals themselves.
    ///
    /// Attached at runtime by the keyboard (SetupHarmonize); the scene and the builder stay untouched.
    public class PianoKeyVisual : MonoBehaviour
    {
        private Vector3 restLocalPos;
        private bool restCaptured;

        private float depth;      // current dip magnitude (local units)
        private float timer;      // seconds until fully back up
        private float rise;       // length of the final rise
        private bool active;

        // Re-articulation: a key struck again while it is still down must visibly come back up before
        // it goes down again — on a real piano the key has to clear the escapement to re-sound. Without
        // this an Alberti figure's repeated top note or a chiptune burst just holds the key down flat.
        private float lift;       // 0..1 of the dip that is momentarily released
        private float liftTimer, liftFall;
        private int lastPressFrame = -1;

        private PianoKey owner;   // white keys only: the motor that physically sinks this same mesh

        /// The motor whose physical sink already moves this mesh. While the slime stands on it the
        /// cosmetic dip is suppressed, otherwise the key would sink twice as far.
        public void SetOwner(PianoKey key) => owner = key;

        void Awake() => CaptureRest();

        private void CaptureRest()
        {
            if (restCaptured) return;
            restLocalPos = transform.localPosition;
            restCaptured = true;
        }

        /// Press this key for `hold` seconds at `depth`, then let it rise over `rise`.
        /// Overlapping presses ACCUMULATE rather than overwrite: a short quiet ornament note must never
        /// cut short the long loud note already ringing on the same key.
        ///
        /// Striking a key that is still down snaps it up by `reartLift` (a fraction of its dip) and
        /// lets it fall back over `reartSeconds`, so a repeated note reads as a second strike instead
        /// of one long hold.
        public void Press(float depth, float hold, float rise, float reartLift, float reartSeconds)
        {
            CaptureRest();

            // Two notes landing on the same key in the SAME frame (e.g. the melody and a chiptune tone)
            // are one onset, not a re-strike — lifting here would shallow out a dip that hasn't even
            // rendered yet.
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

        // LateUpdate so the offset composes on top of the motor's interpolated FixedUpdate move, and
        // animates at frame rate instead of the 50 Hz physics step.
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

            // How far down this key should read overall, this frame.
            float targetDip = depth * DipFactor(timer, rise) * (1f - LiftFactor(liftTimer, liftFall, lift));

            // The motor may already be holding this key down under the slime's weight. Compose against
            // that instead of ignoring it: subtracting the physical sink both prevents double travel AND
            // lets a re-strike lift the mesh back up above the sunk position, which is the only way a
            // repeated note is visible on a key the slime is standing on.
            transform.localPosition = restLocalPos + new Vector3(0f, MeshLocalY(targetDip, PhysicalSinkLocal()), 0f);
        }

        // The motor's sink is in world units; this mesh moves in its parent's local space.
        private float PhysicalSinkLocal()
        {
            if (owner == null) return 0f;
            float scale = transform.parent != null ? Mathf.Abs(transform.parent.lossyScale.y) : 1f;
            return owner.PhysicalSink / Mathf.Max(1e-4f, scale);
        }

        /// Local Y for the mesh so the key reads `targetDip` below rest in total, given the motor has
        /// already sunk it by `physicalSink`. Positive result = the mesh rides UP relative to the motor
        /// (a re-strike popping a held-down key back open).
        public static float MeshLocalY(float targetDip, float physicalSink) => physicalSink - targetDip;

        /// How much of the dip is momentarily released by a re-strike: full `lift` at the instant of
        /// the second strike, decaying linearly to 0 over `fall`. The key pops up, then sinks again.
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

        /// The MIDI note a key mesh represents. Mirrors PianoKeyboardBuilder: PianoKey.001 = 21 (A0)
        /// through PianoKey.088 = 108 (C8), covering black keys too. -1 when the name doesn't parse.
        public static int MidiFromName(string name)
        {
            int idx = ParseKeyIndex(name);
            if (idx < 1 || idx > 88) return -1;
            return 20 + idx;
        }
    }
}
