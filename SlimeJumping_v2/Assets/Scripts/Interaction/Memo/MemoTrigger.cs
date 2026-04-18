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

        private void Reset()
        {
            var col = GetComponent<Collider2D>();
            if (col != null) col.isTrigger = true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!TryResolveSlime(other)) return;
            if (profile == null || display == null) return;
            display.Show(profile);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (!TryResolveSlime(other)) return;
            if (profile == null || display == null) return;
            display.Hide(profile);
        }

        private static bool TryResolveSlime(Collider2D other)
        {
            return other.GetComponentInParent<SlimeController>() != null;
        }
    }
}
