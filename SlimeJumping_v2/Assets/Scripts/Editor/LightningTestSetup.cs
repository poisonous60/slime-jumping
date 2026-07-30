using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;
using SlimeJumping.Controller;

namespace SlimeJumping.Editor
{
    public static class LightningTestSetup
    {
        private const string ConfigDir = "Assets/Settings/Lightning";
        private const string ConfigPath = ConfigDir + "/LightningStrikeConfig_Default.asset";

        // Anchored to the slime so it works in any scene without hard-coded coordinates;
        // re-runnable (deletes the prior root first).
        [MenuItem("SlimeJumping/Mechanism/Setup Lightning Strike Test")]
        public static void Setup()
        {
            var slime = Object.FindFirstObjectByType<SlimeController>(FindObjectsInactive.Exclude);
            if (slime == null)
            {
                EditorUtility.DisplayDialog("Lightning Strike Test",
                    "No SlimeController found in the active scene. Open SampleScene or MainScene first, then re-run this menu.",
                    "OK");
                return;
            }

            var existing = GameObject.Find("LightningTest");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var config = LoadOrCreateConfig();

            var root = new GameObject("LightningTest");
            root.transform.position = slime.transform.position;
            Undo.RegisterCreatedObjectUndo(root, "Create Lightning Strike Test");

            // Four columns spanning the slime, each a beat behind the previous one.
            float[] localX = { -6f, -2f, 2f, 6f };
            float[] gaps = { 0f, 0.3f, 0.3f, 0.3f };

            var anchors = new Transform[localX.Length];
            for (int i = 0; i < localX.Length; i++)
            {
                var go = new GameObject($"Anchor_{i}");
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(localX[i], 10f, 0f);
                anchors[i] = go.transform;
            }

            var field = root.AddComponent<LightningStrikeField>();
            var so = new SerializedObject(field);
            so.FindProperty("config").objectReferenceValue = config;
            so.FindProperty("startDelay").floatValue = 0.5f;
            so.FindProperty("repeat").boolValue = true;
            so.FindProperty("cycleGap").floatValue = 1.1f;
            // Reuse the slime's own impulse source so the hit shake matches other hazards.
            so.FindProperty("hitShakeEmitter").objectReferenceValue =
                slime.GetComponentInChildren<SlimeJumping.Effects.ImpulseEmitter>(true);
            var list = so.FindProperty("strikes");
            list.arraySize = anchors.Length;
            for (int i = 0; i < anchors.Length; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("anchor").objectReferenceValue = anchors[i];
                e.FindPropertyRelative("gap").floatValue = gaps[i];
            }
            so.ApplyModifiedProperties();

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("[LightningTestSetup] Built.\n" +
                "- Four anchors 10 units above the slime, gaps 0 / 0.3 / 0.3 / 0.3 + cycleGap 1.1 = 2s cycle.\n" +
                "- Landing points come from a downward probe on config.groundMask (Environment) — move the root if the columns miss the floor.\n" +
                "- Standing under a column halts the bolt on the slime and flings it sideways.\n" +
                $"- Tuning lives in {ConfigPath} — impact spark / explosion / SFX / hit-stop are GraniteBlast's own assets and values.");
        }

        private static LightningStrikeConfig LoadOrCreateConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<LightningStrikeConfig>(ConfigPath);
            if (config != null) return config;

            if (!AssetDatabase.IsValidFolder(ConfigDir))
                AssetDatabase.CreateFolder("Assets/Settings", "Lightning");
            config = ScriptableObject.CreateInstance<LightningStrikeConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            AssetDatabase.SaveAssets();
            return config;
        }
    }
}
