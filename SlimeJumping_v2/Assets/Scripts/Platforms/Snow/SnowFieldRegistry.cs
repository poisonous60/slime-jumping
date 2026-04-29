using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.Platforms.Snow
{
    /// <summary>
    /// O(1) routing from a host platform's collider to its SnowField. Mirrors the
    /// TrailColliderRegistry pattern — SnowStamper does one Dictionary lookup on
    /// OnLanded instead of every SnowField subscribing and broadcasting.
    /// </summary>
    public static class SnowFieldRegistry
    {
        static readonly Dictionary<Collider2D, SnowField> map = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => map.Clear();

        public static void Register(Collider2D hostCollider, SnowField field)
        {
            if (hostCollider == null || field == null) return;
            map[hostCollider] = field;
        }

        public static void Unregister(Collider2D hostCollider)
        {
            if (hostCollider == null) return;
            map.Remove(hostCollider);
        }

        public static bool TryGet(Collider2D hostCollider, out SnowField field)
        {
            if (hostCollider == null) { field = null; return false; }
            return map.TryGetValue(hostCollider, out field);
        }
    }
}
