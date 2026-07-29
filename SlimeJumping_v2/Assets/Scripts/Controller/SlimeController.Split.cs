using UnityEngine;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // While cut, SlimeSplitSequence ramps this from a reduced value back to 1 as the slime regrows.
        // Applied in Launch() to charge power only — momentum/carrier/rope inheritance stays unscaled.
        // Body size itself lives in SlimeController.BodyScale.cs.
        public float SplitLaunchMultiplier = 1f;
    }
}
