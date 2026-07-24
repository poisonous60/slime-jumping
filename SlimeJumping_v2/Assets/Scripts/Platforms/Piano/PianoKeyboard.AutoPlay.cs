using UnityEngine;

namespace SlimeJumping.Platforms.Piano
{
    /// Idle auto-play half of the PianoKeyboard (opt-in via config.autoPlay.enabled).
    ///
    /// A rotation of recorded pieces. The slime stepping ONTO the piano picks the next piece and
    /// immediately transposes the whole harmonize progression into that piece's key (and swaps in its
    /// ornament preset), so everything the slime plays already belongs to the music that is about to
    /// start. Then the slime plays — minPresses landings — walks away for idleSeconds, and the piano
    /// finishes the thought by playing that same piece: the chords the player was just making resolve
    /// into the recording instead of colliding with it.
    ///
    /// The sound is the recording; the keys are driven from a transcription of that same recording.
    /// The clip's own playhead is the clock — nothing here counts frames or accumulates its own time,
    /// so a hitch, a slow frame or a loop can never let the keys drift away from what is audible.
    /// The slime landing on any key hands the instrument straight back (fade out, stop).
    public partial class PianoKeyboard
    {
        private AudioSource autoPlaySource;

        private PianoScoreData autoPlayScore;     // the CACHE — whatever song was last looked at
        private TextAsset autoPlayScoreSource;   // which TextAsset the cached score came from
        private bool autoPlayScoreBad;           // that TextAsset failed to parse — don't retry/log-spam

        // The score the CURRENT performance is playing, captured at Start. Deliberately separate from
        // the cache: the slime coming back rotates to the next song (which re-fills the cache) while
        // this piece is still fading out, and clocking one recording's playhead against another
        // recording's transcription would press keys that have nothing to do with what is audible.
        private PianoScoreData autoPlayingScore;

        private int autoPlaySongIndex = -1;      // -1 = nothing picked yet
        private bool autoPlayAway = true;        // the slime is not at the piano, so the next touch is an arrival
        private readonly System.Collections.Generic.List<int> autoPlayPickable = new System.Collections.Generic.List<int>();

        private bool autoPlayActive, autoPlayStopping, autoPlayContactPrev;
        private bool autoPlayHeard;              // the source has actually been playing at least one frame
        private int autoPlayNext;                // index of the next note to fire
        private float autoPlayPrevClipTime;      // to notice the clip looping back
        private float autoPlayFade;              // 0..1 envelope applied on top of settings.volume
        private float autoPlayStartTime;

        private int autoPlayPresses;
        private float autoPlayLastContactTime = -999f;
        private float autoPlayEndedTime = -999f;

        // If the clip never starts (audio data failed to load) the performance would otherwise sit
        // "active" forever and the feature could never trigger again. Give up after this long.
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
                    // ARRIVING at the piano rotates the piece — not merely hopping key to key, which is
                    // the same rising edge harmonize counts as a landing. Rotating per landing would
                    // re-transpose mid-phrase every time the slime moved.
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

            // Gone long enough that coming back counts as a fresh visit, whether or not a performance
            // actually happened (auto-play may be off, or the slime may not have played enough).
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

        /// Advance to the next piece and make the instrument belong to it: the progression transposes
        /// into its key and its ornament preset takes over, both as RUNTIME overrides so the shared
        /// PianoConfig asset is never written to.
        private void SelectNextSong(AutoPlaySettings a)
        {
            var list = a.songs;
            if (list == null || list.Count == 0) return;

            // Only entries that are switched on AND actually wired can be picked. Doing this by
            // rebuilding the candidate list each time (rather than skipping forward from the current
            // index) is what keeps the rotation correct when the designer toggles songs at runtime.
            autoPlayPickable.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s == null || !s.enabled || s.clip == null || s.scoreJson == null) continue;
                autoPlayPickable.Add(i);
            }
            if (autoPlayPickable.Count == 0) { autoPlaySongIndex = -1; return; }

            int n = autoPlayPickable.Count;
            if (!a.shuffle)
            {
                // list order, wrapping past the disabled ones
                int next = autoPlayPickable[0];
                for (int i = 0; i < n; i++)
                    if (autoPlayPickable[i] > autoPlaySongIndex) { next = autoPlayPickable[i]; break; }
                autoPlaySongIndex = next;
            }
            else if (n == 1)
            {
                autoPlaySongIndex = autoPlayPickable[0];
            }
            else
            {
                // Never the same piece twice running while anything else is available — an immediate
                // repeat reads as "the rotation is broken", not as chance. Re-rolling uniformly over
                // the other n-1 keeps every remaining piece equally likely.
                int pick = Random.Range(0, n);
                if (autoPlayPickable[pick] == autoPlaySongIndex)
                    pick = (pick + 1 + Random.Range(0, n - 1)) % n;
                autoPlaySongIndex = autoPlayPickable[pick];
            }

            var song = list[autoPlaySongIndex];
            if (song == null) return;

            var h = config.harmonize;
            int tonic = song.tonicMidi;
            ScaleType scale = song.scale;

            if (tonic < 0 && EnsureScore(song) && autoPlayScore.key != null)
            {
                int pc = ((autoPlayScore.key.tonicPc % 12) + 12) % 12;

                // A MINOR piece is tuned to its RELATIVE MAJOR, not to its own tonic. The authored
                // minor schemas (M1/M2) are built as the relative minor OF the tonic — ChordAmTonic
                // sits at -15..0, which from C4 is A minor. So a G minor piece needs tonic B♭ (+3) for
                // M1/M2 to land on G minor; feeding it G would put them on E minor, a third away from
                // the recording. The major schemas then sit on the relative major, which is exactly the
                // major/minor alternation those schemas were designed around.
                if (autoPlayScore.key.mode == "minor") pc = (pc + 3) % 12;

                // Placed in [60, 71]: the authored voicings reach 19 semitones below the tonic and the
                // low-interval-limit rule wants that bass at F2 (41) or above, so the tonic must not sit
                // below C4 — an octave lower would muddy every progression.
                tonic = 60 + pc;

                // Major, deliberately, even for a minor piece: the tonic is now the relative major, and
                // a major pentatonic on it is the SAME note set as the minor pentatonic on the piece's
                // own tonic (B♭ major penta = G minor penta). Forcing Minor here would snap the melody
                // to B♭ minor instead, which is not in the recording's key at all.
                scale = ScaleType.Major;
            }

            if (tonic >= 0) h.SetKeyOverride(tonic, scale);
            h.SetOrnamentOverride(song.ornaments);

            // Rotation is invisible until the piece starts, so say which one the slime just picked up.
            // Without this there is no way to tell "the rotation did not fire" from "the rotation fired
            // but this piece sounds like the last one".
            if (a.logSongChange)
            {
                var o = song.ornaments;
                Debug.Log($"[Piano] song {autoPlaySongIndex + 1}/{list.Count} '{song.label}' "
                          + $"tonic={h.TonicMidi} {h.Scale} | orn:"
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

        // ── Playback ────────────────────────────────────────────────────────────────────────────────

        private void StartAutoPlay(AutoPlaySettings a)
        {
            var song = CurrentSong;
            if (song == null || song.clip == null || !EnsureScore(song)) return;

            EnsureKeyVisuals();          // autoplay needs the 88 key meshes whether or not harmonize is on
            EnsureAutoPlaySource();

            autoPlayingScore = autoPlayScore;   // pin it: rotation may refill the cache mid-fade
            autoPlaySource.clip = song.clip;
            autoPlaySource.loop = a.loop;
            autoPlaySource.time = 0f;
            autoPlayFade = a.fadeInSeconds > 0f ? 0f : 1f;
            autoPlaySource.volume = a.volume * autoPlayFade;
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

            // End-of-clip is checked BEFORE the fade branches, and only once the source has actually
            // been heard. Two reasons, both real:
            //   - a source whose audio data is still loading is not "finished", it has not begun;
            //   - a non-looping clip that runs out DURING the fade-out reports time == 0, which the
            //     loop-wrap test below would otherwise read as "looped" and rewind the cursor, making
            //     keys at t≈0 press themselves with nothing audible behind them.
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
            autoPlaySource.volume = a.volume * autoPlayFade;
            if (!autoPlayHeard) return;   // nothing to clock the keys with yet

            // THE CLIP IS THE CLOCK. Reading the playhead rather than accumulating deltaTime is what
            // guarantees a key goes down on the frame its note is actually audible.
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

        /// Fade out and hand the piano back. Keys already down are left to rise on their own — killing
        /// them here would also wipe the dip of the very key the returning slime just landed on.
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

        // One sounding note = one key going down, at the depth its velocity earned and for as long as
        // the transcription says it was held. Reuses harmonize's Visual tuning so the piano looks the
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

            // Route through the same mixer group as the keys — it is the same instrument sounding,
            // so whatever volume/FX the piano has applies to its own performance too.
            for (int i = 0; i < keys.Count; i++)
                if (keys[i] != null && keys[i].OutputGroup != null)
                {
                    autoPlaySource.outputAudioMixerGroup = keys[i].OutputGroup;
                    break;
                }
        }

        /// Parse (and cache) one song's score. Only one score is held at a time: rotation happens when
        /// the slime arrives at the piano, not per frame, so re-parsing a few KB there costs nothing.
        private bool EnsureScore(PianoSong song)
        {
            if (song == null || song.scoreJson == null) return false;
            if (song.scoreJson == autoPlayScoreSource) return !autoPlayScoreBad;

            autoPlayScoreSource = song.scoreJson;
            autoPlayScoreBad = true;
            autoPlayScore = null;

            // FromJson THROWS on malformed input rather than returning null, and this runs from Update
            // via the rotation — one bad TextAsset would otherwise take the whole keyboard down.
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
