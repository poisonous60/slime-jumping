using UnityEngine;
using UnityEditor;
using SlimeJumping.Platforms;

namespace SlimeJumping.Editor
{
    public static class PushDoorTestSetup
    {
        [MenuItem("SlimeJumping/Setup PushDoor Test")]
        public static void Setup()
        {
            var root = new GameObject("PushDoorTest");
            Undo.RegisterCreatedObjectUndo(root, "Create PushDoor Test");

            float baseX = 4f;
            float baseY = 0f;

            // --- Floor ---
            CreatePlatform("Floor", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f),
                new Vector3(14f, 1f, 1f));

            // --- Left wall ---
            CreatePlatform("LeftWall", root.transform,
                new Vector3(baseX - 7.5f, baseY + 4f, 0f),
                new Vector3(1f, 9f, 1f));

            // --- Right wall ---
            CreatePlatform("RightWall", root.transform,
                new Vector3(baseX + 7.5f, baseY + 4f, 0f),
                new Vector3(1f, 9f, 1f));

            // --- PushDoor ---
            float doorHeight = 2f;
            CreatePushDoor("PushDoor_01", root.transform,
                new Vector3(baseX, baseY + doorHeight * 0.5f, 0f),
                doorHeight: doorHeight, doorDepth: 2f);

            // --- Left ramp (approach from left) ---
            var leftRamp = CreatePlatform("LeftRamp", root.transform,
                new Vector3(baseX - 3f, baseY + 0.25f, 0f),
                new Vector3(3f, 0.5f, 1f));

            // --- Right ramp (approach from right) ---
            var rightRamp = CreatePlatform("RightRamp", root.transform,
                new Vector3(baseX + 3f, baseY + 0.25f, 0f),
                new Vector3(3f, 0.5f, 1f));

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("PushDoor Test setup complete!\n" +
                "- PushDoor in center: crawl into it to push open\n" +
                "- Ramps on both sides for approach");
        }

        static void CreatePushDoor(string name, Transform parent, Vector3 hingePos,
            float doorHeight, float doorDepth)
        {
            float doorThickness = 0.15f;

            // Parent = collider position (Z=0 plane, vertically centered on door)
            var door = new GameObject(name);
            door.transform.parent = parent;
            door.transform.position = hingePos;
            door.layer = 7;

            // Rigidbody2D (Kinematic)
            var rb = door.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;

            // BoxCollider2D — thin cross-section at Z=0 (the door edge the slime sees)
            var col = door.AddComponent<BoxCollider2D>();
            col.size = new Vector2(doorThickness, doorHeight);
            col.offset = Vector2.zero;

            // PushDoor component
            door.AddComponent<PushDoor>();

            // VisualPivot — PushDoor.cs positions this via hingeOffset each frame
            var pivot = new GameObject("VisualPivot");
            pivot.transform.parent = door.transform;
            pivot.transform.localPosition = Vector3.zero;
            pivot.transform.localRotation = Quaternion.identity;

            // Mesh — extends from hinge toward camera (Z- direction)
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mesh.name = "Mesh";
            Object.DestroyImmediate(mesh.GetComponent<BoxCollider>());
            mesh.transform.parent = pivot.transform;
            mesh.transform.localPosition = new Vector3(0f, 0f, -doorDepth * 0.5f);
            mesh.transform.localScale = new Vector3(doorThickness, doorHeight, doorDepth);

            mesh.GetComponent<MeshRenderer>().sharedMaterial =
                new Material(Shader.Find("Universal Render Pipeline/Lit"))
                { color = new Color(0.6f, 0.35f, 0.15f) };

            // Wire PushDoor fields
            var so = new SerializedObject(door.GetComponent<PushDoor>());
            so.FindProperty("visualTransform").objectReferenceValue = pivot.transform;
            so.FindProperty("hingeOffset").vector3Value = new Vector3(0f, 0f, doorDepth);
            so.ApplyModifiedProperties();
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

            go.GetComponent<MeshRenderer>().sharedMaterial =
                new Material(Shader.Find("Universal Render Pipeline/Lit"))
                { color = new Color(0.5f, 0.5f, 0.5f) };
            return go;
        }
    }
}
