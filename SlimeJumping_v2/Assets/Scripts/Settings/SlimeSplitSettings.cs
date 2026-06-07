using UnityEngine;

namespace SlimeJumping.Settings
{
    /// <summary>
    /// Tunables for the "slime sliced by a blade" reaction. One asymmetric cut along
    /// the slime's entry direction:
    ///   • the small severed piece (DynamicMeshCutter geometry, real cross-section) falls
    ///     under the SAME gravity as the slime, then MELTS into the ground leaving a
    ///     WorldTrail stain;
    ///   • the body keeps its full mesh and shows the cut as a shader CLIP wound that
    ///     HEALS smoothly (continuous, like a burn fading) over regenDuration, during
    ///     which the slime is controllable with reduced jump power.
    /// ScriptableObject so the whole feel is Inspector-editable — NO magic numbers in code.
    /// </summary>
    [CreateAssetMenu(menuName = "SlimeJumping/Slime Split Settings", fileName = "SlimeSplitSettings")]
    public class SlimeSplitSettings : ScriptableObject
    {
        [Header("Cut")]
        [Tooltip("Random rotation (± deg) added to the entry-direction cut plane so no two slices look identical.")]
        public float cutNormalJitterDeg = 12f;
        [Range(0f, 0.9f)]
        [Tooltip("Asymmetry: cut plane offset off-center, as a fraction of the slime radius. 0 = 50/50. Higher = smaller severed piece. ~0.45 ≈ a 20–30% piece.")]
        public float cutOffsetFraction = 0.45f;

        [Header("Severed piece — flies off (same gravity as the slime)")]
        [Range(0f, 1f)]
        [Tooltip("Fraction of the slime's incoming velocity the severed piece inherits.")]
        public float inheritVelocityFactor = 0.5f;
        [Tooltip("Outward pop along the cut normal (random in range).")]
        public float severImpulseMin = 2f;
        public float severImpulseMax = 4f;
        [Tooltip("Small upward kick so the piece arcs before falling.")]
        public float upwardImpulse = 1.5f;
        public float linearDamping = 0.05f;
        public float angularDamping = 0.05f;
        [Range(0.1f, 1f)]
        [Tooltip("Severed piece CircleCollider2D radius as a fraction of its mesh half-extent.")]
        public float colliderRadiusFactor = 0.7f;
        [Tooltip("Spin (deg/s) range. Both 0 = no rotation (a piece that flew off, not a tumbling crescent).")]
        public float spinMinDeg = 0f;
        public float spinMaxDeg = 0f;
        [Tooltip("Safety: if the piece never lands (fell into a pit), melt anyway after this airtime.")]
        public float severedMaxAirTime = 4f;

        [Header("Severed wobble (viscous jiggle)")]
        public float jiggleAmplitude = 0.35f;
        public float jiggleFrequency = 9f;
        public float jiggleDecay = 3.5f;
        [Tooltip("How much a bounce re-energises the wobble envelope.")]
        public float landingJiggleBoost = 1f;
        [Range(0f, 1f)]
        [Tooltip("Scales the shader velocity-stretch (teardrop) while the piece flies.")]
        public float velocityStretchScale = 0.5f;
        public float wobbleSpeedRef = 12f;

        [Header("Melt (on landing — NOT a uniform shrink)")]
        [Tooltip("Grace period after spawn before a collision can start the melt. Prevents the piece from melting instantly on the spawn-overlap with the ground/slime — it must actually fly + land first.")]
        public float meltContactGrace = 0.2f;
        [Tooltip("Time to melt away once landed.")]
        public float meltDuration = 1.2f;
        [Range(0f, 3f)]
        [Tooltip("How much the puddle spreads sideways as it flattens.")]
        public float meltSpread = 0.8f;
        [Tooltip("Extra cast distance (world units) BELOW the piece, on top of its own size, when finding the ground to melt onto at landing. The puddle's bottom is then pinned to that surface for the whole melt — so it never floats in the air or sinks through the floor.")]
        public float meltGroundProbe = 0.5f;
        [Tooltip("Layers counted as ground for the melt-onto cast. Leave as Nothing/empty to use every layer except the slime's (recommended).")]
        public LayerMask groundMask = 0;
        [Range(0f, 1.5f)]
        [Tooltip("PIT FALLBACK ONLY (no ground found beneath the piece). On real ground the puddle is hard-pinned to the surface instead. 1 = bottom stays put as it flattens; >1 sinks; <1 lifts.")]
        public float meltSinkFactor = 1f;

        [Header("Melt trail stain (WorldTrail)")]
        [Tooltip("World-radius of the stain stamped where the piece melts. 0 = no stain.")]
        public float meltStampRadius = 0.45f;
        [Range(0f, 1f)]
        [Tooltip("Stain mask strength.")]
        public float meltStampStrength = 0.7f;
        [Tooltip("Seconds between stain re-stamps during the melt (the stain widens with the puddle).")]
        public float meltStampInterval = 0.12f;

        [Header("Regen — wound heals (cut cross-section fills back in, smoothly)")]
        [Tooltip("Time for the shader clip plane to recede off the mesh = the cut filling back to the full slime. The slime is controllable once the stun ends; jump power ramps over this time. Control-loss duration itself = the hazard's stun duration.")]
        public float regenDuration = 2.5f;

        [Header("Slice look")]
        [ColorUsage(false, false)]
        [Tooltip("Flat interior color of the cut cross-section (the 'inside' of the slime). Darken slightly vs the body for a cut-flesh read.")]
        public Color sliceCapColor = new Color(0.5f, 0.75f, 0.5f, 1f);

        [Header("Wounded jump power")]
        [Range(0f, 1f)]
        [Tooltip("Jump-power multiplier at the moment of the slice (fresh wound). Ramps linearly back to 1 as the wound heals over regenDuration. 1 = no reduction.")]
        public float woundedJumpMultiplierMin = 0.4f;
    }
}
