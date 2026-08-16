using System;
using UnityEngine;

namespace SlimeJumping.UI
{
    /// Baked TMP glyph ink contours for the menu letter jelly. Coordinates are font
    /// units relative to the glyph origin/baseline (same frame as the arena ink box
    /// math). Paths wind with ink on the left: outer loops CCW, hole loops CW.
    /// Produced by Tools > SlimeJumping > UI > Bake Menu Glyph Contours.
    public sealed class MenuGlyphContours : ScriptableObject
    {
        [Serializable]
        public sealed class ContourPath
        {
            public Vector2[] points;
        }

        [Serializable]
        public sealed class GlyphEntry
        {
            public uint unicode;
            public ContourPath[] paths;
        }

        public string fontName;
        public float edgeThreshold = 0.5f;
        public GlyphEntry[] glyphs;

        public const string ResourcesPath = "MenuGlyphContours";

        public GlyphEntry Find(uint unicode)
        {
            if (glyphs == null) return null;
            foreach (var g in glyphs)
                if (g.unicode == unicode) return g;
            return null;
        }

        public static MenuGlyphContours Load()
            => Resources.Load<MenuGlyphContours>(ResourcesPath);
    }
}
