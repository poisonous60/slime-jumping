using UnityEngine;
using UnityEditor;
using SlimeJumping.Interaction;

namespace SlimeJumping.Editor
{
    public static class SitPoseTestSetup
    {
        const int InteractableLayer = 10;

        [MenuItem("SlimeJumping/Setup SitPose Test")]
        public static void Setup()
        {
            var existing = GameObject.Find("SitPoseTest");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject("SitPoseTest");
            Undo.RegisterCreatedObjectUndo(root, "Create SitPose Test");

            float baseX = 4f;
            float baseY = 0f;

            CreatePlatform("Ground", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f),
                new Vector3(20f, 1f, 1f));

            // Bench 1 — right side
            CreateSitPose("Bench_A", root.transform,
                hostPos: new Vector3(baseX + 2f, baseY + 0.25f, 0f),
                hostSize: new Vector2(1.5f, 0.5f),
                anchorLocal: new Vector3(0f, 0.5f, 0f),
                triggerSize: new Vector2(1.5f, 1.2f),
                color: new Color(0.7f, 0.5f, 0.3f));

            // Bench 2 — left side, further
            CreateSitPose("Bench_B", root.transform,
                hostPos: new Vector3(baseX - 4f, baseY + 0.25f, 0f),
                hostSize: new Vector2(2f, 0.5f),
                anchorLocal: new Vector3(0f, 0.5f, 0f),
                triggerSize: new Vector2(2f, 1.2f),
                color: new Color(0.3f, 0.5f, 0.7f));

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("SitPose Test setup complete!\n" +
                "- Walk up to a bench, press W to sit (slime teleports onto anchor, physics frozen, visual static).\n" +
                "- Press W again to stand up (returns to Airborne state).");
        }

        static GameObject CreateSitPose(string name, Transform parent,
            Vector3 hostPos, Vector2 hostSize,
            Vector3 anchorLocal, Vector2 triggerSize, Color color)
        {
            // Root — visual bench + trigger collider + SitPoseInteractable
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = hostPos;
            go.layer = InteractableLayer;

            // Visual
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            Object.DestroyImmediate(visual.GetComponent<BoxCollider>());
            visual.transform.parent = go.transform;
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localScale = new Vector3(hostSize.x, hostSize.y, 0.3f);
            visual.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(color);

            // Trigger (for InteractionDetector)
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = triggerSize;

            // Anchor child — where slime sits
            var anchor = new GameObject("SitAnchor");
            anchor.transform.parent = go.transform;
            anchor.transform.localPosition = anchorLocal;

            // Anchor visual marker
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "AnchorMarker";
            Object.DestroyImmediate(marker.GetComponent<SphereCollider>());
            marker.transform.parent = anchor.transform;
            marker.transform.localPosition = Vector3.zero;
            marker.transform.localScale = Vector3.one * 0.15f;
            marker.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(1f, 1f, 0f, 1f));

            // Component
            var sit = go.AddComponent<SitPoseInteractable>();
            var so = new SerializedObject(sit);
            so.FindProperty("sitAnchor").objectReferenceValue = anchor.transform;
            so.FindProperty("freezePhysics").boolValue = true;
            so.FindProperty("interactPrompt").stringValue = "Sit";
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
