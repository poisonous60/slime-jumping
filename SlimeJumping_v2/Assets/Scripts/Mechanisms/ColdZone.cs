using SlimeJumping.Controller;
using SlimeJumping.Visual;
using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// Visual-only frost flag; <see cref="SlimeFrostController"/> owns the freezeT timeline
    /// and MPB push. Single-slime assumption: EnvironmentalZone tracks one trackedSlime.
    /// </summary>
    [AddComponentMenu("SlimeJumping/Mechanisms/Cold Zone")]
    public class ColdZone : EnvironmentalZone
    {
        private SlimeFrostController frost;

        protected override void OnSlimeEnter(SlimeController slime)
        {
            frost = slime.GetComponentInChildren<SlimeFrostController>();
            if (frost != null) frost.PushColdZone();
        }

        protected override void OnSlimeExit(SlimeController slime)
        {
            if (frost != null)
            {
                frost.PopColdZone();
                frost = null;
            }
        }

        protected override void ApplyEffect(SlimeController slime, float dt)
        {
            // No per-tick effect — freezeT advances inside SlimeFrostController.Update.
        }
    }
}
