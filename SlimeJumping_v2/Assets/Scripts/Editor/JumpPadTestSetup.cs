using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    public static class JumpPadTestSetup
    {
        [MenuItem("SlimeJumping/Platform/Setup Jump Pad Test")]
        public static void SetupJumpPadTest()
        {
            var root = new GameObject("JumpPadTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Jump Pad Test");

            float baseX = 4f;
            float baseY = 0f;

            CreatePlatform("Floor", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f),
                new Vector3(20f, 1f, 1f));

            var pad1 = CreateJumpPad("JumpPad_Up", root.transform,
                new Vector3(baseX - 3f, baseY + 0.05f, 0f),
                new Vector3(2f, 0.1f, 1f),
                Vector2.up, 18f);

            var pad2 = CreateJumpPad("JumpPad_45Right", root.transform,
                new Vector3(baseX, baseY + 0.05f, 0f),
                new Vector3(2f, 0.1f, 1f),
                new Vector2(1f, 1f), 20f);

            var pad3 = CreateJumpPad("JumpPad_HighUp", root.transform,
                new Vector3(baseX + 3f, baseY + 0.05f, 0f),
                new Vector3(2f, 0.1f, 1f),
                Vector2.up, 30f);

            CreatePlatform("Landing_Low", root.transform,
                new Vector3(baseX + 6f, baseY + 3f, 0f),
                new Vector3(3f, 0.5f, 1f));

            CreatePlatform("Landing_Mid", root.transform,
                new Vector3(baseX - 6f, baseY + 6f, 0f),
                new Vector3(3f, 0.5f, 1f));

            CreatePlatform("Landing_High", root.transform,
                new Vector3(baseX + 3f, baseY + 10f, 0f),
                new Vector3(3f, 0.5f, 1f));

            var wallPad = CreateJumpPad("JumpPad_WallRight", root.transform,
                new Vector3(baseX - 9.95f, baseY + 2f, 0f),
                new Vector3(0.1f, 2f, 1f),
                Vector2.right, 15f);

            CreatePlatform("LeftWall", root.transform,
                new Vector3(baseX - 10.5f, baseY + 6f, 0f),
                new Vector3(1f, 13f, 1f));

            CreatePlatform("RightWall", root.transform,
                new Vector3(baseX + 10.5f, baseY + 6f, 0f),
                new Vector3(1f, 13f, 1f));

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("Jump Pad Test setup complete!\n" +
                "- JumpPad_Up: straight up (force=18)\n" +
                "- JumpPad_45Right: 45-degree right (force=20)\n" +
                "- JumpPad_HighUp: high vertical (force=30)\n" +
                "- JumpPad_WallRight: wall-mounted, rightward (force=15)");
        }

        static GameObject CreateJumpPad(string name, Transform parent, Vector3 pos,
            Vector3 platformScale, Vector2 launchDir, float force)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.parent = parent;
            go.transform.position = pos;
            go.transform.localScale = platformScale;
            go.layer = 7;

            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            var col2d = go.AddComponent<BoxCollider2D>();
            col2d.size = Vector2.one;

            var pad = go.AddComponent<JumpPad>();
            var so = new SerializedObject(pad);
            so.FindProperty("launchForce").floatValue = force;
            so.FindProperty("launchDirection").vector2Value = launchDir;
            so.FindProperty("cooldown").floatValue = 0.3f;
            so.ApplyModifiedProperties();

            go.GetComponent<MeshRenderer>().sharedMaterial =
                MakeMaterial(new Color(0.2f, 0.9f, 0.3f));

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
