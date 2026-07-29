using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    // Editor-only scene builder for ClothField (#16 passable curtain).
    public static class ClothTestSetup
    {
        const int GroundLayer = 7;          // Environment in TagManager

        struct ClothTuning
        {
            public int gridColumns;
            public int gridRows;
            public float cellSize;
            public float clothThickness;
            public Color baseColor;
            public float clothDragCoefficient;
            public Vector3 windAccelerationWorld;
            public float windTurbulence;
        }

        static ClothTuning LightCurtain() => new ClothTuning
        {
            gridColumns = 10,
            gridRows = 14,
            cellSize = 0.25f,
            clothThickness = 0.10f,
            baseColor = new Color(0.85f, 0.6f, 0.55f, 0.9f),
            clothDragCoefficient = 0.5f,
            windAccelerationWorld = new Vector3(2.0f, 0.3f, 0f),
            windTurbulence = 0.6f,
        };

        static ClothTuning HeavyCurtain() => new ClothTuning
        {
            gridColumns = 10,
            gridRows = 14,
            cellSize = 0.28f,
            clothThickness = 0.16f,
            baseColor = new Color(0.35f, 0.25f, 0.45f, 0.95f),
            clothDragCoefficient = 1.8f,
            windAccelerationWorld = new Vector3(1.0f, 0.15f, 0f),
            windTurbulence = 0.35f,
        };

        static ClothTuning WallCurtain() => new ClothTuning
        {
            gridColumns = 12,
            gridRows = 12,
            cellSize = 0.22f,
            clothThickness = 0.08f,
            baseColor = new Color(0.5f, 0.75f, 0.9f, 0.85f),
            clothDragCoefficient = 0.9f,
            windAccelerationWorld = new Vector3(3.0f, 0.4f, 0f),
            windTurbulence = 0.7f,
        };

        [MenuItem("SlimeJumping/Mechanism/Setup Cloth Test")]
        public static void SetupClothTest()
        {
            var root = new GameObject("ClothTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Cloth Test");

            CreatePlatform("Floor", root.transform,
                new Vector3(0f, -6f, 0f), new Vector3(30f, 1f, 1f));

            CreatePlatform("LaunchPad_Left", root.transform,
                new Vector3(-10f, -2f, 0f), new Vector3(3f, 1f, 1f));

            // Camera-facing (Euler 0,0,0): cloth simulates in its LOCAL XY plane, so deformation
            // is only visible from the 2.5D camera while local XY = world XY.
            CreateCurtain(root.transform, "Cloth_Light",
                new Vector3(-5f, 4f, 0f),
                Quaternion.identity,
                LightCurtain());

            CreateCurtain(root.transform, "Cloth_Heavy",
                new Vector3(5f, 4f, 0f),
                Quaternion.identity,
                HeavyCurtain());

            // Slight Y rotation only. Euler(0, 90, 0) gives a true doorway, but its deformation
            // runs along world Z = invisible from the camera.
            CreateCurtain(root.transform, "Cloth_Angled30",
                new Vector3(0f, 4f, 3f),
                Quaternion.Euler(0f, 30f, 0f),
                WallCurtain());

            Finish(root,
                "Cloth Test:\n" +
                "  Cloth_Light      (rot 0/0/0,  drag 0.5)  — camera-facing light curtain\n" +
                "  Cloth_Heavy      (rot 0/0/0,  drag 1.8)  — camera-facing heavy curtain\n" +
                "  Cloth_Angled30   (rot 0/30/0, drag 0.9)  — slight depth rotation\n" +
                "\n" +
                "NOTE on orientation: cloth simulates in its LOCAL XY plane. Wind and slime\n" +
                "push-out act in that plane. A curtain rotated to Euler(0, 90, 0) (width along\n" +
                "world Z) still works mechanically, but its deformation is in world Z = depth =\n" +
                "invisible from the 2.5D camera. Use camera-facing curtains unless the scene\n" +
                "specifically needs a corridor doorway seen from above/side.\n" +
                "\n" +
                "How to test:\n" +
                "  1. Play: launch slime through Cloth_Light. Cloth bulges around the slime\n" +
                "     and swings back afterwards. Wind makes it gently sway continuously.\n" +
                "  2. Compare Cloth_Light vs Cloth_Heavy at matched entry velocity — heavy\n" +
                "     saps more speed per traversal (never stops slime — commitment #2).\n" +
                "  3. Drift into a curtain while Attached — drag = 0 (commitment #3).\n" +
                "  4. Cloth bottom rests on the floor (ground collision).");
        }

        // === Curtain factory ===

        static GameObject CreateCurtain(Transform parent, string name,
            Vector3 worldPos, Quaternion worldRot, ClothTuning tuning)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer), typeof(ClothField));
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;
            go.transform.rotation = worldRot;

            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var mat = MakeClothMaterial(tuning.baseColor);
            Undo.RegisterCreatedObjectUndo(mat, "Create Cloth Material");
            mr.sharedMaterial = mat;

            var cloth = go.GetComponent<ClothField>();
            var so = new SerializedObject(cloth);
            so.FindProperty("gridColumns").intValue = tuning.gridColumns;
            so.FindProperty("gridRows").intValue = tuning.gridRows;
            so.FindProperty("cellSize").floatValue = tuning.cellSize;
            so.FindProperty("clothThickness").floatValue = tuning.clothThickness;
            so.FindProperty("clothDragCoefficient").floatValue = tuning.clothDragCoefficient;
            so.FindProperty("windAccelerationWorld").vector3Value = tuning.windAccelerationWorld;
            so.FindProperty("windTurbulence").floatValue = tuning.windTurbulence;
            so.FindProperty("material").objectReferenceValue = mat;
            // groundLayerMask auto-resolves to "Environment" layer in Awake; leave at default (0).
            so.ApplyModifiedProperties();

            cloth.Rebuild();
            return go;
        }

        static Material MakeClothMaterial(Color baseColor)
        {
            var shader = Shader.Find("SlimeJumping/Cloth_Unlit");
            if (shader == null)
            {
                Debug.LogError("[ClothTest] Shader 'SlimeJumping/Cloth_Unlit' not found. Is Cloth_Unlit.shader imported?");
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            var mat = new Material(shader) { name = "Cloth_Mat (runtime)" };
            mat.SetColor("_BaseColor", baseColor);
            return mat;
        }

        // === Helpers ===

        static GameObject CreatePlatform(string name, Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.layer = GroundLayer;
            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            go.AddComponent<BoxCollider2D>().size = Vector2.one;
            go.GetComponent<MeshRenderer>().sharedMaterial = MakePlatformMaterial(new Color(0.5f, 0.5f, 0.5f));
            return go;
        }

        static Material MakePlatformMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            return mat;
        }

        static void Finish(GameObject root, string message)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = root;
            Debug.Log("[ClothTest] " + message);
        }
    }
}
