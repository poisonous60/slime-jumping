using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.Physics
{
    public enum IgnoreMode
    {
        // Probe filter only (physics stays on). Released on separation OR timer.
        ReattachGuard,

        // Probe filter + Physics2D.IgnoreCollision, slime transits through. Timer only;
        // owner (KnifeHazard) refreshes while overlapping and Unregisters explicitly.
        Passthrough,

        // Probe filter, timer-only release. For surfaces that MOVE and chase the slime
        // (BambooPlatformV3 whip-back): separation release re-sticks it mid-flight.
        TimedReattachGuard,
    }

    public class IgnoredColliderList
    {
        private readonly List<Collider2D> colliders = new();
        private readonly List<float> timers = new();
        private readonly List<IgnoreMode> modes = new();
        private readonly float maxIgnoreTime;
        // Every collider the slime body is made of. A cut slime is several circles, and ignoring
        // only one of them leaves the rest catching on the blade it is supposed to pass through.
        private readonly Collider2D[] slimeColliders;

        public IgnoredColliderList(float maxIgnoreTime, Collider2D[] slimeColliders)
        {
            this.maxIgnoreTime = maxIgnoreTime;
            this.slimeColliders = slimeColliders;
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

                // Only ReattachGuard gets the separation early-out: distance oscillates while
                // transiting a blade body, so a Passthrough clearance release would re-collide.
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

        // Launch/Hop clearance: guards overlapping colliders without suppressing physics.
        // Sweeps ReattachGuard entries only — wiping Passthrough re-enables collision mid-blade.
        public void RegisterOverlapping(Vector2 position, float radius, LayerMask mask, bool keepExisting = false)
        {
            // keepExisting: a cut body sweeps this once per lobe, and only the first pass may clear.
            if (!keepExisting)
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

        /// <summary>Re-arms every Passthrough pair. The body's collider set changes while a wound
        /// opens and closes, and IgnoreCollision is per pair — a lobe enabled afterwards would not
        /// carry the ignore and would catch on the blade the slime is transiting.</summary>
        public void ReapplySuppression()
        {
            for (int i = 0; i < colliders.Count; i++)
                SuppressCollision(colliders[i], modes[i]);
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
                // Upgrade drops the separation early-out: launch clearance registers this
                // collider first, and a chasing surface would re-stick on separation release.
                if (modes[existing] == IgnoreMode.ReattachGuard && mode == IgnoreMode.TimedReattachGuard)
                    modes[existing] = mode;
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

        private void SuppressCollision(Collider2D col, IgnoreMode mode) => SetIgnore(col, mode, true);
        private void RestoreCollision(Collider2D col, IgnoreMode mode) => SetIgnore(col, mode, false);

        private void SetIgnore(Collider2D col, IgnoreMode mode, bool ignore)
        {
            if (mode != IgnoreMode.Passthrough) return;
            // Unity fake-null: destroyed collider != null in CLR but == null via overloaded ==.
            if (col == null || slimeColliders == null) return;
            // Triggers don't physically block; IgnoreCollision on them would suppress
            // OnTriggerEnter/Stay events (breaks hazard re-hit loops). Probe filtering still
            // takes effect via Contains() check, which is what we want for triggers.
            if (col.isTrigger) return;
            for (int i = 0; i < slimeColliders.Length; i++)
            {
                if (slimeColliders[i] == null) continue;
                Physics2D.IgnoreCollision(slimeColliders[i], col, ignore);
            }
        }
    }
}
