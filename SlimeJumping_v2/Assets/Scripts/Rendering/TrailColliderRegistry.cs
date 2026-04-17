using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.Rendering
{
    /// Maps the platform's physics collider to its LocalTrailReceiver. Used by
    /// TrailStamper to route stamps to the correct per-platform RT. Needed because
    /// visual and collider live on separate GameObjects (project convention), so
    /// a simple TryGetComponent on the contact collider would miss the receiver.
    public static class TrailColliderRegistry
    {
        private static readonly Dictionary<Collider2D, LocalTrailReceiver> map
            = new Dictionary<Collider2D, LocalTrailReceiver>();

        public static void Register(Collider2D collider, LocalTrailReceiver receiver)
        {
            if (collider == null || receiver == null) return;
            map[collider] = receiver;
        }

        public static void Unregister(Collider2D collider)
        {
            if (collider == null) return;
            map.Remove(collider);
        }

        public static bool TryGet(Collider2D collider, out LocalTrailReceiver receiver)
        {
            if (collider == null) { receiver = null; return false; }
            return map.TryGetValue(collider, out receiver);
        }

        public static void Clear() => map.Clear();
    }
}
