using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    public static class KnifeTestSetup
    {
        [MenuItem("SlimeJumping/Mechanism/Setup Knife Test")]
        public static void Setup()
        {
            var existing = GameObject.Find("KnifeTest");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject("KnifeTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Knife Test");

            float baseX = 4f;
            float baseY = 0f;

            CreatePlatform("Ground", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f),
                new Vector3(20f, 1f, 1f));

            // A floating knife: side faces = safe surface, edge face = hazard
            CreateKnife("Knife", root.transform,
                hostPos: new Vector3(baseX + 2f, baseY + 3f, 0f));

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("Knife Test setup complete!\n" +
                "- Slowly touch the knife's side face (below) → slime attaches safely (low speed).\n" +
                "- Fly fast into the edge face (top, red trigger) → stun + pull into blade.\n" +
                "- During stun, body collider is ignored so the slime slips through.\n" +
                "- velocityThreshold 4. Below that, edge is ignored too (slow contact is safe).");
        }

        static GameObject CreateKnife(string name, Transform parent, Vector3 hostPos)
        {
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = hostPos;

            // Body (flat side) = the "safe" surface.
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<BoxCollider>());
            body.transform.parent = go.transform;
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale = new Vector3(2f, 0.25f, 0.3f);
            body.layer = 7; // Environment — normal surface
            var bodyCol = body.AddComponent<BoxCollider2D>();
            bodyCol.isTrigger = false;
            bodyCol.size = Vector2.one;
            body.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.85f, 0.85f, 0.9f));

            // KnifeHazard lives on this child, not the root — only the top face is hazardous.
            var edge = new GameObject("Edge");
            edge.transform.parent = go.transform;
            edge.transform.localPosition = new Vector3(0f, 0.18f, 0f); // just above body top
            var edgeCol = edge.AddComponent<BoxCollider2D>();
            edgeCol.isTrigger = true;
            edgeCol.size = new Vector2(2f, 0.15f);

            var knife = edge.AddComponent<KnifeHazard>();
            var so = new SerializedObject(knife);
            so.FindProperty("velocityThreshold").floatValue = 4f;
            so.FindProperty("speedRetainRatio").floatValue = 0.4f;
            so.FindProperty("pullForce").floatValue = 6f;
            so.FindProperty("stunDuration").floatValue = 0.8f;
            so.FindProperty("hitCooldown").floatValue = 1.2f;
            so.FindProperty("bodyRoot").objectReferenceValue = body.transform;
            so.ApplyModifiedProperties();

            var edgeVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            edgeVisual.name = "EdgeVisual";
            Object.DestroyImmediate(edgeVisual.GetComponent<BoxCollider>());
            edgeVisual.transform.parent = edge.transform;
            edgeVisual.transform.localPosition = Vector3.zero;
            edgeVisual.transform.localScale = new Vector3(2f, 0.08f, 0.05f);
            edgeVisual.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(1f, 0.1f, 0.1f));

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
