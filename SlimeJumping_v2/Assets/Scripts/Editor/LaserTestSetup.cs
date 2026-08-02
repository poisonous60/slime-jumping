using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    public static class LaserTestSetup
    {
        [MenuItem("SlimeJumping/Mechanism/Setup Laser Test")]
        public static void Setup()
        {
            var existing = GameObject.Find("LaserTest");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject("LaserTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Laser Test");

            float baseX = 4f;
            float baseY = 0f;

            CreatePlatform("Ground", root.transform,
                new Vector3(baseX + 4f, baseY - 0.5f, 0f),
                new Vector3(24f, 1f, 1f));

            // Shaft: two climbable walls, horizontal laser sweeping up/down between them.
            CreatePlatform("Wall_L", root.transform,
                new Vector3(baseX - 3f, baseY + 6f, 0f),
                new Vector3(1f, 12f, 1f));
            CreatePlatform("Wall_R", root.transform,
                new Vector3(baseX + 3f, baseY + 6f, 0f),
                new Vector3(1f, 12f, 1f));

            CreateLaser("Laser_Horizontal", root.transform,
                pos: new Vector3(baseX, baseY + 1.5f, 0f),
                zRotation: 0f,
                beamLength: 5.4f,
                patrolDistance: 7f,
                moveSpeed: 2f,
                endDelay: 0.5f);

            // Vertical laser standing on the ground, sweeping left toward the shaft.
            CreateLaser("Laser_Vertical", root.transform,
                pos: new Vector3(baseX + 12f, baseY + 2.7f, 0f),
                zRotation: 90f,
                beamLength: 5f,
                patrolDistance: 4f,
                moveSpeed: 3f,
                endDelay: 0.2f);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("Laser Test setup complete!\n" +
                "- Laser_Horizontal: sweeps up 7 units inside the shaft, 0.5s end delay.\n" +
                "- Laser_Vertical: rotated 90°, sweeps 4 units left, faster + shorter delay.\n" +
                "- Touch a beam → fixed right-up knockback (additive) + burn mark, no stun.");
        }

        static LaserHazardConfig EnsureConfig()
        {
            const string path = "Assets/Resources/LaserHazardConfig.asset";
            var cfg = AssetDatabase.LoadAssetAtPath<LaserHazardConfig>(path);
            if (cfg == null)
            {
                cfg = ScriptableObject.CreateInstance<LaserHazardConfig>();
                AssetDatabase.CreateAsset(cfg, path);
                AssetDatabase.SaveAssets();
            }
            return cfg;
        }

        static GameObject CreateLaser(string name, Transform parent, Vector3 pos, float zRotation,
            float beamLength, float patrolDistance, float moveSpeed, float endDelay)
        {
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, 0f, zRotation);

            var laser = go.AddComponent<LaserHazard>();
            var so = new SerializedObject(laser);
            so.FindProperty("config").objectReferenceValue = EnsureConfig();
            so.FindProperty("beamLength").floatValue = beamLength;
            so.FindProperty("patrolDistance").floatValue = patrolDistance;
            so.FindProperty("moveSpeed").floatValue = moveSpeed;
            so.FindProperty("endDelay").floatValue = endDelay;
            so.ApplyModifiedProperties();

            var box = go.GetComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = new Vector2(beamLength, 0.25f);

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
            go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.35f, 0.45f, 0.75f));
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
