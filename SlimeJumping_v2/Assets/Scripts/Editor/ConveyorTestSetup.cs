using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    public static class ConveyorTestSetup
    {
        [MenuItem("SlimeJumping/Platform/Setup Conveyor Test")]
        public static void SetupConveyorTest()
        {
            var root = new GameObject("ConveyorTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Conveyor Test");

            float baseX = 4f;
            float baseY = 0f;

            CreatePlatform("Floor_Left", root.transform,
                new Vector3(baseX - 7f, baseY - 0.5f, 0f),
                new Vector3(6f, 1f, 1f));

            var conv1 = CreateConveyor("Conveyor_Right", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f),
                new Vector3(6f, 1f, 1f),
                Vector2.right, 8f, 12f);

            CreatePlatform("Floor_Right", root.transform,
                new Vector3(baseX + 7f, baseY - 0.5f, 0f),
                new Vector3(6f, 1f, 1f));

            CreateConveyor("Conveyor_Left", root.transform,
                new Vector3(baseX, baseY + 3.5f, 0f),
                new Vector3(6f, 1f, 1f),
                Vector2.left, 6f, 10f);

            CreatePlatform("Platform_AboveConveyor", root.transform,
                new Vector3(baseX - 5f, baseY + 3.5f, 0f),
                new Vector3(4f, 1f, 1f));

            CreatePlatform("Platform_AboveConveyor2", root.transform,
                new Vector3(baseX + 5f, baseY + 3.5f, 0f),
                new Vector3(4f, 1f, 1f));

            var wallConv = CreateConveyor("Conveyor_WallUp", root.transform,
                new Vector3(baseX + 10f, baseY + 3f, 0f),
                new Vector3(1f, 6f, 1f),
                Vector2.up, 10f, 15f);

            CreatePlatform("RightWall", root.transform,
                new Vector3(baseX + 11f, baseY + 3f, 0f),
                new Vector3(1f, 8f, 1f));

            CreatePlatform("Ceiling", root.transform,
                new Vector3(baseX + 7f, baseY + 7f, 0f),
                new Vector3(8f, 1f, 1f));

            CreatePlatform("LeftWall", root.transform,
                new Vector3(baseX - 10.5f, baseY + 3f, 0f),
                new Vector3(1f, 8f, 1f));

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("Conveyor Test setup complete!\n" +
                "- Conveyor_Right: floor rightward (speed=8)\n" +
                "- Conveyor_Left: elevated leftward (speed=6)\n" +
                "- Conveyor_WallUp: vertical wall upward (speed=10)\n" +
                "Try crawling against the conveyor direction!");
        }

        static GameObject CreateConveyor(string name, Transform parent, Vector3 pos,
            Vector3 scale, Vector2 dir, float speed, float terminal)
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

            var conv = go.AddComponent<ConveyorBelt>();
            var so = new SerializedObject(conv);
            so.FindProperty("speed").floatValue = speed;
            so.FindProperty("localDirection").vector2Value = dir;
            so.FindProperty("terminalSpeed").floatValue = terminal;
            so.ApplyModifiedProperties();

            go.GetComponent<MeshRenderer>().sharedMaterial =
                MakeMaterial(new Color(1f, 0.8f, 0.2f));

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
