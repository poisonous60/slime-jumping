using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    public static class SawBladeTestSetup
    {
        [MenuItem("SlimeJumping/Setup SawBlade Test")]
        public static void Setup()
        {
            var existing = GameObject.Find("SawBladeTest");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject("SawBladeTest");
            Undo.RegisterCreatedObjectUndo(root, "Create SawBlade Test");

            float baseX = 4f;
            float baseY = 0f;

            CreatePlatform("Ground", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f),
                new Vector3(24f, 1f, 1f));

            // CW blade (positive RPM) — tangent goes CCW in screen space
            CreateSawBlade("Saw_CW", root.transform,
                hostPos: new Vector3(baseX + 2f, baseY + 1.5f, 0f),
                rotationRPM: 600f,
                tangentWeight: 0.5f);

            // CCW blade (negative RPM) — opposite fling direction
            CreateSawBlade("Saw_CCW", root.transform,
                hostPos: new Vector3(baseX - 3f, baseY + 1.5f, 0f),
                rotationRPM: -600f,
                tangentWeight: 0.5f);

            // Pure tangent blade
            CreateSawBlade("Saw_PureTangent", root.transform,
                hostPos: new Vector3(baseX + 6f, baseY + 1.5f, 0f),
                rotationRPM: 600f,
                tangentWeight: 1f);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("SawBlade Test setup complete!\n" +
                "- Saw_CW: clockwise blade, balanced radial+tangent knockback.\n" +
                "- Saw_CCW: counter-clockwise — knockback direction mirrors.\n" +
                "- Saw_PureTangent: tangent-only fling (pure spin transfer).\n" +
                "- Touch any blade → stun + knockback.");
        }

        static GameObject CreateSawBlade(string name, Transform parent,
            Vector3 hostPos, float rotationRPM, float tangentWeight)
        {
            // Root — carries SawBladeHazard + trigger (OnTriggerEnter2D only fires on same-GO MonoBehaviours).
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = hostPos;

            // Trigger on root
            var trigger = go.AddComponent<CircleCollider2D>();
            trigger.isTrigger = true;
            trigger.radius = 0.6f;

            // Blade visual (rotating child, no collider)
            var bladeGo = new GameObject("Blade");
            bladeGo.transform.parent = go.transform;
            bladeGo.transform.localPosition = Vector3.zero;

            var bladeVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            bladeVisual.name = "BladeVisual";
            Object.DestroyImmediate(bladeVisual.GetComponent<CapsuleCollider>());
            bladeVisual.transform.parent = bladeGo.transform;
            bladeVisual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            bladeVisual.transform.localScale = new Vector3(1.2f, 0.05f, 1.2f);
            bladeVisual.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.8f, 0.8f, 0.9f));

            // Tooth markers (so rotation is visually obvious)
            for (int i = 0; i < 4; i++)
            {
                var tooth = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tooth.name = $"Tooth_{i}";
                Object.DestroyImmediate(tooth.GetComponent<BoxCollider>());
                tooth.transform.parent = bladeGo.transform;
                float ang = i * 90f * Mathf.Deg2Rad;
                tooth.transform.localPosition = new Vector3(Mathf.Cos(ang) * 0.55f, Mathf.Sin(ang) * 0.55f, 0f);
                tooth.transform.localRotation = Quaternion.Euler(0f, 0f, i * 90f);
                tooth.transform.localScale = new Vector3(0.2f, 0.15f, 0.2f);
                tooth.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.95f, 0.3f, 0.3f));
            }

            // Component on root (trigger already added above)
            var saw = go.AddComponent<SawBladeHazard>();
            var so = new SerializedObject(saw);
            so.FindProperty("bladeTransform").objectReferenceValue = bladeGo.transform;
            so.FindProperty("rotationRPM").floatValue = rotationRPM;
            so.FindProperty("knockbackForce").floatValue = 25f;
            so.FindProperty("stunDuration").floatValue = 1f;
            so.FindProperty("tangentWeight").floatValue = tangentWeight;
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
