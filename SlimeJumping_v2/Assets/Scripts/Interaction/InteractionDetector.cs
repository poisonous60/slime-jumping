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

        // Narrow-phase: slime's own body collider must actually overlap the interactable.
        // Detector's large trigger is only a broad-phase wake-up.
        private CircleCollider2D slimeBody;

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

        // Repopulate candidates from current overlaps. Unity doesn't fire OnTriggerEnter2D
        // for colliders that are already overlapping when we re-enable detection
        // (e.g. after ReleaseOverride from SitPose while still inside the bench's trigger).
        public void Rescan()
        {
            candidates.Clear();
            currentTarget = null;

            var myCol = GetComponent<Collider2D>();
            if (myCol == null) return;

            var contactFilter = new ContactFilter2D { useTriggers = true };
            contactFilter.SetLayerMask(Physics2D.GetLayerCollisionMask(gameObject.layer));
            var results = new Collider2D[16];
            int hits = myCol.Overlap(contactFilter, results);
            for (int i = 0; i < hits; i++)
            {
                var interactable = results[i].GetComponentInParent<IInteractable>();
                if (interactable != null) candidates.Add(interactable);
            }
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

            if (slimeBody == null) slimeBody = slime.GetComponent<CircleCollider2D>();
            if (slimeBody == null) return;

            float bestDistSq = float.MaxValue;
            Vector2 slimePos = slime.Position;

            // Remove destroyed objects and find best candidate
            candidates.RemoveWhere(c => (c as Object) == null);

            foreach (var candidate in candidates)
            {
                if (!candidate.CanInteract(slime)) continue;

                var mb = candidate as MonoBehaviour;
                if (mb == null) continue;

                // Narrow-phase: require slime body to actually touch one of the candidate's colliders.
                if (!IsSlimeTouching(mb)) continue;

                float distSq = ((Vector2)mb.transform.position - slimePos).sqrMagnitude;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    currentTarget = candidate;
                }
            }
        }

        private bool IsSlimeTouching(MonoBehaviour candidate)
        {
            var cols = candidate.GetComponentsInChildren<Collider2D>();
            for (int i = 0; i < cols.Length; i++)
            {
                var c = cols[i];
                if (c == null || !c.enabled) continue;
                if (slimeBody.Distance(c).isOverlapped) return true;
            }
            return false;
        }

    }
}
