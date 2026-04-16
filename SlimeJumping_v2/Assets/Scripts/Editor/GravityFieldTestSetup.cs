using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    public static class GravityFieldTestSetup
    {
        [MenuItem("SlimeJumping/Mechanism/Setup GravityField Test")]
        public static void SetupGravityFieldTest()
        {
            var root = new GameObject("GravityFieldTest");
            Undo.RegisterCreatedObjectUndo(root, "Create GravityField Test");

            float baseX = 20f;
            float baseY = 0f;

            // --- Normal-gravity floor (launch area) ---
            CreatePlatform("Floor", root.transform,
                new Vector3(baseX - 6f, baseY - 0.5f, 0f),
                new Vector3(8f, 1f, 1f));

            // --- Test 1: Fixed mode — Inverted gravity ---
            // Ceiling platform inside inverted zone
            CreatePlatform("InvertedCeiling", root.transform,
                new Vector3(baseX + 2f, baseY + 8.5f, 0f),
                new Vector3(6f, 1f, 1f));

            // Inverted gravity zone
            var invertedZone = CreateGravityFieldFixed(
                "GravityField_Inverted", root.transform,
                new Vector3(baseX + 2f, baseY + 5f, 0f),
                new Vector2(8f, 8f),
                Vector2.up); // down = up → inverted gravity

            // --- Test 2: Fixed mode — Sideways gravity ---
            CreatePlatform("SidewaysWall", root.transform,
                new Vector3(baseX + 14.5f, baseY + 4f, 0f),
                new Vector3(1f, 8f, 1f));

            var sidewaysZone = CreateGravityFieldFixed(
                "GravityField_Sideways", root.transform,
                new Vector3(baseX + 11f, baseY + 4f, 0f),
                new Vector2(6f, 10f),
                Vector2.right); // down = right → fall toward right wall

            // --- Test 3: Fixed mode — 45 degree angle ---
            CreatePlatform("DiagonalPlatform", root.transform,
                new Vector3(baseX + 20f, baseY + 2f, 0f),
                new Vector3(6f, 1f, 1f));

            var diagonalZone = CreateGravityFieldFixed(
                "GravityField_Diagonal", root.transform,
                new Vector3(baseX + 20f, baseY + 5f, 0f),
                new Vector2(8f, 8f),
                new Vector2(1f, -1f).normalized); // 45 degrees

            // --- Test 4: Radial mode — Circular planet ---
            // Ring of platforms around a center point
            float ringRadius = 3.5f;
            Vector3 ringCenter = new Vector3(baseX + 30f, baseY + 6f, 0f);
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI * 2f / 12f;
                Vector3 pos = ringCenter + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * ringRadius;
                float angleDeg = angle * Mathf.Rad2Deg;
                var platform = CreatePlatform($"RingPlatform_{i}", root.transform,
                    pos, new Vector3(2f, 0.5f, 1f));
                platform.transform.rotation = Quaternion.Euler(0f, 0f, angleDeg);
            }

            // Radial gravity zone covering the ring
            var radialZone = CreateGravityFieldRadial(
                "GravityField_Radial", root.transform,
                ringCenter,
                5.5f,
                Vector2.zero, // pivot at center
                false); // inward

            // --- Transition bridge (walk from normal into inverted) ---
            CreatePlatform("TransitionBridge", root.transform,
                new Vector3(baseX - 1f, baseY - 0.5f, 0f),
                new Vector3(4f, 1f, 1f));

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("GravityField Test setup complete! 4 test zones: Inverted, Sideways, 45-degree, Radial ring.");
        }

        static GameObject CreateGravityFieldFixed(string name, Transform parent, Vector3 pos,
            Vector2 boxSize, Vector2 downDirection)
        {
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = pos;
            go.layer = LayerMask.NameToLayer("GravityField");

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = boxSize;

            var gf = go.AddComponent<GravityField>();
            var so = new SerializedObject(gf);
            so.FindProperty("gravityMode").enumValueIndex = 0; // Fixed
            so.FindProperty("localDownDirection").vector2Value = downDirection;
            so.ApplyModifiedProperties();

            // Visual tint
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = boxSize;
            sr.color = new Color(0.7f, 0.3f, 1f, 0.06f);
            sr.sortingOrder = -1;

            return go;
        }

        static GameObject CreateGravityFieldRadial(string name, Transform parent, Vector3 pos,
            float radius, Vector2 localPivot, bool invert)
        {
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = pos;
            go.layer = LayerMask.NameToLayer("GravityField");

            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = radius;

            var gf = go.AddComponent<GravityField>();
            var so = new SerializedObject(gf);
            so.FindProperty("gravityMode").enumValueIndex = 1; // Radial
            so.FindProperty("localPivot").vector2Value = localPivot;
            so.FindProperty("invertDirection").boolValue = invert;
            so.ApplyModifiedProperties();

            // Visual tint
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = Vector2.one * radius * 2f;
            sr.color = new Color(0.7f, 0.3f, 1f, 0.04f);
            sr.sortingOrder = -1;

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
            go.AddComponent<BoxCollider2D>().size = Vector2.one;

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
