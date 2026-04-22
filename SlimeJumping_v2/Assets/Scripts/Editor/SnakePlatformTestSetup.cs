using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using SlimeJumping.Platforms;

namespace SlimeJumping.Editor
{
    public static class SnakePlatformTestSetup
    {
        [MenuItem("SlimeJumping/Platform/Setup SnakePlatform Test")]
        public static void SetupSnakeTest()
        {
            var root = new GameObject("SnakePlatformTest");
            Undo.RegisterCreatedObjectUndo(root, "Create SnakePlatform Test");
            root.transform.position = Vector3.zero; // Force origin — Unity's "Create at Pivot" preference would otherwise drop this at scene-view focus.

            float baseX = 0f;
            float baseY = 0f;

            // Floor
            CreateStaticPlatform("Floor", root.transform,
                new Vector3(baseX, baseY - 6f, 0f),
                new Vector3(40f, 1f, 1f));

            // Launch pads
            CreateStaticPlatform("LaunchPad_Left", root.transform,
                new Vector3(baseX - 14f, baseY - 4f, 0f),
                new Vector3(3f, 1f, 1f));

            CreateStaticPlatform("LaunchPad_Right", root.transform,
                new Vector3(baseX + 14f, baseY - 4f, 0f),
                new Vector3(3f, 1f, 1f));

            // Snake root
            var snake = new GameObject("Snake");
            snake.transform.SetParent(root.transform, false);
            snake.transform.position = Vector3.zero;
            snake.layer = 7;

            var rb = snake.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;

            var poly = snake.AddComponent<PolygonCollider2D>();
            poly.pathCount = 0;

            var path = snake.AddComponent<WaypointSnakePath>();
            // Broad S-curve with mixed speeds; one waypoint pushed to Z=0.5 to demo gating.
            var waypoints = new List<WaypointSnakePath.PathWaypoint>
            {
                Wp(-10f,  2f,    0f, 2f),
                Wp( -6f,  3f,    0f, 4f),
                Wp( -2f,  1.5f,  0.5f, 1f),   // this segment rides at non-zero Z
                Wp(  2f,  3f,    0f, 3f),
                Wp(  6f,  1.5f,  0f, 2f),
                Wp( 10f,  3f,    0f, 2f),
            };
            path.SetWaypoints(waypoints, loop: true, catmullRom: true);

            var spine = snake.AddComponent<SnakeSpine>();

            // Head visual is authored here so the user can swap the prefab later for a
            // face-decorated model. The body is a single dynamic ribbon mesh managed by
            // SnakeSpine — no body-cube children to wire up.
            var head = CreateCubeVisual("HeadVisual", snake.transform);

            var so = new SerializedObject(spine);
            so.FindProperty("pathProviderBehaviour").objectReferenceValue = path;
            so.FindProperty("headVisual").objectReferenceValue = head;
            so.FindProperty("bodySegmentCount").intValue = 8;
            so.FindProperty("segmentLength").floatValue = 1.0f;
            so.FindProperty("segmentWidth").floatValue = 0.6f;
            so.FindProperty("followness").floatValue = 0.6f;
            so.FindProperty("zColliderThreshold").floatValue = 0.05f;
            so.ApplyModifiedProperties();

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = snake;
            Debug.Log("SnakePlatform Test setup complete.\n" +
                      "- Head: swappable cube (red when on-plane, gray when off).\n" +
                      "- Body: single dynamic ribbon mesh (green when any on-plane, gray when entirely off).\n" +
                      "- Waypoint 3 is at Z=0.5 → ribbon splits into two pieces as the off-plane segment passes through.\n" +
                      "- Change 'Body Segment Count' on SnakeSpine to resize the ribbon live.");
        }

        static WaypointSnakePath.PathWaypoint Wp(float x, float y, float z, float speedAfter)
        {
            return new WaypointSnakePath.PathWaypoint
            {
                localPos = new Vector3(x, y, z),
                speedAfter = speedAfter,
            };
        }

        static Transform CreateCubeVisual(string name, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            // Pure visual — remove the BoxCollider Unity adds to primitives. Color is set
            // per-frame by SnakeSpine via MaterialPropertyBlock (on/off-plane tint).
            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            go.layer = parent.gameObject.layer;
            return go.transform;
        }

        static GameObject CreateStaticPlatform(string name, Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
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
