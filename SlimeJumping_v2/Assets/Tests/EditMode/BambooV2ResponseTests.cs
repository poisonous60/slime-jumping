using NUnit.Framework;
using SlimeJumping.Platforms;
using UnityEngine;

namespace SlimeJumping.Tests.EditMode
{
    public class BambooV2ResponseTests
    {
        [Test]
        public void EvaluateLaunchReleaseAngularVelocity_LowContactProducesLessKickThanHighContact()
        {
            Vector2 launchVelocity = new Vector2(-10f, 0f);
            float velocityScale = 0.85f;

            float lowHeightScale = BambooV2Response.EvaluateOptionalHeightScale(true, 0.25f, 0f, 1.75f);
            float highHeightScale = BambooV2Response.EvaluateOptionalHeightScale(true, 1f, 0f, 1.75f);

            float lowKick = BambooV2Response.EvaluateLaunchReleaseAngularVelocityDeg(
                new Vector2(0f, 1f),
                launchVelocity,
                velocityScale,
                lowHeightScale);
            float highKick = BambooV2Response.EvaluateLaunchReleaseAngularVelocityDeg(
                new Vector2(0f, 4f),
                launchVelocity,
                velocityScale,
                highHeightScale);

            Assert.That(Mathf.Abs(lowKick), Is.LessThan(Mathf.Abs(highKick)));
        }

        [Test]
        public void EvaluateLaunchReleaseAngularVelocity_FasterImpactBendsMore()
        {
            // Impact bend (v2BendImpactSpeedScale path) reuses this helper, so a faster
            // tangential side hit must inject a larger tip angular velocity — the
            // "auto-shake harder on a faster hit" guarantee.
            Vector2 arm = new Vector2(0.15f, 2.5f);
            float scale = 1.5f;
            float heightScale = 1f;

            float slowHit = BambooV2Response.EvaluateLaunchReleaseAngularVelocityDeg(
                arm, new Vector2(-6f, 0f), scale, heightScale);
            float fastHit = BambooV2Response.EvaluateLaunchReleaseAngularVelocityDeg(
                arm, new Vector2(-14f, 0f), scale, heightScale);

            Assert.That(Mathf.Abs(fastHit), Is.GreaterThan(Mathf.Abs(slowHit)));
        }

        [Test]
        public void EvaluateOptionalHeightScale_DisabledReturnsFullScale()
        {
            float scale = BambooV2Response.EvaluateOptionalHeightScale(false, 0.1f, 0f, 2f);

            Assert.AreEqual(1f, scale);
        }

        [Test]
        public void SignedPerpendicularSpeed_VerticalArm_EqualsHorizontalSpeed()
        {
            // Vertical bamboo, contact partway up: the +tangent of the arm is horizontal,
            // so a purely horizontal hit's perpendicular speed equals its x-speed.
            Vector2 arm = new Vector2(0f, 2.5f);

            float perp = BambooV2Response.SignedPerpendicularSpeed(arm, new Vector2(-8f, -3f));

            // +tangent = (-arm.y, arm.x)/|arm| = (-1, 0); dot with (-8,-3) = 8.
            Assert.That(perp, Is.EqualTo(8f).Within(1e-3f));
        }

        [Test]
        public void EvaluateTipTravelByImpactSpeed_FasterImpactProducesMoreTravel()
        {
            var curve = AnimationCurve.Linear(0f, 0f, 18f, 3f);

            float slow = BambooV2Response.EvaluateTipTravelByImpactSpeed(curve, 6f);
            float fast = BambooV2Response.EvaluateTipTravelByImpactSpeed(curve, 14f);

            Assert.That(fast, Is.GreaterThan(slow));
        }

        [Test]
        public void EvaluateTipTravelByImpactSpeed_EmptyCurveIsZero()
        {
            Assert.AreEqual(0f, BambooV2Response.EvaluateTipTravelByImpactSpeed(new AnimationCurve(), 10f));
            Assert.AreEqual(0f, BambooV2Response.EvaluateTipTravelByImpactSpeed(null, 10f));
        }

        [Test]
        public void PeakBendDegForTipTravel_LongStalkBendsLessAngleForSameTravel()
        {
            // Same target tip travel (2 m): a long bamboo reaches it at a smaller bend angle,
            // a short bamboo at a larger angle — the length-normalisation guarantee.
            float travel = 2f;

            float longStalk = BambooV2Response.PeakBendDegForTipTravel(travel, 8f);
            float shortStalk = BambooV2Response.PeakBendDegForTipTravel(travel, 2f);

            Assert.That(longStalk, Is.LessThan(shortStalk));
            // Cantilever arc: θ = 2 * travel / L (rad). Short stalk (L=2) → 2 rad.
            Assert.That(shortStalk, Is.EqualTo(2f * Mathf.Rad2Deg).Within(1e-2f));
        }

        [Test]
        public void AngularVelocityForPeakBendDeg_LargerPeakNeedsLargerKick_SignPreserved()
        {
            float small = BambooV2Response.AngularVelocityForPeakBendDeg(20f, 0.7f, 0.12f);
            float large = BambooV2Response.AngularVelocityForPeakBendDeg(60f, 0.7f, 0.12f);

            Assert.That(large, Is.GreaterThan(small));
            Assert.That(small, Is.GreaterThan(0f));
            // Negative target (other bend direction) flips the kick sign.
            float neg = BambooV2Response.AngularVelocityForPeakBendDeg(-60f, 0.7f, 0.12f);
            Assert.That(neg, Is.EqualTo(-large).Within(1e-3f));
        }

        [Test]
        public void AngularVelocityForPeakBendDeg_UndampedKickReachesPeakInOneQuarterPeriod()
        {
            // ζ=0: x(t) = (ω0/ωn) sin(ωn t), peak = ω0/ωn → ω0 = peak·ωn. The helper must
            // return that (decay factor = 1, ωd = ωn).
            float freqHz = 1.0f;
            float omegaN = 2f * Mathf.PI * freqHz;
            float peakDeg = 45f;

            float kickDeg = BambooV2Response.AngularVelocityForPeakBendDeg(peakDeg, freqHz, 0f);

            Assert.That(kickDeg, Is.EqualTo(peakDeg * omegaN).Within(1e-2f));
        }
    }
}
