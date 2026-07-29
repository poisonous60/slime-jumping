using UnityEngine;
using UnityEditor;
using SlimeJumping.Interaction;

namespace SlimeJumping.Editor
{
    public static class PhonographTestSetup
    {
        const int InteractableLayer = 10;

        [MenuItem("SlimeJumping/Interaction/Setup Phonograph Test")]
        public static void Setup()
        {
            var existing = GameObject.Find("PhonographTest");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject("PhonographTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Phonograph Test");

            float baseX = 4f;
            float baseY = 0f;

            CreatePlatform("Ground", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f),
                new Vector3(16f, 1f, 1f));

            CreatePhono("Phonograph", root.transform,
                hostPos: new Vector3(baseX + 2f, baseY + 0.5f, 0f));

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("Phonograph Test setup complete!\n" +
                "- W → disc starts rotating (audio if tracks assigned).\n" +
                "- W again → stops. Next W → advances to next track.");
        }

        static GameObject CreatePhono(string name, Transform parent, Vector3 hostPos)
        {
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = hostPos;
            go.layer = InteractableLayer;

            var baseBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseBox.name = "Base";
            Object.DestroyImmediate(baseBox.GetComponent<BoxCollider>());
            baseBox.transform.parent = go.transform;
            baseBox.transform.localPosition = Vector3.zero;
            baseBox.transform.localScale = new Vector3(1.2f, 0.3f, 1.2f);
            baseBox.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.4f, 0.25f, 0.15f));

            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Disc";
            Object.DestroyImmediate(disc.GetComponent<CapsuleCollider>());
            disc.transform.parent = go.transform;
            disc.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            // Flatten cylinder to disc; orient so rotation around +Z spins visibly
            disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            disc.transform.localScale = new Vector3(0.9f, 0.05f, 0.9f);
            disc.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.1f, 0.1f, 0.1f));

            var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stripe.name = "Stripe";
            Object.DestroyImmediate(stripe.GetComponent<BoxCollider>());
            stripe.transform.parent = disc.transform;
            stripe.transform.localPosition = new Vector3(0.3f, 1.1f, 0f);
            stripe.transform.localScale = new Vector3(0.3f, 0.5f, 0.05f);
            stripe.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(1f, 0.9f, 0.2f));

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.4f, 1.2f);

            var audio = go.AddComponent<AudioSource>();
            audio.spatialBlend = 1f;
            audio.playOnAwake = false;
            audio.loop = true;

            var phono = go.AddComponent<MediaInteractable_Phonograph>();
            var so = new SerializedObject(phono);
            so.FindProperty("audioSource").objectReferenceValue = audio;
            so.FindProperty("discTransform").objectReferenceValue = disc.transform;
            so.FindProperty("discRPM").floatValue = 60f;
            so.FindProperty("interactPrompt").stringValue = "Toggle Music";
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
