using System.IO;
using UnityEditor;
using UnityEngine;

namespace SlimeJumping.Platforms.Snow.EditorScripts
{
    /// <summary>Authors a Quibli toon ramp PNG from a SnowSettings asset and assigns it to shadowRamp.</summary>
    /// Quibli samples it at uv=(NdotL*0.5+0.5, 0.5) — 1D gradient; Height=4 only so the importer doesn't squash it to 1×1.
    public static class SnowRampGenerator
    {
        const int   Height            = 4;
        const string DefaultSettingsPath = "Assets/Settings/Snow_Default.asset";

        [MenuItem("Tools/SlimeJumping/Snow/Generate Default Snow Ramp")]
        public static void GenerateDefault()
        {
            var settings = AssetDatabase.LoadAssetAtPath<SnowSettings>(DefaultSettingsPath);
            if (settings == null)
            {
                Debug.LogWarning($"[Snow] {DefaultSettingsPath} not found. Run 'Create Default Settings Asset' first.");
                return;
            }
            Generate(settings);
        }

        /// <summary>Generate / overwrite the ramp PNG and assign it to settings.shadowRamp.</summary>
        public static void Generate(SnowSettings settings)
        {
            if (settings == null) return;

            string outDir  = ResolveRampFolder(settings);
            string outPath = $"{outDir}/{settings.name}_Ramp.png";
            EnsureFolder(outDir);

            int width = Mathf.Clamp(settings.rampWidth, 16, 4096);
            var pixels = new Color32[width * Height];
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                Color32 c = SampleGradient(u, settings);
                for (int y = 0; y < Height; y++)
                    pixels[y * width + x] = c;
            }

            var tex = new Texture2D(width, Height, TextureFormat.RGBA32, mipChain: false, linear: false);
            tex.SetPixels32(pixels);
            tex.Apply();

            var bytes = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            File.WriteAllBytes(outPath, bytes);

            AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceUpdate);
            ApplyImporterSettings(outPath);

            var rampAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
            if (rampAsset != null && settings.shadowRamp != rampAsset)
            {
                settings.shadowRamp = rampAsset;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssetIfDirty(settings);
            }

            Selection.activeObject = rampAsset;
            EditorGUIUtility.PingObject(rampAsset);
            Debug.Log($"[Snow] Wrote {outPath} ({width}×{Height}); assigned to '{settings.name}'.");
        }

        static Color32 SampleGradient(float u, SnowSettings s)
        {
            float lo = Mathf.Clamp01(s.rampStepCenter - s.rampStepWidth * 0.5f);
            float hi = Mathf.Clamp01(s.rampStepCenter + s.rampStepWidth * 0.5f);
            if (hi <= lo) hi = Mathf.Min(1f, lo + 1e-4f);

            Color c;
            if (u <= lo)
            {
                float t = lo > 0f ? Mathf.Clamp01(u / lo) : 0f;
                c = Color.Lerp(s.rampShadowDeep, s.rampShadowEdge, t);
            }
            else if (u >= hi)
            {
                float t = hi < 1f ? Mathf.Clamp01((u - hi) / (1f - hi)) : 0f;
                c = Color.Lerp(s.rampLitEdge, s.rampLitPeak, t);
            }
            else
            {
                float k = Mathf.SmoothStep(0f, 1f, (u - lo) / (hi - lo));
                c = Color.Lerp(s.rampShadowEdge, s.rampLitEdge, k);
            }
            c.a = 1f;
            return c;
        }

        static string ResolveRampFolder(SnowSettings settings)
        {
            string assetPath = AssetDatabase.GetAssetPath(settings);
            string parent = string.IsNullOrEmpty(assetPath)
                ? "Assets/Settings"
                : Path.GetDirectoryName(assetPath).Replace('\\', '/');
            return $"{parent}/SnowRamps";
        }

        static void ApplyImporterSettings(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            importer.textureType         = TextureImporterType.Default;
            importer.sRGBTexture         = true;
            importer.alphaSource         = TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled       = false;
            importer.wrapMode            = TextureWrapMode.Clamp;
            importer.filterMode          = FilterMode.Bilinear;
            importer.npotScale           = TextureImporterNPOTScale.None;
            importer.maxTextureSize      = 512;
            importer.SaveAndReimport();
        }

        static void EnsureFolder(string assetsPath)
        {
            if (AssetDatabase.IsValidFolder(assetsPath)) return;
            string parent = Path.GetDirectoryName(assetsPath).Replace('\\', '/');
            string leaf   = Path.GetFileName(assetsPath);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
