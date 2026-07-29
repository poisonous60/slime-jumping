using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Interaction;

namespace SlimeJumping.Debugging
{
    /// <summary>Test helper: fires KnockdownController on slime entry, cooldown-gated so it can repeat without a scene reload.</summary>
    public class KnockdownTrigger : MonoBehaviour
    {
        [SerializeField] private KnockdownController knockdownController;

        private bool isOnCooldown;
        private float cooldownTimer;

        private void Update()
        {
            if (!isOnCooldown) return;

            cooldownTimer -= Time.deltaTime;
            if (cooldownTimer <= 0f)
                isOnCooldown = false;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (isOnCooldown) return;

            var slime = other.GetComponentInParent<SlimeController>();
            if (slime == null) return;
            if (slime.HasOverride) return;

            slime.SetOverride(knockdownController);
            isOnCooldown = true;
            cooldownTimer = knockdownController.Duration + 3f;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = isOnCooldown
                ? new Color(0.5f, 0.5f, 0.5f, 0.4f)
                : new Color(1f, 0.3f, 0.3f, 0.6f);
            var col = GetComponent<BoxCollider2D>();
            if (col != null)
                Gizmos.DrawWireCube(transform.position + (Vector3)col.offset, col.size);
            else
                Gizmos.DrawWireSphere(transform.position, 0.5f);
            Gizmos.DrawIcon(transform.position + Vector3.up * 0.8f, "d_EventTrigger Icon", true);
        }
    }
}
