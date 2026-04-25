using UnityEngine;

namespace SlimeJumping.TerrainShape.Editor
{
    [CreateAssetMenu(fileName = "HeightmapPreset", menuName = "SlimeJumping/Heightmap Preset", order = 200)]
    public class HeightmapPreset : ScriptableObject
    {
        public int width = 128;
        public int height = 128;
        public byte[] data;
        public float zMin = -0.5f;
        public float zMax = 0.5f;
    }
}
