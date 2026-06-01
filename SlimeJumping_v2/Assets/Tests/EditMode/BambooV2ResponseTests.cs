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
        public void EvaluateOptionalHeightScale_DisabledReturnsFullScale()
        {
            float scale = BambooV2Response.EvaluateOptionalHeightScale(false, 0.1f, 0f, 2f);

            Assert.AreEqual(1f, scale);
        }
    }
}
