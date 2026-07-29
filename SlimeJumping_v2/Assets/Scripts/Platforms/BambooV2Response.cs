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

        /// <summary>Signed speed of <paramref name="velocity"/> along the arm's +tangent — linear m/s, NOT divided by arm length. Positive = bends CCW.</summary>
        public static float SignedPerpendicularSpeed(Vector2 arm, Vector2 velocity)
        {
            float armLen = arm.magnitude;
            if (armLen < 0.05f) return 0f;

            Vector2 positiveTangent = new Vector2(-arm.y, arm.x) / armLen;
            return Vector2.Dot(velocity, positiveTangent);
        }

        /// <summary>Maps perpendicular impact speed (m/s) to target tip travel (m). Empty/null curve → 0.</summary>
        public static float EvaluateTipTravelByImpactSpeed(AnimationCurve curve, float impactSpeed)
        {
            if (curve == null || curve.length == 0) return 0f;
            return curve.Evaluate(Mathf.Max(0f, impactSpeed));
        }

        /// <summary>Tip travel (m) → bend angle θ ≈ 2·travel/L in degrees, so one travel graph reads the same on any stalk length.</summary>
        /// Factor 2 = circular-arc cantilever (tip travel ≈ L·θ/2, half a rigid rod); travel saturates at ~0.72·L, so curve values past ~0.36·L are wasted.
        public static float PeakBendDegForTipTravel(float tipTravel, float bambooLength)
        {
            if (bambooLength < 1e-4f) return 0f;
            return (2f * tipTravel / bambooLength) * Mathf.Rad2Deg;
        }

        /// <summary>Initial kick (deg/s) an underdamped spring (freqHz, zeta) needs from rest to peak at <paramref name="targetPeakDeg"/>.</summary>
        /// In-game peak is only approximate — the ODE also carries riderDecayRate, so designers tune by eye.
        public static float AngularVelocityForPeakBendDeg(float targetPeakDeg, float freqHz, float zeta)
        {
            if (Mathf.Abs(targetPeakDeg) < 1e-5f) return 0f;

            float omegaN = 2f * Mathf.PI * Mathf.Max(0.01f, freqHz);
            zeta = Mathf.Clamp(zeta, 0f, 0.99f);
            float sqrt1mz2 = Mathf.Sqrt(1f - zeta * zeta);
            float omegaD = omegaN * sqrt1mz2;
            float phi = Mathf.Atan2(sqrt1mz2, zeta);
            float decay = Mathf.Exp(-(zeta / sqrt1mz2) * phi);
            return targetPeakDeg * omegaD / Mathf.Max(decay, 1e-4f);
        }
    }
}
