using UnityEngine;
using System.Collections.Generic;
using SlimeJumping.Controller;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Interaction
{
    /// <summary>
    /// Trigger-based interactable detector attached to a child GameObject of Slime.
    /// The child must have its own Kinematic Rigidbody2D + trigger CircleCollider2D
    /// to isolate trigger events from the parent's Rigidbody2D.
    /// </summary>
    public class InteractionDetector : MonoBehaviour
    {
        [SerializeField] private SlimeController slime;
        [SerializeField] private float detectionRadius = 1.5f;

        // NOTE: Trigger-based candidate set. Slime teleport/override can cause stale
        // entries because Unity doesn't fire OnTriggerExit2D after rb.position changes.
        // ClearCandidates() is called from SlimeController.TeleportTo/SetOverride to fix this.
        private readonly HashSet<IInteractable> candidates = new HashSet<IInteractable>();
        private IInteractable currentTarget;

        public bool HasInteractable => currentTarget != null;

        public void TryInteract()
        {
            if (currentTarget == null) return;
            currentTarget.Interact(slime);
        }

        public void ClearCandidates()
        {
            candidates.Clear();
            currentTarget = null;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var interactable = other.GetComponentInParent<IInteractable>();
            if (interactable != null)
                candidates.Add(interactable);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            var interactable = other.GetComponentInParent<IInteractable>();
            if (interactable != null)
                candidates.Remove(interactable);
        }

        private void Update()
        {
            currentTarget = null;

            if (candidates.Count == 0) return;

            float bestDistSq = float.MaxValue;
            Vector2 slimePos = slime.Position;

            // Remove destroyed objects and find best candidate
            candidates.RemoveWhere(c => (c as Object) == null);

            foreach (var candidate in candidates)
            {
                if (!candidate.CanInteract(slime)) continue;

                var mb = candidate as MonoBehaviour;
                if (mb == null) continue;

                float distSq = ((Vector2)mb.transform.position - slimePos).sqrMagnitude;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    currentTarget = candidate;
                }
            }
        }

    }
}
