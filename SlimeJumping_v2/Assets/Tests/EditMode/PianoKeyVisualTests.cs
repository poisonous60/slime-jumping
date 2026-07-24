using NUnit.Framework;
using SlimeJumping.Platforms.Piano;

namespace SlimeJumping.Tests.EditMode
{
    public class PianoKeyVisualTests
    {
        [Test]
        public void MidiFromName_MatchesTheBuilderRuleAcrossAll88Keys()
        {
            // PianoKeyboardBuilder: midi = 20 + ParseIndex(name). 88 keys => MIDI 21 (A0) .. 108 (C8),
            // black keys included — that's what lets an Ab light up the real black key.
            Assert.AreEqual(21, PianoKeyVisual.MidiFromName("PianoKey.001"));
            Assert.AreEqual(22, PianoKeyVisual.MidiFromName("PianoKey.002"), "black key A#0");
            Assert.AreEqual(60, PianoKeyVisual.MidiFromName("PianoKey.040"), "middle C");
            Assert.AreEqual(108, PianoKeyVisual.MidiFromName("PianoKey.088"));
        }

        [Test]
        public void MidiFromName_RejectsAnythingThatIsNotANumberedKeyMesh()
        {
            Assert.AreEqual(-1, PianoKeyVisual.MidiFromName("KeyMotor_PianoKey.001"), "the motor, not the mesh");
            Assert.AreEqual(-1, PianoKeyVisual.MidiFromName("PianoKey"), "no index");
            Assert.AreEqual(-1, PianoKeyVisual.MidiFromName("PianoKey.000"), "out of the 1..88 range");
            Assert.AreEqual(-1, PianoKeyVisual.MidiFromName("PianoKey.089"), "out of the 1..88 range");
            Assert.AreEqual(-1, PianoKeyVisual.MidiFromName("Pedal_1"));
            Assert.AreEqual(-1, PianoKeyVisual.MidiFromName(null));
        }

        [Test]
        public void DipFactor_HoldsFullDepthThenRisesLinearly()
        {
            const float rise = 0.2f;

            Assert.AreEqual(1f, PianoKeyVisual.DipFactor(1.0f, rise), 1e-4f, "still held down");
            Assert.AreEqual(1f, PianoKeyVisual.DipFactor(0.21f, rise), 1e-4f, "just before the rise starts");
            Assert.AreEqual(0.5f, PianoKeyVisual.DipFactor(0.1f, rise), 1e-4f, "halfway back up");
            Assert.AreEqual(0f, PianoKeyVisual.DipFactor(0f, rise), 1e-4f, "back at rest");
        }

        [Test]
        public void LiftFactor_PopsUpOnTheRestrikeThenSinksBack()
        {
            const float fall = 0.05f, lift = 0.55f;

            // full lift at the instant of the second strike — the key visibly comes back up
            Assert.AreEqual(0.55f, PianoKeyVisual.LiftFactor(fall, fall, lift), 1e-4f);
            Assert.AreEqual(0.275f, PianoKeyVisual.LiftFactor(fall * 0.5f, fall, lift), 1e-4f, "halfway back down");
            Assert.AreEqual(0f, PianoKeyVisual.LiftFactor(0f, fall, lift), 1e-4f, "fully sunk again");
        }

        [Test]
        public void MeshLocalY_ComposesAgainstTheMotorsPhysicalSink()
        {
            // free key: the mesh carries the whole dip
            Assert.AreEqual(-0.09f, PianoKeyVisual.MeshLocalY(0.09f, 0f), 1e-4f);

            // slime standing on it and already sunk exactly as deep: mesh adds nothing (no double dip)
            Assert.AreEqual(0f, PianoKeyVisual.MeshLocalY(0.09f, 0.09f), 1e-4f);

            // re-strike on a held-down key: the dip target drops below the sink, so the mesh rides UP
            // above the motor — the only way a repeated note is visible while the slime stands there.
            Assert.AreEqual(0.04f, PianoKeyVisual.MeshLocalY(0.05f, 0.09f), 1e-4f);
        }

        [Test]
        public void LiftFactor_ZeroLiftMeansRepeatedNotesLookLikeOneHold()
        {
            // This was the original bug: with no re-articulation an Alberti figure's repeated top note
            // and a chiptune burst just held the key flat down.
            Assert.AreEqual(0f, PianoKeyVisual.LiftFactor(0.05f, 0.05f, 0f), 1e-4f);
        }

        [Test]
        public void DipFactor_ZeroRiseIsAHardCut()
        {
            Assert.AreEqual(1f, PianoKeyVisual.DipFactor(0.05f, 0f), 1e-4f);
            Assert.AreEqual(0f, PianoKeyVisual.DipFactor(0f, 0f), 1e-4f);
        }
    }
}
