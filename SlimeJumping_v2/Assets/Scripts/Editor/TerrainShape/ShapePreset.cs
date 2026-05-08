using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SlimeJumping.TerrainShape.Editor
{
    [CreateAssetMenu(fileName = "ShapePreset", menuName = "SlimeJumping/Shape Preset", order = 201)]
    public class ShapePreset : ScriptableObject
    {
        // Closed polygon outline in normalized centered space — centroid at (0,0). When stamped,
        // multiplied by stampScale and rotated by stampRotationDeg, then offset to click position.
        public List<Vector2> outline = new List<Vector2>();

        // 96×96 RGBA32 thumbnail for picker UI. Stored as a sub-asset of this preset.
        public Texture2D thumbnail;
    }

    // Generates a transparent-background outline thumbnail. Used at preset save time and never
    // during Inspector repaints — thumbnails live as sub-assets on the preset.
    internal static class ShapePresetThumbnail
    {
        public static Texture2D Generate(IList<Vector2> outline, int size = 96)
        {
            if (outline == null || outline.Count < 3) return null;

            Vector2 mn = outline[0], mx = outline[0];
            for (int i = 1; i < outline.Count; i++)
            {
                mn = Vector2.Min(mn, outline[i]);
                mx = Vector2.Max(mx, outline[i]);
            }
            Vector2 ext = mx - mn;
            float maxDim = Mathf.Max(ext.x, ext.y, 1e-4f);
            Vector2 center = (mn + mx) * 0.5f;
            const float pad = 0.15f;
            float fit = maxDim * (1f + pad);

            // Hidden/Internal-Colored is bundled with Unity but can be stripped in unusual setups.
            // Bail out cleanly rather than NRE-ing inside SetPass.
            var lineShader = Shader.Find("Hidden/Internal-Colored");
            if (lineShader == null) return null;

            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
            var prevRT = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));

            var mat = new Material(lineShader);
            mat.SetInt("_ZWrite", 0);
            mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetPass(0);

            GL.PushMatrix();
            GL.LoadOrtho();
            GL.Begin(GL.LINES);
            // White on dark editor theme, black on light. Internal-Colored doesn't antialias —
            // 1px stroke is usually readable enough for 96×96.
            Color stroke = EditorGUIUtility.isProSkin ? new Color(0.95f, 0.95f, 0.95f, 1f) : new Color(0.1f, 0.1f, 0.1f, 1f);
            GL.Color(stroke);

            int n = outline.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = (outline[i] - center) / fit + new Vector2(0.5f, 0.5f);
                Vector2 b = (outline[(i + 1) % n] - center) / fit + new Vector2(0.5f, 0.5f);
                GL.Vertex3(a.x, a.y, 0f);
                GL.Vertex3(b.x, b.y, 0f);
            }
            GL.End();
            GL.PopMatrix();

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            tex.hideFlags = HideFlags.HideInHierarchy;
            tex.name = "Thumbnail";

            RenderTexture.active = prevRT;
            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(mat);
            return tex;
        }
    }
}
