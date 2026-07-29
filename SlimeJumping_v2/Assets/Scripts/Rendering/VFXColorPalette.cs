using UnityEngine;

namespace SlimeJumping.Rendering
{
    /// <summary>Single source of truth for VFX colors; ParticlePaletteBinder reads this asset at edit-time and runtime.</summary>
    [CreateAssetMenu(fileName = "VFXColorPalette", menuName = "SlimeJumping/VFX Color Palette")]
    public class VFXColorPalette : ScriptableObject
    {
        [System.Serializable]
        public struct VFXColorEntry
        {
            public VFXType type;
            public Color primary;
            public Color secondary;
        }

        [SerializeField] private VFXColorEntry[] entries = new VFXColorEntry[]
        {
            new VFXColorEntry { type = VFXType.Wind, primary = new Color(0.7f, 0.9f, 0.7f), secondary = new Color(0.5f, 0.8f, 0.5f) },
            new VFXColorEntry { type = VFXType.Fire, primary = new Color(1f, 0.6f, 0.1f), secondary = new Color(1f, 0.2f, 0f) },
            new VFXColorEntry { type = VFXType.Water, primary = new Color(0.3f, 0.6f, 1f), secondary = new Color(0.1f, 0.3f, 0.8f) },
            new VFXColorEntry { type = VFXType.Dust, primary = new Color(0.76f, 0.7f, 0.5f), secondary = new Color(0.6f, 0.55f, 0.4f) },
            new VFXColorEntry { type = VFXType.Lightning, primary = new Color(0.9f, 0.9f, 1f), secondary = new Color(0.5f, 0.5f, 1f) },
            new VFXColorEntry { type = VFXType.Pipe, primary = new Color(0.4f, 1f, 0.8f), secondary = new Color(0.2f, 0.7f, 0.5f) },
            // TODO: 디자이너가 슬라임 바디 색과 맞춰 튜닝 (placeholder: green)
            new VFXColorEntry { type = VFXType.Slice, primary = new Color(0.5f, 0.85f, 0.4f), secondary = new Color(0.35f, 0.6f, 0.25f) },
        };

        public Color GetPrimary(VFXType type)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].type == type) return entries[i].primary;
            }
            return Color.magenta;
        }

        public Color GetSecondary(VFXType type)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].type == type) return entries[i].secondary;
            }
            return Color.magenta;
        }

        public bool TryGetColors(VFXType type, out Color primary, out Color secondary)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].type == type)
                {
                    primary = entries[i].primary;
                    secondary = entries[i].secondary;
                    return true;
                }
            }
            primary = Color.magenta;
            secondary = Color.magenta;
            return false;
        }
    }
}
