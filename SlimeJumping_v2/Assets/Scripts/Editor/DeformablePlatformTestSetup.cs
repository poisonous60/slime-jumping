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

            CreateDeformablePillar("ClayPillar", root.transform,
                new Vector3(4f, 0f, 0f),
                width: 1.5f, height: 7f, segmentCount: 22,
                preset: DeformablePlatformSpawner.ShapePreset.Rectangle);

            CreateDeformablePillar("ClayBlock", root.transform,
                new Vector3(1f, 4.5f, 0f),
                width: 3f, height: 1.5f, segmentCount: 8,
                preset: DeformablePlatformSpawner.ShapePreset.Rectangle);

            EnsureDirectionalLight(root.transform);
            EnsureActivator(root.transform);

            // Shape-variant examples so you can see authoring-driven outlines.
            CreateDeformablePillar("ClayHourglass", root.transform,
                new Vector3(8f, 0f, 0f),
                width: 2f, height: 6f, segmentCount: 22,
                preset: DeformablePlatformSpawner.ShapePreset.Hourglass);

            CreateDeformablePillar("ClayDome", root.transform,
                new Vector3(11f, 0f, 0f),
                width: 2f, height: 6f, segmentCount: 22,
                preset: DeformablePlatformSpawner.ShapePreset.DomeTop);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("DeformablePlatform test setup complete.\n" +
                      "- Slingshot the slime off LaunchWall into ClayPillar; each hit should dent it.\n" +
                      "- Fall below the pillar to watch it restore.");
        }

        static GameObject CreateDeformablePillar(string name, Transform parent, Vector3 pos,
            float width, float height, int segmentCount, DeformablePlatformSpawner.ShapePreset preset)
        {
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = pos;
            go.layer = GroundLayer;

            go.AddComponent<PolygonCollider2D>();
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = DeformablePlatformSpawner.EnsureClayMaterial();

            var dp = go.AddComponent<DeformablePlatform>();
            var so = new SerializedObject(dp);
            so.FindProperty("genWidth").floatValue = width;
            so.FindProperty("genHeight").floatValue = height;
            so.FindProperty("genSegmentCount").intValue = segmentCount;
            so.ApplyModifiedPropertiesWithoutUndo();

            switch (preset)
            {
                case DeformablePlatformSpawner.ShapePreset.Hourglass: dp.GenerateHourglass(); break;
                case DeformablePlatformSpawner.ShapePreset.Taper:     dp.GenerateTaper();     break;
                case DeformablePlatformSpawner.ShapePreset.DomeTop:   dp.GenerateDomeTop();   break;
                default:                                              dp.GenerateRectangle(); break;
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

        static void EnsureActivator(Transform parent)
        {
            if (Object.FindAnyObjectByType<DeformablePlatformActivator>() != null) return;

            var go = new GameObject("DeformableActivator");
            go.transform.parent = parent;
            var rb  = go.AddComponent<Rigidbody2D>();
            var col = go.AddComponent<CircleCollider2D>();
            go.AddComponent<DeformablePlatformActivator>();
            // AddComponent doesn't fire Reset — configure explicitly or the Inspector shows defaults.
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.useFullKinematicContacts = true;
            rb.gravityScale = 0f;
            col.isTrigger = true;
            col.radius = 15f;
        }

        static void EnsureDirectionalLight(Transform parent)
        {
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) return;

            var go = new GameObject("TestDirectionalLight");
            go.transform.parent = parent;
            var l2 = go.AddComponent<Light>();
            l2.type = LightType.Directional;
            l2.intensity = 1.1f;
            l2.shadows = LightShadows.Soft;
            // 3/4 front-left so the front-face bumps cast micro-shadows.
            go.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
        }

    }
}
