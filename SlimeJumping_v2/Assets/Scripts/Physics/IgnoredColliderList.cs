using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.Physics
{
    public class IgnoredColliderList
    {
        private readonly List<Collider2D> colliders = new();
        private readonly List<float> timers = new();
        private readonly float maxIgnoreTime;

        public IgnoredColliderList(float maxIgnoreTime)
        {
            this.maxIgnoreTime = maxIgnoreTime;
        }

        /// <summary>
        /// The underlying collider list. SurfaceProbe reads this for Contains checks.
        /// </summary>
        public List<Collider2D> Colliders => colliders;

        public void Tick(float dt, Vector2 rbPosition, float slimeRadius, float clearanceThreshold)
        {
            for (int i = colliders.Count - 1; i >= 0; i--)
            {
                timers[i] -= dt;
                var c = colliders[i];
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy)
                {
                    colliders.RemoveAt(i);
                    timers.RemoveAt(i);
                    continue;
                }
                float dist = Vector2.Distance(rbPosition, c.ClosestPoint(rbPosition));
                if (dist > slimeRadius + clearanceThreshold || timers[i] <= 0f)
                {
                    colliders.RemoveAt(i);
                    timers.RemoveAt(i);
                }
            }
        }

        public void RegisterOverlapping(Vector2 position, float radius, LayerMask mask)
        {
            colliders.Clear();
            timers.Clear();
            Collider2D[] overlaps = Physics2D.OverlapCircleAll(position, radius, mask);
            foreach (var c in overlaps)
            {
                colliders.Add(c);
                timers.Add(maxIgnoreTime);
            }
        }

        public bool Contains(Collider2D col)
        {
            return colliders.Contains(col);
        }

        public void Clear()
        {
            colliders.Clear();
            timers.Clear();
        }
    }
}
