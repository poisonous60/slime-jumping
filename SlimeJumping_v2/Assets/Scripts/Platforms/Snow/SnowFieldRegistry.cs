using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.Platforms.Snow
{
    /// <summary>O(1) collider → SnowField routing, so SnowStamper does one lookup on
    /// OnLanded instead of every SnowField subscribing and broadcasting.</summary>
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

        /// <summary>Remaining snow coverage in [0,1]; unregistered hosts and out-of-patch points
        /// return 0. The only channel gameplay may read snow through — snow never raises events.</summary>
        public static float SampleDepthRatio01(Collider2D hostCollider, Vector2 worldXY)
        {
            if (hostCollider == null) return 0f;
            return map.TryGetValue(hostCollider, out var field) && field != null
                ? field.SampleDepthRatio01(worldXY)
                : 0f;
        }
    }
}
