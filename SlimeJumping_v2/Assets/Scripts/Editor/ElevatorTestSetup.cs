using UnityEngine;
using UnityEditor;
using SlimeJumping.Platforms;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    public static class ElevatorTestSetup
    {
        [MenuItem("SlimeJumping/Platform/Setup Elevator Test")]
        public static void Setup()
        {
            var root = new GameObject("ElevatorTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Elevator Test");

            float baseX = 4f;
            float baseY = 0f;

            CreatePlatform("Ground", root.transform,
                new Vector3(baseX - 6f, baseY - 0.5f, 0f),
                new Vector3(8f, 1f, 1f));

            CreatePlatform("TopLanding", root.transform,
                new Vector3(baseX + 6f, baseY + 5.5f, 0f),
                new Vector3(8f, 1f, 1f));

            CreateElevator("Elevator", root.transform,
                startPos: new Vector3(baseX, baseY, 0f),
                travelUp: 5f,
                deckSize: new Vector2(3f, 0.5f),
                speed: 2f,
                waitTime: 1.2f);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("Elevator Test setup complete!\n" +
                "- Step onto the deck: platform rises to the top landing.\n" +
                "- Step off mid-trip: elevator finishes the current segment, then waits.\n" +
                "- Step back on: resumes toward the next waypoint.\n" +
                "- Arrival jolt shakes the visual only; the slime stays put.");
        }

        static GameObject CreateElevator(string name, Transform parent,
            Vector3 startPos, float travelUp, Vector2 deckSize,
            float speed, float waitTime)
        {
            // Visual is a child so the jolt shakes the mesh without dragging the rider.
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = startPos;
            go.layer = 7;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            var col = go.AddComponent<BoxCollider2D>();
            col.size = deckSize;

            // WaypointPlatform — ping-pong (loop=false) between local {0,0} and {0,+travelUp}.
            var wp = go.AddComponent<WaypointPlatform>();
            var wpSo = new SerializedObject(wp);
            var waypoints = wpSo.FindProperty("localWaypoints");
            waypoints.arraySize = 2;
            waypoints.GetArrayElementAtIndex(0).vector2Value = Vector2.zero;
            waypoints.GetArrayElementAtIndex(1).vector2Value = new Vector2(0f, travelUp);
            wpSo.FindProperty("speed").floatValue = speed;
            wpSo.FindProperty("waitTime").floatValue = waitTime;
            wpSo.FindProperty("loop").boolValue = false;
            wpSo.ApplyModifiedProperties();

            var sensorGo = new GameObject("RiderSensor");
            sensorGo.transform.parent = go.transform;
            sensorGo.transform.localPosition = new Vector3(0f, deckSize.y * 0.5f + 0.3f, 0f);
            var sensorCol = sensorGo.AddComponent<BoxCollider2D>();
            sensorCol.isTrigger = true;
            sensorCol.size = new Vector2(deckSize.x * 0.95f, 0.6f);
            var sensor = sensorGo.AddComponent<PlatformRiderSensor>();

            // Motor wires the sensor to the WaypointPlatform.CanAdvance gate.
            var motor = go.AddComponent<RiderActivatedMotor>();
            var motorSo = new SerializedObject(motor);
            motorSo.FindProperty("sensor").objectReferenceValue = sensor;
            motorSo.ApplyModifiedProperties();

            // Visual child — placeholder cube. Replace with old_elevator prefab in scene.
            var visualGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visualGo.name = "Visual";
            Object.DestroyImmediate(visualGo.GetComponent<BoxCollider>());
            visualGo.transform.parent = go.transform;
            visualGo.transform.localPosition = Vector3.zero;
            visualGo.transform.localScale = new Vector3(deckSize.x, deckSize.y, 1f);
            visualGo.GetComponent<MeshRenderer>().sharedMaterial =
                MakeMaterial(new Color(0.4f, 0.6f, 0.9f));

            var jolt = go.AddComponent<ElevatorJoltVisual>();
            var joltSo = new SerializedObject(jolt);
            joltSo.FindProperty("visualTransform").objectReferenceValue = visualGo.transform;
            joltSo.ApplyModifiedProperties();

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
