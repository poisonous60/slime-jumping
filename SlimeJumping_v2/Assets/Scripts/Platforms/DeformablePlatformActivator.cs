using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Platforms
{
    /// <summary>
    /// Scene-wide manager that keeps DeformablePlatform dents alive only while the slime
    /// is within its interaction radius. Uses a trigger CircleCollider2D on a kinematic
    /// Rigidbody2D that follows the slime — Unity's physics broadphase fires enter/exit
    /// per platform, so no per-frame polling or per-platform distance checks happen.
    ///
    /// Exit → platform starts its rest-restore lerp (same path the fall-below check uses).
    /// Enter → no-op. An in-progress reset is not cancelled (it'll finish on its own, and
    /// any fresh impact will override it anyway via ApplyImpactAt).
    ///
    /// Drop one of these into any scene with DeformablePlatforms; platforms find it
    /// automatically via the physics system — no references to wire up.
    /// </summary>
    [RequireComponent(typeof(CircleCollider2D))]
    [RequireComponent(typeof(Rigidbody2D))]
    public class DeformablePlatformActivator : MonoBehaviour
    {
        [Tooltip("Transform whose world position the range circle follows. If null, finds " +
                 "the first SlimeController in the scene at runtime.")]
        [SerializeField] Transform follow;

        [Tooltip("Keep-alive radius (world units). Platforms whose collider overlaps this " +
                 "circle preserve their dents; platforms outside reset to rest.")]
        [Min(0.1f)] [SerializeField] float radius = 15f;

        Rigidbody2D rb;
        CircleCollider2D col;
        SlimeController cachedSlime;

        void Reset()
        {
            // Applied when the component is first added — matches the Awake-time runtime setup
            // so the editor inspector reflects the intended state immediately.
            var r = GetComponent<Rigidbody2D>();
            var c = GetComponent<CircleCollider2D>();
            if (r != null)
            {
                r.bodyType = RigidbodyType2D.Kinematic;
                r.useFullKinematicContacts = true;
                r.gravityScale = 0f;
            }
            if (c != null)
            {
                c.isTrigger = true;
                c.radius = radius;
            }
        }

        void Awake()
        {
            rb  = GetComponent<Rigidbody2D>();
            col = GetComponent<CircleCollider2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            // Ensures trigger events fire even against kinematic-bodied platforms (safety net).
            rb.useFullKinematicContacts = true;
            rb.gravityScale = 0f;
            col.isTrigger = true;
            col.radius = radius;
        }

        void OnValidate()
        {
            if (col == null) col = GetComponent<CircleCollider2D>();
            if (col != null) col.radius = radius;
        }

        void FixedUpdate()
        {
            if (follow == null)
            {
                if (cachedSlime == null) cachedSlime = FindAnyObjectByType<SlimeController>();
                if (cachedSlime != null) follow = cachedSlime.transform;
                if (follow == null) return;
            }
            // rb.position (not transform.position) so trigger events fire correctly in
            // the same FixedUpdate step — PhysX computes overlaps against the Rigidbody pose.
            rb.position = follow.position;
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            var dp = other.GetComponent<DeformablePlatform>();
            if (dp != null) dp.SetActiveByManager(true);
        }

        void OnTriggerExit2D(Collider2D other)
        {
            var dp = other.GetComponent<DeformablePlatform>();
            if (dp != null) dp.SetActiveByManager(false);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
