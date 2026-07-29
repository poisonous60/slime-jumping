using UnityEngine;
using UnityEditor;
using SlimeJumping.Interaction;

namespace SlimeJumping.Editor
{
    public static class WashingTestSetup
    {
        const int InteractableLayer = 10;

        [MenuItem("SlimeJumping/Interaction/Setup Washing Test")]
        public static void Setup()
        {
            var existing = GameObject.Find("WashingTest");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject("WashingTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Washing Test");

            float baseX = 4f;
            float baseY = 0f;

            CreatePlatform("Ground", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f),
                new Vector3(16f, 1f, 1f));

            CreateWashing("Washing", root.transform,
                hostPos: new Vector3(baseX + 2f, baseY + 0.7f, 0f));

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("Washing Test setup complete!\n" +
                "- W → toggles vibration. Visual child vibrates; the body collider stays still.\n" +
                "- Verify: colliders (boxes visible via gizmo) do NOT move, only the inner visual cube.");
        }

        static GameObject CreateWashing(string name, Transform parent, Vector3 hostPos)
        {
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = hostPos;
            go.layer = InteractableLayer;

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.4f, 1.4f);

            var visual = new GameObject("Visual");
            visual.transform.parent = go.transform;
            visual.transform.localPosition = Vector3.zero;

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<BoxCollider>());
            body.transform.parent = visual.transform;
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale = new Vector3(1.2f, 1.2f, 0.8f);
            body.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.9f, 0.9f, 0.95f));

            var porthole = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            porthole.name = "Porthole";
            Object.DestroyImmediate(porthole.GetComponent<SphereCollider>());
            porthole.transform.parent = visual.transform;
            porthole.transform.localPosition = new Vector3(0f, 0f, -0.5f);
            porthole.transform.localScale = Vector3.one * 0.6f;
            porthole.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.2f, 0.2f, 0.25f));

            var audio = go.AddComponent<AudioSource>();
            audio.spatialBlend = 1f;
            audio.playOnAwake = false;

            var wash = go.AddComponent<ApplianceInteractable_Washing>();
            var so = new SerializedObject(wash);
            so.FindProperty("visualRoot").objectReferenceValue = visual.transform;
            so.FindProperty("amplitude").floatValue = 0.05f; // larger for visibility
            so.FindProperty("frequency").floatValue = 10f;
            so.FindProperty("audioSource").objectReferenceValue = audio;
            so.FindProperty("interactPrompt").stringValue = "Toggle Washer";
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
