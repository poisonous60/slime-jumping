using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace SlimeJumping.Platforms.Piano
{
    /// A small pool of AudioSources that plays a voiced chord as several simultaneous pitched piano
    /// notes — the audio half of harmonize mode (the physical keys only sink visually). Mirrors
    /// SoundService's pooled-source + soft-steal pattern. Clips come from the keyboard's own keys (a
    /// midi->clip map built at construction); notes with no exact key sample (accidentals)
    /// pitch-shift from the nearest key (≤ ~1 semitone on a fully-chromatic set).
    ///
    /// Two things here are performance expression, not mixing:
    /// • A note RINGS at full gain for its measured length and is only killed at the end, by a short
    ///   exponential damper. The clips are real piano recordings that already decay on their own; the
    ///   old linear fade across the whole tail fought that decay and was the single loudest reason a
    ///   landing read as "a sample being played" rather than a key being struck.
    /// • Cutoff follows velocity. A real piano hammer is a hardening spring, so a hard strike is not
    ///   just louder but brighter; with one sample per key we can only take brightness away, so a
    ///   soft strike is filtered down toward its fundamental instead of merely turned down.
    public class PianoVoicePool
    {
        /// How a struck note behaves after its attack. Set from HarmonizeSettings — every number is
        /// a config knob, nothing here is a constant.
        public struct Envelope
        {
            public float damperSeconds;
            public bool brightness;
            public float softHz, hardHz, harmonicFloor;
            public float velocityLow, velocityHigh;   // the velocity span the cutoff sweeps over
        }

        private const float DamperDecibels = 6.9f;   // e^-6.9 ≈ -60 dB by the end of the damper fall
        private const float MaxCutoffHz = 22000f;

        private class Voice
        {
            public AudioSource src;
            public AudioLowPassFilter lowpass;
            public double startDsp;
            public float playVol, ringTimer, damperTimer, damperDur;
            public bool sounding, sustained;
        }

        private struct Sample { public AudioClip clip; public float basePitch; public int baseMidi; }

        // A note waiting out its in-gesture offset (a rolled strike, or an ornament figure). Counted
        // down in Tick, so it still belongs to the landing that spawned it — no clock involved.
        private struct Pending { public int midi; public float volume, hold, fall, delay, velocity; }
        private readonly List<Pending> pending = new List<Pending>();

        /// While true, a note also rings for as long as the key is held: its stop time becomes the later
        /// of its measured length and ReleaseSustained(), so neither the transcription (which can read a
        /// few tens of ms, or plain wrong) nor the press alone decides it. Read at the note's true onset,
        /// so a delayed note firing after the key came up is not held.
        public bool sustainNewNotes;

        /// Post-attack behaviour for every note played from now on.
        public Envelope envelope = new Envelope
        {
            damperSeconds = 0.18f, brightness = false, softHz = 1600f,
            hardHz = MaxCutoffHz, harmonicFloor = 4f, velocityLow = 0.35f, velocityHigh = 0.85f,
        };

        /// Fired the instant a note actually sounds — (midi, volume, releaseSeconds). Delayed ornament
        /// notes fire from Tick when their delay runs out, so a subscriber sees the true onset time of
        /// every note in a rolled chord / Alberti figure / chiptune burst. The keyboard uses this to
        /// press the matching physical key, which is why no ornament has to wire up visuals itself.
        public event System.Action<int, float, float> NotePlayed;

        private readonly GameObject host;
        private readonly List<Voice> voices = new List<Voice>();
        private readonly Dictionary<int, Sample> byMidi = new Dictionary<int, Sample>();
        private readonly List<int> sortedMidis = new List<int>();

        public PianoVoicePool(Transform parent, int size, AudioMixerGroup group, IReadOnlyList<PianoKey> keys)
        {
            host = new GameObject("PianoVoices");
            host.transform.SetParent(parent, false);

            int n = Mathf.Max(1, size);
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject($"Voice_{i}");
                go.transform.SetParent(host.transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                src.spatialBlend = 0f;
                src.outputAudioMixerGroup = group;
                var lp = go.AddComponent<AudioLowPassFilter>();
                lp.cutoffFrequency = MaxCutoffHz;
                voices.Add(new Voice { src = src, lowpass = lp });
            }

            BuildMap(keys);
        }

        private void BuildMap(IReadOnlyList<PianoKey> keys)
        {
            if (keys == null) return;
            for (int i = 0; i < keys.Count; i++)
            {
                var k = keys[i];
                if (k == null || k.Clip == null || byMidi.ContainsKey(k.MidiNote)) continue;
                byMidi[k.MidiNote] = new Sample { clip = k.Clip, basePitch = k.Pitch, baseMidi = k.MidiNote };
            }
            sortedMidis.AddRange(byMidi.Keys);
            sortedMidis.Sort();
        }

        // Exact sample if present, else the nearest key sample transposed by 2^(semitones/12).
        private bool Resolve(int midi, out AudioClip clip, out float pitch)
        {
            if (byMidi.TryGetValue(midi, out var exact))
            {
                clip = exact.clip; pitch = exact.basePitch; return true;
            }
            if (sortedMidis.Count == 0) { clip = null; pitch = 1f; return false; }

            int nearest = sortedMidis[0];
            int bestDist = Mathf.Abs(midi - nearest);
            for (int i = 1; i < sortedMidis.Count; i++)
            {
                int d = Mathf.Abs(midi - sortedMidis[i]);
                if (d < bestDist) { bestDist = d; nearest = sortedMidis[i]; }
            }
            var s = byMidi[nearest];
            clip = s.clip;
            pitch = s.basePitch * Mathf.Pow(2f, (midi - s.baseMidi) / 12f);
            return clip != null;
        }

        // Free voice first; then one already under the damper (nearest to silent); only then a voice
        // still ringing, and of those the QUIETEST. Ring-phase voices sit at full gain now, so
        // "nearest to done" — which used to also mean "already faded out" — would hard-cut a loud
        // note and click.
        private Voice Acquire()
        {
            Voice damping = null, ringing = null;
            for (int i = 0; i < voices.Count; i++)
            {
                var v = voices[i];
                if (!v.sounding || !v.src.isPlaying) return v;
                if (v.ringTimer <= 0f)
                {
                    if (damping == null || v.damperTimer < damping.damperTimer) damping = v;
                }
                else if (ringing == null || v.playVol < ringing.playVol) ringing = v;
            }
            var steal = damping != null ? damping : ringing;
            if (steal == null) return voices[0];
            steal.src.Stop();
            return steal;
        }

        /// A soft strike is darker, not merely quieter. One sample per key means brightness can only
        /// be removed, so cutoff falls toward the note's own fundamental as velocity drops — floored
        /// at a few harmonics up so a quiet high note does not vanish.
        /// The sweep runs over [velocityLow, velocityHigh], not 0..1: transcribed velocities sit in a
        /// narrow band (~0.4-0.8 across the rotation), and mapping that band onto the middle of a
        /// 0..1 ramp leaves a light touch barely darker than a heavy one.
        private float CutoffFor(int midi, float velocity)
        {
            if (!envelope.brightness) return MaxCutoffHz;
            float fundamental = 440f * Mathf.Pow(2f, (midi - 69) / 12f);
            float t = Mathf.InverseLerp(envelope.velocityLow, envelope.velocityHigh, velocity);
            float byVelocity = Mathf.Lerp(envelope.softHz, envelope.hardHz, t);
            float floor = fundamental * Mathf.Max(1f, envelope.harmonicFloor);
            return Mathf.Clamp(Mathf.Max(byVelocity, floor), 10f, MaxCutoffHz);
        }

        /// Play one note at `volume`, held for `holdSeconds` before the damper falls.
        /// `delaySeconds` > 0 queues it instead — how a strike unfolds over its measured spread.
        public void Play(int midi, float volume, float holdSeconds, float delaySeconds)
            => PlayExpressive(midi, volume, holdSeconds, 0f, delaySeconds, -1f);

        public void Play(int midi, float volume, float holdSeconds)
            => PlayExpressive(midi, volume, holdSeconds, 0f, 0f, -1f);

        /// As Play, with the note's own measured tail and velocity.
        /// `fallSeconds` <= 0 uses the configured damper time (no pedal was down in the source).
        /// `velocity01` < 0 means "take it from the volume" — the ornament paths, which carry no
        /// dynamics of their own.
        public void PlayExpressive(int midi, float volume, float holdSeconds, float fallSeconds,
                                   float delaySeconds, float velocity01)
        {
            if (delaySeconds > 0f)
            {
                // Kept sorted by remaining delay so a long frame that expires several notes at once
                // still fires them in order (a rolled strike must keep its direction).
                var p = new Pending { midi = midi, volume = volume, hold = holdSeconds, fall = fallSeconds,
                                      delay = delaySeconds, velocity = velocity01 };
                int i = pending.Count;
                while (i > 0 && pending[i - 1].delay > delaySeconds) i--;
                pending.Insert(i, p);
                return;
            }
            PlayNow(midi, volume, holdSeconds, fallSeconds, velocity01);
        }

        private void PlayNow(int midi, float volume, float holdSeconds, float fallSeconds, float velocity01)
        {
            if (!Resolve(midi, out var clip, out var pitch)) return;
            float fall = fallSeconds > 0f ? fallSeconds : Mathf.Max(0.02f, envelope.damperSeconds);
            // Subscribers (the key visuals) want how long the note is audible, not just held.
            NotePlayed?.Invoke(midi, volume, holdSeconds + fall);

            var v = Acquire();
            v.src.clip = clip;
            v.src.pitch = pitch;
            v.src.volume = volume;
            v.src.time = 0f;
            if (v.lowpass != null)
                v.lowpass.cutoffFrequency = CutoffFor(midi, velocity01 < 0f ? Mathf.Clamp01(volume) : velocity01);
            v.src.Play();

            v.playVol = volume;
            v.ringTimer = Mathf.Max(0.02f, holdSeconds);
            v.damperDur = fall;
            v.damperTimer = fall;
            v.sounding = true;
            v.sustained = sustainNewNotes;
            v.startDsp = AudioSettings.dspTime;
        }

        /// Key up: the held notes go back to their own measured length. A note whose length already ran
        /// out under the press damps now; one that is still longer than the press rings out the rest.
        /// The stop time is the LATER of the two, never one of them chosen over the other.
        public void ReleaseSustained()
        {
            sustainNewNotes = false;
            for (int i = 0; i < voices.Count; i++)
                voices[i].sustained = false;
        }

        /// Hold at full gain (the recording decays by itself), then fall: an exponential drop to
        /// silence over the note's tail — the song's own pedal tail when it had one, the configured
        /// damper time when it did not. Also fires any note whose in-gesture offset ran out.
        public void Tick(float dt)
        {
            int due = 0;
            for (int i = 0; i < pending.Count; i++)     // sorted by delay: the due ones are at the front
            {
                var p = pending[i];
                p.delay -= dt;
                pending[i] = p;
                if (p.delay <= 0f) due = i + 1;
            }
            for (int i = 0; i < due; i++)
                PlayNow(pending[i].midi, pending[i].volume, pending[i].hold, pending[i].fall, pending[i].velocity);
            if (due > 0) pending.RemoveRange(0, due);

            for (int i = 0; i < voices.Count; i++)
            {
                var v = voices[i];
                if (!v.sounding) continue;
                if (v.sustained)
                {
                    // The measured length still runs down — the key being held only blocks the damper,
                    // it does not restart the note. So a long measured note that outlives the press
                    // keeps the remainder of its own length after key-up.
                    if (v.ringTimer > 0f) v.ringTimer -= dt;
                    continue;
                }
                if (v.ringTimer > 0f)
                {
                    v.ringTimer -= dt;
                    continue;                                   // untouched: the sample's own decay
                }
                v.damperTimer -= dt;
                float fallen = 1f - Mathf.Clamp01(v.damperTimer / v.damperDur);
                v.src.volume = v.playVol * Mathf.Exp(-DamperDecibels * fallen);
                if (v.damperTimer <= 0f)
                {
                    v.src.Stop();
                    v.sounding = false;
                }
            }
        }

        public void Dispose()
        {
            NotePlayed = null;
            if (host != null) Object.Destroy(host);
            pending.Clear();
            voices.Clear();
            byMidi.Clear();
            sortedMidis.Clear();
        }
    }
}
