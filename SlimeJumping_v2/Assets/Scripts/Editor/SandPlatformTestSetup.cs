using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    public static class SandPlatformTestSetup
    {
        [MenuItem("SlimeJumping/Setup Sand Volume Test")]
        public static void Setup()
        {
            int dragVolumeLayer = LayerMask.NameToLayer("DragVolume");
            if (dragVolumeLayer < 0)
            {
                Debug.LogError("Layer 'DragVolume' not found! Add it in Project Settings > Tags and Layers first.");
                return;
            }

            var root = new GameObject("SandVolumeTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Sand Volume Test");

            float baseX = 4f;
            float baseY = 0f;

            // --- Floor ---
            CreatePlatform("Floor", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f),
                new Vector3(12f, 1f, 1f));

            // --- Left wall ---
            CreatePlatform("LeftWall", root.transform,
                new Vector3(baseX - 6.5f, baseY + 5f, 0f),
                new Vector3(1f, 11f, 1f));

            // --- Right wall ---
            CreatePlatform("RightWall", root.transform,
                new Vector3(baseX + 6.5f, baseY + 5f, 0f),
                new Vector3(1f, 11f, 1f));

            // --- Sand Volume (main test) ---
            CreateSandVolume("SandVolume_Main", root.transform,
                new Vector3(baseX, baseY + 4f, 0f),
                new Vector2(6f, 3f),
                dragVolumeLayer);

            // --- Small Sand Volume ---
            CreateSandVolume("SandVolume_Small", root.transform,
                new Vector3(baseX - 3f, baseY + 9f, 0f),
                new Vector2(2f, 2f),
                dragVolumeLayer);

            // --- High platform (jump down into sand) ---
            CreatePlatform("HighPlatform", root.transform,
                new Vector3(baseX + 4f, baseY + 7f, 0f),
                new Vector3(3f, 0.5f, 1f));

            // --- Catch floor ---
            CreatePlatform("CatchFloor", root.transform,
                new Vector3(baseX, baseY - 4f, 0f),
                new Vector3(12f, 1f, 1f));

            // --- Standalone DragZone (drag only, no charging) ---
            CreateSandVolume("DragZone_NoChrg", root.transform,
                new Vector3(baseX + 4f, baseY + 2f, 0f),
                new Vector2(3f, 4f),
                dragVolumeLayer, allowCharging: false);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("Sand Volume Test setup complete!\n" +
                "- Main sand: enter from any direction, drag + charge\n" +
                "- Small sand: test volume exit during charging\n" +
                "- Drag-only zone: drag without charging\n" +
                "- High platform: jump down into sand");
        }

        static void CreateSandVolume(string name, Transform parent, Vector3 pos,
            Vector2 size, int layer, bool allowCharging = true)
        {
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = pos;
            go.layer = layer;

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = size;

            var dz = go.AddComponent<DragZone>();
            var so = new SerializedObject(dz);
            so.FindProperty("entryMultiplier").floatValue = 0.5f;
            so.FindProperty("exitMultiplier").floatValue = 2f;
            so.FindProperty("launchMultiplier").floatValue = 0.5f;
            so.FindProperty("dragFactor").floatValue = 0.5f;
            so.FindProperty("maxFallSpeed").floatValue = 5f;
            so.FindProperty("allowCharging").boolValue = allowCharging;
            so.ApplyModifiedProperties();

            var vis = go.AddComponent<SpriteRenderer>();
            vis.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            vis.drawMode = SpriteDrawMode.Sliced;
            vis.size = size;
            vis.color = allowCharging
                ? new Color(0.9f, 0.7f, 0.3f, 0.12f)
                : new Color(0.5f, 0.7f, 1f, 0.08f);
            vis.sortingOrder = -1;
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
            go.AddComponent<BoxCollider2D>().size = Vector2.one;

            go.GetComponent<MeshRenderer>().sharedMaterial =
                new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.5f, 0.5f, 0.5f) };
            return go;
        }
    }
}
