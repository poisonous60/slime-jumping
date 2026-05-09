using SlimeJumping.Controller;
using SlimeJumping.Visual;
using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// Trigger zone that freezes the slime visually while inside.
    /// Pure visual: no dynamics modifier — flagged as forwarded to
    /// <see cref="SlimeFrostController"/> on the slime, which handles
    /// the freezeT timeline + body MPB push + trail-stamp routing.
    ///
    /// Stage2 single-slime assumption — EnvironmentalZone tracks one
    /// trackedSlime, which lines up with manager-side single freezeT.
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
