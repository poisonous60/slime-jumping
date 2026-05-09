using UnityEditor;
using UnityEngine;
using SlimeJumping.Platforms.ThinIceV2;

namespace SlimeJumping.Platforms.ThinIceV2.EditorOnly
{
    /// Custom inspector for ThinIcePlatformV2. Adds a tiny header showing what the
    /// shared <see cref="ThinIceV2Config"/> resolves to (Resources auto-load OR an
    /// inspector-assigned override) and a one-click button to generate the default
    /// config + VFX prefabs if they don't exist yet. The rest of the inspector is
    /// the default property drawer — only behavior tuning lives there now.
    [CustomEditor(typeof(ThinIcePlatformV2))]
    [CanEditMultipleObjects]
    public class ThinIcePlatformV2Editor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var plat = (ThinIcePlatformV2)target;
            DrawConfigSection(plat);
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }

        public static void DrawConfigSection(ThinIcePlatformV2 plat)
        {
            if (plat == null) return;

            // Editor-time resolve mirrors runtime: configOverride > Resources/ThinIceV2Config.
            var so = new SerializedObject(plat);
            var overrideProp = so.FindProperty("configOverride");
            var configOverride = overrideProp != null ? overrideProp.objectReferenceValue as ThinIceV2Config : null;
            var resolved = ThinIceV2Config.Resolve(configOverride);

            if (resolved != null)
            {
                string source = configOverride != null ? "override" : "Resources auto-load";
                EditorGUILayout.LabelField("Config", $"{resolved.name} ({source})");
                EditorGUILayout.LabelField($"Shards in set: {(resolved.shardSet != null ? resolved.shardSet.shards.Count : 0)}",
                                           EditorStyles.miniLabel);

                // Quick warnings if the config is half-wired — easier to spot here than
                // hunting down NullReferenceException at runtime.
                if (resolved.shardSet == null)
                    EditorGUILayout.HelpBox("Config has no shardSet — shatter will spawn nothing.", MessageType.Warning);
                if (resolved.shardRuntimePrefab == null)
                    EditorGUILayout.HelpBox("Config has no shardRuntimePrefab — shatter will spawn nothing.", MessageType.Warning);
                if (resolved.debrisVFXPrefab == null)
                    EditorGUILayout.HelpBox("Config has no debrisVFXPrefab — telegraph debris won't appear.", MessageType.Warning);
                if (resolved.frostCloudVFXPrefab == null)
                    EditorGUILayout.HelpBox("Config has no frostCloudVFXPrefab — shatter cloud won't appear.", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "No ThinIceV2Config resolved. Click below to generate the default " +
                    "config asset at Assets/Resources/ThinIceV2Config.asset along with " +
                    "the debris + frost-cloud VFX prefabs.", MessageType.Warning);
                if (GUILayout.Button("Generate Default Config and Prefabs"))
                {
                    ThinIceV2ConfigGenerator.GenerateMenu();
                }
            }
        }
    }
}
