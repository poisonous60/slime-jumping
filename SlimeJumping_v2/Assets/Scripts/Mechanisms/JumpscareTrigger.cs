using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.UI;

namespace SlimeJumping.Mechanisms
{
    [RequireComponent(typeof(Collider2D))]
    public class JumpscareTrigger : MonoBehaviour
    {
        [SerializeField] private JumpscareCanvasController canvas;
        [SerializeField] private Sprite scareImage;
        [SerializeField] private AudioClip scareSound;
        [SerializeField] private bool oneShot = true;
        [SerializeField] private float displaySeconds = 0.4f;
        [SerializeField] private float fadeSeconds = 0.3f;

        private SlimeController trackedSlime;
        private int overlappingColliderCount;

        void Awake()
        {
            if (canvas == null)
                canvas = FindFirstObjectByType<JumpscareCanvasController>(FindObjectsInactive.Include);
        }

        void Reset()
        {
            var col = GetComponent<Collider2D>();
            if (col != null) col.isTrigger = true;
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            var slime = other.GetComponentInParent<SlimeController>();
            if (slime == null) return;

            overlappingColliderCount++;
            if (trackedSlime == slime) return; // already tracked (multi-collider)

            trackedSlime = slime;
            if (canvas == null) return;

            canvas.Trigger(scareImage, scareSound, displaySeconds, fadeSeconds);
            if (oneShot) enabled = false;
        }

        void OnTriggerExit2D(Collider2D other)
        {
            var slime = other.GetComponentInParent<SlimeController>();
            if (slime == null || slime != trackedSlime) return;

            overlappingColliderCount--;
            if (overlappingColliderCount > 0) return; // other colliders still inside

            trackedSlime = null;
        }

        // Disable/destroy mid-overlap fires no OnTriggerExit2D, stranding the counter on a
        // slime that has left. Treat disable as exit.
        void OnDisable()
        {
            trackedSlime = null;
            overlappingColliderCount = 0;
        }
    }
}
