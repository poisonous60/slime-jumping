using UnityEditor;
using UnityEngine;

namespace SlimeJumping.Platforms.Snow.EditorScripts
{
    /// <summary>Menu item that creates Snow_Default.asset seeded with the Ground_Quibli_Snow material.</summary>
    public static class SnowDefaultSettingsBootstrap
    {
        const string SettingsPath  = "Assets/Settings/Snow_Default.asset";
        const string MaterialPath  = "Assets/Art/Materials/Ground_Quibli_Snow.mat";
        const string SettingsDir   = "Assets/Settings";

        [MenuItem("Tools/SlimeJumping/Snow/Create Default Settings Asset")]
        public static void Create()
        {
            if (!AssetDatabase.IsValidFolder(SettingsDir))
                AssetDatabase.CreateFolder("Assets", "Settings");

            var existing = AssetDatabase.LoadAssetAtPath<SnowSettings>(SettingsPath);
            if (existing != null)
            {
                Debug.Log($"[Snow] Default settings already exist at {SettingsPath}");
                Selection.activeObject = existing;
                return;
            }

            var settings = ScriptableObject.CreateInstance<SnowSettings>();
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat != null) settings.snowMaterial = mat;

            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = settings;
            Debug.Log($"[Snow] Created {SettingsPath} (material={(mat != null ? mat.name : "NULL")})");
        }
    }
}
