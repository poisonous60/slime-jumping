using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.Rendering
{
    /// Maps a platform's physics collider to its LocalTrailReceiver so TrailStamper hits the right RT.
    /// Visual and collider are separate GameObjects, so TryGetComponent on the contact collider misses it.
    public static class TrailColliderRegistry
    {
        private static readonly Dictionary<Collider2D, LocalTrailReceiver> map
            = new Dictionary<Collider2D, LocalTrailReceiver>();

        // Domain Reload off ⇒ statics survive Play sessions and leak fake-null Collider2D keys.
        // SubsystemRegistration runs at the start of each Play entry, before any Awake.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => map.Clear();

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
