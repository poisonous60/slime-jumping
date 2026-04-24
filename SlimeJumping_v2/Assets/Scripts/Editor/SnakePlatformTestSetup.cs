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

            // Build a composite faced head: root 'Head' transform (SnakeSpine drives position/
            // rotation — never scale), with child HeadSkin cube sized for the segment, plus
            // eyes and mouth as sibling children in the head root's un-scaled space so their
            // offsets don't smear.
            const float segLen = 1.0f;
            const float segW   = 0.6f;
            Transform headRoot;
            Renderer headSkin;
            BuildFacedHead(snake.transform, segLen, segW, out headRoot, out headSkin);

            var so = new SerializedObject(spine);
            so.FindProperty("pathProviderBehaviour").objectReferenceValue = path;
            so.FindProperty("headVisual").objectReferenceValue = headRoot;
            so.FindProperty("headSkinRenderer").objectReferenceValue = headSkin;
            so.FindProperty("bodySegmentCount").intValue = 8;
            so.FindProperty("segmentLength").floatValue = segLen;
            so.FindProperty("followness").floatValue = 0.6f;
            so.FindProperty("zColliderThreshold").floatValue = 0.05f;
            so.FindProperty("visualRingsPerSegment").intValue = 4;
            so.ApplyModifiedProperties();

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = snake;
            Debug.Log("SnakePlatform Test setup complete.\n" +
                      "- Head: composite 'Head' (HeadSkin cube + two eyes + mouth) — only the skin is tinted on/off-plane, eyes/mouth keep their authored colors. Swap by dropping a prefab into SnakeSpine.headPrefab (and leaving Head Visual empty) — the prefab is instantiated at Awake at its authored scale (SnakeSpine never writes localScale). Point Head Skin Renderer at whichever child receives the plane tint.\n" +
                      "- Body: single dynamic ribbon mesh. Each coarse spine band is subdivided into 'Visual Rings Per Segment' (default 4) Catmull-Rom samples, so the tube bends smoothly even at sharp turns. Collider stays per-segment.\n" +
                      "- Waypoint 3 is at Z=0.5 → ribbon splits into two pieces as the off-plane segment passes through.\n" +
                      "- Change 'Body Segment Count' or 'Visual Rings Per Segment' on SnakeSpine to resize the ribbon live.");
        }

        static WaypointSnakePath.PathWaypoint Wp(float x, float y, float z, float speedAfter)
        {
            return new WaypointSnakePath.PathWaypoint
            {
                localPos = new Vector3(x, y, z),
                speedAfter = speedAfter,
            };
        }

        static void BuildFacedHead(Transform parent, float segLen, float segW,
                                   out Transform headRoot, out Renderer headSkinRenderer)
        {
            // Root is an empty transform — the SnakeSpine writes position/rotation here.
            // Rotation is a 2D yaw around Z, so local +X tracks motion direction and local
            // +Z stays aligned with world +Z (toward the camera), which is where we place
            // the eyes and mouth so they're always visible.
            var root = new GameObject("Head");
            root.transform.SetParent(parent, false);
            root.layer = parent.gameObject.layer;
            headRoot = root.transform;

            // Skin cube — this is the mesh SnakeSpine tints on/off-plane. Scaled to segment
            // dimensions; eye/mouth siblings use absolute offsets in the parent's (unity) space
            // so they don't stretch when the skin scales non-uniformly.
            var skin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            skin.name = "HeadSkin";
            skin.transform.SetParent(root.transform, false);
            skin.transform.localPosition = Vector3.zero;
            skin.transform.localScale = new Vector3(segLen, segW, segW);
            Object.DestroyImmediate(skin.GetComponent<BoxCollider>());
            skin.layer = parent.gameObject.layer;
            headSkinRenderer = skin.GetComponent<MeshRenderer>();

            // Eyes — two flattened white spheres on the camera-facing (+Z) side of the head,
            // biased forward (+X) so they read as "looking where the snake is going".
            float frontZ = segW * 0.5f + 0.01f; // just outside the skin's +Z face
            float eyeX   = segLen * 0.22f;
            float eyeY   = segW   * 0.28f;
            CreateEye(root.transform, "Eye_L", new Vector3(eyeX,  eyeY, frontZ));
            CreateEye(root.transform, "Eye_R", new Vector3(eyeX, -eyeY, frontZ));

            // Mouth — a short dark horizontal bar near the forward tip of the head.
            var mouth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mouth.name = "Mouth";
            mouth.transform.SetParent(root.transform, false);
            mouth.transform.localPosition = new Vector3(segLen * 0.42f, 0f, frontZ);
            mouth.transform.localScale = new Vector3(segLen * 0.18f, segW * 0.08f, 0.04f);
            Object.DestroyImmediate(mouth.GetComponent<BoxCollider>());
            mouth.layer = parent.gameObject.layer;
            mouth.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.08f, 0.04f, 0.04f));
        }

        static void CreateEye(Transform headRoot, string name, Vector3 localPos)
        {
            var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eye.name = name;
            eye.transform.SetParent(headRoot, false);
            eye.transform.localPosition = localPos;
            eye.transform.localScale = new Vector3(0.18f, 0.18f, 0.10f);
            Object.DestroyImmediate(eye.GetComponent<SphereCollider>());
            eye.layer = headRoot.gameObject.layer;
            eye.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(Color.white);

            // Pupil — a smaller darker sphere nudged slightly further toward the camera, so it
            // reads as a pupil on the white eye regardless of snake yaw.
            var pupil = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pupil.name = name + "_Pupil";
            pupil.transform.SetParent(eye.transform, false);
            // Local space here is the stretched eye sphere, so translate in a way that cancels
            // the eye's non-uniform scale (z=0.10 is narrower than xy=0.18).
            pupil.transform.localPosition = new Vector3(0f, 0f, 0.55f);
            pupil.transform.localScale = new Vector3(0.45f, 0.45f, 0.45f);
            Object.DestroyImmediate(pupil.GetComponent<SphereCollider>());
            pupil.layer = headRoot.gameObject.layer;
            pupil.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.05f, 0.05f, 0.05f));
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
