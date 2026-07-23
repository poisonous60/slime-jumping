using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.Platforms.Piano
{
    /// Pure, stateless music math for the harmonize system: build a chord's absolute MIDI notes,
    /// snap a landed key to a scale/chord melody note above the voicing, resolve the landing zone,
    /// pick the next progression step, and thicken by velocity. Kept free of UnityEngine.Object so
    /// it can be exercised by EditMode tests without entering Play mode.
    public static class HarmonyMath
    {
        // Pitch-class sets relative to the tonic.
        static readonly int[] MajorScale = { 0, 2, 4, 5, 7, 9, 11 };
        static readonly int[] MinorScale = { 0, 2, 3, 5, 7, 8, 10 };
        static readonly int[] MajorPenta = { 0, 2, 4, 7, 9 };
        static readonly int[] MinorPenta = { 0, 3, 5, 7, 10 };

        static int Mod12(int n) => ((n % 12) + 12) % 12;

        /// Absolute MIDI notes for a chord step's offsets-from-tonic voicing.
        public static void BuildChord(ChordStep step, int tonic, List<int> outMidis)
        {
            outMidis.Clear();
            if (step == null || step.offsetsFromTonic == null) return;
            for (int i = 0; i < step.offsetsFromTonic.Length; i++)
                outMidis.Add(tonic + step.offsetsFromTonic[i]);
        }

        /// The allowed melody pitch-classes (relative to tonic) for the snap mode.
        static int[] AllowedDegrees(HarmonizeSettings h, int[] chordOffsets, MelodySnapMode mode)
        {
            if (mode == MelodySnapMode.ChordTones && chordOffsets != null && chordOffsets.Length > 0)
            {
                var set = new SortedSet<int>();
                for (int i = 0; i < chordOffsets.Length; i++) set.Add(Mod12(chordOffsets[i]));
                var arr = new int[set.Count]; set.CopyTo(arr); return arr;
            }
            if (mode == MelodySnapMode.ScaleFull)
                return h.scale == ScaleType.Minor ? MinorScale : MajorScale;
            return h.scale == ScaleType.Minor ? MinorPenta : MajorPenta;   // Pentatonic (default)
        }

        // Degree in `allowed` (assumed ascending) nearest (circular) to relPc; ties keep the lower.
        static int NearestDegree(int relPc, int[] allowed)
        {
            int best = allowed[0], bestDist = 99;
            for (int i = 0; i < allowed.Length; i++)
            {
                int dist = Mod12(relPc - allowed[i]);
                dist = Mathf.Min(dist, 12 - dist);
                if (dist < bestDist) { bestDist = dist; best = allowed[i]; }
            }
            return best;
        }

        /// Snap a landed key to a melody note: nearest allowed degree to the key's pitch (keeps the
        /// key's register so left keys read low / right keys high), then floated above the voicing top.
        public static int SnapMelody(int keyMidi, HarmonizeSettings h, int[] chordOffsets)
            => SnapMelody(keyMidi, h, chordOffsets, h.snapMode);

        /// Same, with the snap mode forced — a chord outside the key (bVI, bVII) must restrict the
        /// melody to its own chord tones, or the diatonic pentatonic collides with it by a half step
        /// (A natural over an Ab triad is a minor 9th above the root: the textbook avoid note).
        public static int SnapMelody(int keyMidi, HarmonizeSettings h, int[] chordOffsets, MelodySnapMode mode)
        {
            int[] allowed = AllowedDegrees(h, chordOffsets, mode);
            int relPc = Mod12(keyMidi - h.tonicMidi);
            int deg = NearestDegree(relPc, allowed);
            int targetPc = Mod12(h.tonicMidi + deg);

            int diff = Mod12(targetPc - Mod12(keyMidi));   // 0..11 upward
            if (diff > 6) diff -= 12;                       // move to nearest direction
            int melody = keyMidi + diff;

            int voicingTop = h.tonicMidi + MaxOffset(chordOffsets);
            int floor = voicingTop + Mathf.RoundToInt(h.minMelodyGap);
            while (melody < floor) melody += 12;
            return melody;
        }

        static int MaxOffset(int[] offsets)
        {
            if (offsets == null || offsets.Length == 0) return 0;
            int m = offsets[0];
            for (int i = 1; i < offsets.Length; i++) if (offsets[i] > m) m = offsets[i];
            return m;
        }

        /// Landing zone from a normalized 0..1 x across the keyboard span.
        public static LandingZone Zone(float normX, HarmonizeSettings h)
        {
            if (!h.branchingEnabled) return LandingZone.Center;
            if (normX < h.zoneLeftMax) return LandingZone.Left;
            if (normX > h.zoneRightMin) return LandingZone.Right;
            return LandingZone.Center;
        }

        /// Next progression index, in priority order: an authored landing-zone branch, then a weighted
        /// pick among the step's authored candidates (`roll` = 0..1), then linear (i+1). Randomness only
        /// ever chooses between successors the designer already approved — the research's verdict was
        /// that a free Markov chain buys surface variety at the cost of the "always sounds good" floor.
        public static int NextIndex(ChordStep step, LandingZone zone, int currentIndex, int count, float roll)
        {
            int chosen = -1;
            if (step != null)
                chosen = zone == LandingZone.Left ? step.leftNext
                       : zone == LandingZone.Right ? step.rightNext
                       : step.centerNext;
            if (chosen >= 0 && chosen < count) return chosen;

            int weighted = WeightedNext(step, roll, count);
            if (weighted >= 0) return weighted;

            return count > 0 ? (currentIndex + 1) % count : 0;
        }

        /// Same, with no authored candidates consulted (kept for callers that never roll).
        public static int NextIndex(ChordStep step, LandingZone zone, int currentIndex, int count)
            => NextIndex(step, zone, currentIndex, count, -1f);

        /// Weighted random over `step.nextOptions`; -1 when the step authored none (or `roll` is < 0,
        /// which disables the probabilistic path entirely). Options out of range or with weight ≤ 0 are
        /// skipped, so a half-authored table degrades to the linear fallback instead of misfiring.
        public static int WeightedNext(ChordStep step, float roll, int count)
        {
            if (roll < 0f || step == null || step.nextOptions == null || step.nextOptions.Length == 0) return -1;

            float total = 0f;
            for (int i = 0; i < step.nextOptions.Length; i++)
            {
                var o = step.nextOptions[i];
                if (o == null || o.stepIndex < 0 || o.stepIndex >= count || o.weight <= 0f) continue;
                total += o.weight;
            }
            if (total <= 0f) return -1;

            float pick = Mathf.Clamp01(roll) * total;
            int last = -1;
            for (int i = 0; i < step.nextOptions.Length; i++)
            {
                var o = step.nextOptions[i];
                if (o == null || o.stepIndex < 0 || o.stepIndex >= count || o.weight <= 0f) continue;
                last = o.stepIndex;
                pick -= o.weight;
                if (pick <= 0f) return o.stepIndex;
            }
            return last;   // float slop on the last bucket
        }

        /// Thicken a chord by velocity: strong landings double the top note an octave up.
        public static void ApplyVelocityDensity(List<int> midis, float vel, HarmonizeSettings h)
        {
            if (!h.velocityDensity || midis.Count == 0) return;
            if (vel < h.octaveDoubleVelocity) return;
            int top = midis[0];
            for (int i = 1; i < midis.Count; i++) if (midis[i] > top) top = midis[i];
            int doubled = top + 12;
            if (!midis.Contains(doubled)) midis.Add(doubled);
        }

        static readonly string[] Names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

        /// e.g. 60 -> "C4". For logging/tests only.
        public static string NoteName(int midi) => Names[Mod12(midi)] + (midi / 12 - 1);
    }
}
