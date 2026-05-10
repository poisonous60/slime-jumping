using UnityEngine;

namespace SlimeJumping.Optimization
{
    /// <summary>
    /// Marker component attached to platforms/mechanisms that can be safely deactivated when
    /// off-screen. <see cref="OffscreenSleeperManager"/> drives the toggle; this component
    /// owns the bounds caching and the GameObject SetActive call.
    ///
    /// Place on the same GameObject as the candidate component (TautLine, WaterZone_v2, etc.)
    /// or the prefab root that contains it. Bounds are computed from a Renderer ∪ Collider2D
    /// union — using both because some candidates (TautLine) have a thin LineRenderer but a
    /// wider PolygonCollider2D, so renderer-only bounds underestimate the interactable area.
    ///
    /// Bounds caching rule: while sleeping, the cached bounds are *frozen*. We never recache
    /// from a deactivated Renderer (its bounds may be zero/stale) and `transform.hasChanged`
    /// won't fire on inactive transforms anyway.
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

        // Bounds caching is deferred to the first GetActiveWorldBounds() call (not Awake).
        // Some candidate components (SnowField, TautLine, ClothField...) generate child
        // renderers/colliders in their OWN Awake — if our Awake reads bounds before theirs
        // runs, we'd cache an empty 1m cube at the parent transform and the manager would
        // promptly deactivate the GameObject. Lazy + per-call re-fetch sidesteps the
        // Awake order race entirely.

        void OnEnable()
        {
            // Idempotent — Register is no-op if already in list. Needed for first-time
            // registration AND for a wake from sleep (SetActive(true) re-fires OnEnable, but
            // we never Unregistered so the entry's still there → contains check skips the add).
            if (OffscreenSleeperManager.Instance != null)
                OffscreenSleeperManager.Instance.Register(this);
        }

        void OnDestroy()
        {
            // Critical: Unregister on Destroy, NOT on OnDisable. SetSleeping(true) calls
            // gameObject.SetActive(false) which fires OnDisable — if we Unregistered there,
            // the marker would vanish from the manager's list and the GameObject could
            // never be reactivated (manager has no reference left). Persisting through
            // sleep cycles is the whole point of this design.
            if (OffscreenSleeperManager.Instance != null)
                OffscreenSleeperManager.Instance.Unregister(this);
        }

        public Bounds GetActiveWorldBounds()
        {
            // Sleeping = bounds frozen at last active recache.
            if (isSleeping) return cachedActiveBounds;

            if (!boundsValid || transform.hasChanged)
            {
                RecacheBounds();
                transform.hasChanged = false;
            }

            // No renderer/collider visible yet — candidate's Awake/Start may create children
            // later. Return huge bounds so manager treats as always-near and keeps the
            // GameObject active until next-frame retry. Avoids spurious deactivation during
            // the Awake-order race.
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

            // Re-fetch each invalidation (not cached in Awake) — candidate components like
            // SnowField generate child renderers in their OWN Awake/Start. If we cached in
            // our Awake we'd miss those. Cost: O(child count) per first-FU per sleeper, fine.
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
                // Leave invalid → GetActiveWorldBounds returns huge bounds (always-near).
                // Next frame retry catches up if children appear later.
                boundsValid = false;
            }
        }

        public void SetSleeping(bool sleep)
        {
            if (isSleeping == sleep) return;

            // Lock down the last-active bounds before flipping inactive — this is what the
            // manager will read while we're asleep.
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
