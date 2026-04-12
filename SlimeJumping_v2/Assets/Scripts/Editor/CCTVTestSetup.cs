using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    public static class CCTVTestSetup
    {
        [MenuItem("SlimeJumping/Setup CCTV Test")]
        public static void Setup()
        {
            var existing = GameObject.Find("CCTVTest");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject("CCTVTest");
            Undo.RegisterCreatedObjectUndo(root, "Create CCTV Test");

            float baseX = 4f;
            float baseY = 0f;

            CreatePlatform("Ground", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f),
                new Vector3(30f, 1f, 1f));

            // Two CCTV units with different initial Head rotations and different Z positions
            // to verify that per-prefab initial pose + 3D offset is handled without manual tuning.
            CreateCCTV("CCTV_Left", root.transform,
                hostPos: new Vector3(baseX - 6f, baseY + 5f, 0f),
                headEuler: new Vector3(0f, 0f, -30f),
                trackingRange: 20f);

            CreateCCTV("CCTV_Right", root.transform,
                hostPos: new Vector3(baseX + 6f, baseY + 5f, 2f),
                headEuler: new Vector3(0f, 0f, 30f),
                trackingRange: 15f);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("CCTV Test setup complete!\n" +
                "- CCTV_Left (Z=0, range 20), CCTV_Right (Z=2, range 15) — different initial tilt + different Z.\n" +
                "- Lens axis = +X (HeadBody elongated along +X, Lens sphere at +X tip).\n" +
                "- Yellow gizmo = current lens direction; cyan = initial lens direction + tracking range.");
        }

        static GameObject CreateCCTV(string name, Transform parent, Vector3 hostPos, Vector3 headEuler, float trackingRange)
        {
            // Root (static mount position)
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = hostPos;

            // Mount box — static, does not rotate
            var mount = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mount.name = "Mount";
            Object.DestroyImmediate(mount.GetComponent<BoxCollider>());
            mount.transform.parent = go.transform;
            mount.transform.localPosition = Vector3.zero;
            mount.transform.localScale = new Vector3(0.6f, 0.3f, 0.3f);
            mount.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.2f, 0.2f, 0.2f));

            // Head — the rotating node. Its lens faces local +X (HeadBody/Lens are placed along +X).
            var head = new GameObject("Head");
            head.transform.parent = go.transform;
            head.transform.localPosition = new Vector3(0f, -0.3f, 0f);
            head.transform.localEulerAngles = headEuler;

            var headBody = GameObject.CreatePrimitive(PrimitiveType.Cube);
            headBody.name = "HeadBody";
            Object.DestroyImmediate(headBody.GetComponent<BoxCollider>());
            headBody.transform.parent = head.transform;
            headBody.transform.localPosition = new Vector3(0.3f, 0f, 0f);
            headBody.transform.localScale = new Vector3(0.8f, 0.4f, 0.4f);
            headBody.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.85f, 0.85f, 0.9f));

            var lens = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lens.name = "Lens";
            Object.DestroyImmediate(lens.GetComponent<SphereCollider>());
            lens.transform.parent = head.transform;
            lens.transform.localPosition = new Vector3(0.7f, 0f, 0f);
            lens.transform.localScale = Vector3.one * 0.25f;
            lens.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.05f, 0.05f, 0.1f));

            var cctv = go.AddComponent<CCTVTracker>();
            var so = new SerializedObject(cctv);
            so.FindProperty("headTransform").objectReferenceValue = head.transform;
            so.FindProperty("lensAxis").enumValueIndex = (int)CCTVTracker.LensAxis.PositiveX;
            so.FindProperty("trackingRange").floatValue = trackingRange;
            so.FindProperty("angularSpeed").floatValue = 120f;
            so.FindProperty("maxLeftDeg").floatValue = 90f;
            so.FindProperty("maxRightDeg").floatValue = 90f;
            so.FindProperty("maxUpDeg").floatValue = 60f;
            so.FindProperty("maxDownDeg").floatValue = 60f;
            so.ApplyModifiedProperties();

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
            go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.5f, 0.5f, 0.5f));
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
