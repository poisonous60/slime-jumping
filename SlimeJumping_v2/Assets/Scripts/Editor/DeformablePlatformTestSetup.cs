using UnityEngine;
using UnityEditor;
using SlimeJumping.Platforms;

namespace SlimeJumping.Editor
{
    public static class DeformablePlatformTestSetup
    {
        const int GroundLayer = 7;

        [MenuItem("SlimeJumping/Platform/Setup DeformablePlatform Test")]
        public static void Setup()
        {
            var root = new GameObject("DeformablePlatformTest");
            Undo.RegisterCreatedObjectUndo(root, "Create DeformablePlatform Test");

            // Floor so the slime has somewhere to stand / slingshot from.
            CreateBoxPlatform("Floor", root.transform,
                new Vector3(0f, -4f, 0f), new Vector3(18f, 1f, 1f));

            // Catch floor far below — lets the reset trigger fire when slime falls past.
            CreateBoxPlatform("CatchFloor", root.transform,
                new Vector3(0f, -14f, 0f), new Vector3(24f, 1f, 1f));

            // Left launch wall (solid — so the slime can climb and slingshot toward the pillar).
            CreateBoxPlatform("LaunchWall", root.transform,
                new Vector3(-7f, 1f, 0f), new Vector3(1f, 9f, 1f));

            // The deformable pillar — tall, narrow, rectangle default.
            CreateDeformablePillar("ClayPillar", root.transform,
                new Vector3(4f, 0f, 0f),
                width: 1.5f, height: 7f, segmentCount: 22,
                preset: ShapePreset.Rectangle);

            // A wider block at a different height.
            CreateDeformablePillar("ClayBlock", root.transform,
                new Vector3(1f, 4.5f, 0f),
                width: 3f, height: 1.5f, segmentCount: 8,
                preset: ShapePreset.Rectangle);

            // Shape-variant examples so you can see authoring-driven outlines.
            CreateDeformablePillar("ClayHourglass", root.transform,
                new Vector3(8f, 0f, 0f),
                width: 2f, height: 6f, segmentCount: 22,
                preset: ShapePreset.Hourglass);

            CreateDeformablePillar("ClayDome", root.transform,
                new Vector3(11f, 0f, 0f),
                width: 2f, height: 6f, segmentCount: 22,
                preset: ShapePreset.DomeTop);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("DeformablePlatform test setup complete.\n" +
                      "- Slingshot the slime off LaunchWall into ClayPillar; each hit should dent it.\n" +
                      "- Fall below the pillar to watch it restore.");
        }

        enum ShapePreset { Rectangle, Hourglass, Taper, DomeTop }

        static GameObject CreateDeformablePillar(string name, Transform parent, Vector3 pos,
            float width, float height, int segmentCount, ShapePreset preset)
        {
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = pos;
            go.layer = GroundLayer;

            go.AddComponent<PolygonCollider2D>();
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = EnsureClayMaterial();

            var dp = go.AddComponent<DeformablePlatform>();
            var so = new SerializedObject(dp);
            so.FindProperty("genWidth").floatValue = width;
            so.FindProperty("genHeight").floatValue = height;
            so.FindProperty("genSegmentCount").intValue = segmentCount;
            so.FindProperty("impactToDisplacement").floatValue = 0.04f;
            so.FindProperty("maxPerHitDisplacement").floatValue = 0.12f;
            so.FindProperty("falloffYRange").floatValue = 0.8f;
            so.FindProperty("depth").floatValue = 0.8f;
            so.FindProperty("fallBelowMargin").floatValue = 1.5f;
            so.FindProperty("resetDuration").floatValue = 0.6f;
            so.ApplyModifiedPropertiesWithoutUndo();

            switch (preset)
            {
                case ShapePreset.Hourglass: dp.GenerateHourglass(); break;
                case ShapePreset.Taper:     dp.GenerateTaper();     break;
                case ShapePreset.DomeTop:   dp.GenerateDomeTop();   break;
                default:                    dp.GenerateRectangle(); break;
            }
            dp.BuildRuntime();
            return go;
        }

        static GameObject CreateBoxPlatform(string name, Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.parent = parent;
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.layer = GroundLayer;

            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            go.AddComponent<BoxCollider2D>().size = Vector2.one;

            go.GetComponent<MeshRenderer>().sharedMaterial =
                new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.35f, 0.35f, 0.38f) };
            return go;
        }

        const string ClayMaterialPath = "Assets/Prebs/Platform/Materials/ClayPlatform.mat";

        static Material EnsureClayMaterial()
        {
            EnsureFolder("Assets/Prebs/Platform/Materials");
            var existing = AssetDatabase.LoadAssetAtPath<Material>(ClayMaterialPath);
            if (existing != null) return existing;

            // Unlit so it reads as mud-brown regardless of scene lighting
            // (URP Lit goes nearly black here without a directional light rigged).
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var mat = new Material(shader) { name = "ClayPlatform" };
            mat.SetColor("_BaseColor", new Color(0.55f, 0.38f, 0.25f));
            AssetDatabase.CreateAsset(mat, ClayMaterialPath);
            return mat;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            string curr = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = curr + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(curr, parts[i]);
                curr = next;
            }
        }
    }
}
