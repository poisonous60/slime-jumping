using UnityEngine;

namespace SlimeJumping.Physics
{
    public struct SurfaceAnchorInfo
    {
        public Vector2 anchorWorldPos;
        public Vector2 normalWorld;
        public Vector2 tangentWorld;
        public bool consumesPlatformCarry;
        public string providerName;

        public SurfaceAnchorInfo(Vector2 anchorWorldPos, Vector2 normalWorld, Vector2 tangentWorld,
            bool consumesPlatformCarry, string providerName)
        {
            this.anchorWorldPos = anchorWorldPos;
            this.normalWorld = normalWorld;
            this.tangentWorld = tangentWorld;
            this.consumesPlatformCarry = consumesPlatformCarry;
            this.providerName = providerName;
        }
    }

    public interface ISurfaceAnchorProvider
    {
        bool TryGetSurfaceAnchor(Collider2D sourceCollider, Vector2 riderWorldPos, float riderRadius,
            out SurfaceAnchorInfo anchorInfo);
    }
}
