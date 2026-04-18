using UnityEngine;
using UnityEditor;
using SlimeJumping.Platforms;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    public static class WaypointTiltPlatformTestSetup
    {
        [MenuItem("SlimeJumping/Platform/Setup WaypointTilt Test")]
        public static void Setup()
        {
            var root = new GameObject("WaypointTiltTest");
            Undo.RegisterCreatedObjectUndo(root, "Create WaypointTilt Test");

            float baseX = 4f;
            float baseY = 0f;

            // --- Top landing (slime spawn / starting platform) ---
            CreatePlatform("TopLanding", root.transform,
                new Vector3(baseX - 6f, baseY + 5.5f, 0f),
                new Vector3(8f, 1f, 1f));

            // --- Ground (descent destination) ---
            CreatePlatform("Ground", root.transform,
                new Vector3(baseX + 6f, baseY - 5.5f, 0f),
                new Vector3(8f, 1f, 1f));

            // --- WaypointTilt deck (descends from top to bottom, tilts under rider weight) ---
            CreateDeck("WaypointTiltDeck", root.transform,
                startPos: new Vector3(baseX, baseY + 5f, 0f),
                travelDown: 5f,
                deckSize: new Vector2(3f, 0.5f),
                speed: 2f,
                waitTime: 1.2f,
                maxTiltAngle: 15f,
                stiffness: 20f,
                tiltDamping: 5f);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("WaypointTilt Test setup complete!\n" +
                "- Land on the deck from TopLanding: deck descends to the Ground level.\n" +
                "- Stand off-center: deck tilts toward your side (spring-damped).\n" +
                "- Step off mid-descent: deck completes the current segment, then waits; tilt returns to 0.\n" +
                "- Reach the bottom, step off, then ride back on: ping-pong back to the top.");
        }

        static GameObject CreateDeck(string name, Transform parent,
            Vector3 startPos, float travelDown, Vector2 deckSize,
            float speed, float waitTime,
            float maxTiltAngle, float stiffness, float tiltDamping)
        {
            // Root — kinematic body + collider. Tilt rotates this whole transform.
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = startPos;
            go.layer = 7;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            var col = go.AddComponent<BoxCollider2D>();
            col.size = deckSize;

            var deck = go.AddComponent<WaypointTiltPlatform>();
            var so = new SerializedObject(deck);
            var waypoints = so.FindProperty("localWaypoints");
            waypoints.arraySize = 2;
            waypoints.GetArrayElementAtIndex(0).vector2Value = Vector2.zero;
            waypoints.GetArrayElementAtIndex(1).vector2Value = new Vector2(0f, -travelDown);
            so.FindProperty("speed").floatValue = speed;
            so.FindProperty("waitTime").floatValue = waitTime;
            so.FindProperty("loop").boolValue = false;
            so.FindProperty("maxTiltAngle").floatValue = maxTiltAngle;
            so.FindProperty("stiffness").floatValue = stiffness;
            so.FindProperty("tiltDamping").floatValue = tiltDamping;
            so.ApplyModifiedProperties();

            // Rider sensor child — trigger collider sitting just above the deck.
            var sensorGo = new GameObject("RiderSensor");
            sensorGo.transform.parent = go.transform;
            sensorGo.transform.localPosition = new Vector3(0f, deckSize.y * 0.5f + 0.3f, 0f);
            var sensorCol = sensorGo.AddComponent<BoxCollider2D>();
            sensorCol.isTrigger = true;
            sensorCol.size = new Vector2(deckSize.x * 0.95f, 0.6f);
            var sensor = sensorGo.AddComponent<PlatformRiderSensor>();

            // RiderActivatedMotor requires WaypointPlatform; wire CanAdvance manually instead.
            // Hook it up via a tiny inline component that ties sensor → CanAdvance.
            var gate = go.AddComponent<WaypointTiltRiderGate>();
            var gateSo = new SerializedObject(gate);
            gateSo.FindProperty("sensor").objectReferenceValue = sensor;
            gateSo.ApplyModifiedProperties();

            // Visual child — placeholder cube (separate from collider so visual rotation
            // could later be animated independently if desired).
            var visualGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visualGo.name = "Visual";
            Object.DestroyImmediate(visualGo.GetComponent<BoxCollider>());
            visualGo.transform.parent = go.transform;
            visualGo.transform.localPosition = Vector3.zero;
            visualGo.transform.localScale = new Vector3(deckSize.x, deckSize.y, 1f);
            visualGo.GetComponent<MeshRenderer>().sharedMaterial =
                MakeMaterial(new Color(0.4f, 0.6f, 0.9f));

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
