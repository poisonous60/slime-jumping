using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    public static class SpikeV2TestSetup
    {
        [MenuItem("SlimeJumping/Mechanism/Setup SpikeV2 Test")]
        public static void SetupSpikeV2Test()
        {
            var root = new GameObject("SpikeV2Test");
            Undo.RegisterCreatedObjectUndo(root, "Create SpikeV2 Test");

            float baseX = 4f;
            float baseY = 0f;

            CreatePlatform("Platform", root.transform,
                new Vector3(baseX, baseY, 0f),
                new Vector3(14f, 1.2f, 1f));

            var exitPointGO = new GameObject("SpikeV2_ExitPoint");
            exitPointGO.transform.parent = root.transform;
            exitPointGO.transform.position = new Vector3(baseX, baseY - 2f, 0f);

            var spike = CreateSpikeV2("SpikeV2_Warp", root.transform,
                new Vector3(baseX, baseY + 0.75f, 0f),
                new Vector2(1.5f, 0.6f),
                exitPointGO.transform);

            CreatePlatform("Floor", root.transform,
                new Vector3(baseX, baseY - 4f, 0f),
                new Vector3(20f, 1f, 1f));

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = spike;
            Debug.Log("SpikeV2 Test setup complete!\n" +
                "- Platform with a SpikeV2_Warp on top\n" +
                "- SpikeV2_ExitPoint positioned beneath the platform\n" +
                "- On contact, slime warps in a forward-Z arc and lands at rest at the exit point");
        }

        static GameObject CreateSpikeV2(string name, Transform parent, Vector3 pos,
            Vector2 triggerSize, Transform exitPoint)
        {
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = pos;

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = triggerSize;

            var spike = go.AddComponent<SpikeHazardV2>();
            var so = new SerializedObject(spike);
            so.FindProperty("exitPoint").objectReferenceValue = exitPoint;
            so.ApplyModifiedProperties();

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
