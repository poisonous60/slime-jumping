namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // --- Slime-split integration ---
        //
        // While the slime is "wounded" (sliced, mid-regeneration), SlimeSplitSequence
        // ramps this multiplier from a reduced value back to 1 as the cross-section
        // heals — so a fragmented slime jumps weaker, recovering full power as it fills
        // back in. Default 1 = no effect, so normal play and every other system are
        // byte-for-byte unchanged.
        //
        // Applied in Launch() to the charge-launch power only (momentum/carrier/rope
        // inheritance are NOT scaled — those are inherited motion, not jump strength).
        public float SplitLaunchMultiplier = 1f;
    }
}
