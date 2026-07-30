using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Interaction
{
    [RequireComponent(typeof(Collider2D))]
    public class MemoTrigger : MonoBehaviour
    {
        [SerializeField] private MemoProfile profile;
        [Tooltip("Memo panel owner. Its transform.position is where the memo renders — move it to position the memo.")]
        [SerializeField] private MemoDisplay display;

        private int overlappingColliderCount;

        private void Reset()
        {
            var col = GetComponent<Collider2D>();
            if (col != null) col.isTrigger = true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!TryResolveSlime(other)) return;
            if (profile == null || display == null) return;

            overlappingColliderCount++;
            if (overlappingColliderCount > 1) return; // already shown (multi-collider)

            display.Show(profile);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (!TryResolveSlime(other)) return;
            if (profile == null || display == null) return;
            if (overlappingColliderCount == 0) return; // nothing tracked (enabled mid-overlap)

            overlappingColliderCount--;
            if (overlappingColliderCount > 0) return; // other colliders still inside

            display.Hide(profile);
        }

        // Disable/destroy mid-overlap fires no OnTriggerExit2D, stranding the counter
        // and leaving the memo shown. Treat disable as exit.
        private void OnDisable()
        {
            if (overlappingColliderCount > 0 && display != null && display.isActiveAndEnabled)
                display.Hide(profile);
            overlappingColliderCount = 0;
        }

        private static bool TryResolveSlime(Collider2D other)
        {
            return other.GetComponentInParent<SlimeController>() != null;
        }
    }
}
