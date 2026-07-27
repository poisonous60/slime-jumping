using NUnit.Framework;
using UnityEngine;
using SlimeJumping.Platforms;

namespace SlimeJumping.Tests.EditMode
{
    /// <summary>
    /// The config exposes the body recoil as a DISTANCE ("the cloud moves this far"). That only
    /// holds if the kick is pre-compensated for the spring's frequency and damping AND the spring
    /// is evaluated, not integrated — a 50 Hz explicit integrator under-shoots the first peak by
    /// 10-50%. These tests sample the shipped solution at the real fixed step.
    /// </summary>
    public class SoftCloudRecoilTests
    {
        const float Dt = 0.02f; // Unity's default fixed step

        // Kick the spring the way OnSlimeAttach does, sample per fixed step, return max |offset|.
        static float PeakOf(float distance, float frequency, float damping)
        {
            Vector2 v0 = Vector2.down * (distance * SoftCloudPlatform.ImpulsePerPeakDistance(frequency, damping));

            float peak = 0f;
            for (int i = 1; i <= 500; i++) // 10 s — long past the first peak at any tested frequency
            {
                SoftCloudPlatform.EvaluateSpring(Vector2.zero, v0, i * Dt, frequency, damping,
                                                 out Vector2 x, out _);
                peak = Mathf.Max(peak, x.magnitude);
            }
            return peak;
        }

        [TestCase(0.25f, 1.6f, 0.35f)]
        [TestCase(0.05f, 1.6f, 0.35f)]
        [TestCase(0.4f, 3.0f, 0.6f)]
        [TestCase(0.25f, 0.8f, 0.15f)]
        [TestCase(0.3f, 2.5f, 0.9f)]
        public void RecoilPeakMatchesAuthoredDistance(float distance, float frequency, float damping)
        {
            // 5% tolerance: the continuous peak is exact, but we only sample it every fixed step.
            Assert.That(PeakOf(distance, frequency, damping), Is.EqualTo(distance).Within(0.05f * distance));
        }

        [Test]
        public void SpringReturnsToRest()
        {
            SoftCloudPlatform.EvaluateSpring(Vector2.down * 0.3f, Vector2.zero, 10f, 1.6f, 0.35f,
                                             out Vector2 x, out Vector2 v);
            Assert.That(x.magnitude, Is.LessThan(1e-4f));
            Assert.That(v.magnitude, Is.LessThan(1e-4f));
        }

        [Test]
        public void ZeroDistanceMeansNoBodyMotion()
        {
            Assert.That(PeakOf(0f, 1.6f, 0.35f), Is.EqualTo(0f).Within(1e-6f));
        }

        [Test]
        public void RecoilDirectionIsTheKickDirection()
        {
            Vector2 v0 = Vector2.down * SoftCloudPlatform.ImpulsePerPeakDistance(1.6f, 0.35f) * 0.25f;
            SoftCloudPlatform.EvaluateSpring(Vector2.zero, v0, 0.05f, 1.6f, 0.35f, out Vector2 x, out _);
            Assert.That(x.y, Is.LessThan(0f));
            Assert.That(Mathf.Abs(x.x), Is.LessThan(1e-6f));
        }
    }
}
