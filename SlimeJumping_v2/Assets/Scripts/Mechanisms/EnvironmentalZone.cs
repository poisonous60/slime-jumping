using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// Base class for trigger zones that apply continuous effects to the slime.
    /// Handles enter/exit detection and slime tracking.
    /// Subclasses override ApplyEffect and optionally OnSlimeEnter/OnSlimeExit.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public abstract class EnvironmentalZone : MonoBehaviour
    {
        protected SlimeController trackedSlime;
        private int overlappingColliderCount;

        protected virtual void Reset()
        {
            GetComponent<Collider2D>().isTrigger = true;
        }

        protected virtual void OnTriggerEnter2D(Collider2D other)
        {
            var slime = other.GetComponentInParent<SlimeController>();
            if (slime == null) return;

            overlappingColliderCount++;
            if (trackedSlime == slime) return; // already tracked (multi-collider)

            trackedSlime = slime;
            OnSlimeEnter(slime);
        }

        protected virtual void OnTriggerExit2D(Collider2D other)
        {
            var slime = other.GetComponentInParent<SlimeController>();
            if (slime == null || slime != trackedSlime) return;

            overlappingColliderCount--;
            if (overlappingColliderCount > 0) return; // other colliders still inside

            OnSlimeExit(slime);
            trackedSlime = null;
        }

        protected virtual void FixedUpdate()
        {
            if (trackedSlime == null) return;
            ApplyEffect(trackedSlime, Time.fixedDeltaTime);
        }

        protected virtual void OnSlimeEnter(SlimeController slime) { }
        protected virtual void OnSlimeExit(SlimeController slime) { }
        protected abstract void ApplyEffect(SlimeController slime, float dt);
    }
}
