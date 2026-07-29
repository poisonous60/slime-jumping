using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace SlimeJumping.Platforms.Piano
{
    /// Progression-harmonize half of PianoKeyboard (opt-in via config.harmonize.enabled): each landing advances
    /// the progression, plays that step through a PianoVoicePool and sinks its keys. Disabled = whole file
    /// early-returns and every key plays its literal note.
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
        private readonly List<int> pcsScratch = new List<int>();   // pitch classes the landed key may use
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
            else if (!anyContacting && anyContactingPrev && harmonizePool != null)
                harmonizePool.ReleaseSustained();   // left the keys = key up = damper down
            anyContactingPrev = anyContacting;
        }

        private void HarmonizeUpdate()
        {
            harmonizePool?.Tick(Time.deltaTime);

            // Idle → restart the CURRENT schema, not the whole list: jumping to 0 undoes every schema the
            // player rolled into, pinning the piano to schema 1 through a platformer's normal pauses.
            if (config != null && harmonizePoolBuilt && config.harmonize.enabled
                && Time.time - lastAdvanceTime > config.harmonize.resetIdleSeconds)
                harmonizeIndex = Mathf.Clamp(harmonizeLoopStart, 0, Mathf.Max(0, config.harmonize.Progression.Count - 1));
        }

        private void HarmonizeDisable()
        {
            // Without teardown, Update() (and its pool Tick) stops while notes are mid-release: they freeze at
            // their last volume and the AudioSources stay alive.
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

            // KEEP the map: auto-play drives the same 88 visuals while harmonize is off. Released in HarmonizeDisable.
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

            // The note stops at the LATER of its measured length and the press: a few-ms (or mis-measured) note
            // must not cut off while the slime stands there, and a long one must not be cut by a quick jump.
            harmonizePool.sustainNewNotes = h.sustainWhileStanding;

            harmonizePool.envelope = new PianoVoicePool.Envelope
            {
                damperSeconds = h.damperSeconds,
                brightness = h.brightnessEnabled,
                softHz = h.brightnessSoftHz,
                hardHz = h.brightnessHardHz,
                harmonicFloor = h.brightnessHarmonicFloor,
                velocityLow = h.brightnessVelocityLow,
                velocityHigh = h.brightnessVelocityHigh,
            };

            // The song's own strike for this step, verbatim. It replaces the rebuild-and-decorate path below:
            // inventing a chord shape overwrote the song's measured voicing and made every piece sound alike.
            var previewSteps = h.PreviewSteps;
            if (h.previewGesture && previewSteps != null
                && harmonizeIndex < previewSteps.Length && previewSteps[harmonizeIndex] != null)
            {
                PlayPreviewGesture(h, previewSteps[harmonizeIndex], baseVol,
                                   SustainMultiplier(h.Ornaments));
                // Reverb send follows the same rule as loudness: the song decides, not the landing.
                AdvanceHarmonizeStep(h, step, h.previewUseLandingVelocity ? vel : 1f);
                return;
            }

            // chord voicing (absolute MIDI)
            HarmonyMath.BuildChord(step, h.TonicMidi, chordScratch);

            // melody note(s) from the landed key(s); bump a colliding dyad up an octave so both ring
            melodyScratch.Clear();
            for (int i = 0; i < contactingKeys.Count; i++)
            {
                if (contactingKeys[i] == null) continue;
                MelodySnapMode snap = h.snapLandedKeyToChord ? MelodySnapMode.ChordTones
                                    : step.overrideSnapMode ? step.snapMode : h.snapMode;
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

            // Density thickens the CHORD, not the melody, so the ducked chordVol never touches the lead.
            HarmonyMath.ApplyVelocityDensity(chordScratch, vel, h);
            if (orn.dropTwo) HarmonyMath.DropTwo(chordScratch);
            FindSuspension(orn);

            // melody first (louder / dyad-scaled), then the chord figure; dedupe so nothing double-triggers.
            playedScratch.Clear();
            MelodySnapMode melSnap = h.snapLandedKeyToChord ? MelodySnapMode.ChordTones
                                   : step.overrideSnapMode ? step.snapMode : h.snapMode;
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

            // Key visuals are NOT driven from here: every sounding note (an ornament's delayed ones included)
            // comes back through PianoVoicePool.NotePlayed and presses its own key, so the two can't drift.

            AdvanceHarmonizeStep(h, step, vel);
        }

        // Shared tail of a landing: reverb send, then walk to the next step. A preview song's
        // progression is linear data (no nextOptions), so this walks it 0,1,2,... and wraps.
        private void AdvanceHarmonizeStep(HarmonizeSettings h, ChordStep step, float vel)
        {
            if (h.reverbEnabled && harmonizeMixer != null)
                harmonizeMixer.SetFloat(h.reverbWetParam, h.velocityToReverb.Evaluate(vel));

            var prog = h.Progression;
            LandingZone zone = h.branchingEnabled ? HarmonyMath.Zone(NormalizedLandingX(), h) : LandingZone.Center;
            float roll = h.randomBranching ? Random.value : -1f;   // < 0 disables the weighted path
            bool wasLoopEnd = step.nextOptions != null && step.nextOptions.Length > 0;
            harmonizeIndex = HarmonyMath.NextIndex(step, zone, harmonizeIndex, prog.Count, roll);
            if (wasLoopEnd) harmonizeLoopStart = harmonizeIndex;   // a loop end always lands on a schema start
            lastAdvanceTime = Time.time;
        }

        // ── The song's own figure ────────────────────────────────────────────────────────────────
        // The landed key is BLENDED IN, not swapped for the melody: replacing the sung note removed the piece's
        // own line and left an arbitrary note on top of it. Lead mode is the swap-for-melody behaviour.
        private void PlayPreviewGesture(HarmonizeSettings h, ScorePreviewStep step,
                                        float landingVol, float sustain)
        {
            var support = step.support;
            bool lead = h.previewPlayerAsLead;

            // Loudness/timbre come from the SONG's measured dynamics: a hard landing playing louder than the
            // piece ever plays breaks the reproduction. The landing curve is opt-in.
            float baseVol = h.previewUseLandingVelocity ? landingVol : h.masterVolume;

            int supportTop = int.MinValue;
            float maxDt = 0f, velSum = 0f, holdSum = 0f, fallSum = 0f;
            int voiced = 0;
            if (support != null)
                for (int i = 0; i < support.Length; i++)
                {
                    var n = support[i];
                    if (lead && n.sings) continue;               // the landed key takes its place
                    if (n.midi > supportTop) supportTop = n.midi;
                    if (n.dt > maxDt) maxDt = n.dt;
                    velSum += n.vel; holdSum += n.hold; fallSum += n.fall; voiced++;
                }

            // A figure wider than the gesture budget is compressed, never truncated — dropping its
            // tail notes would take it apart, and stretching it would collide with the next landing.
            float spreadScale = maxDt > h.gestureMaxSpreadSeconds && maxDt > 0f
                              ? h.gestureMaxSpreadSeconds / maxDt : 1f;

            // Blend: the landed key takes the figure's mean level/length so it sits inside the texture.
            // Lead: it is the singing voice and takes that voice's own measured dynamics.
            float playerVel = lead || voiced == 0 ? Mathf.Clamp01(step.melodyVel) : velSum / voiced;
            float playerHold = HoldOf(lead || voiced == 0 ? step.melodyHold : holdSum / voiced, h);
            float playerFall = FallOf(lead || voiced == 0 ? step.melodyFall : fallSum / voiced, h, sustain);

            // Which pitch classes the landed key may use. Chord-snap narrows it to the tones the figure itself
            // strikes; snapPcs also carries the sung line's passing tones, which can sit outside the harmony.
            pcsScratch.Clear();
            if (h.snapLandedKeyToChord && support != null)
                for (int i = 0; i < support.Length; i++)
                {
                    int pc = ((support[i].midi % 12) + 12) % 12;
                    if (!pcsScratch.Contains(pc)) pcsScratch.Add(pc);
                }
            if (pcsScratch.Count == 0)
            {
                int[] pcs = lead ? step.melodyPcs : step.snapPcs;
                if (pcs == null || pcs.Length == 0) pcs = lead ? step.snapPcs : step.melodyPcs;
                if (pcs != null) pcsScratch.AddRange(pcs);
            }

            melodyScratch.Clear();
            for (int i = 0; i < contactingKeys.Count; i++)
            {
                if (contactingKeys[i] == null) continue;
                int m = SnapLandedKey(contactingKeys[i].MidiNote, pcsScratch, h, lead, supportTop);
                while (melodyScratch.Contains(m)) m += 12;
                // The landed key often falls on a pitch the figure already strikes; doubling it is inaudible as
                // a separate event and only makes that voice loud, so move it an octave (still a chord tone).
                if (!lead && FigureHas(support, m))
                {
                    if (m + 12 <= 108) m += 12;
                    else if (m - 12 >= 21) m -= 12;
                }
                melodyScratch.Add(m);
            }

            float playerVol = baseVol * playerVel * (contactingKeys.Count >= 2 ? h.dyadVolumeScale : 1f);
            for (int i = 0; i < melodyScratch.Count; i++)
                harmonizePool.PlayExpressive(melodyScratch[i], playerVol, playerHold, playerFall, 0f, playerVel);

            prevLandingMidi = melodyScratch.Count > 0 ? melodyScratch[0] : prevLandingMidi;
            prevChordScratch.Clear();

            if (support == null) return;

            // Chord blur: this stretch's pitches as ONE chord instead of the song's note order — copying the
            // transcription note for note read as a clumsy imitation. Level, length, tail and register all
            // survive; only the melodic order goes, and the chord rolls by blurRollSeconds so it isn't a block.
            chordScratch.Clear();
            for (int i = 0; i < support.Length; i++)
            {
                var n = support[i];
                if (lead && n.sings) continue;
                if (h.previewChordBlur && chordScratch.Contains(n.midi)) continue;
                chordScratch.Add(n.midi);
            }
            if (h.previewChordBlur) chordScratch.Sort();

            for (int k = 0; k < chordScratch.Count; k++)
            {
                int midi = chordScratch[k];
                var n = FindNote(support, midi);
                prevChordScratch.Add(midi);   // keep the legacy path honest if it ever resumes
                // Only lead mode drops a clashing voice — there the landed key stands in for the melody.
                if (lead && ClashesWithMelody(midi, h.melodyClashSemitones)) continue;
                float delay = h.previewChordBlur
                            ? (chordScratch.Count > 1 ? h.blurRollSeconds * k / (chordScratch.Count - 1) : 0f)
                            : n.dt * spreadScale;
                harmonizePool.PlayExpressive(midi, baseVol * Mathf.Clamp01(n.vel),
                                             HoldOf(n.hold, h), FallOf(n.fall, h, sustain),
                                             delay, Mathf.Clamp01(n.vel));
            }
            prevChordScratch.Sort();
        }

        private static ScoreSupportNote FindNote(ScoreSupportNote[] support, int midi)
        {
            for (int i = 0; i < support.Length; i++) if (support[i].midi == midi) return support[i];
            return support[0];
        }

        // How long the key stays down — the song's own key-down time, never inflated by a pedal.
        private static float HoldOf(float measured, HarmonizeSettings h)
            => Mathf.Min(h.gestureMaxRingSeconds, Mathf.Max(0.05f, measured));

        // How long the string is left to die away: the song's own pedal tail if it had one, else the configured
        // damper stretched by the slime's pedal. Applying both would hold a note far longer than the piece does.
        private static float FallOf(float measured, HarmonizeSettings h, float sustain)
        {
            float fall = measured > 0f ? measured : h.damperSeconds * sustain;
            return Mathf.Min(h.gestureMaxRingSeconds, Mathf.Max(0.02f, fall));
        }

        private static bool FigureHas(ScoreSupportNote[] support, int midi)
        {
            if (support == null) return false;
            for (int i = 0; i < support.Length; i++) if (support[i].midi == midi) return true;
            return false;
        }

        // The landed key moved to the nearest pitch class it may use here (see caller) — at most a tone.
        // Only lead mode lifts it above the figure; a blended voice stays at the octave the slime landed on.
        private static int SnapLandedKey(int keyMidi, List<int> pcs, HarmonizeSettings h,
                                         bool lead, int supportTop)
        {
            int best = keyMidi, bestD = 99;
            for (int i = 0; i < pcs.Count; i++)
            {
                int d = (((pcs[i] - keyMidi) % 12) + 12) % 12;
                if (d > 6) d -= 12;
                if (Mathf.Abs(d) < bestD) { bestD = Mathf.Abs(d); best = keyMidi + d; }
            }
            if (lead && h.melodyAboveSupport && supportTop > int.MinValue)
            {
                int floor = supportTop + Mathf.Max(0, Mathf.RoundToInt(h.minMelodyGap));
                while (best < floor) best += 12;
            }
            return best;
        }

        // A support note this close muddies the melody. The song never had the collision (it comes from the
        // landed key being somewhere the song was not), so dropping the voice is the more song-like answer.
        private bool ClashesWithMelody(int midi, int semitones)
        {
            for (int i = 0; i < melodyScratch.Count; i++)
                if (Mathf.Abs(melodyScratch[i] - midi) <= semitones) return true;
            return false;
        }

        // One PianoKeyVisual per key MESH, all 88. MIDI comes from the mesh name via the builder's own rule
        // (PianoKey.001 = 21) so accidentals light the real black key, and components are added at runtime so
        // the scene stays untouched. Shared with auto-play: idempotent, and the map outlives a teardown.
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

        // Notes can legitimately fall outside the 88 keys (pedal point, octave-doubled top, glissando running
        // off). The pool still sounds them pitch-shifted, so the nearest key must move rather than nothing.
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

        // Fired by the voice pool at each note's true onset, including an ornament's delayed notes.
        private void OnNoteSounded(int midi, float volume, float releaseSeconds)
        {
            if (config == null) return;
            var h = config.harmonize;
            if (volume < h.visualMinVolume) return;
            var vis = ResolveKeyVisual(midi);
            if (vis == null) return;

            float loud = h.masterVolume > 0.0001f ? Mathf.Clamp01(volume / h.masterVolume) : 1f;
            float depth = h.visualSinkDepth * Mathf.Lerp(h.visualMinDepthScale, h.visualDepthVelocityScale, loud);

            // Pedal down = dampers off: the string rings on after the key comes up, so the key must NOT stay
            // pinned for the longer tail. Gated on the same toggle that lengthens the audio.
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

        // Damper up: while the slime stands on any pedal every tail gets longer.
        private float SustainMultiplier(OrnamentSettings o)
        {
            if (!o.sustainPedal || pedals == null) return 1f;
            for (int i = 0; i < pedals.Length; i++)
                if (pedals[i] != null && pedals[i].IsPressed) return o.pedalReleaseMultiplier;
            return 1f;
        }

        // A suspension holds one note of the PREVIOUS chord over the new one and resolves it down by step.
        // Only fires when the previous chord has a note a step above one of this chord's upper voices.
        private void FindSuspension(OrnamentSettings o)
        {
            susIndex = -1;
            if (!o.suspension || prevChordScratch.Count == 0 || chordScratch.Count < 2) return;
            if (o.chiptuneArp || o.alberti) return;   // those figures re-play every tone; a held voice would double

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

        // A run in the direction the slime travelled after a big gap; the landing note itself stays instant.
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

        // Every figure's FIRST note is instant — only later notes are offset — so the landing and its sound
        // never separate. Precedence when several are on: chiptune > Alberti > rolled > block.
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

            if (o.chiptuneArp)
            {
                // Cycle the chord tones fast enough that the ear fuses them into harmony. Deliberately NOT run
                // through playedScratch: re-striking the same tones IS the technique (same for Alberti below).
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
