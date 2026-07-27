using System.Collections.Generic;
using NUnit.Framework;
using SlimeJumping.Platforms.Piano;

namespace SlimeJumping.Tests.EditMode
{
    public class PianoHarmonyMathTests
    {
        static int Pc(int midi) => ((midi % 12) + 12) % 12;

        static HarmonizeSettings Default() => new HarmonizeSettings();  // tonic C4, Major, Pentatonic, gap 3

        [Test]
        public void BuildChord_OffsetsFromTonicAreAbsoluteMidi()
        {
            var step = new ChordStep { offsetsFromTonic = new[] { -12, 0, 4, 7 } };
            var outMidis = new List<int>();

            HarmonyMath.BuildChord(step, 60, outMidis);

            CollectionAssert.AreEqual(new[] { 48, 60, 64, 67 }, outMidis);
        }

        [Test]
        public void SnapMelody_InScaleKeyKeepsItsPitchClass()
        {
            var h = Default();
            int[] chord = { -12, -5, 4, 7, 12 };   // voicing top = 72

            int melody = HarmonyMath.SnapMelody(62 /* D4, in major pentatonic */, h, chord);

            Assert.AreEqual(Pc(62), Pc(melody), "D should stay D");
            Assert.That(melody, Is.GreaterThanOrEqualTo(72 + 3), "melody floats above the voicing top + gap");
        }

        [Test]
        public void SnapMelody_OutOfScaleKeySnapsToNearestAllowedDegree()
        {
            var h = Default();
            int[] chord = { -12, 0, 4, 7 };

            // F#4 (66) over C major pentatonic {0,2,4,7,9}: nearest degree to pc 6 is 7 (G).
            int melody = HarmonyMath.SnapMelody(66, h, chord);

            Assert.AreEqual(7, Pc(melody), "F# snaps up to G");
        }

        [Test]
        public void Zone_SplitsNormalizedXIntoThirds()
        {
            var h = Default();  // zoneLeftMax 0.33, zoneRightMin 0.66, branchingEnabled true

            Assert.AreEqual(LandingZone.Left, HarmonyMath.Zone(0.10f, h));
            Assert.AreEqual(LandingZone.Center, HarmonyMath.Zone(0.50f, h));
            Assert.AreEqual(LandingZone.Right, HarmonyMath.Zone(0.80f, h));
        }

        [Test]
        public void Zone_BranchingDisabledIsAlwaysCenter()
        {
            var h = Default();
            h.branchingEnabled = false;

            Assert.AreEqual(LandingZone.Center, HarmonyMath.Zone(0.10f, h));
        }

        [Test]
        public void NextIndex_UnauthoredBranchIsLinearWrap()
        {
            var step = new ChordStep();  // leftNext/centerNext/rightNext all -1

            Assert.AreEqual(1, HarmonyMath.NextIndex(step, LandingZone.Center, 0, 4));
            Assert.AreEqual(0, HarmonyMath.NextIndex(step, LandingZone.Center, 3, 4), "wraps at end");
        }

        [Test]
        public void NextIndex_AuthoredBranchIsTaken()
        {
            var step = new ChordStep { leftNext = 3, centerNext = 2 };

            Assert.AreEqual(3, HarmonyMath.NextIndex(step, LandingZone.Left, 0, 4));
            Assert.AreEqual(2, HarmonyMath.NextIndex(step, LandingZone.Center, 0, 4));
        }

        static NextOption Opt(int i, float w) => new NextOption { stepIndex = i, weight = w };

        [Test]
        public void WeightedNext_SplitsTheRollByWeight()
        {
            // 0.75 / 0.25 split over 9 steps: roll < 0.75 → step 1, above → step 2
            var step = new ChordStep { nextOptions = new[] { Opt(1, 0.75f), Opt(2, 0.25f) } };

            Assert.AreEqual(1, HarmonyMath.WeightedNext(step, 0.00f, 9));
            Assert.AreEqual(1, HarmonyMath.WeightedNext(step, 0.74f, 9));
            Assert.AreEqual(2, HarmonyMath.WeightedNext(step, 0.76f, 9));
            Assert.AreEqual(2, HarmonyMath.WeightedNext(step, 1.00f, 9), "the last bucket absorbs float slop");
        }

        [Test]
        public void WeightedNext_SkipsInvalidOptionsAndReportsNoneWhenEmpty()
        {
            var outOfRange = new ChordStep { nextOptions = new[] { Opt(99, 1f) } };
            Assert.AreEqual(-1, HarmonyMath.WeightedNext(outOfRange, 0.5f, 4), "index past the list is skipped");

            var zeroWeight = new ChordStep { nextOptions = new[] { Opt(1, 0f) } };
            Assert.AreEqual(-1, HarmonyMath.WeightedNext(zeroWeight, 0.5f, 4), "zero weight is not a candidate");

            Assert.AreEqual(-1, HarmonyMath.WeightedNext(new ChordStep(), 0.5f, 4), "no options authored");
        }

        [Test]
        public void NextIndex_NegativeRollDisablesRandomness()
        {
            var step = new ChordStep { nextOptions = new[] { Opt(5, 1f) } };

            Assert.AreEqual(5, HarmonyMath.NextIndex(step, LandingZone.Center, 0, 9, 0.5f));
            Assert.AreEqual(1, HarmonyMath.NextIndex(step, LandingZone.Center, 0, 9, -1f), "falls back to linear");
        }

        [Test]
        public void NextIndex_ZoneBranchOutranksWeights()
        {
            var step = new ChordStep { leftNext = 3, nextOptions = new[] { Opt(7, 1f) } };

            Assert.AreEqual(3, HarmonyMath.NextIndex(step, LandingZone.Left, 0, 9, 0.5f), "authored zone wins");
            Assert.AreEqual(7, HarmonyMath.NextIndex(step, LandingZone.Center, 0, 9, 0.5f), "unauthored zone → weights");
        }

        [Test]
        public void DefaultProgression_IsSevenSchemasOfFour()
        {
            var h = Default();
            int n = h.progression.Count;
            Assert.AreEqual(28, n, "7 authored schemas x 4 chords (S1-S5 + M1, M2)");

            for (int i = 0; i < n; i++)
            {
                var s = h.progression[i];
                bool isLoopEnd = i % 4 == 3;

                // Randomness happens ONLY at a schema's last chord — inside a loop the order is fixed,
                // which is what makes it read as a song instead of a chord salad.
                Assert.AreEqual(isLoopEnd, s.nextOptions.Length > 0, $"step {i}: options only at loop end");

                for (int k = 0; k < s.nextOptions.Length; k++)
                {
                    int target = s.nextOptions[k].stepIndex;
                    Assert.That(target, Is.InRange(0, n - 1), $"step {i} option {k} in range");
                    Assert.AreEqual(0, target % 4, $"step {i} option {k} must land on a schema start");
                }
            }
        }

        [Test]
        public void DefaultProgression_EveryLoopEndCanReachEverySchema()
        {
            var h = Default();
            int schemas = h.progression.Count / 4;

            for (int i = 3; i < h.progression.Count; i += 4)
            {
                var opts = h.progression[i].nextOptions;
                float total = 0f;
                var reached = new HashSet<int>();
                foreach (var o in opts) { total += o.weight; reached.Add(o.stepIndex / 4); }

                Assert.AreEqual(1f, total, 0.01f, $"step {i}: weights are authored as a probability split");

                // S5 (the rare borrowed colour) is entered from everywhere but never repeats itself —
                // a borrowed chord is a one-off colour, not a place to sit. Everything else may repeat.
                int ownSchema = i / 4;
                bool isBorrowed = ownSchema == 4;
                Assert.AreEqual(!isBorrowed, reached.Contains(ownSchema), $"step {i}: self-repeat option");
                Assert.That(reached.Count, Is.GreaterThanOrEqualTo(4), $"step {i}: reaches several schemas");
            }
        }

        [Test]
        public void DefaultProgression_MajorLoopsFlipToMinorAboutHalfTheTime()
        {
            var h = Default();
            // schemas 0..3 are major, 4 is the borrowed colour, 5..6 are the relative-minor ones
            for (int i = 3; i < 16; i += 4)
            {
                float total = 0f, changed = 0f;
                foreach (var o in h.progression[i].nextOptions)
                {
                    total += o.weight;
                    if (o.stepIndex / 4 >= 4) changed += o.weight;   // borrowed or minor = mode change
                }
                Assert.AreEqual(0.52f, changed / total, 0.02f, $"step {i}: chance of leaving major");
            }
        }

        [Test]
        public void ApplyVelocityDensity_StrongLandingDoublesTopOctave()
        {
            var h = Default();  // velocityDensity true, octaveDoubleVelocity 0.7
            var notes = new List<int> { 60, 64, 67 };

            HarmonyMath.ApplyVelocityDensity(notes, 0.9f, h);

            Assert.Contains(79, notes, "top note 67 doubled an octave up");
            Assert.AreEqual(4, notes.Count);
        }

        [Test]
        public void ApplyVelocityDensity_WeakLandingStaysThin()
        {
            var h = Default();
            var notes = new List<int> { 60, 64, 67 };

            HarmonyMath.ApplyVelocityDensity(notes, 0.4f, h);

            Assert.AreEqual(3, notes.Count);
        }
    }
}
