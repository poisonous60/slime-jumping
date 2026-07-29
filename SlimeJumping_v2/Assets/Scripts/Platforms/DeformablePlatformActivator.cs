using UnityEngine;
using SlimeJumping.Controller;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SlimeJumping.Platforms
{
    /// <summary>Trigger circle following the slime keeps DeformablePlatform dents alive; exit starts
    /// the platform's rest-restore. Broadphase enter/exit replaces per-platform distance polling.</summary>
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

        [Header("Impact VFX (broadcast to all DeformablePlatforms)")]
        [Tooltip("One-shot VFX prefabs spawned on slime impact. A random entry is picked per hit " +
                 "for variety. These overwrite per-platform 'Sand VFX Prefabs' assignments — set " +
                 "once on this manager instead of on each platform.")]
        [SerializeField] GameObject[] impactVFXPrefabs;
        [Tooltip("Include inactive DeformablePlatform objects when broadcasting.")]
        [SerializeField] bool includeInactive = true;
        [Tooltip("Push the VFX prefab list to every platform on Awake. If false, call ApplyToAll() manually.")]
        [SerializeField] bool applyOnAwake = true;

        Rigidbody2D rb;
        CircleCollider2D col;
        SlimeController cachedSlime;

        void Reset()
        {
            // Mirrors the Awake setup so the inspector shows the runtime state on add.
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
            // Else trigger events never fire against kinematic-bodied platforms.
            rb.useFullKinematicContacts = true;
            rb.gravityScale = 0f;
            col.isTrigger = true;
            col.radius = radius;

            if (applyOnAwake) ApplyToAll();
        }

        [ContextMenu("Apply To All Platforms")]
        public void ApplyToAll()
        {
            var platforms = FindObjectsByType<DeformablePlatform>(
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (int i = 0; i < platforms.Length; i++)
            {
                platforms[i].ApplyImpactVFX(impactVFXPrefabs);
#if UNITY_EDITOR
                if (!Application.isPlaying) EditorUtility.SetDirty(platforms[i]);
#endif
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[DeformablePlatformActivator] Applied impact VFX to {platforms.Length} DeformablePlatform instance(s).", this);
#endif
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
            // rb.position, not transform.position: overlaps are computed against the body pose,
            // so a transform write misses trigger events in the same FixedUpdate step.
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
