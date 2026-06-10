using UnityEngine;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // --- Slime-split integration ---
        //
        // While the slime is small (cut), SlimeSplitSequence ramps this multiplier from a
        // reduced value back to 1 as it regrows — a smaller slime jumps weaker, recovering
        // full power. Default 1 = no effect, so normal play is byte-for-byte unchanged.
        //
        // Applied in Launch() to the charge-launch power only (momentum/carrier/rope inheritance
        // are NOT scaled — those are inherited motion, not jump strength).
        //
        // The body size itself lives in SlimeController.BodyScale.cs (SetBodyScale /
        // BodyScaleRatio) — the cut IS a smaller slime, there is no cut-plane collision shape.
        public float SplitLaunchMultiplier = 1f;
    }
}
