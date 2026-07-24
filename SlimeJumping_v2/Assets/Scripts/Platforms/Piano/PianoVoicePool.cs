using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace SlimeJumping.Platforms.Piano
{
    /// A small pool of AudioSources that plays a voiced chord as several simultaneous pitched piano
    /// notes with a linear release tail — the audio half of harmonize mode (the physical keys only
    /// sink visually). Mirrors SoundService's pooled-source + soft-steal pattern. Clips come from the
    /// keyboard's own keys (a midi->clip map built at construction); notes with no exact key sample
    /// (accidentals) pitch-shift from the nearest key (≤ ~1 semitone on a fully-chromatic set).
    public class PianoVoicePool
    {
        private class Voice
        {
            public AudioSource src;
            public double startDsp;
            public float playVol, releaseDur, releaseTimer;
            public bool releasing;
        }

        private struct Sample { public AudioClip clip; public float basePitch; public int baseMidi; }

        // A note waiting out its ornament delay (rolled chord / Alberti / chiptune burst). Counted down
        // in Tick, so the whole figure still belongs to the landing that spawned it — no clock involved.
        private struct Pending { public int midi; public float volume, release, delay; }
        private readonly List<Pending> pending = new List<Pending>();

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
                voices.Add(new Voice { src = src });
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

        private Voice Acquire()
        {
            Voice steal = null;
            for (int i = 0; i < voices.Count; i++)
            {
                var v = voices[i];
                if (!v.releasing || !v.src.isPlaying) return v;             // free
                if (steal == null || v.releaseTimer < steal.releaseTimer) steal = v; // nearest to done
            }
            if (steal != null) steal.src.Stop();
            return steal != null ? steal : voices[0];
        }

        /// Play one chord note at `volume`, fading to silence over `releaseSeconds` (the tail).
        /// `delaySeconds` > 0 queues it instead — used by the ornaments to spread a chord in time.
        public void Play(int midi, float volume, float releaseSeconds, float delaySeconds)
        {
            if (delaySeconds > 0f)
            {
                // Kept sorted by remaining delay so a long frame that expires several ornament notes at
                // once still fires them in their authored order (a rolled chord must stay low-to-high).
                var p = new Pending { midi = midi, volume = volume, release = releaseSeconds, delay = delaySeconds };
                int i = pending.Count;
                while (i > 0 && pending[i - 1].delay > delaySeconds) i--;
                pending.Insert(i, p);
                return;
            }
            Play(midi, volume, releaseSeconds);
        }

        public void Play(int midi, float volume, float releaseSeconds)
        {
            if (!Resolve(midi, out var clip, out var pitch)) return;
            NotePlayed?.Invoke(midi, volume, releaseSeconds);   // the note the score asked for, not the sampled one

            var v = Acquire();
            v.src.clip = clip;
            v.src.pitch = pitch;
            v.src.volume = volume;
            v.src.time = 0f;
            v.src.Play();

            v.playVol = volume;
            v.releaseDur = Mathf.Max(0.05f, releaseSeconds);
            v.releaseTimer = v.releaseDur;
            v.releasing = true;
            v.startDsp = AudioSettings.dspTime;
        }

        /// Advance every voice's linear release fade; stop and free those that finished. Also fires any
        /// ornament note whose delay ran out.
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
            for (int i = 0; i < due; i++) Play(pending[i].midi, pending[i].volume, pending[i].release);
            if (due > 0) pending.RemoveRange(0, due);

            for (int i = 0; i < voices.Count; i++)
            {
                var v = voices[i];
                if (!v.releasing) continue;
                v.releaseTimer -= dt;
                float k = Mathf.Clamp01(v.releaseTimer / v.releaseDur);
                v.src.volume = v.playVol * k;
                if (v.releaseTimer <= 0f)
                {
                    v.src.Stop();
                    v.releasing = false;
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
