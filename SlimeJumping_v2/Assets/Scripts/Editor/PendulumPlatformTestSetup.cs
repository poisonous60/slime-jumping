using UnityEngine;
using UnityEditor;
using SlimeJumping.Platforms;

namespace SlimeJumping.Editor
{
    public static class PendulumPlatformTestSetup
    {
        [MenuItem("SlimeJumping/Platform/Setup PendulumPlatform Test")]
        public static void SetupPendulumTest()
        {
            var root = new GameObject("PendulumPlatformTest");
            Undo.RegisterCreatedObjectUndo(root, "Create PendulumPlatform Test");

            float baseX = 4f;
            float baseY = 0f;

            CreatePlatform("Floor", root.transform,
                new Vector3(baseX, baseY - 5f, 0f),
                new Vector3(30f, 1f, 1f));

            CreatePlatform("LaunchPad_Left", root.transform,
                new Vector3(baseX - 12f, baseY + 2f, 0f),
                new Vector3(3f, 1f, 1f));

            CreatePlatform("LaunchPad_Right", root.transform,
                new Vector3(baseX + 12f, baseY + 2f, 0f),
                new Vector3(3f, 1f, 1f));

            CreatePendulum("Pendulum_Default", root.transform,
                new Vector3(baseX - 6f, baseY + 2f, 0f),
                localPivot: new Vector2(0f, 3f),
                ropeLength: 3f,
                startAngleDeg: 30f,
                landingImpulseScale: 1f,
                riderPosAngAccelScale: 0.1f,
                damping: 0.02f);

            CreatePendulum("Pendulum_HighImpulse", root.transform,
                new Vector3(baseX, baseY + 2f, 0f),
                localPivot: new Vector2(0f, 3f),
                ropeLength: 3f,
                startAngleDeg: 0f,
                landingImpulseScale: 2.5f,
                riderPosAngAccelScale: 0.05f,
                damping: 0.02f);

            CreatePendulum("Pendulum_HighTorque", root.transform,
                new Vector3(baseX + 6f, baseY + 2f, 0f),
                localPivot: new Vector2(0f, 3f),
                ropeLength: 3f,
                startAngleDeg: 0f,
                landingImpulseScale: 0.3f,
                riderPosAngAccelScale: 0.4f,
                damping: 0.05f);

            CreatePendulum("Pendulum_Long", root.transform,
                new Vector3(baseX - 3f, baseY + 6f, 0f),
                localPivot: new Vector2(0f, 5f),
                ropeLength: 5f,
                startAngleDeg: 0f,
                landingImpulseScale: 1.5f,
                riderPosAngAccelScale: 0.15f,
                damping: 0.02f);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("PendulumPlatform Test setup complete!\n" +
                "- Pendulum_Default: balanced (impulse=1.0, torque=0.1)\n" +
                "- Pendulum_HighImpulse: landing inertia emphasized (impulse=2.5)\n" +
                "- Pendulum_HighTorque: standing-weight emphasized (torque=0.4)\n" +
                "- Pendulum_Long: long rope, slow period (L=5)\n" +
                "Slingshot from LaunchPad_Left/Right onto pendulums to see impulse. Stand still to see position torque.");
        }

        static GameObject CreatePendulum(string name, Transform parent, Vector3 pos,
            Vector2 localPivot, float ropeLength, float startAngleDeg,
            float landingImpulseScale, float riderPosAngAccelScale, float damping)
        {
            var root = new GameObject(name);
            root.transform.parent = parent;
            root.transform.position = pos;

            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "Board";
            board.transform.SetParent(root.transform, false);
            board.transform.localScale = new Vector3(3f, 0.3f, 1f);
            board.layer = 7;

            Object.DestroyImmediate(board.GetComponent<BoxCollider>());
            var col2d = board.AddComponent<BoxCollider2D>();
            col2d.size = Vector2.one;

            var pend = board.AddComponent<PendulumPlatform>();

            var ropeGo = new GameObject("Rope");
            ropeGo.transform.SetParent(root.transform, false);
            var lr = ropeGo.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.startWidth = lr.endWidth = 0.08f;
            lr.textureMode = LineTextureMode.Tile;
            lr.alignment = LineAlignment.View;
            lr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Prebs/Platform/Materials/RopeMaterial.mat");
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            var so = new SerializedObject(pend);
            so.FindProperty("localPivot").vector2Value = localPivot;
            so.FindProperty("ropeLength").floatValue = ropeLength;
            so.FindProperty("startAngleDeg").floatValue = startAngleDeg;
            so.FindProperty("landingImpulseScale").floatValue = landingImpulseScale;
            so.FindProperty("riderPosAngAccelScale").floatValue = riderPosAngAccelScale;
            so.FindProperty("damping").floatValue = damping;
            var ropesProp = so.FindProperty("ropes");
            ropesProp.arraySize = 1;
            ropesProp.GetArrayElementAtIndex(0).objectReferenceValue = lr;
            so.ApplyModifiedProperties();

            board.GetComponent<MeshRenderer>().sharedMaterial =
                MakeMaterial(new Color(0.3f, 0.6f, 0.9f));

            return root;
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
