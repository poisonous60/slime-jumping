using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Interaction
{
    /// <summary>
    /// Tier 3 verification IExternalController: holds slime in place for a duration,
    /// then releases. Intentionally minimal — Timeline-based production implementation
    /// is deferred to #29.
    /// </summary>
    public class KnockdownController : MonoBehaviour, IExternalController
    {
        [SerializeField] private float duration = 3f;

        private float timer;

        public float Duration => duration;

        public void OnTakeControl(SlimeController slime)
        {
            timer = 0f;
            slime.OverrideVelocity(Vector2.zero);
            Debug.Log("[Knockdown] TakeControl");
        }

        public void OnReleaseControl(SlimeController slime)
        {
            Debug.Log("[Knockdown] ReleaseControl");
        }

        public void ExternalUpdate(SlimeController slime)
        {
            // No visual effects — avoids conflict with SlimeVisual body rotation pipeline
        }

        public void ExternalFixedUpdate(SlimeController slime, float dt)
        {
            slime.OverrideVelocity(Vector2.zero);

            timer += dt;
            if (timer >= duration)
            {
                slime.ReleaseOverride(this);
            }
        }
    }
}
