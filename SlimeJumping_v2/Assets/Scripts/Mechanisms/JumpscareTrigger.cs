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

        void Awake()
        {
            // Scene migration fallback: pick up the new JumpscareCanvasController
            // under UIRoot if the old scene ref was cleared.
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
            if (canvas == null) return;

            canvas.Trigger(scareImage, scareSound, displaySeconds, fadeSeconds);
            if (oneShot) enabled = false;
        }
    }
}
