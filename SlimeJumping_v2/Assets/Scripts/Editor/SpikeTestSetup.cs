using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    public static class SpikeTestSetup
    {
        [MenuItem("SlimeJumping/Mechanism/Setup Spike Test")]
        public static void SetupSpikeTest()
        {
            var root = new GameObject("SpikeTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Spike Test");

            float baseX = 4f;
            float baseY = 0f;

            // --- Floor ---
            CreatePlatform("Floor", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f),
                new Vector3(20f, 1f, 1f));

            // --- Left wall ---
            CreatePlatform("LeftWall", root.transform,
                new Vector3(baseX - 10.5f, baseY + 4f, 0f),
                new Vector3(1f, 9f, 1f));

            // --- Right wall ---
            CreatePlatform("RightWall", root.transform,
                new Vector3(baseX + 10.5f, baseY + 4f, 0f),
                new Vector3(1f, 9f, 1f));

            // --- Spike 1: Floor spike (useLocalUp = true, launches upward) ---
            var spike1 = CreateSpike("Spike_FloorUp", root.transform,
                new Vector3(baseX, baseY + 0.15f, 0f),
                new Vector2(1.5f, 0.3f),
                useLocalUp: true);

            // --- Spike 2: Wall spike (useLocalUp = true, launches away from wall) ---
            var spike2 = CreateSpike("Spike_WallRight", root.transform,
                new Vector3(baseX - 10f + 0.15f, baseY + 2f, 0f),
                new Vector2(0.3f, 1.5f),
                useLocalUp: true);
            spike2.transform.rotation = Quaternion.Euler(0f, 0f, -90f);

            // --- Spike 3: Auto-direction (useLocalUp = false, pushes away from center) ---
            var spike3 = CreateSpike("Spike_AutoDir", root.transform,
                new Vector3(baseX + 4f, baseY + 0.15f, 0f),
                new Vector2(1.5f, 0.3f),
                useLocalUp: false);

            // --- Spike 4: Redirect spike (sends slime toward redirect target) ---
            var redirectTarget = new GameObject("RedirectTarget");
            redirectTarget.transform.parent = root.transform;
            redirectTarget.transform.position = new Vector3(baseX - 5f, baseY + 6f, 0f);

            var spike4 = CreateSpike("Spike_Redirect", root.transform,
                new Vector3(baseX + 8f, baseY + 0.15f, 0f),
                new Vector2(1.5f, 0.3f),
                useLocalUp: false);
            var so4 = new SerializedObject(spike4.GetComponent<SpikeHazard>());
            so4.FindProperty("redirectTarget").objectReferenceValue = redirectTarget.transform;
            so4.ApplyModifiedProperties();

            // --- Elevated platform (to test charging -> spike) ---
            CreatePlatform("ElevatedPlatform", root.transform,
                new Vector3(baseX - 4f, baseY + 3f, 0f),
                new Vector3(4f, 0.5f, 1f));

            // --- Ceiling spike (test spike on ceiling) ---
            var spike5 = CreateSpike("Spike_Ceiling", root.transform,
                new Vector3(baseX - 4f, baseY + 5.85f, 0f),
                new Vector2(2f, 0.3f),
                useLocalUp: true);
            spike5.transform.rotation = Quaternion.Euler(0f, 0f, 180f);

            // --- Ceiling above ceiling spike ---
            CreatePlatform("CeilingAboveSpike", root.transform,
                new Vector3(baseX - 4f, baseY + 6.5f, 0f),
                new Vector3(6f, 1f, 1f));

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("Spike Test setup complete!\n" +
                "- Spike_FloorUp: upward knockback (useLocalUp)\n" +
                "- Spike_WallRight: rightward knockback from left wall\n" +
                "- Spike_AutoDir: auto-direction from spike center\n" +
                "- Spike_Redirect: sends slime toward RedirectTarget\n" +
                "- Spike_Ceiling: downward knockback from ceiling");
        }

        static GameObject CreateSpike(string name, Transform parent, Vector3 pos,
            Vector2 triggerSize, bool useLocalUp)
        {
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = pos;

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = triggerSize;

            var spike = go.AddComponent<SpikeHazard>();
            var so = new SerializedObject(spike);
            so.FindProperty("knockbackForce").floatValue = 12f;
            so.FindProperty("stunDuration").floatValue = 0.4f;
            so.FindProperty("useLocalUp").boolValue = useLocalUp;
            so.ApplyModifiedProperties();

            // Visual indicator (red tint)
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = triggerSize;
            sr.color = new Color(1f, 0.2f, 0.2f, 0.4f);
            sr.sortingOrder = -1;

            return go;
        }

        static GameObject CreatePlatform(string name, Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.parent = parent;
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.layer = 7;

            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            var col2d = go.AddComponent<BoxCollider2D>();
            col2d.size = Vector2.one;

            go.GetComponent<MeshRenderer>().sharedMaterial =
                MakeMaterial(new Color(0.5f, 0.5f, 0.5f));

            return go;
        }

        static Material MakeMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            return mat;
        }
    }
}
