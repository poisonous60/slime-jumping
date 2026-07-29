using UnityEngine;

namespace SlimeJumping.Optimization
{
    /// <summary>
    /// Marker for objects <see cref="OffscreenSleeperManager"/> may deactivate off-screen;
    /// owns bounds caching + the SetActive call. Bounds = Renderer ∪ Collider2D, because
    /// candidates like TautLine have a thin LineRenderer but a much wider PolygonCollider2D.
    /// While sleeping the cached bounds are frozen: a deactivated Renderer reports zero/stale
    /// bounds and transform.hasChanged never fires on an inactive transform.
    /// </summary>
    public class OffscreenSleepable : MonoBehaviour
    {
        [Tooltip("Skip the proximity check entirely — this object stays active forever. " +
                 "Use for boss arenas, cutscenes, or debug/escape-hatch overrides.")]
        [SerializeField] bool forceAlwaysActive = false;

        [Tooltip("0 = use OffscreenSleeperManager.defaultMarginMultiplier. Override to widen " +
                 "or narrow the active radius for this specific instance (e.g. a large ClothField " +
                 "wants more leeway than the default).")]
        [Min(0f)] [SerializeField] float marginMultiplierOverride = 0f;

        [Tooltip("On = ignore Renderer/Collider2D and use the manualBoundsOverride field " +
                 "below as the proximity test region. For cases where auto bounds is wrong " +
                 "(e.g. a wildly oversized LineRenderer bounds, or no renderer at all).")]
        [SerializeField] bool useManualBounds = false;

        [Tooltip("World-space AABB used when 'Use Manual Bounds' is on.")]
        [SerializeField] Bounds manualBoundsOverride;

        Bounds cachedActiveBounds;
        bool boundsValid;
        bool isSleeping;

        public bool ForceAlwaysActive => forceAlwaysActive;
        public float MarginMultiplierOverride => marginMultiplierOverride;
        public bool IsSleeping => isSleeping;

        // Bounds caching is lazy, never in Awake: candidates (SnowField, TautLine, ClothField)
        // build their child renderers in their OWN Awake, so caching early yields an empty
        // 1m cube and the manager immediately deactivates the object.

        void OnEnable()
        {
            // Also fires on wake from sleep; Register is a no-op when already listed.
            if (OffscreenSleeperManager.Instance != null)
                OffscreenSleeperManager.Instance.Register(this);
        }

        void OnDestroy()
        {
            // Unregister here, never in OnDisable: sleeping calls SetActive(false), and dropping
            // the entry there would leave the manager with no reference to ever wake it again.
            if (OffscreenSleeperManager.Instance != null)
                OffscreenSleeperManager.Instance.Unregister(this);
        }

        public Bounds GetActiveWorldBounds()
        {
            // Frozen at the last active recache.
            if (isSleeping) return cachedActiveBounds;

            if (!boundsValid || transform.hasChanged)
            {
                RecacheBounds();
                transform.hasChanged = false;
            }

            // Children may not exist yet: huge bounds read as always-near, so the object survives
            // the Awake-order race until a later frame recaches for real.
            if (!boundsValid)
                return new Bounds(transform.position, new Vector3(1e9f, 1e9f, 1e9f));

            return cachedActiveBounds;
        }

        void RecacheBounds()
        {
            if (useManualBounds)
            {
                cachedActiveBounds = manualBoundsOverride;
                boundsValid = true;
                return;
            }

            var renderers = GetComponentsInChildren<Renderer>(true);
            var colliders = GetComponentsInChildren<Collider2D>(true);

            bool haveAny = false;
            Bounds combined = default;

            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!haveAny) { combined = r.bounds; haveAny = true; }
                else combined.Encapsulate(r.bounds);
            }
            for (int i = 0; i < colliders.Length; i++)
            {
                var c = colliders[i];
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                if (!haveAny) { combined = c.bounds; haveAny = true; }
                else combined.Encapsulate(c.bounds);
            }

            if (haveAny)
            {
                cachedActiveBounds = combined;
                boundsValid = true;
            }
            else
            {
                // Leave invalid → GetActiveWorldBounds returns huge bounds (always-near) and retries.
                boundsValid = false;
            }
        }

        public void SetSleeping(bool sleep)
        {
            if (isSleeping == sleep) return;

            // Last chance to recache: after SetActive(false) the renderers report nothing.
            if (sleep) RecacheBounds();

            isSleeping = sleep;
            gameObject.SetActive(!sleep);
        }

        void OnDrawGizmosSelected()
        {
            Bounds b;
            if (useManualBounds) b = manualBoundsOverride;
            else if (boundsValid) b = cachedActiveBounds;
            else
            {
                bool haveAny = false;
                b = default;
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    if (!haveAny) { b = r.bounds; haveAny = true; }
                    else b.Encapsulate(r.bounds);
                }
                foreach (var c in GetComponentsInChildren<Collider2D>(true))
                {
                    if (c == null) continue;
                    if (!haveAny) { b = c.bounds; haveAny = true; }
                    else b.Encapsulate(c.bounds);
                }
                if (!haveAny) b = new Bounds(transform.position, Vector3.one);
            }

            Gizmos.color = isSleeping
                ? new Color(0.4f, 0.4f, 0.4f, 0.8f)
                : new Color(0.4f, 1f, 0.6f, 0.9f);
            Gizmos.DrawWireCube(b.center, b.size);
        }
    }
}
