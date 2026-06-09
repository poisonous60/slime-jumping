using UnityEngine;

namespace SlimeJumping.Settings
{
    /// <summary>
    /// Tunables for the "slime sliced by a blade" reaction. One asymmetric cut along
    /// the slime's entry direction:
    ///   • the small severed piece (the slime's own blob mesh, spawned small and squashed to
    ///     a sliver, then un-squashed into a round droplet) falls under the SAME gravity as the
    ///     slime, then MELTS into the ground leaving a WorldTrail stain;
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
        [Range(0.01f, 0.9f)]
        [Tooltip("Asymmetry: cut plane offset off-center, as a fraction of the slime radius. Min 0.01 (NOT 0) so the cut plane never passes through the body center — that guarantees the slime core (dot=0) is never clipped away, so no number of overlapping cuts can erase the blob. Higher = smaller severed piece. ~0.45 ≈ a 20–30% piece.")]
        public float cutOffsetFraction = 0.45f;
        [Range(0f, 0.9f)]
        [Tooltip("The bottom band (surface-contact side, -localY) that is NEVER clipped, as a fraction of the slime height. Keeps the slime from visually floating when a cut would remove its ground-contact bottom — only the upper part is cut. 0.3 = bottom 30% always kept.")]
        public float bottomProtectFraction = 0.3f;

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

        [Header("Severed piece — surface-tension round-up (flat sliver → round droplet)")]
        [Tooltip("Final droplet radius as a fraction of the slime radius. The piece is the slime's own blob mesh scaled to this — a genuinely round ball, not a reprojected crescent.")]
        [Range(0.1f, 0.9f)]
        public float pieceFinalScale = 0.4f;
        [Tooltip("Initial flatten along the cut normal at spawn: the round droplet starts squashed to a thin sliver (matches 'just cut'), then un-squashes to a ball. 0.1 = very flat.")]
        [Range(0.02f, 1f)]
        public float pieceFlatten = 0.12f;
        [Tooltip("Time the piece holds its flat sliver shape (matches the body's clean-cut beat) before it starts rounding up.")]
        public float pieceSharpBeat = 0.15f;
        [Tooltip("Time the piece takes to un-squash from the flat sliver into a round goo droplet, in the air.")]
        public float pieceRoundUpDuration = 0.35f;
        [Tooltip("0→1 ease of the sliver→ball un-squash. Default ease-in-out if no curve set.")]
        public AnimationCurve pieceRoundUpCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

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

        [Header("Regen timeline — back-loaded per wound (hold → gather → fill)")]
        [Tooltip("HOLD: the clean flat cut face is held this long before anything heals. Slime is wounded + weak here.")]
        public float regenHoldDuration = 1.5f;
        [Tooltip("GATHER: the fresh-goo fill tint ramps up at the wound before the cut starts closing. Brief lead-in to the fill.")]
        public float regenGatherDuration = 0.4f;
        [Tooltip("FILL: the cut slowly closes (clip recedes off the mesh) as slime goo wells up to fill the opening. SLOW — the wound should take its time refilling, not snap shut.")]
        public float regenFillDuration = 2.5f;
        [Tooltip("0→1 ease of the fill. Ease-in-out reads as goo steadily welling up. Default ease-in-out.")]
        public AnimationCurve regenFillCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Regeneration look — fresh slime goo wells up to fill the cut (no energy glow)")]
        [ColorUsage(false, false)]
        [Tooltip("Color of the fresh goo at the fill front — a lighter/wetter slime tone, NOT energy. The band at the closing edge is tinted toward this, settling to the body color behind. Keep close to the slime's own green.")]
        public Color sliceHealColor = new Color(0.6f, 0.88f, 0.55f, 1f);
        [Range(0f, 1f)]
        [Tooltip("How strongly the fill front is tinted toward the fresh-goo color. 0 = no tint (plain clip recede). ~0.7 = clearly fresh goo welling up.")]
        public float sliceHealIntensity = 0.7f;
        [Tooltip("OS band width of the fresh-goo fill front on the kept side at the closing edge (how thick the wet 'rising goo' band is). A fraction of the slime radius.")]
        public float sliceHealWidth = 0.3f;

        [Header("Stacking — re-cut while regenerating")]
        [Range(1, 3)]
        [Tooltip("Max simultaneous wounds. A new cut beyond this evicts the most-healed wound. Shader supports up to 3 planes.")]
        public int maxWounds = 3;
        [Tooltip("Minimum seconds between two cuts (internal guard against same-frame / double-hazard re-trigger). The blade's own hitCooldown is the primary gate.")]
        public float recutMinInterval = 0.15f;

        [Header("Slice look")]
        [ColorUsage(false, false)]
        [Tooltip("Flat interior color of the cut cross-section (the 'inside' of the slime). Darker than the body so the open cut reads as deep flesh, not a bright fill.")]
        public Color sliceCapColor = new Color(0.18f, 0.32f, 0.18f, 1f);

        [Header("Wounded jump power")]
        [Range(0f, 1f)]
        [Tooltip("Jump-power multiplier at the moment of the slice (fresh wound). Ramps linearly back to 1 as the wound heals over regenDuration. 1 = no reduction.")]
        public float woundedJumpMultiplierMin = 0.4f;
    }
}
