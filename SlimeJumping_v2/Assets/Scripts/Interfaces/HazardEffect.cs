using UnityEngine;

namespace SlimeJumping.Interfaces
{
    public enum DamageType
    {
        Generic,
        Spike,
        SawBlade,
        Lightning,
        Fire,
        CameraFlash,
        Slice
    }

    [System.Serializable]
    public struct HazardEffect
    {
        /// <summary>Knockback magnitude (units/s). Applied in knockbackDirection.</summary>
        public float knockbackForce;

        /// <summary>World-space knockback direction. Zero = gravityUp, resolved in ApplyHazardEffect.</summary>
        public Vector2 knockbackDirection;

        /// <summary>Duration of the Stunned state (seconds).</summary>
        public float stunDuration;

        /// <summary>Type tag for visual/audio differentiation. No gameplay effect.</summary>
        public DamageType damageType;
    }
}
