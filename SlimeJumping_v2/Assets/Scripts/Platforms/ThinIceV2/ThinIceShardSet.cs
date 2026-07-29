using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.Platforms.ThinIceV2
{
    /// One pre-split shard; its mesh is a sub-asset of the ThinIceShardSet so regen replaces it without leaking.
    [Serializable]
    public struct ShardData
    {
        public Mesh mesh;
        public Vector2 localCentroid; // origin-relative; used as outward launch direction
        public Bounds  localBounds;
    }

    /// Edit-time output of ThinIceShardGenerator: one ShardData per Voronoi cell over the source TerrainShape2D.
    [CreateAssetMenu(fileName = "ThinIceShardSet", menuName = "SlimeJumping/Thin Ice v2/Shard Set", order = 0)]
    public class ThinIceShardSet : ScriptableObject
    {
        public List<ShardData> shards = new List<ShardData>();

        [Tooltip("GUID of the TerrainShape2D source shape this set was generated from.")]
        public string sourceShapeGuid;

        [Tooltip("RNG seed used for Poisson-disk site distribution. Same seed → same partition.")]
        public int siteSeed = 1;

        [Tooltip("Average distance between sites in local units. Smaller = more shards.")]
        public float density = 0.5f;
    }
}
