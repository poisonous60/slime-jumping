using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace SlimeJumping.Platforms.Piano
{
    /// Progression-harmonize half of the PianoKeyboard (opt-in via config.harmonize.enabled). Each
    /// landing advances a chord progression one step, plays that step's voicing (+ a position-snapped
    /// melody) through a PianoVoicePool, thickens by velocity, sinks the chord's keys visually, and —
    /// when branching is authored — lets the landing zone pick the next step. When disabled the
    /// keyboard plays each key's literal note exactly as before (this whole file early-returns).
    public partial class PianoKeyboard
    {
        private PianoVoicePool harmonizePool;
        private AudioMixer harmonizeMixer;
        private int harmonizeIndex;
        private int harmonizeLoopStart;   // first step of the schema we're currently inside
        private bool anyContactingPrev, harmonizePoolBuilt;
        private float lastAdvanceTime;

        private bool spanCached;
        private float keyboardMinX, keyboardMaxX;

        // per-frame / per-press scratch (no GC churn)
        private readonly List<PianoKey> contactingKeys = new List<PianoKey>();
        private readonly List<int> chordScratch = new List<int>();
        private readonly List<int> melodyScratch = new List<int>();
        private readonly HashSet<int> playedScratch = new HashSet<int>();

        // Called at the end of the FixedUpdate contact loop.
        private void HarmonizeStep(bool anyContacting, float impact)
        {
            if (config == null) return;
            var h = config.harmonize;

            if (!h.enabled)
            {
                if (harmonizePoolBuilt) TeardownHarmonize();
                return;
            }
            if (!harmonizePoolBuilt) SetupHarmonize(h);

            if (anyContacting && !anyContactingPrev) OnHarmonizePress(h, impact); // aggregate rising edge = a landing
            anyContactingPrev = anyContacting;
        }

        private void Update()
        {
            harmonizePool?.Tick(Time.deltaTime);

            // Idle or left the piano → restart the CURRENT schema, not the whole list. Jumping to 0
            // would undo every schema the player rolled into, so a platformer's normal pauses would
            // pin the piano to schema 1 forever.
            if (config != null && harmonizePoolBuilt && config.harmonize.enabled
                && Time.time - lastAdvanceTime > config.harmonize.resetIdleSeconds)
                harmonizeIndex = Mathf.Clamp(harmonizeLoopStart, 0, Mathf.Max(0, config.harmonize.progression.Count - 1));
        }

        private void SetupHarmonize(HarmonizeSettings h)
        {
            AudioMixerGroup group = null;
            for (int i = 0; i < keys.Count; i++)
                if (keys[i] != null && keys[i].OutputGroup != null) { group = keys[i].OutputGroup; break; }
            harmonizeMixer = group != null ? group.audioMixer : null;

            harmonizePool = new PianoVoicePool(transform, h.voicePoolSize, group, keys);
            for (int i = 0; i < keys.Count; i++)
                if (keys[i] != null) keys[i].SetMuted(true);

            harmonizeIndex = 0;
            harmonizeLoopStart = 0;
            lastAdvanceTime = Time.time;
            anyContactingPrev = false;
            harmonizePoolBuilt = true;
        }

        private void TeardownHarmonize()
        {
            harmonizePool?.Dispose();
            harmonizePool = null;
            harmonizeMixer = null;
            for (int i = 0; i < keys.Count; i++)
                if (keys[i] != null) keys[i].SetMuted(false);
            harmonizePoolBuilt = false;
            anyContactingPrev = false;
        }

        private void OnHarmonizePress(HarmonizeSettings h, float impact)
        {
            if (harmonizePool == null || h.progression == null || h.progression.Count == 0) return;

            harmonizeIndex = Mathf.Clamp(harmonizeIndex, 0, h.progression.Count - 1);
            ChordStep step = h.progression[harmonizeIndex];

            float vel = config.keys.NormalizedImpact(impact);
            float baseVol = Mathf.Clamp01(h.velocityToVolume.Evaluate(vel)) * h.masterVolume;
            bool isDyad = contactingKeys.Count >= 2;
            float melodyVol = baseVol * (isDyad ? h.dyadVolumeScale : 1f);   // melody = lead, stays full
            float chordVol = baseVol;
            float release = step.releaseSeconds > 0f ? step.releaseSeconds : h.defaultReleaseSeconds;

            // chord voicing (absolute MIDI)
            HarmonyMath.BuildChord(step, h.tonicMidi, chordScratch);

            // melody note(s) from the landed key(s); bump a colliding dyad up an octave so both ring
            melodyScratch.Clear();
            for (int i = 0; i < contactingKeys.Count; i++)
            {
                if (contactingKeys[i] == null) continue;
                MelodySnapMode snap = step.overrideSnapMode ? step.snapMode : h.snapMode;
                int m = HarmonyMath.SnapMelody(contactingKeys[i].MidiNote, h, step.offsetsFromTonic, snap);
                while (melodyScratch.Contains(m)) m += 12;
                melodyScratch.Add(m);
            }

            // velocity density thickens the CHORD (octave-double its top) on strong landings — not the
            // melody, so the ducked chordVol never touches the full-volume lead.
            HarmonyMath.ApplyVelocityDensity(chordScratch, vel, h);

            // play: melody first (louder / dyad-scaled), then the chord; dedupe so nothing double-triggers
            playedScratch.Clear();
            for (int i = 0; i < melodyScratch.Count; i++)
                if (playedScratch.Add(melodyScratch[i])) harmonizePool.Play(melodyScratch[i], melodyVol, release);
            for (int i = 0; i < chordScratch.Count; i++)
                if (playedScratch.Add(chordScratch[i])) harmonizePool.Play(chordScratch[i], chordVol, release);

            // visual sink: each chord tone's nearest physical (white) key
            float sinkDepth = h.visualSinkDepth * Mathf.Lerp(1f, h.visualDepthVelocityScale, vel);
            for (int i = 0; i < chordScratch.Count; i++)
            {
                var nk = NearestKey(chordScratch[i]);
                if (nk != null) nk.VisualPress(sinkDepth, h.visualHoldSeconds);
            }

            if (h.reverbEnabled && harmonizeMixer != null)
                harmonizeMixer.SetFloat(h.reverbWetParam, h.velocityToReverb.Evaluate(vel));

            LandingZone zone = h.branchingEnabled ? HarmonyMath.Zone(NormalizedLandingX(), h) : LandingZone.Center;
            float roll = h.randomBranching ? Random.value : -1f;   // < 0 disables the weighted path
            bool wasLoopEnd = step.nextOptions != null && step.nextOptions.Length > 0;
            harmonizeIndex = HarmonyMath.NextIndex(step, zone, harmonizeIndex, h.progression.Count, roll);
            if (wasLoopEnd) harmonizeLoopStart = harmonizeIndex;   // a loop end always lands on a schema start
            lastAdvanceTime = Time.time;
        }

        private PianoKey NearestKey(int midi)
        {
            PianoKey best = null;
            int bestDist = int.MaxValue;
            for (int i = 0; i < keys.Count; i++)
            {
                var k = keys[i];
                if (k == null) continue;
                int d = Mathf.Abs(k.MidiNote - midi);
                if (d < bestDist) { bestDist = d; best = k; }
            }
            return best;
        }

        // Mean x of the landed keys, normalized across the keyboard's key span (0 = left, 1 = right).
        private float NormalizedLandingX()
        {
            if (!spanCached) CacheSpan();
            if (keyboardMaxX <= keyboardMinX) return 0.5f;

            float sum = 0f; int n = 0;
            for (int i = 0; i < contactingKeys.Count; i++)
            {
                var k = contactingKeys[i];
                if (k == null || k.Contact == null) continue;
                sum += k.Contact.bounds.center.x; n++;
            }
            if (n == 0) return 0.5f;
            return Mathf.InverseLerp(keyboardMinX, keyboardMaxX, sum / n);
        }

        private void CacheSpan()
        {
            float mn = float.MaxValue, mx = float.MinValue;
            for (int i = 0; i < keys.Count; i++)
            {
                var k = keys[i];
                if (k == null || k.Contact == null) continue;
                Bounds b = k.Contact.bounds;
                if (b.min.x < mn) mn = b.min.x;
                if (b.max.x > mx) mx = b.max.x;
            }
            keyboardMinX = mn; keyboardMaxX = mx; spanCached = true;
        }
    }
}
