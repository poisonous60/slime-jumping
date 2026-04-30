using UnityEngine;

namespace SlimeJumping.Physics
{
    [System.Serializable]
    public struct AngleScaledValue
    {
        [Tooltip("Value at angleMin or below")]
        public float baseValue;
        [Tooltip("Value at angleMax or above")]
        public float fullValue;
        [Tooltip("Interpolation start angle (degrees, 0 = floor, 90 = wall)")]
        public float angleMin;
        [Tooltip("Interpolation end angle (degrees, 0 = floor, 90 = wall)")]
        public float angleMax;

        public float Evaluate(float surfaceAngleDeg)
        {
            float t = Mathf.InverseLerp(angleMin, angleMax, surfaceAngleDeg);
            return Mathf.Lerp(baseValue, fullValue, t);
        }

        public static AngleScaledValue Constant(float value) => new AngleScaledValue
        {
            baseValue = value,
            fullValue = value,
            angleMin = 0f,
            angleMax = 90f
        };

        // Component-wise lerp. The four scalars (baseValue/fullValue/angleMin/angleMax)
        // each interpolate independently — for two reasonable surface profiles the
        // interpolated curve stays a sensible angle ramp at every t.
        public static AngleScaledValue Lerp(AngleScaledValue a, AngleScaledValue b, float t) => new AngleScaledValue
        {
            baseValue = Mathf.Lerp(a.baseValue, b.baseValue, t),
            fullValue = Mathf.Lerp(a.fullValue, b.fullValue, t),
            angleMin  = Mathf.Lerp(a.angleMin,  b.angleMin,  t),
            angleMax  = Mathf.Lerp(a.angleMax,  b.angleMax,  t),
        };
    }
}
