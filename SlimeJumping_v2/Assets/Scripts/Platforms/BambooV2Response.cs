using UnityEngine;

namespace SlimeJumping.Platforms
{
    public enum BambooV2ResponseMode
    {
        Additive = 0,
        SpeedCurve = 1
    }

    public static class BambooV2Response
    {
        public static float Evaluate(BambooV2ResponseMode mode, float speed, AnimationCurve curve, float additiveValue)
        {
            if (mode != BambooV2ResponseMode.SpeedCurve)
                return additiveValue;

            if (curve == null || curve.length == 0)
                return additiveValue;

            return curve.Evaluate(Mathf.Max(0f, speed));
        }

        public static float EvaluateHeightScale(float height01, float minHeightScale, float heightPower)
        {
            float shaped = Mathf.Pow(Mathf.Clamp01(height01), Mathf.Max(0.1f, heightPower));
            return Mathf.Lerp(Mathf.Clamp01(minHeightScale), 1f, shaped);
        }

        public static float EvaluateOptionalHeightScale(bool useHeightScale, float height01, float minHeightScale, float heightPower)
        {
            return useHeightScale ? EvaluateHeightScale(height01, minHeightScale, heightPower) : 1f;
        }

        public static float EvaluateLaunchReleaseAngularVelocityDeg(
            Vector2 arm,
            Vector2 launchVelocity,
            float velocityScale,
            float heightScale)
        {
            if (velocityScale <= 0f) return 0f;

            float armLen = arm.magnitude;
            if (armLen < 0.05f) return 0f;

            Vector2 positiveTangent = new Vector2(-arm.y, arm.x) / armLen;
            float tangentialLaunchSpeed = Vector2.Dot(launchVelocity, positiveTangent);
            return (tangentialLaunchSpeed / armLen)
                * velocityScale
                * Mathf.Clamp01(heightScale)
                * Mathf.Rad2Deg;
        }
    }
}
