using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.Physics
{
    public class IgnoredColliderList
    {
        private readonly List<Collider2D> colliders = new();
        private readonly List<float> timers = new();
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
            for (int i = colliders.Count - 1; i >= 0; i--)
            {
                timers[i] -= dt;
                var c = colliders[i];
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy || timers[i] <= 0f)
                {
                    RestoreCollision(c);
                    colliders.RemoveAt(i);
                    timers.RemoveAt(i);
                }
            }
        }

        public void RegisterOverlapping(Vector2 position, float radius, LayerMask mask)
        {
            for (int i = 0; i < colliders.Count; i++)
                RestoreCollision(colliders[i]);
            colliders.Clear();
            timers.Clear();
            Collider2D[] overlaps = Physics2D.OverlapCircleAll(position, radius, mask);
            foreach (var c in overlaps)
            {
                colliders.Add(c);
                timers.Add(maxIgnoreTime);
                SuppressCollision(c);
            }
        }

        public bool Contains(Collider2D col)
        {
            return colliders.Contains(col);
        }

        public void RegisterSingle(Collider2D col, float duration)
        {
            if (col == null) return;
            int existing = colliders.IndexOf(col);
            if (existing >= 0)
            {
                if (timers[existing] < duration)
                    timers[existing] = duration;
                return;
            }
            colliders.Add(col);
            timers.Add(duration);
            SuppressCollision(col);
        }

        public void Unregister(Collider2D col)
        {
            int i = colliders.IndexOf(col);
            if (i < 0) return;
            RestoreCollision(col);
            colliders.RemoveAt(i);
            timers.RemoveAt(i);
        }

        public void Clear()
        {
            for (int i = 0; i < colliders.Count; i++)
                RestoreCollision(colliders[i]);
            colliders.Clear();
            timers.Clear();
        }

        private void SuppressCollision(Collider2D col)
        {
            if (col == null || slimeCollider == null) return;
            // Triggers don't physically block; IgnoreCollision on them would suppress
            // OnTriggerEnter/Stay events (breaks hazard re-hit loops). Probe filtering still
            // takes effect via Contains() check, which is what we want for triggers.
            if (col.isTrigger) return;
            Physics2D.IgnoreCollision(slimeCollider, col, true);
        }

        private void RestoreCollision(Collider2D col)
        {
            // Unity fake-null: destroyed collider != null in CLR but == null via overloaded ==.
            if (col == null || slimeCollider == null) return;
            if (col.isTrigger) return;
            Physics2D.IgnoreCollision(slimeCollider, col, false);
        }
    }
}
