using UnityEngine;
using UnityEditor;
using SlimeJumping.Audio;
using SlimeJumping.Interaction;

namespace SlimeJumping.Editor
{
    /// Spawns a test gramophone (v2) in the current scene next to the slime's
    /// spawn area. Assign a PhonographPlaylist asset on the instance after
    /// running; rows will render in the panel once the playlist is set.
    public static class PhonographV2TestSetup
    {
        const int InteractableLayer = 10;

        [MenuItem("SlimeJumping/Interaction/Setup PhonographV2 Test")]
        public static void Setup()
        {
            var existing = GameObject.Find("PhonographV2Test");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject("PhonographV2Test");
            Undo.RegisterCreatedObjectUndo(root, "Create PhonographV2 Test");

            float baseX = 6f;
            float baseY = 0f;

            CreatePlatform("Ground", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f),
                new Vector3(16f, 1f, 1f));

            CreatePhono("PhonographV2_A", root.transform,
                hostPos: new Vector3(baseX + 1.5f, baseY + 0.5f, 0f));
            CreatePhono("PhonographV2_B", root.transform,
                hostPos: new Vector3(baseX + 5.0f, baseY + 0.5f, 0f));

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("PhonographV2 Test setup complete.\n" +
                "- Assign a PhonographPlaylist asset on each gramophone.\n" +
                "- Ensure a GramophonePanel is registered in the scene UI.\n" +
                "- Walk up + interact to open the modal track list.");
        }

        static GameObject CreatePhono(string name, Transform parent, Vector3 hostPos)
        {
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = hostPos;
            go.layer = InteractableLayer;

            // Body (static collider owner)
            var baseBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseBox.name = "Body";
            Object.DestroyImmediate(baseBox.GetComponent<BoxCollider>());
            baseBox.transform.parent = go.transform;
            baseBox.transform.localPosition = Vector3.zero;
            baseBox.transform.localScale = new Vector3(1.2f, 0.3f, 1.2f);
            baseBox.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.4f, 0.25f, 0.15f));

            // Disc pivot (visual rotation only — no collider attached)
            var discPivot = new GameObject("DiscPivot");
            discPivot.transform.parent = go.transform;
            discPivot.transform.localPosition = new Vector3(0f, 0.2f, 0f);

            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Disc";
            Object.DestroyImmediate(disc.GetComponent<CapsuleCollider>());
            disc.transform.parent = discPivot.transform;
            disc.transform.localPosition = Vector3.zero;
            disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            disc.transform.localScale = new Vector3(0.9f, 0.05f, 0.9f);
            disc.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.08f, 0.08f, 0.08f));

            var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stripe.name = "Stripe";
            Object.DestroyImmediate(stripe.GetComponent<BoxCollider>());
            stripe.transform.parent = disc.transform;
            stripe.transform.localPosition = new Vector3(0.3f, 1.1f, 0f);
            stripe.transform.localScale = new Vector3(0.3f, 0.5f, 0.05f);
            stripe.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(1f, 0.9f, 0.2f));

            // Needle arm (simple rod pivoting at top-right of body)
            var needlePivot = new GameObject("NeedleArm");
            needlePivot.transform.parent = go.transform;
            needlePivot.transform.localPosition = new Vector3(0.55f, 0.22f, 0f);
            needlePivot.transform.localEulerAngles = new Vector3(0f, 0f, -20f);

            var rod = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rod.name = "Rod";
            Object.DestroyImmediate(rod.GetComponent<BoxCollider>());
            rod.transform.parent = needlePivot.transform;
            rod.transform.localPosition = new Vector3(-0.35f, 0f, 0f);
            rod.transform.localScale = new Vector3(0.7f, 0.06f, 0.06f);
            rod.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.75f, 0.75f, 0.8f));

            // Trigger collider for interaction detection
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.6f, 1.4f);

            var phono = go.AddComponent<MediaInteractable_PhonographV2>();
            var so = new SerializedObject(phono);
            so.FindProperty("discTransform").objectReferenceValue = discPivot.transform;
            so.FindProperty("discRPM").floatValue = 60f;
            so.FindProperty("needleArm").objectReferenceValue = needlePivot.transform;
            so.FindProperty("interactPrompt").stringValue = "Play Music";
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
