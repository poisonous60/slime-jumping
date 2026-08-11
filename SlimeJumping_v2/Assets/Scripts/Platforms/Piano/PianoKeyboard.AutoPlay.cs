using UnityEngine;

namespace SlimeJumping.Platforms.Piano
{
    /// Idle auto-play half of the PianoKeyboard (opt-in via config.autoPlay.enabled).
    /// Arriving at the piano rotates to the next piece and transposes the harmonize progression into
    /// its key, so what the slime plays belongs to the recording that starts after it walks away.
    /// Sound = the recording, keys = a transcription of it, clocked off the clip's own playhead —
    /// nothing accumulates time, so keys can never drift from what is audible.
    public partial class PianoKeyboard
    {
        private AudioSource autoPlaySource;

        private PianoScoreData autoPlayScore;     // the CACHE — whatever song was last looked at
        private TextAsset autoPlayScoreSource;   // which TextAsset the cached score came from
        private bool autoPlayScoreBad;           // that TextAsset failed to parse — don't retry/log-spam

        // Separate from the cache on purpose: the returning slime rotates songs (refilling the cache)
        // while this piece still fades out, and clocking one recording against another's
        // transcription presses keys with nothing audible behind them.
        private PianoScoreData autoPlayingScore;

        private int autoPlaySongIndex = -1;      // -1 = nothing picked yet
        private bool autoPlayAway = true;        // the slime is not at the piano, so the next touch is an arrival
        private readonly System.Collections.Generic.List<int> autoPlayPickable = new System.Collections.Generic.List<int>();
        private readonly System.Collections.Generic.List<int> autoPlayBag = new System.Collections.Generic.List<int>();

        // Own generator: UnityEngine.Random is global state with no reliably fresh per-run seed.
        private System.Random autoPlayRng;

        private bool autoPlayActive, autoPlayStopping, autoPlayContactPrev;
        private bool autoPlayHeard;              // the source has actually been playing at least one frame
        private int autoPlayNext;                // index of the next note to fire
        private float autoPlayPrevClipTime;      // to notice the clip looping back
        private float autoPlayFade;              // 0..1 envelope applied on top of settings.volume
        private float autoPlaySongVolume = 1f;   // pinned like the score: rotation may pick the next song mid-fade
        private float autoPlayStartTime;

        private int autoPlayPresses;
        private float autoPlayLastContactTime = -999f;
        private float autoPlayEndedTime = -999f;

        // Audio data that never loads would leave the performance "active" forever and the feature
        // could never fire again.
        private const float AutoPlayStartTimeout = 5f;

        /// Is the piano playing itself right now?
        public bool IsAutoPlaying => autoPlayActive;

        /// Which piece is currently in force (its key and ornaments are what the slime is playing in).
        public PianoSong CurrentSong
        {
            get
            {
                if (config == null) return null;
                var list = config.autoPlay.songs;
                if (list == null || autoPlaySongIndex < 0 || autoPlaySongIndex >= list.Count) return null;
                return list[autoPlaySongIndex];
            }
        }

        // ── FixedUpdate hook (called from PianoKeyboard.FixedUpdate, after the contact loop) ────────
        private void AutoPlayStep(bool anyContacting)
        {
            var a = config.autoPlay;
            if (!a.enabled)
            {
                if (autoPlayActive) FinishAutoPlay();
                config.harmonize.ClearOverrides();   // hand the key/ornaments back to the asset's own values
                return;
            }

            if (anyContacting)
            {
                if (!autoPlayContactPrev)
                {
                    // Only ARRIVING rotates: this rising edge also fires on every key-to-key hop, and
                    // rotating there would re-transpose mid-phrase.
                    if (autoPlayAway)
                    {
                        autoPlayAway = false;
                        autoPlayPresses = 0;
                        SelectNextSong(a);
                    }
                    autoPlayPresses++;
                }
                autoPlayLastContactTime = Time.time;
                if (autoPlayActive) StopAutoPlay();   // the slime wants the piano back
            }
            autoPlayContactPrev = anyContacting;
        }

        // ── Update hook (called from PianoKeyboard.Update) ──────────────────────────────────────────
        private void AutoPlayUpdate()
        {
            if (config == null) return;
            var a = config.autoPlay;

            if (autoPlayActive) { TickAutoPlay(a); return; }
            if (!a.enabled) return;

            bool played = autoPlayPresses >= a.minPresses;
            bool gone = Time.time - autoPlayLastContactTime >= a.idleSeconds;
            bool rested = Time.time - autoPlayEndedTime >= a.cooldownSeconds;

            // Set even when no performance happened, else a slime that never earned one can't ever
            // rotate the song again.
            if (gone) autoPlayAway = true;

            if (played && gone && rested) StartAutoPlay(a);
        }

        private void AutoPlayDisable()
        {
            if (autoPlayActive) FinishAutoPlay();
            if (config != null) config.harmonize.ClearOverrides();
            if (autoPlaySource != null)
            {
                Destroy(autoPlaySource.gameObject);
                autoPlaySource = null;
            }
        }

        // ── Rotation ────────────────────────────────────────────────────────────────────────────────

        /// Advance to the next piece: key + ornaments applied as RUNTIME overrides only, so the shared
        /// PianoConfig asset is never written to.
        private void SelectNextSong(AutoPlaySettings a)
        {
            var list = a.songs;
            if (list == null || list.Count == 0) return;

            // Rebuilt every time rather than stepping forward from the current index, so toggling
            // songs at runtime can't corrupt the rotation.
            autoPlayPickable.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s == null || !s.enabled || s.clip == null || s.scoreJson == null) continue;
                autoPlayPickable.Add(i);
            }
            if (autoPlayPickable.Count == 0) { autoPlaySongIndex = -1; return; }

            // A bag, not a dice roll: memoryless picking repeats songs long before the list is spent.
            if (autoPlayRng == null) autoPlayRng = new System.Random(System.Guid.NewGuid().GetHashCode());
            autoPlayBag.RemoveAll(i => !autoPlayPickable.Contains(i));
            if (autoPlayBag.Count == 0) autoPlayBag.AddRange(autoPlayPickable);

            int pick = autoPlayRng.Next(autoPlayBag.Count);
            if (autoPlayBag.Count > 1 && autoPlayBag[pick] == autoPlaySongIndex)
                pick = (pick + 1) % autoPlayBag.Count;   // only a fresh bag can repeat the last song
            autoPlaySongIndex = autoPlayBag[pick];
            autoPlayBag.RemoveAt(pick);

            var song = list[autoPlaySongIndex];
            if (song == null) return;

            var h = config.harmonize;
            int tonic = song.tonicMidi;
            ScaleType scale = song.scale;

            // The song's own opening loop, baked offline into its score JSON by
            // tools/extract_preview.py. When usable it replaces the shared schemas.
            bool hasScore = EnsureScore(song);
            ScorePreview preview = hasScore ? autoPlayScore.preview : null;
            var previewProg = PreviewUsable(preview) ? BuildPreviewProgression(preview) : null;

            if (previewProg != null)
            {
                if (tonic < 0)
                {
                    // Preview steps are voiced in the piece's own key, minor included — the
                    // relative-major detour below is only needed by the tonic-relative schemas.
                    tonic = preview.tonicMidi;
                    scale = preview.minor ? ScaleType.Minor : ScaleType.Major;
                }
                h.SetProgressionOverride(previewProg);
                h.SetPreviewStepsOverride(preview.steps);   // index-aligned with previewProg
            }
            else
            {
                h.SetProgressionOverride(null);
                h.SetPreviewStepsOverride(null);

                if (tonic < 0 && hasScore && autoPlayScore.key != null)
                {
                    int pc = ((autoPlayScore.key.tonicPc % 12) + 12) % 12;

                    // A minor piece is tuned to its RELATIVE MAJOR: the authored minor schemas (M1/M2)
                    // are the relative minor OF the tonic, so G minor needs tonic B♭ (+3) — feeding G
                    // puts them on E minor, a third off the recording.
                    if (autoPlayScore.key.mode == "minor") pc = (pc + 3) % 12;

                    // Must sit in [60, 71]: voicings reach 19 semitones below the tonic and the
                    // low-interval-limit rule wants that bass at F2 (41) or above.
                    tonic = 60 + pc;

                    // Major even for a minor piece: major penta on the relative major is the same note
                    // set as minor penta on its own tonic; Minor here would snap the melody a third off.
                    scale = ScaleType.Major;
                }
            }

            if (tonic >= 0) h.SetKeyOverride(tonic, scale);
            h.SetOrnamentOverride(song.ornaments);

            if (a.logSongChange)
            {
                var o = song.ornaments;
                Debug.Log($"[Piano] song {autoPlaySongIndex + 1}/{list.Count} '{song.label}' "
                          + $"tonic={h.TonicMidi} {h.Scale} | "
                          + (previewProg != null
                              ? $"preview {previewProg.Count} strikes [{PreviewShape(preview)}]"
                              : "schema")
                          + " | orn:"
                          + (o.rolledChord ? " rolled" : "") + (o.voiceLeading ? " voiceLead" : "")
                          + (o.pedalPoint ? " pedalPoint" : "") + (o.alberti ? " alberti" : "")
                          + (o.chiptuneArp ? " chiptune" : "") + (o.appoggiatura ? " appogg" : "")
                          + (o.suspension ? " susp" : "") + (o.dropTwo ? " drop2" : "")
                          + (o.sustainPedal ? " sustain" : "") + (o.resonance ? " reson" : "")
                          + (o.glissando ? " gliss" : ""), this);
            }

            // A new key restarts the progression rather than continuing mid-cadence in the old one.
            harmonizeIndex = 0;
            harmonizeLoopStart = 0;
            prevChordScratch.Clear();
            prevLandingMidi = -1;
            lastAdvanceTime = Time.time;
        }

        /// Validity by CONTENT, not null: JsonUtility materializes a default ScorePreview even when the
        /// JSON has no "preview" field at all.
        private static bool PreviewUsable(ScorePreview p)
        {
            return p != null && p.steps != null && p.steps.Length >= 2
                   && p.tonicMidi >= 60 && p.tonicMidi <= 71;
        }

        /// Keys per strike as a digit string, e.g. "1212..." for bass/melody alternation.
        private static string PreviewShape(ScorePreview p)
        {
            var sb = new System.Text.StringBuilder(p.steps.Length);
            for (int i = 0; i < p.steps.Length; i++)
            {
                var s = p.steps[i];
                int n = s == null || s.support == null ? 0 : s.support.Length;
                sb.Append((char)('0' + Mathf.Min(9, n)));
            }
            return sb.ToString();
        }

        /// JSON steps as runtime ChordSteps, ONE PER STEP so the list stays index-aligned with
        /// ScorePreview.steps. Never deserialize straight into ChordStep: leftNext/centerNext/rightNext
        /// must keep their -1 defaults and nextOptions must stay empty, which is what makes
        /// HarmonyMath.NextIndex walk linearly and leaves zone branching / weighted dice inert.
        private static System.Collections.Generic.List<ChordStep> BuildPreviewProgression(ScorePreview p)
        {
            var steps = new System.Collections.Generic.List<ChordStep>(p.steps.Length);
            for (int i = 0; i < p.steps.Length; i++)
            {
                var s = p.steps[i];
                steps.Add(new ChordStep
                {
                    label = s != null ? s.label : "",
                    offsetsFromTonic = s != null && s.offsetsFromTonic != null && s.offsetsFromTonic.Length > 0
                                     ? s.offsetsFromTonic : new[] { 0 },
                    overrideSnapMode = s != null && s.chordTones,   // borrowed chords force chord-tone snap
                    snapMode = MelodySnapMode.ChordTones,
                });
            }
            return steps.Count >= 2 ? steps : null;
        }

        // ── Playback ────────────────────────────────────────────────────────────────────────────────

        private void StartAutoPlay(AutoPlaySettings a)
        {
            var song = CurrentSong;
            if (song == null || song.clip == null || !EnsureScore(song)) return;

            EnsureKeyVisuals();          // autoplay needs the 88 key meshes whether or not harmonize is on
            EnsureAutoPlaySource();

            autoPlayingScore = autoPlayScore;   // pin it: rotation may refill the cache mid-fade
            autoPlaySongVolume = song.volume;
            autoPlaySource.clip = song.clip;
            autoPlaySource.loop = a.loop;
            autoPlaySource.time = 0f;
            autoPlayFade = a.fadeInSeconds > 0f ? 0f : 1f;
            autoPlaySource.volume = a.volume * autoPlaySongVolume * autoPlayFade;
            autoPlaySource.Play();

            autoPlayNext = 0;
            autoPlayPrevClipTime = 0f;
            autoPlayStopping = false;
            autoPlayHeard = false;
            autoPlayStartTime = Time.time;
            autoPlayActive = true;
        }

        private void TickAutoPlay(AutoPlaySettings a)
        {
            if (autoPlaySource == null || autoPlayingScore == null) { FinishAutoPlay(); return; }

            // End-of-clip before the fade branches, and only once actually heard: a source still
            // loading has not begun (≠ finished), and a non-looping clip that ends DURING the fade-out
            // reports time == 0, which the loop-wrap test below would read as "looped" and re-press
            // keys at t≈0 with nothing audible behind them.
            if (autoPlaySource.isPlaying) autoPlayHeard = true;
            else if (autoPlayHeard) { FinishAutoPlay(); return; }
            else if (Time.time - autoPlayStartTime > AutoPlayStartTimeout) { FinishAutoPlay(); return; }

            if (autoPlayStopping)
            {
                autoPlayFade -= a.fadeOutSeconds > 0f ? Time.deltaTime / a.fadeOutSeconds : 1f;
                if (autoPlayFade <= 0f) { FinishAutoPlay(); return; }
            }
            else if (autoPlayFade < 1f)
            {
                autoPlayFade = Mathf.Min(1f, autoPlayFade
                    + (a.fadeInSeconds > 0f ? Time.deltaTime / a.fadeInSeconds : 1f));
            }
            autoPlaySource.volume = a.volume * autoPlaySongVolume * autoPlayFade;
            if (!autoPlayHeard) return;   // nothing to clock the keys with yet

            // The clip is the clock: reading the playhead, never accumulating deltaTime, is what keeps
            // a key going down on the frame its note is audible.
            float t = autoPlaySource.time;
            if (t < autoPlayPrevClipTime) autoPlayNext = 0;   // looped back to the top
            autoPlayPrevClipTime = t;

            var notes = autoPlayingScore.notes;
            while (autoPlayNext < notes.Length && notes[autoPlayNext].t <= t)
            {
                PressScoreNote(a, notes[autoPlayNext]);
                autoPlayNext++;
            }
        }

        /// Fade out and hand the piano back. Keys already down rise on their own — killing them here
        /// would also wipe the dip of the key the returning slime just landed on.
        private void StopAutoPlay()
        {
            if (!autoPlayActive || autoPlayStopping) return;
            if (config != null && config.autoPlay.fadeOutSeconds > 0f) { autoPlayStopping = true; return; }
            FinishAutoPlay();
        }

        private void FinishAutoPlay()
        {
            autoPlayActive = false;
            autoPlayStopping = false;
            autoPlayFade = 0f;
            autoPlayEndedTime = Time.time;
            autoPlayPresses = 0;           // the slime has to earn the next performance
            autoPlayingScore = null;
            if (autoPlaySource != null)
            {
                autoPlaySource.Stop();
                autoPlaySource.volume = 0f;
            }
        }

        // One sounding note = one key down. Reuses harmonize's Visual tuning so the piano looks the
        // same whoever is playing it.
        private void PressScoreNote(AutoPlaySettings a, ScoreNote n)
        {
            var vis = ResolveKeyVisual(n.midi);
            if (vis == null) return;

            var h = config.harmonize;
            float loud = Mathf.Clamp01(n.vel);
            float depth = h.visualSinkDepth * a.visualDepthScale
                          * Mathf.Lerp(h.visualMinDepthScale, h.visualDepthVelocityScale, loud);
            float hold = Mathf.Clamp(n.dur, h.visualHoldSeconds, h.visualMaxHoldSeconds);

            vis.Press(depth, hold, h.visualReleaseSeconds,
                      h.visualRearticulateLift, h.visualRearticulateSeconds);
        }

        private void EnsureAutoPlaySource()
        {
            if (autoPlaySource != null) return;

            var go = new GameObject("PianoAutoPlay");
            go.transform.SetParent(transform, false);
            autoPlaySource = go.AddComponent<AudioSource>();
            autoPlaySource.playOnAwake = false;
            autoPlaySource.loop = false;
            autoPlaySource.spatialBlend = 0f;

            // Same mixer group as the keys, so the piano's volume/FX apply to its own performance.
            for (int i = 0; i < keys.Count; i++)
                if (keys[i] != null && keys[i].OutputGroup != null)
                {
                    autoPlaySource.outputAudioMixerGroup = keys[i].OutputGroup;
                    break;
                }
        }

        /// Parse (and cache) one song's score; only one is held at a time.
        private bool EnsureScore(PianoSong song)
        {
            if (song == null || song.scoreJson == null) return false;
            if (song.scoreJson == autoPlayScoreSource) return !autoPlayScoreBad;

            autoPlayScoreSource = song.scoreJson;
            autoPlayScoreBad = true;
            autoPlayScore = null;

            // JsonUtility.FromJson THROWS on malformed input instead of returning null, and this runs
            // from Update — one bad TextAsset would take the whole keyboard down.
            PianoScoreData parsed;
            try
            {
                parsed = JsonUtility.FromJson<PianoScoreData>(song.scoreJson.text);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[PianoKeyboard] AutoPlay score '{song.scoreJson.name}' is not valid JSON: {e.Message}", this);
                return false;
            }
            if (parsed == null || parsed.notes == null || parsed.notes.Length == 0)
            {
                Debug.LogWarning($"[PianoKeyboard] AutoPlay score '{song.scoreJson.name}' has no notes.", this);
                return false;
            }

            System.Array.Sort(parsed.notes, (x, y) => x.t.CompareTo(y.t));
            autoPlayScore = parsed;
            autoPlayScoreBad = false;
            return true;
        }
    }
}
