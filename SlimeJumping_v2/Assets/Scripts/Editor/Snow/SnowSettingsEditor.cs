using UnityEngine;
using UnityEditor;
using SlimeJumping.Platforms.Snow;

namespace SlimeJumping.Platforms.Snow.EditorScripts
{
    /// <summary>
    /// Custom inspector for SnowSettings that adds a one-click button to rebuild every
    /// SnowField in the open scene that references this settings asset. After tweaking
    /// rest thickness / slope / Phase 3 textures or keywords, designers click once here
    /// instead of selecting each SnowField and pressing Rebuild Now.
    /// </summary>
    [CustomEditor(typeof(SnowSettings))]
    public sealed class SnowSettingsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var settings = (SnowSettings)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Shadow Ramp", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate Shadow Ramp from Colors Above"))
                {
                    SnowRampGenerator.Generate(settings);
                }
                if (settings.shadowRamp != null && GUILayout.Button("Ping", GUILayout.Width(50)))
                {
                    EditorGUIUtility.PingObject(settings.shadowRamp);
                }
            }
            if (settings.shadowRamp != null)
            {
                var rect = GUILayoutUtility.GetRect(0, 18, GUILayout.ExpandWidth(true));
                EditorGUI.DrawPreviewTexture(rect, settings.shadowRamp);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "No shadowRamp assigned. Tweak the ramp colors / step under 'Shadow Ramp Generation' above, then click the Generate button to author a PNG and auto-assign it.",
                    MessageType.Info);
            }

            EditorGUILayout.Space();
            int referencing = CountReferencingFields(settings);

            using (new EditorGUI.DisabledScope(referencing == 0))
            {
                if (GUILayout.Button($"Rebuild All Referencing SnowFields In Scene ({referencing})"))
                {
                    int rebuilt = 0;
                    var fields = Object.FindObjectsByType<SnowField>(FindObjectsSortMode.None);
                    foreach (var sf in fields)
                    {
                        if (sf == null || sf.settings != settings) continue;
                        sf.RebuildNow();
                        EditorUtility.SetDirty(sf);
                        rebuilt++;
                    }
                    Debug.Log($"[Snow] Rebuilt {rebuilt} SnowField(s) referencing '{settings.name}'.");
                }
            }

            if (GUILayout.Button("Rebuild All SnowFields In Scene"))
            {
                SnowFieldEditor.RebuildAllInScene();
            }

            if (referencing == 0)
            {
                EditorGUILayout.HelpBox(
                    "No SnowField in the open scene references this settings asset.",
                    MessageType.Info);
            }
        }

        static int CountReferencingFields(SnowSettings settings)
        {
            int n = 0;
            var fields = Object.FindObjectsByType<SnowField>(FindObjectsSortMode.None);
            foreach (var sf in fields)
                if (sf != null && sf.settings == settings) n++;
            return n;
        }
    }
}
