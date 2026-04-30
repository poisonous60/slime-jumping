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

        /// <summary>
        /// Read-only sampler for gameplay code: given a host collider currently under
        /// the slime + a world position, returns remaining snow coverage in [0, 1]
        /// (0 = bare ground / not a snow host, 1 = full untouched snow). Null-safe;
        /// unregistered hosts and out-of-patch points both return 0. This is the only
        /// channel through which gameplay reads snow state — keeps the snow visual
        /// stack the producer and gameplay the consumer (no events from snow → game).
        /// </summary>
        public static float SampleDepthRatio01(Collider2D hostCollider, Vector2 worldXY)
        {
            if (hostCollider == null) return 0f;
            return map.TryGetValue(hostCollider, out var field) && field != null
                ? field.SampleDepthRatio01(worldXY)
                : 0f;
        }
    }
}
