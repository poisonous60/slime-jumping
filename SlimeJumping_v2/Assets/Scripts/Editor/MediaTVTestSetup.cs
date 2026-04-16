using UnityEngine;
using UnityEditor;
using SlimeJumping.Interaction;

namespace SlimeJumping.Editor
{
    public static class MediaTVTestSetup
    {
        const int InteractableLayer = 10;

        [MenuItem("SlimeJumping/Interaction/Setup Media TV Test")]
        public static void Setup()
        {
            var existing = GameObject.Find("MediaTVTest");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject("MediaTVTest");
            Undo.RegisterCreatedObjectUndo(root, "Create MediaTV Test");

            float baseX = 4f;
            float baseY = 0f;

            CreatePlatform("Ground", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f),
                new Vector3(16f, 1f, 1f));

            CreateTV("TV", root.transform,
                hostPos: new Vector3(baseX + 2f, baseY + 1f, 0f));

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("Media TV Test setup complete!\n" +
                "- Walk up to TV, press W to toggle on/off (screen material swaps, audio placeholder if clips assigned).\n" +
                "- AudioClips left empty — assign on the AudioSource for sound test.");
        }

        static GameObject CreateTV(string name, Transform parent, Vector3 hostPos)
        {
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = hostPos;
            go.layer = InteractableLayer;

            // TV body cube
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<BoxCollider>());
            body.transform.parent = go.transform;
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale = new Vector3(1.4f, 1f, 0.5f);
            body.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.15f, 0.15f, 0.15f));

            // Screen quad (front face), its own renderer — this is the swap target
            var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "Screen";
            Object.DestroyImmediate(screen.GetComponent<MeshCollider>());
            screen.transform.parent = go.transform;
            screen.transform.localPosition = new Vector3(0f, 0f, -0.26f);
            screen.transform.localScale = new Vector3(1.2f, 0.8f, 1f);
            var offMat = MakeMaterial(new Color(0.05f, 0.05f, 0.05f));
            screen.GetComponent<MeshRenderer>().sharedMaterial = offMat;
            var onMat = MakeMaterialEmissive(new Color(0.3f, 0.8f, 1f));

            // Trigger
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.6f, 1.2f);

            // Audio
            var audio = go.AddComponent<AudioSource>();
            audio.spatialBlend = 1f;
            audio.playOnAwake = false;

            // Component
            var tv = go.AddComponent<MediaInteractable_TV>();
            var so = new SerializedObject(tv);
            so.FindProperty("audioSource").objectReferenceValue = audio;
            so.FindProperty("screenRenderer").objectReferenceValue = screen.GetComponent<MeshRenderer>();
            so.FindProperty("onMaterial").objectReferenceValue = onMat;
            so.FindProperty("offMaterial").objectReferenceValue = offMat;
            so.FindProperty("screenMaterialIndex").intValue = 0;
            so.FindProperty("interactPrompt").stringValue = "Toggle TV";
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

        static Material MakeMaterialEmissive(Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 2f);
            return mat;
        }
    }
}
