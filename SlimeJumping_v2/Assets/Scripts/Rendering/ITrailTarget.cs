using System;
using UnityEngine;

namespace SlimeJumping.Rendering
{
    /// Persistence-side view of a paintable RT (the world RT or a platform-local RT).
    public interface ITrailTarget
    {
        RenderTexture Rt { get; }
        string SaveKey { get; }
        Vector2Int Resolution { get; }
        bool Dirty { get; }
        void MarkDirty();
        void ClearDirty();
        TrailTargetMeta BuildMeta();
        /// Invoked after TrailPersistence has blit a loaded image into Rt.
        void OnLoadedInto();
    }

    [Serializable]
    public struct TrailTargetMeta
    {
        public int version;
        public int resolutionX;
        public int resolutionY;
        public float originX;
        public float originY;
        public float sizeX;
        public float sizeY;
        // "R8" for now; future formats change the loader path.
        public string format;
    }
}
