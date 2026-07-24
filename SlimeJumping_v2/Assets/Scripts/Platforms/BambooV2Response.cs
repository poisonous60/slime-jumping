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

        /// <summary>
        /// Signed speed of <paramref name="velocity"/> projected onto the direction
        /// perpendicular to the bamboo axis at the contact height (the +tangent of the arm).
        /// This is the "직각으로 부딪히는 속도" — the velocity component straight into/across
        /// the stalk that actually drives the bend. Positive = bends CCW (+currentAngle).
        /// Linear m/s, NOT divided by arm length (unlike the angular-velocity helper above).
        /// </summary>
        public static float SignedPerpendicularSpeed(Vector2 arm, Vector2 velocity)
        {
            float armLen = arm.magnitude;
            if (armLen < 0.05f) return 0f;

            Vector2 positiveTangent = new Vector2(-arm.y, arm.x) / armLen;
            return Vector2.Dot(velocity, positiveTangent);
        }

        /// <summary>
        /// Graph version of the impact bend: maps perpendicular impact speed (m/s) to the
        /// target tip travel (metres — how far the tip moves). Empty/null curve → 0.
        /// </summary>
        public static float EvaluateTipTravelByImpactSpeed(AnimationCurve curve, float impactSpeed)
        {
            if (curve == null || curve.length == 0) return 0f;
            return curve.Evaluate(Mathf.Max(0f, impactSpeed));
        }

        /// <summary>
        /// Length normalisation: a target tip travel (metres) on a stalk of length
        /// <paramref name="bambooLength"/> maps to bend angle θ ≈ 2·travel / L, returned in
        /// degrees. The same travel therefore needs a SMALLER angle on a long stalk and a
        /// LARGER angle on a short one, so one tip-travel graph reads consistently across
        /// every bamboo length.
        ///
        /// The factor 2 comes from the bamboo being a circular-arc CANTILEVER, not a rigid
        /// rod on a hinge. ComputeCenterlinePoint/BambooBend.shader put the tip at
        /// x = (L/θ)(cos θ − 1), so its lateral travel is L·(1−cos θ)/θ ≈ L·θ/2 — half what a
        /// rigid rod (L·θ) would give. Inverting the small-angle form yields θ ≈ 2·travel/L,
        /// which is exact as θ→0 and ~15% optimistic near 90°.
        ///
        /// Note the arc saturates: lateral tip travel peaks at ~0.72·L (around θ=130°) and
        /// cannot exceed it, so authoring travel beyond ~0.36·L in the curve is wasted range.
        /// </summary>
        public static float PeakBendDegForTipTravel(float tipTravel, float bambooLength)
        {
            if (bambooLength < 1e-4f) return 0f;
            return (2f * tipTravel / bambooLength) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Initial tip angular velocity (deg/s) an underdamped spring of frequency
        /// <paramref name="freqHz"/> and damping ratio <paramref name="zeta"/> needs, when
        /// kicked from rest, to reach a first-swing peak deflection of
        /// <paramref name="targetPeakDeg"/>. Lets the designer author "this impact speed →
        /// this bend angle" directly: the curve gives the peak, this returns the kick.
        ///
        /// For ẍ + 2ζωₙẋ + ωₙ²x = 0 with x(0)=0, ẋ(0)=ω₀ the first peak is
        /// x_peak = (ω₀/ω_d)·exp(−ζ/√(1−ζ²)·atan2(√(1−ζ²), ζ)), so
        /// ω₀ = x_peak·ω_d/decay. Angle units cancel (linear system), so deg in → deg/s out.
        /// The peak is approximate in-game because the ODE also carries the separate
        /// riderDecayRate exp-decay — designers tune the curve by eye.
        /// </summary>
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
