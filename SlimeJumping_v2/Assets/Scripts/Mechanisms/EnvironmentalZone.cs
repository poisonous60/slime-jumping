using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    /// <summary>Trigger zone base: tracks the slime across multiple colliders and calls ApplyEffect while it overlaps.</summary>
    [RequireComponent(typeof(Collider2D))]
    public abstract class EnvironmentalZone : MonoBehaviour
    {
        protected SlimeController trackedSlime;
        protected int overlappingColliderCount;

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

        // Disable/destroy mid-overlap fires no OnTriggerExit2D, stranding the slime's
        // Push/Pop counters (blockAttach, inFluid). Treat disable as exit.
        protected virtual void OnDisable()
        {
            if (trackedSlime != null)
            {
                OnSlimeExit(trackedSlime);
                trackedSlime = null;
            }
            overlappingColliderCount = 0;
        }
    }
}
