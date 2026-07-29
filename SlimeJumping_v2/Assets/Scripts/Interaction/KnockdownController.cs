using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Interaction
{
    /// <summary>IExternalController stub: pins the slime in place for duration, then releases.</summary>
    public class KnockdownController : MonoBehaviour, IExternalController
    {
        [SerializeField] private float duration = 3f;

        private float timer;

        public float Duration => duration;

        public void OnTakeControl(SlimeController slime)
        {
            timer = 0f;
            slime.OverrideVelocity(Vector2.zero);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[Knockdown] TakeControl");
#endif
        }

        public void OnReleaseControl(SlimeController slime)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[Knockdown] ReleaseControl");
#endif
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
