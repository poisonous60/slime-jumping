using System;
using UnityEngine;
using System.Collections.Generic;
using SlimeJumping.Controller;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Interaction
{
    /// <summary>Interactable detector on a Slime child; the child needs its own Kinematic Rigidbody2D + trigger collider to isolate trigger events from the parent body.</summary>
    public class InteractionDetector : MonoBehaviour
    {
        [SerializeField] private SlimeController slime;
        [SerializeField] private float detectionRadius = 1.5f;

        // Unity fires no OnTriggerExit2D after an rb.position jump, so teleport/override leaves
        // stale entries; SlimeController.TeleportTo/SetOverride call ClearCandidates().
        private readonly HashSet<IInteractable> candidates = new HashSet<IInteractable>();
        private IInteractable currentTarget;

        // Narrow phase: the detector's large trigger is broad-phase only, the body must really overlap.
        private CircleCollider2D slimeBody;

        public bool HasInteractable => currentTarget != null;
        public IInteractable CurrentTarget => currentTarget;

        // Fires when the best candidate changes. Args: (previous, next). Either may be null.
        public event Action<IInteractable, IInteractable> OnTargetChanged;
        // Fires right after a successful Interact() call on the current target.
        public event Action<IInteractable> OnInteracted;

        public void TryInteract()
        {
            if (currentTarget == null) return;
            var fired = currentTarget;
            fired.Interact(slime);
            OnInteracted?.Invoke(fired);
        }

        public void ClearCandidates()
        {
            candidates.Clear();
            SetCurrentTarget(null);
        }

        // Unity fires no OnTriggerEnter2D for colliders already overlapping when detection re-enables
        // (e.g. ReleaseOverride from SitPose while still inside the bench trigger), so rescan by hand.
        public void Rescan()
        {
            candidates.Clear();
            SetCurrentTarget(null);

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
            IInteractable best = null;

            if (candidates.Count > 0)
            {
                if (slimeBody == null) slimeBody = slime.GetComponent<CircleCollider2D>();
                if (slimeBody != null)
                {
                    float bestDistSq = float.MaxValue;
                    Vector2 slimePos = slime.Position;

                    candidates.RemoveWhere(c => (c as UnityEngine.Object) == null);

                    foreach (var candidate in candidates)
                    {
                        if (!candidate.CanInteract(slime)) continue;

                        var mb = candidate as MonoBehaviour;
                        if (mb == null) continue;

                        if (!IsSlimeTouching(mb)) continue;

                        float distSq = ((Vector2)mb.transform.position - slimePos).sqrMagnitude;
                        if (distSq < bestDistSq)
                        {
                            bestDistSq = distSq;
                            best = candidate;
                        }
                    }
                }
            }

            SetCurrentTarget(best);
        }

        private void SetCurrentTarget(IInteractable next)
        {
            if (ReferenceEquals(currentTarget, next)) return;
            var prev = currentTarget;
            currentTarget = next;
            OnTargetChanged?.Invoke(prev, next);
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
