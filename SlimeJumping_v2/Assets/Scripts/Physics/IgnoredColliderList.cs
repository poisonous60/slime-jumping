using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.Physics
{
    public enum IgnoreMode
    {
        // Probe filter only. SurfaceProbe.Contains() skips the collider so reattach
        // is suppressed, but Physics2D is untouched — slime still bounces off.
        // Released when slime separates by (slimeRadius + clearanceThreshold) OR timer.
        // Used for Launch/Hop/ForceAirborne clearance and stun-knockback guards.
        ReattachGuard,

        // Probe filter + Physics2D.IgnoreCollision — slime physically transits through.
        // Timer only; caller (e.g. KnifeHazard) refreshes the duration while overlap
        // persists and Unregisters explicitly.
        Passthrough,
    }

    public class IgnoredColliderList
    {
        private readonly List<Collider2D> colliders = new();
        private readonly List<float> timers = new();
        private readonly List<IgnoreMode> modes = new();
        private readonly float maxIgnoreTime;
        private readonly Collider2D slimeCollider;

        public IgnoredColliderList(float maxIgnoreTime, Collider2D slimeCollider)
        {
            this.maxIgnoreTime = maxIgnoreTime;
            this.slimeCollider = slimeCollider;
        }

        // Exposed so SurfaceProbe can share the backing store without a copy per frame.
        public List<Collider2D> Colliders => colliders;

        public void Tick(float dt, Vector2 rbPosition, float slimeRadius, float clearanceThreshold)
        {
            float separationDist = slimeRadius + clearanceThreshold;
            for (int i = colliders.Count - 1; i >= 0; i--)
            {
                timers[i] -= dt;
                var c = colliders[i];
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy || timers[i] <= 0f)
                {
                    RestoreCollision(c, modes[i]);
                    RemoveAt(i);
                    continue;
                }

                // ReattachGuard: also release once slime has physically cleared the collider.
                // Passthrough: timer only — distance oscillates while transiting a blade body,
                // a clearance release mid-passthrough would re-collide.
                if (modes[i] == IgnoreMode.ReattachGuard)
                {
                    float dist = Vector2.Distance(rbPosition, c.ClosestPoint(rbPosition));
                    if (dist > separationDist)
                    {
                        RestoreCollision(c, modes[i]);
                        RemoveAt(i);
                    }
                }
            }
        }

        // Registers every collider on `mask` overlapping the slime as ReattachGuard entries.
        // Used by Launch/Hop/ForceAirborneWithVelocity — prevents instant re-stick to the
        // surface the slime is leaving, without suppressing physical collision.
        //
        // Only REATTACH-GUARD entries are swept; Passthrough entries survive. Wiping them
        // here used to re-enable Physics2D collision mid-overlap (launching while transiting
        // a knife blade, or right after a cut while the severed chunk was still registered),
        // because Passthrough owners register once / on a slow refresh — not every frame.
        public void RegisterOverlapping(Vector2 position, float radius, LayerMask mask)
        {
            for (int i = colliders.Count - 1; i >= 0; i--)
            {
                if (modes[i] != IgnoreMode.ReattachGuard) continue;
                RestoreCollision(colliders[i], modes[i]);
                RemoveAt(i);
            }
            Collider2D[] overlaps = Physics2D.OverlapCircleAll(position, radius, mask);
            foreach (var c in overlaps)
            {
                if (colliders.Contains(c)) continue;   // already guarded (e.g. surviving Passthrough)
                colliders.Add(c);
                timers.Add(maxIgnoreTime);
                modes.Add(IgnoreMode.ReattachGuard);
                // ReattachGuard: no SuppressCollision — physics stays on.
            }
        }

        public bool Contains(Collider2D col)
        {
            return colliders.Contains(col);
        }

        // Explicit mode — no default. Picking the wrong mode silently changes whether
        // the slime physically passes through the collider; callers must opt in.
        public void RegisterSingle(Collider2D col, float duration, IgnoreMode mode)
        {
            if (col == null) return;
            int existing = colliders.IndexOf(col);
            if (existing >= 0)
            {
                if (timers[existing] < duration)
                    timers[existing] = duration;
                // If mode changes from ReattachGuard -> Passthrough, apply suppression now.
                // Reverse transition is not expected; ignore to keep semantics simple.
                if (modes[existing] != mode && mode == IgnoreMode.Passthrough)
                {
                    SuppressCollision(col, mode);
                    modes[existing] = mode;
                }
                return;
            }
            colliders.Add(col);
            timers.Add(duration);
            modes.Add(mode);
            SuppressCollision(col, mode);
        }

        public void Unregister(Collider2D col)
        {
            int i = colliders.IndexOf(col);
            if (i < 0) return;
            RestoreCollision(col, modes[i]);
            RemoveAt(i);
        }

        public void Clear()
        {
            for (int i = 0; i < colliders.Count; i++)
                RestoreCollision(colliders[i], modes[i]);
            colliders.Clear();
            timers.Clear();
            modes.Clear();
        }

        private void RemoveAt(int i)
        {
            colliders.RemoveAt(i);
            timers.RemoveAt(i);
            modes.RemoveAt(i);
        }

        private void SuppressCollision(Collider2D col, IgnoreMode mode)
        {
            if (mode != IgnoreMode.Passthrough) return;
            if (col == null || slimeCollider == null) return;
            // Triggers don't physically block; IgnoreCollision on them would suppress
            // OnTriggerEnter/Stay events (breaks hazard re-hit loops). Probe filtering still
            // takes effect via Contains() check, which is what we want for triggers.
            if (col.isTrigger) return;
            Physics2D.IgnoreCollision(slimeCollider, col, true);
        }

        private void RestoreCollision(Collider2D col, IgnoreMode mode)
        {
            if (mode != IgnoreMode.Passthrough) return;
            // Unity fake-null: destroyed collider != null in CLR but == null via overloaded ==.
            if (col == null || slimeCollider == null) return;
            if (col.isTrigger) return;
            Physics2D.IgnoreCollision(slimeCollider, col, false);
        }
    }
}
