using UnityEngine;
using UnityEditor;
using SlimeJumping.Platforms;
using SlimeJumping.Settings;

namespace SlimeJumping.Editor
{
    public static class BambooPlatformTestSetup
    {
        [MenuItem("SlimeJumping/Platform/Setup BambooPlatform Test")]
        public static void SetupBambooTest()
        {
            SetupBambooTestInternal("BambooPlatformTest", "Assets/Settings/BambooSettings.asset");
        }

        [MenuItem("SlimeJumping/Platform/Setup BambooPlatform V2 Test")]
        public static void SetupBambooV2Test()
        {
            SetupBambooTestInternal("BambooPlatformV2Test", "Assets/Settings/BambooV2Settings.asset");
        }

        static void SetupBambooTestInternal(string rootName, string settingsAssetPath)
        {
            // Remove any stale BambooPlatformTest left over from a previous menu run —
            // those still reference last-build's material/shader and won't pick up the
            // latest defaults until the GameObject is recreated.
            var existing = GameObject.Find(rootName);
            if (existing != null) Object.DestroyImmediate(existing);

            var root = new GameObject(rootName);
            Undo.RegisterCreatedObjectUndo(root, "Create BambooPlatform Test");
            root.transform.position = Vector3.zero;
            var bambooSettings = AssetDatabase.LoadAssetAtPath<BambooSettings>(settingsAssetPath);

            // Floor.
            CreateStaticPlatform("Floor", root.transform,
                new Vector3(0f, -6f, 0f),
                new Vector3(40f, 1f, 1f));

            // Side walls so the slime can ricochet back into the bamboo grove.
            CreateStaticPlatform("Wall_Left", root.transform,
                new Vector3(-18f, 0f, 0f),
                new Vector3(1f, 16f, 1f));
            CreateStaticPlatform("Wall_Right", root.transform,
                new Vector3(18f, 0f, 0f),
                new Vector3(1f, 16f, 1f));

            // A small ledge to launch from.
            CreateStaticPlatform("LaunchPad", root.transform,
                new Vector3(-12f, -4f, 0f),
                new Vector3(3f, 1f, 1f));

            // Bamboo grove: a few stalks of varying length / spring across the playfield,
            // base pivot sitting on the floor.
            float floorTop = -5.5f; // floor center -6, half-height 0.5
            CreateBamboo(root.transform, "Bamboo_A", new Vector3(-6f, floorTop, 0f), length: 5.0f, segments: 12, bambooSettings);
            CreateBamboo(root.transform, "Bamboo_B", new Vector3(-2f, floorTop, 0f), length: 6.5f, segments: 14, bambooSettings);
            CreateBamboo(root.transform, "Bamboo_C", new Vector3( 2f, floorTop, 0f), length: 4.0f, segments: 10, bambooSettings);
            CreateBamboo(root.transform, "Bamboo_D", new Vector3( 6f, floorTop, 0f), length: 7.0f, segments: 16, bambooSettings);
            CreateBamboo(root.transform, "Bamboo_E", new Vector3(10f, floorTop, 0f), length: 5.5f, segments: 12, bambooSettings);

            EditorSceneManager_MarkActiveDirty();

            Selection.activeGameObject = root;
            Debug.Log(
                "BambooPlatform Test setup complete.\n" +
                "- 5 bamboos with varied length / natural frequency / damping. Slime can attach to any segment of the collider polyline; visual is a single mesh bent by the SlimeJumping/BambooBend shader using the same closed-form arc.\n" +
                "- Visual mesh: procedurally generated tall cylinder per bamboo (no skinning, no MegaFiers, no per-segment seams). Shader applies a circular-arc bend in the vertex stage so the entire stalk reads as one continuous tube.\n" +
                "- Tuning: shape is hardcoded linear (h(t)=t) on both sides to guarantee collider/visual match. Spring/launch behavior comes from the assigned BambooSettings asset.\n" +
                "- Right-click the BambooPlatform header → 'Rebuild Segments' after changing length/segmentCount/colliderShape/visual* in the inspector.");
        }

        static void CreateBamboo(Transform parent, string name, Vector3 basePos,
                                 float length, int segments, BambooSettings bambooSettings)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = basePos;
            go.layer = 7;

            // The platform adds Rigidbody2D itself in RebuildSegments, but we set the
            // serialized fields first so RebuildSegments uses them.
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;

            var bamboo = go.AddComponent<BambooPlatform>();

            var so = new SerializedObject(bamboo);
            so.FindProperty("settings").objectReferenceValue = bambooSettings;
            so.FindProperty("bambooLength").floatValue = length;
            so.FindProperty("segmentCount").intValue = segments;
            // Force the stalk colors explicitly so leftover serialized data from any prior
            // BambooPlatform component layout cannot leak into the new material.
            so.FindProperty("stalkColorBase").colorValue = new Color(0.30f, 0.52f, 0.20f, 1f);
            so.FindProperty("stalkColorTip").colorValue = new Color(0.50f, 0.68f, 0.28f, 1f);
            so.FindProperty("shadowDarken").floatValue = 0.60f;
            so.FindProperty("scarColor").colorValue = new Color(0.22f, 0.26f, 0.14f, 1f);
            so.FindProperty("scarHalfWidth").floatValue = 0.004f;
            so.FindProperty("scarDepth").floatValue = 0.03f;
            so.ApplyModifiedProperties();

            bamboo.RebuildSegments();
        }

        static GameObject CreateStaticPlatform(string name, Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.layer = 7;

            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            var col2d = go.AddComponent<BoxCollider2D>();
            col2d.size = Vector2.one;

            go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.5f, 0.5f, 0.5f));
            return go;
        }

        static Material MakeMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            return mat;
        }

        static void EditorSceneManager_MarkActiveDirty()
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        }
    }
}
