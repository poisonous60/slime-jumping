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

            // The rotation song's own strike for this step, played verbatim. It replaces the whole
            // rebuild-and-decorate path below — voice leading, velocity density and every ornament
            // exist to invent a chord shape, and inventing one is exactly what overwrote the song's
            // measured voicing and made every piece sound alike.
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
        // One landing = what the song actually played here: its notes, at its measured offsets, its
        // measured loudness, ringing for as long as they really rang (pedal included).
        //
        // The landed key is BLENDED IN, not swapped for the melody. Replacing the sung note with the
        // landed key removed the piece's own line and left an arbitrary note on top of it — the
        // reason it read as not belonging. Now the whole figure sounds, and the landed key joins as
        // one more voice: snapped into the pitch classes the song is using right here, and set at the
        // figure's own level so it sits inside the texture. Lead mode restores the old behaviour.
        private void PlayPreviewGesture(HarmonizeSettings h, ScorePreviewStep step,
                                        float landingVol, float sustain)
        {
            var support = step.support;
            bool lead = h.previewPlayerAsLead;

            // Loudness and timbre come from the SONG's measured dynamics, not from how fast the
            // slime hit the key: a hard landing playing louder than the piece ever plays breaks the
            // reproduction. The landing curve is opt-in.
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

            // Blend: the landed key is voiced like the rest of the figure (its mean level and
            // length), so it reads as part of the playing. Lead: it is the singing voice and takes
            // that voice's own measured dynamics.
            float playerVel = lead || voiced == 0 ? Mathf.Clamp01(step.melodyVel) : velSum / voiced;
            float playerHold = HoldOf(lead || voiced == 0 ? step.melodyHold : holdSum / voiced, h);
            float playerFall = FallOf(lead || voiced == 0 ? step.melodyFall : fallSum / voiced, h, sustain);

            melodyScratch.Clear();
            for (int i = 0; i < contactingKeys.Count; i++)
            {
                if (contactingKeys[i] == null) continue;
                int m = SnapLandedKey(contactingKeys[i].MidiNote, step, h, lead, supportTop);
                while (melodyScratch.Contains(m)) m += 12;
                // Blending, the landed key often lands on a note the figure already strikes — then
                // the pressed key IS sounding and doubling it would only make that one voice loud.
                if (!lead && FigureHas(support, m)) continue;
                melodyScratch.Add(m);
            }

            float playerVol = baseVol * playerVel * (contactingKeys.Count >= 2 ? h.dyadVolumeScale : 1f);
            for (int i = 0; i < melodyScratch.Count; i++)
                harmonizePool.PlayExpressive(melodyScratch[i], playerVol, playerHold, playerFall, 0f, playerVel);

            prevLandingMidi = melodyScratch.Count > 0 ? melodyScratch[0] : prevLandingMidi;
            prevChordScratch.Clear();

            if (support == null) return;

            // Chord blur: play the pitches of this stretch as ONE chord instead of walking the
            // song's note order. Copying the transcription note for note read as a clumsy imitation
            // of the piece; the harmony and the way it is played carry the impression on their own.
            // Everything measured survives — level, length, tail, register — only the melodic order
            // is dropped, and the chord rolls by blurRollSeconds so it does not land as a block.
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
                // Only lead mode drops a clashing voice: there the landed key stands in for the
                // melody, so a neighbouring note muddies it. Otherwise nothing the song played is
                // removed.
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

        // How long the string is then left to die away. The song's own pedal tail if it had one;
        // otherwise the configured damper, stretched while the slime stands on the pedal. Applying
        // the slime's pedal on top of a tail the song already pedalled would hold a note far longer
        // than the piece ever does.
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

        // The landed key, moved to the nearest pitch class the song is using here — the whole figure
        // plus its line when blending, only the sung line when leading. Off the song's material this
        // bends the key by at most a tone; on it, the key does not move at all.
        // Only lead mode lifts the note above the figure: a blended voice belongs inside the texture,
        // at the octave the slime actually landed on.
        private static int SnapLandedKey(int keyMidi, ScorePreviewStep step, HarmonizeSettings h,
                                         bool lead, int supportTop)
        {
            int[] pcs = lead ? step.melodyPcs : step.snapPcs;
            if (pcs == null || pcs.Length == 0) pcs = lead ? step.snapPcs : step.melodyPcs;
            int best = keyMidi;
            if (pcs != null && pcs.Length > 0)
            {
                int bestD = 99;
                for (int i = 0; i < pcs.Length; i++)
                {
                    int d = (((pcs[i] - keyMidi) % 12) + 12) % 12;
                    if (d > 6) d -= 12;
                    if (Mathf.Abs(d) < bestD) { bestD = Mathf.Abs(d); best = keyMidi + d; }
                }
            }
            if (lead && h.melodyAboveSupport && supportTop > int.MinValue)
            {
                int floor = supportTop + Mathf.Max(0, Mathf.RoundToInt(h.minMelodyGap));
                while (best < floor) best += 12;
            }
            return best;
        }

        // A support note this close to the melody muddies it. The song never had this collision —
        // it comes from the landed key being somewhere the song was not — so the smaller chord is
        // the more song-like answer.
        private bool ClashesWithMelody(int midi, int semitones)
        {
            for (int i = 0; i < melodyScratch.Count; i++)
                if (Mathf.Abs(melodyScratch[i] - midi) <= semitones) return true;
            return false;
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
