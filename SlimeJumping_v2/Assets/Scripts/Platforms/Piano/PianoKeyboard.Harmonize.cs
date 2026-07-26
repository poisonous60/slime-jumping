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
        private readonly List<int> prevChordScratch = new List<int>();   // voice-leading / suspension reference
        private PianoPedal[] pedals;
        private readonly Dictionary<int, PianoKeyVisual> keyVisuals = new Dictionary<int, PianoKeyVisual>();
        private readonly List<int> keyMidis = new List<int>();   // sorted, for the nearest-key fallback
        private int prevLandingMidi = -1;          // glissando: where the last landing was
        private int susIndex = -1, susNote = 0;    // suspension: which voice is held, and by what note
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

        private void HarmonizeUpdate()
        {
            harmonizePool?.Tick(Time.deltaTime);

            // Idle or left the piano → restart the CURRENT schema, not the whole list. Jumping to 0
            // would undo every schema the player rolled into, so a platformer's normal pauses would
            // pin the piano to schema 1 forever.
            if (config != null && harmonizePoolBuilt && config.harmonize.enabled
                && Time.time - lastAdvanceTime > config.harmonize.resetIdleSeconds)
                harmonizeIndex = Mathf.Clamp(harmonizeLoopStart, 0, Mathf.Max(0, config.harmonize.Progression.Count - 1));
        }

        private void HarmonizeDisable()
        {
            // Avoid leaking the voice pool / NotePlayed subscription when the component is disabled or
            // the scene unloads. Without this, Update() (and its pool Tick) stops while notes are still
            // mid-release, freezing them at their last volume and leaving the AudioSources alive.
            if (harmonizePoolBuilt) TeardownHarmonize();
            ReleaseKeyVisuals();
        }

        private void SetupHarmonize(HarmonizeSettings h)
        {
            AudioMixerGroup group = null;
            for (int i = 0; i < keys.Count; i++)
                if (keys[i] != null && keys[i].OutputGroup != null) { group = keys[i].OutputGroup; break; }
            harmonizeMixer = group != null ? group.audioMixer : null;

            harmonizePool = new PianoVoicePool(transform, h.voicePoolSize, group, keys);
            harmonizePool.NotePlayed += OnNoteSounded;   // every sounding note presses its own key
            for (int i = 0; i < keys.Count; i++)
                if (keys[i] != null) keys[i].SetMuted(true);

            pedals = transform.root.GetComponentsInChildren<PianoPedal>(true);
            EnsureKeyVisuals();

            harmonizeIndex = 0;
            harmonizeLoopStart = 0;
            lastAdvanceTime = Time.time;
            anyContactingPrev = false;
            prevLandingMidi = -1;
            prevChordScratch.Clear();
            harmonizePoolBuilt = true;
        }

        private void TeardownHarmonize()
        {
            if (harmonizePool != null) harmonizePool.NotePlayed -= OnNoteSounded;
            harmonizePool?.Dispose();
            harmonizePool = null;

            // Snap keys back up, but KEEP the map: auto-play drives the same 88 visuals and may be
            // running (or about to) while harmonize is off. It is released in HarmonizeDisable.
            foreach (var kv in keyVisuals)
                if (kv.Value != null) kv.Value.ResetNow();

            harmonizeMixer = null;
            for (int i = 0; i < keys.Count; i++)
                if (keys[i] != null) keys[i].SetMuted(false);
            harmonizePoolBuilt = false;
            anyContactingPrev = false;
        }

        private void OnHarmonizePress(HarmonizeSettings h, float impact)
        {
            var prog = h.Progression;   // the song's own loop while one is in rotation
            if (harmonizePool == null || prog == null || prog.Count == 0) return;

            harmonizeIndex = Mathf.Clamp(harmonizeIndex, 0, prog.Count - 1);
            ChordStep step = prog[harmonizeIndex];

            float vel = config.keys.NormalizedImpact(impact);
            float baseVol = Mathf.Clamp01(h.velocityToVolume.Evaluate(vel)) * h.masterVolume;
            bool isDyad = contactingKeys.Count >= 2;
            float melodyVol = baseVol * (isDyad ? h.dyadVolumeScale : 1f);   // melody = lead, stays full
            float chordVol = baseVol;
            float release = step.releaseSeconds > 0f ? step.releaseSeconds : h.defaultReleaseSeconds;
            release *= SustainMultiplier(h.Ornaments);   // damper up while the slime stands on a pedal

            // chord voicing (absolute MIDI)
            HarmonyMath.BuildChord(step, h.TonicMidi, chordScratch);

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

            // Voice leading first (it re-octaves the authored voicing), then density — otherwise the
            // octave-doubled note would get re-octaved right back down.
            var orn = h.Ornaments;
            if (orn.voiceLeading)
            {
                int authoredTop = chordScratch[0];
                for (int i = 1; i < chordScratch.Count; i++) if (chordScratch[i] > authoredTop) authoredTop = chordScratch[i];
                int ceiling = authoredTop + Mathf.Max(0, Mathf.RoundToInt(h.minMelodyGap) - 1);   // stay under the melody
                HarmonyMath.VoiceLead(chordScratch, prevChordScratch, orn.voiceLeadingKeepLow, ceiling);
            }

            // velocity density thickens the CHORD (octave-double its top) on strong landings — not the
            // melody, so the ducked chordVol never touches the full-volume lead.
            HarmonyMath.ApplyVelocityDensity(chordScratch, vel, h);
            if (orn.dropTwo) HarmonyMath.DropTwo(chordScratch);
            FindSuspension(orn);

            // play: melody first (louder / dyad-scaled), then the chord as whatever figure is enabled;
            // dedupe so nothing double-triggers.
            playedScratch.Clear();
            MelodySnapMode melSnap = step.overrideSnapMode ? step.snapMode : h.snapMode;
            for (int i = 0; i < melodyScratch.Count; i++)
            {
                int m = melodyScratch[i];
                if (!playedScratch.Add(m)) continue;
                if (orn.appoggiatura)
                {
                    // lean on the note above, then resolve into the real melody note
                    int lean = HarmonyMath.NeighborAbove(m, h, step.offsetsFromTonic, melSnap);
                    if (lean > m)
                    {
                        playedScratch.Add(lean);   // a chord tone on the same pitch must not re-strike it
                        harmonizePool.Play(lean, melodyVol, release);
                        harmonizePool.Play(m, melodyVol, release, orn.appoggiaturaSeconds);
                        continue;
                    }
                }
                harmonizePool.Play(m, melodyVol, release);
            }
            PlayChordFigure(orn, chordVol, release, vel);

            if (orn.pedalPoint && harmonizeIndex == harmonizeLoopStart)   // first chord of a schema
                harmonizePool.Play(h.TonicMidi + orn.pedalOffsetFromTonic,
                                   baseVol * orn.pedalVolume, orn.pedalReleaseSeconds * SustainMultiplier(orn));

            if (orn.resonance && chordScratch.Count > 0)
            {
                int root = chordScratch[0];
                harmonizePool.Play(root + 12, chordVol * orn.resonanceVolume, release);
                harmonizePool.Play(root + 19, chordVol * orn.resonanceVolume, release);   // octave + fifth
            }

            if (orn.glissando && melodyScratch.Count > 0) PlayGlissando(orn, h, step, melSnap, melodyVol, release);
            prevLandingMidi = melodyScratch.Count > 0 ? melodyScratch[0] : prevLandingMidi;

            prevChordScratch.Clear();
            prevChordScratch.AddRange(chordScratch);

            // NOTE: key visuals are NOT driven from here. Every note that sounds — including the
            // delayed ones inside an ornament — comes back through PianoVoicePool.NotePlayed and
            // presses its own key there (OnNoteSounded), so sound and picture can't drift apart.

            if (h.reverbEnabled && harmonizeMixer != null)
                harmonizeMixer.SetFloat(h.reverbWetParam, h.velocityToReverb.Evaluate(vel));

            LandingZone zone = h.branchingEnabled ? HarmonyMath.Zone(NormalizedLandingX(), h) : LandingZone.Center;
            float roll = h.randomBranching ? Random.value : -1f;   // < 0 disables the weighted path
            bool wasLoopEnd = step.nextOptions != null && step.nextOptions.Length > 0;
            harmonizeIndex = HarmonyMath.NextIndex(step, zone, harmonizeIndex, prog.Count, roll);
            if (wasLoopEnd) harmonizeLoopStart = harmonizeIndex;   // a loop end always lands on a schema start
            lastAdvanceTime = Time.time;
        }

        // One PianoKeyVisual per key MESH, covering all 88 — the 52 white ones ride their KeyMotor, the
        // 36 black ones are inert meshes under the piano root. MIDI comes from the mesh name using the
        // builder's own rule (PianoKey.001 = 21), so accidentals light up the real black key instead of
        // the nearest white one. Components are added at runtime so the scene and PianoKeyboardBuilder
        // stay untouched.
        //
        // Shared: both harmonize and auto-play press these, and either may be on without the other, so
        // this is idempotent and the map outlives a harmonize teardown.
        private void EnsureKeyVisuals()
        {
            if (keyVisuals.Count > 0) return;
            keyMidis.Clear();

            var meshes = transform.root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < meshes.Length; i++)
            {
                var t = meshes[i];
                int midi = PianoKeyVisual.MidiFromName(t.name);
                if (midi < 0 || keyVisuals.ContainsKey(midi)) continue;

                var vis = t.GetComponent<PianoKeyVisual>();
                if (vis == null) vis = t.gameObject.AddComponent<PianoKeyVisual>();
                vis.enabled = true;   // a disabled component takes Press() but never runs LateUpdate
                vis.SetOwner(t.parent != null ? t.parent.GetComponent<PianoKey>() : null);
                keyVisuals[midi] = vis;
                keyMidis.Add(midi);
            }
            keyMidis.Sort();
        }

        private void ReleaseKeyVisuals()
        {
            foreach (var kv in keyVisuals)
                if (kv.Value != null) kv.Value.ResetNow();
            keyVisuals.Clear();
            keyMidis.Clear();
        }

        // Notes can legitimately fall outside the 88 keys — a low pedal point, an octave-doubled top,
        // a chiptune burst lifted an octave, a glissando running off the end. The pool still sounds them
        // (pitch-shifted from the nearest sample), so the nearest key must move rather than nothing.
        private PianoKeyVisual ResolveKeyVisual(int midi)
        {
            if (keyVisuals.TryGetValue(midi, out var exact)) return exact;
            if (keyMidis.Count == 0) return null;

            int nearest = keyMidis[0], bestDist = Mathf.Abs(nearest - midi);
            for (int i = 1; i < keyMidis.Count; i++)
            {
                int d = Mathf.Abs(keyMidis[i] - midi);
                if (d >= bestDist) continue;
                bestDist = d; nearest = keyMidis[i];
            }
            return keyVisuals.TryGetValue(nearest, out var near) ? near : null;
        }

        // Fired by the voice pool at each note's true onset — including the delayed notes of a rolled
        // chord, an Alberti figure, a chiptune burst or a suspension's resolution.
        private void OnNoteSounded(int midi, float volume, float releaseSeconds)
        {
            if (config == null) return;
            var h = config.harmonize;
            if (volume < h.visualMinVolume) return;
            var vis = ResolveKeyVisual(midi);
            if (vis == null) return;

            float loud = h.masterVolume > 0.0001f ? Mathf.Clamp01(volume / h.masterVolume) : 1f;
            float depth = h.visualSinkDepth * Mathf.Lerp(h.visualMinDepthScale, h.visualDepthVelocityScale, loud);

            // Pedal down = dampers off: the string keeps ringing after the key comes back up, so the
            // key must NOT stay pinned for the (now much longer) tail. Gated on the same toggle that
            // lengthens the audio — otherwise a pedal that doesn't sustain would still shorten the key.
            float hold = (h.Ornaments.sustainPedal && AnyPedalPressed())
                ? h.visualPedaledHoldSeconds
                : Mathf.Clamp(releaseSeconds * h.visualHoldScale, h.visualHoldSeconds, h.visualMaxHoldSeconds);

            vis.Press(depth, hold, h.visualReleaseSeconds, h.visualRearticulateLift, h.visualRearticulateSeconds);
        }

        private bool AnyPedalPressed()
        {
            if (pedals == null) return false;
            for (int i = 0; i < pedals.Length; i++)
                if (pedals[i] != null && pedals[i].IsPressed) return true;
            return false;
        }

        // Damper up: while the slime stands on any pedal every tail gets longer, exactly like the real
        // instrument. PianoPedal was a mute prop until now (it only sank and played its own click).
        private float SustainMultiplier(OrnamentSettings o)
        {
            if (!o.sustainPedal || pedals == null) return 1f;
            for (int i = 0; i < pedals.Length; i++)
                if (pedals[i] != null && pedals[i].IsPressed) return o.pedalReleaseMultiplier;
            return 1f;
        }

        // A suspension holds one note of the PREVIOUS chord over the new one and resolves it down by
        // step ("approached by the same note and resolves down by step"). Only fires when the previous
        // chord actually has a note sitting a step above one of this chord's upper voices.
        private void FindSuspension(OrnamentSettings o)
        {
            susIndex = -1;
            if (!o.suspension || prevChordScratch.Count == 0 || chordScratch.Count < 2) return;
            if (o.chiptuneArp || o.alberti) return;   // those figures re-play every tone; a held voice would double
            if (config.harmonize.Texture != null) return;   // same reason: the song texture re-plays its voices

            for (int i = chordScratch.Count - 1; i >= 1; i--)      // upper voices only — never the bass
            {
                for (int p = 0; p < prevChordScratch.Count; p++)
                {
                    int d = prevChordScratch[p] - chordScratch[i];
                    if (d != 1 && d != 2) continue;                // a step above = the suspended note
                    if (chordScratch.Contains(prevChordScratch[p])) continue;
                    susIndex = i; susNote = prevChordScratch[p];
                    return;
                }
            }
        }

        // A run in the direction the slime travelled, when it crossed a big gap. The landing note is
        // still instant; the run trails after it, so the jump distance becomes a musical gesture.
        private void PlayGlissando(OrnamentSettings o, HarmonizeSettings h, ChordStep step,
                                   MelodySnapMode snap, float vol, float release)
        {
            int from = melodyScratch[0];
            if (prevLandingMidi < 0) return;
            int span = from - prevLandingMidi;
            if (Mathf.Abs(span) < o.glissMinSemitones) return;

            int dir = span > 0 ? 1 : -1;
            int cur = from;
            for (int n = 1; n <= o.glissMaxNotes; n++)
            {
                cur = dir > 0 ? HarmonyMath.NeighborAbove(cur, h, step.offsetsFromTonic, snap)
                              : HarmonyMath.NeighborBelow(cur, h, step.offsetsFromTonic, snap);
                if (cur <= 0 || cur > 127) return;
                float fade = vol * o.glissVolume * (1f - (n - 1) / (float)Mathf.Max(1, o.glissMaxNotes));
                harmonizePool.Play(cur, fade, release * 0.5f, n * o.glissStepSeconds);
            }
        }

        // The chord, played as whichever performance figure is switched on. Every figure's FIRST note is
        // instant — only later notes are offset — so the landing and its sound never separate.
        // Precedence when several are on: chiptune > Alberti > rolled > block.
        private void PlayChordFigure(OrnamentSettings o, float vol, float release, float vel)
        {
            if (chordScratch.Count == 0) return;
            chordScratch.Sort();

            // Suspension replaces one voice at the onset and resolves it down a step shortly after.
            if (susIndex >= 0 && susIndex < chordScratch.Count)
            {
                if (playedScratch.Add(susNote)) harmonizePool.Play(susNote, vol, release);
                harmonizePool.Play(chordScratch[susIndex], vol, release, o.suspensionResolveSeconds);
                playedScratch.Add(chordScratch[susIndex]);   // the block/roll loop must not double it
            }

            // The rotation song's measured texture — it outranks every generic ornament figure
            // because it reproduces how THIS piece sounds a chord: its own note spread and
            // direction, its own ring, its own low drone, its own accompaniment level. All values
            // were measured from the transcription offline; nothing here is a preset. One landing
            // = ONE sounding gesture (user-set contract): the whole chord unfolds inside the
            // measured spread (tens of ms) and only RINGS after that — no re-strikes later.
            var tex = config.harmonize.Texture;
            if (tex != null)
            {
                float chordVol = vol * (tex.chordVolumeScale > 0f ? tex.chordVolumeScale : 1f);
                bool drone = tex.bassReleaseSeconds > 0f;   // chordScratch is sorted: index 0 = bass

                int n = chordScratch.Count;
                for (int k = 0; k < n; k++)
                {
                    int idx = tex.rollUp ? k : n - 1 - k;
                    float delay = n > 1 ? tex.spreadSeconds * k / (n - 1) : 0f;   // k=0 instant — landing contract
                    bool isBass = idx == 0;
                    float rel = drone && isBass ? tex.bassReleaseSeconds : release;
                    float vs = drone && isBass && tex.bassVolumeScale > 0f ? tex.bassVolumeScale : 1f;
                    harmonizePool.Play(chordScratch[idx], chordVol * vs, rel, delay);
                }
                return;
            }

            if (o.chiptuneArp)
            {
                // "In early video game music, arpeggios were often the only way to play a chord" —
                // cycle the chord tones fast enough that the ear fuses them into harmony.
                // Deliberately NOT run through playedScratch: re-striking the same tones over and over
                // IS the technique. Same for the Alberti figure below (its "high" note sounds twice).
                int lift = 12 * o.chiptuneOctaveUp;
                for (int i = 0; i < o.chiptuneNotes; i++)
                    harmonizePool.Play(chordScratch[i % chordScratch.Count] + lift,
                                       vol * o.chiptuneVolume, release, i * o.chiptuneStepSeconds);
                return;
            }

            if (o.alberti && vel >= o.albertiVelocity && chordScratch.Count >= 3)
            {
                // Alberti bass order: lowest, highest, middle, highest.
                int lo = 0, hi = chordScratch.Count - 1, mid = chordScratch.Count / 2;
                int[] order = { lo, hi, mid, hi };
                for (int i = 0; i < order.Length; i++)
                    harmonizePool.Play(chordScratch[order[i]], vol, release, i * o.albertiStepSeconds);
                return;
            }

            if (o.rolledChord && o.rollStepSeconds > 0f)
            {
                for (int i = 0; i < chordScratch.Count; i++)
                {
                    int idx = o.rollLowToHigh ? i : chordScratch.Count - 1 - i;
                    if (!playedScratch.Add(chordScratch[idx])) continue;
                    harmonizePool.Play(chordScratch[idx], vol, release, i * o.rollStepSeconds);
                }
                return;
            }

            for (int i = 0; i < chordScratch.Count; i++)
                if (playedScratch.Add(chordScratch[i])) harmonizePool.Play(chordScratch[i], vol, release);
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
