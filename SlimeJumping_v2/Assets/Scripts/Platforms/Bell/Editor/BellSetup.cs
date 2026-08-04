using UnityEngine;
using UnityEditor;
using SlimeJumping.Platforms.Bell;

namespace SlimeJumping.Editor
{
    // Wires a KayKit bell already placed in the scene into a SwingingBell (menu-run, one-shot).
    public static class BellSetup
    {
        const string ConfigAssetPath = "Assets/Resources/BellConfig.asset";

        [MenuItem("SlimeJumping/Bell/Set Up Selected Bell")]
        public static void SetUpSelected()
        {
            var root = Selection.activeGameObject;
            if (root == null)
            {
                EditorUtility.DisplayDialog("Bell Setup",
                    "Select the bell's collider root (the object holding the PolygonCollider2D).", "OK");
                return;
            }

            var visual = FindChild(root.transform, "bell");
            var clapper = visual != null ? FindChild(visual, "bell_clapper") : null;
            if (visual == null || clapper == null)
            {
                EditorUtility.DisplayDialog("Bell Setup",
                    $"'{root.name}' needs a child named 'bell' with a child named 'bell_clapper'.", "OK");
                return;
            }

            var config = EnsureConfig();

            Undo.RegisterFullObjectHierarchyUndo(root, "Bell Setup");

            var body = root.GetComponent<Rigidbody2D>();
            if (body == null) body = Undo.AddComponent<Rigidbody2D>(root);
            body.bodyType = RigidbodyType2D.Kinematic;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.useFullKinematicContacts = true;

            var bell = root.GetComponent<SwingingBell>();
            if (bell == null) bell = Undo.AddComponent<SwingingBell>(root);

            var so = new SerializedObject(bell);
            so.FindProperty("bellVisual").objectReferenceValue = visual;
            so.FindProperty("clapperVisual").objectReferenceValue = clapper;
            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(root);
            Debug.Log($"[BellSetup] '{root.name}' is a SwingingBell. Tuning lives in {ConfigAssetPath} " +
                      $"(assign strikeClips there to make it audible).", config);
        }

        // Strike-sound sets. Each folder holds its clips already named in soft -> loud order.
        const string SoundRoot = "Assets/Sound/Bell";

        [MenuItem("SlimeJumping/Bell/Strike Sound/Gong 1 — big bronze, longest decay")]
        static void UseGong1() => UseSet("Gong1");

        [MenuItem("SlimeJumping/Bell/Strike Sound/Gong 2 — lighter, 5 layers")]
        static void UseGong2() => UseSet("Gong2");

        [MenuItem("SlimeJumping/Bell/Strike Sound/Hand Bells — short, clear")]
        static void UseHandBells() => UseSet("HandBells");

        [MenuItem("SlimeJumping/Bell/Strike Sound/Agogo — shortest decay")]
        static void UseAgogo() => UseSet("Agogo");

        [MenuItem("SlimeJumping/Bell/Strike Sound/Tubular Bells — church chime")]
        static void UseTubularBells() => UseSet("TubularBells");

        static void UseSet(string folder)
        {
            string dir = $"{SoundRoot}/{folder}";
            var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { dir });
            if (guids.Length == 0)
            {
                EditorUtility.DisplayDialog("Bell Strike Sound", $"No clips under {dir}.", "OK");
                return;
            }

            var paths = new System.Collections.Generic.List<string>();
            foreach (var g in guids) paths.Add(AssetDatabase.GUIDToAssetPath(g));
            paths.Sort(System.StringComparer.Ordinal);   // the filenames carry the soft -> loud order

            var clips = new AudioClip[paths.Count];
            for (int i = 0; i < paths.Count; i++) clips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(paths[i]);

            var config = EnsureConfig();
            Undo.RecordObject(config, "Bell Strike Sound");
            config.strikeClips = clips;
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            Debug.Log($"[BellSetup] strike sound = {folder} ({clips.Length} layers).", config);
        }

        static Transform FindChild(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i).name == name) return parent.GetChild(i);
            return null;
        }

        static BellConfig EnsureConfig()
        {
            var existing = AssetDatabase.LoadAssetAtPath<BellConfig>(ConfigAssetPath);
            if (existing != null) return existing;

            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");

            var config = ScriptableObject.CreateInstance<BellConfig>();
            AssetDatabase.CreateAsset(config, ConfigAssetPath);
            AssetDatabase.SaveAssets();
            return config;
        }
    }
}
