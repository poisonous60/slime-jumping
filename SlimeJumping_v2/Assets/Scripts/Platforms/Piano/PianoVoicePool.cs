using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace SlimeJumping.Platforms.Piano
{
    /// Pool of AudioSources playing a voiced chord as simultaneous pitched piano notes (audio half of
    /// harmonize mode). Clips come from the keyboard's own keys; a note with no exact sample pitch-shifts
    /// from the nearest key. Two deliberate choices: a note rings at full gain and is killed only by a
    /// short exponential damper (a linear fade across the tail fights the recording's own decay and reads
    /// as "a sample being played"); and cutoff follows velocity, so a soft strike is darker, not just quieter.
    public class PianoVoicePool
    {
        /// Post-attack behaviour of a struck note. Every field is a config knob from HarmonizeSettings.
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

        // A note waiting out its in-gesture offset (rolled strike, ornament figure); counted down in
        // Tick, so it still belongs to the landing that spawned it — no clock involved.
        private struct Pending { public int midi; public float volume, hold, fall, delay, velocity; }
        private readonly List<Pending> pending = new List<Pending>();

        /// While true a note's stop time is the LATER of its measured length and ReleaseSustained().
        /// Read at the note's true onset, so a delayed note firing after key-up is not held.
        public bool sustainNewNotes;

        /// Post-attack behaviour for every note played from now on.
        public Envelope envelope = new Envelope
        {
            damperSeconds = 0.18f, brightness = false, softHz = 1600f,
            hardHz = MaxCutoffHz, harmonicFloor = 4f, velocityLow = 0.35f, velocityHigh = 0.85f,
        };

        /// Fired the instant a note actually sounds — (midi, volume, releaseSeconds). Delayed ornament
        /// notes fire from Tick, so subscribers (the keyboard's key visuals) see every note's true onset.
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

        // Free voice, then one already under the damper, then the QUIETEST still ringing. Ring-phase
        // voices sit at full gain, so stealing "nearest to done" would hard-cut a loud note and click.
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

        /// One sample per key means brightness can only be removed: cutoff falls toward the note's own
        /// fundamental as velocity drops, floored a few harmonics up. Sweeps [velocityLow, velocityHigh],
        /// not 0..1 — transcribed velocities sit in a narrow band that a 0..1 ramp flattens.
        private float CutoffFor(int midi, float velocity)
        {
            if (!envelope.brightness) return MaxCutoffHz;
            float fundamental = 440f * Mathf.Pow(2f, (midi - 69) / 12f);
            float t = Mathf.InverseLerp(envelope.velocityLow, envelope.velocityHigh, velocity);
            float byVelocity = Mathf.Lerp(envelope.softHz, envelope.hardHz, t);
            float floor = fundamental * Mathf.Max(1f, envelope.harmonicFloor);
            return Mathf.Clamp(Mathf.Max(byVelocity, floor), 10f, MaxCutoffHz);
        }

        /// Play one note at `volume`, held for `holdSeconds` before the damper falls; `delaySeconds` > 0 queues it.
        public void Play(int midi, float volume, float holdSeconds, float delaySeconds)
            => PlayExpressive(midi, volume, holdSeconds, 0f, delaySeconds, -1f);

        public void Play(int midi, float volume, float holdSeconds)
            => PlayExpressive(midi, volume, holdSeconds, 0f, 0f, -1f);

        /// As Play, with the note's own measured tail and velocity. `fallSeconds` <= 0 uses the configured
        /// damper time; `velocity01` < 0 takes it from the volume (the ornament paths carry no dynamics).
        public void PlayExpressive(int midi, float volume, float holdSeconds, float fallSeconds,
                                   float delaySeconds, float velocity01)
        {
            if (delaySeconds > 0f)
            {
                // Sorted by remaining delay: a long frame expiring several notes at once must still
                // fire them in order (a rolled strike has to keep its direction).
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

        /// Key up: held notes revert to their own measured length — stop time is the LATER of that
        /// length and the press, never one chosen over the other.
        public void ReleaseSustained()
        {
            sustainNewNotes = false;
            for (int i = 0; i < voices.Count; i++)
                voices[i].sustained = false;
        }

        /// Hold at full gain (the recording decays by itself), then an exponential drop over the note's
        /// tail. Also fires any note whose in-gesture offset ran out.
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
                    // The measured length keeps running down — holding the key only blocks the damper,
                    // so a note that outlives the press keeps its remainder after key-up.
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
