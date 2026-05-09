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

        Renderer[] cachedRenderers;
        Collider2D[] cachedColliders;
        Bounds cachedActiveBounds;
        bool boundsValid;
        bool isSleeping;

        public bool ForceAlwaysActive => forceAlwaysActive;
        public float MarginMultiplierOverride => marginMultiplierOverride;
        public bool IsSleeping => isSleeping;

        void Awake()
        {
            // Union of ALL child renderers/colliders, not just the first. SnowField with N
            // strip children, or any prefab where the candidate component sits on a parent
            // and visuals are spread across children, would underestimate bounds with the
            // single-component lookup and get spuriously deactivated.
            cachedRenderers = GetComponentsInChildren<Renderer>(true);
            cachedColliders = GetComponentsInChildren<Collider2D>(true);
            RecacheBounds();
        }

        void OnEnable()
        {
            if (OffscreenSleeperManager.Instance != null)
                OffscreenSleeperManager.Instance.Register(this);
        }

        void OnDisable()
        {
            if (OffscreenSleeperManager.Instance != null)
                OffscreenSleeperManager.Instance.Unregister(this);
        }

        public Bounds GetActiveWorldBounds()
        {
            // Sleeping = bounds frozen at last active recache. Reading Renderer.bounds on a
            // disabled Renderer can return zero/stale data; transform.hasChanged also won't
            // tick while inactive. Frozen cache is correct as long as nobody moves the
            // transform externally during sleep (rare; if it happens the next active frame
            // will refresh).
            if (isSleeping) return cachedActiveBounds;

            if (!boundsValid || transform.hasChanged)
            {
                RecacheBounds();
                transform.hasChanged = false;
            }
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

            // Union of ALL renderers AND colliders — TautLine (thin LineRenderer + wide
            // PolygonCollider2D), SnowField (N strip children, each its own MeshRenderer),
            // multi-piece DeformablePlatform-style prefabs all need every child contributing.
            bool haveAny = false;
            Bounds combined = default;

            if (cachedRenderers != null)
            {
                for (int i = 0; i < cachedRenderers.Length; i++)
                {
                    var r = cachedRenderers[i];
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    if (!haveAny) { combined = r.bounds; haveAny = true; }
                    else combined.Encapsulate(r.bounds);
                }
            }
            if (cachedColliders != null)
            {
                for (int i = 0; i < cachedColliders.Length; i++)
                {
                    var c = cachedColliders[i];
                    if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                    if (!haveAny) { combined = c.bounds; haveAny = true; }
                    else combined.Encapsulate(c.bounds);
                }
            }

            if (haveAny) cachedActiveBounds = combined;
            else
            {
                // No active source. 1m cube around transform fallback — designer should set
                // useManualBounds + manualBoundsOverride if they hit this path.
                cachedActiveBounds = new Bounds(transform.position, Vector3.one);
            }
            boundsValid = true;
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
