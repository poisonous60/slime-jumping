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
    }
}
