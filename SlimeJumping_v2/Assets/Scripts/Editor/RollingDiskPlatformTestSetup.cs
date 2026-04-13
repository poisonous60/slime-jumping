using UnityEngine;
using UnityEditor;
using SlimeJumping.Platforms;

namespace SlimeJumping.Editor
{
    public static class RollingDiskPlatformTestSetup
    {
        [MenuItem("SlimeJumping/Setup RollingDiskPlatform Test")]
        public static void SetupRollingDiskTest()
        {
            var root = new GameObject("RollingDiskPlatformTest");
            Undo.RegisterCreatedObjectUndo(root, "Create RollingDiskPlatform Test");

            float baseX = 4f;
            float baseY = 0f;

            // Ground floor
            CreatePlatform("Floor", root.transform,
                new Vector3(baseX, baseY - 4f, 0f),
                new Vector3(20f, 1f, 1f));

            // Left wall
            CreatePlatform("LeftWall", root.transform,
                new Vector3(baseX - 10f, baseY + 1f, 0f),
                new Vector3(1f, 10f, 1f));

            // Right wall
            CreatePlatform("RightWall", root.transform,
                new Vector3(baseX + 10f, baseY + 1f, 0f),
                new Vector3(1f, 10f, 1f));

            // Disk 1: default tuning — sensitive rotation
            CreateRollingDisk("Disk_Default", root.transform,
                new Vector3(baseX - 5f, baseY, 0f),
                radius: 1.5f,
                riderAngAccelScale: 50f,
                angularDamping: 0.3f,
                maxAngularSpeed: 720f);

            // Disk 2: heavily damped — slow rotation, easy to stand on
            CreateRollingDisk("Disk_Damped", root.transform,
                new Vector3(baseX, baseY, 0f),
                radius: 1.5f,
                riderAngAccelScale: 30f,
                angularDamping: 1.5f,
                maxAngularSpeed: 360f);

            // Disk 3: low damping, high sensitivity — quickly rolls off
            CreateRollingDisk("Disk_Sensitive", root.transform,
                new Vector3(baseX + 5f, baseY, 0f),
                radius: 1.5f,
                riderAngAccelScale: 100f,
                angularDamping: 0.1f,
                maxAngularSpeed: 1080f);

            // Large disk for tangential velocity jump testing
            CreateRollingDisk("Disk_Large", root.transform,
                new Vector3(baseX, baseY + 6f, 0f),
                radius: 2.5f,
                riderAngAccelScale: 40f,
                angularDamping: 0.2f,
                maxAngularSpeed: 540f);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("RollingDiskPlatform Test setup complete!\n" +
                "- Disk_Default: balanced (scale=50, damp=0.3)\n" +
                "- Disk_Damped: slow rotation (scale=30, damp=1.5)\n" +
                "- Disk_Sensitive: fast roll-off (scale=100, damp=0.1)\n" +
                "- Disk_Large: r=2.5 for tangential velocity test\n" +
                "Stand near disk edges to see it roll. Jump from spinning disk to feel tangential launch.");
        }

        static GameObject CreateRollingDisk(string name, Transform parent, Vector3 pos,
            float radius, float riderAngAccelScale, float angularDamping, float maxAngularSpeed)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.parent = parent;
            go.transform.position = pos;
            go.transform.localScale = new Vector3(radius * 2f, 0.1f, radius * 2f);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.layer = 7;

            Object.DestroyImmediate(go.GetComponent<CapsuleCollider>());
            var col2d = go.AddComponent<CircleCollider2D>();
            col2d.radius = 0.5f;

            var disk = go.AddComponent<RollingDiskPlatform>();
            var so = new SerializedObject(disk);
            so.FindProperty("riderAngAccelScale").floatValue = riderAngAccelScale;
            so.FindProperty("angularDamping").floatValue = angularDamping;
            so.FindProperty("maxAngularSpeed").floatValue = maxAngularSpeed;
            so.FindProperty("deadZoneAngleDeg").floatValue = 15f;
            so.ApplyModifiedProperties();

            go.GetComponent<MeshRenderer>().sharedMaterial =
                MakeMaterial(new Color(0.8f, 0.4f, 0.2f));

            // Orientation marker — small child cube at +X edge so rotation is visible
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "Marker";
            marker.transform.parent = go.transform;
            marker.transform.localPosition = new Vector3(0.4f, 0f, 0f);
            marker.transform.localScale = new Vector3(0.15f, 2f, 0.15f);
            marker.transform.localRotation = Quaternion.identity;
            Object.DestroyImmediate(marker.GetComponent<BoxCollider>());
            marker.GetComponent<MeshRenderer>().sharedMaterial =
                MakeMaterial(new Color(1f, 1f, 1f));

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

            go.GetComponent<MeshRenderer>().sharedMaterial =
                MakeMaterial(new Color(0.5f, 0.5f, 0.5f));

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
