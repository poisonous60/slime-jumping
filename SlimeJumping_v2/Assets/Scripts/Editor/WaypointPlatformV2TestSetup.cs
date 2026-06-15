using UnityEngine;
using UnityEditor;
using SlimeJumping.Platforms;
using SlimeJumping.Physics;

namespace SlimeJumping.Editor
{
    /// <summary>
    /// Spawns a WaypointPlatformV2 test bed with three clusters:
    ///   A. Mount-trigger trip + range hold (F1)
    ///   B. Tilt + ice → idle slime slides off (forced jumping)
    ///   C. Leader/partner sandwich → slime extrudes out the side (F2)
    /// Tune waypoints/speeds in the inspector afterward (visual test).
    /// </summary>
    public static class WaypointPlatformV2TestSetup
    {
        const string IcePath = "Assets/Settings/SurfaceMaterials/Ice.asset";

        static readonly Color GroundColor  = new Color(0.5f, 0.5f, 0.5f);
        static readonly Color TripColor    = new Color(0.4f, 0.6f, 0.9f);
        static readonly Color IceColor     = new Color(0.6f, 0.85f, 1f);
        static readonly Color LeaderColor  = new Color(1f, 0.6f, 0.2f);
        static readonly Color PartnerColor = new Color(0.9f, 0.3f, 0.3f);

        [MenuItem("SlimeJumping/Platform/Setup WaypointPlatformV2 Test")]
        public static void Setup()
        {
            var root = new GameObject("WaypointPlatformV2Test");
            Undo.RegisterCreatedObjectUndo(root, "Create WaypointPlatformV2 Test");

            var ice = AssetDatabase.LoadAssetAtPath<SurfaceMaterial>(IcePath);
            if (ice == null)
                Debug.LogWarning($"[WaypointPlatformV2Test] Ice material not found at {IcePath}. Ice cluster will have no override.");

            BuildTripCluster(root.transform, new Vector3(-16f, 0f, 0f));
            BuildIceCluster(root.transform, new Vector3(0f, 0f, 0f), ice);
            BuildSandwichCluster(root.transform, new Vector3(14f, 0f, 0f), ice);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("WaypointPlatformV2 Test setup complete!\n" +
                "A (x≈-16) Mount trip: step on → rides to destination, holds while you stay in range, returns + rearms when you leave.\n" +
                "B (x≈0)   Tilt+Ice: stand still → platform tips, ice slides you off → keep jumping.\n" +
                "C (x≈+14) Sandwich: mount the orange leader → both converge → slime extrudes out the open side.\n" +
                "Set each platform's riderLayers to the slime layer if hold/return misfires. Tweak waypoint heights so the sandwich final gap ≈ slime size.");
        }

        // --- Cluster A: mount-trigger trip + range hold ---
        static void BuildTripCluster(Transform parent, Vector3 c)
        {
            CreateGround("A_Ground_Start", parent, c + new Vector3(-3.5f, 0f, 0f), new Vector3(5f, 1f, 1f));

            var v2 = CreateV2("A_TripPlatform", parent, c + new Vector3(0f, 1f, 0f), new Vector2(3f, 0.6f), TripColor);
            var so = new SerializedObject(v2);
            SetWaypoints(so, Vector2.zero, new Vector2(6f, 4f));
            so.FindProperty("travelSpeed").floatValue = 3f;
            so.FindProperty("mountTriggered").boolValue = true;
            so.FindProperty("holdRange").floatValue = 2.5f;
            // Wobble/sag on, tilt mild and non-dumping so the rider survives the trip.
            so.FindProperty("enableTilt").boolValue = true;
            so.FindProperty("maxTiltAngle").floatValue = 8f;
            so.FindProperty("idleTipBias").floatValue = 0f;
            so.ApplyModifiedProperties();
        }

        // --- Cluster B: tilt + ice (forced jumping) ---
        static void BuildIceCluster(Transform parent, Vector3 c, SurfaceMaterial ice)
        {
            CreateGround("B_Ground_Catch", parent, c + new Vector3(0f, -1f, 0f), new Vector3(10f, 1f, 1f));
            CreateGround("B_Ground_Step",  parent, c + new Vector3(-3.5f, 1f, 0f), new Vector3(3f, 1f, 1f));

            var v2 = CreateV2("B_IcePlatform", parent, c + new Vector3(0f, 2.2f, 0f), new Vector2(4f, 0.6f), IceColor);
            var so = new SerializedObject(v2);
            so.FindProperty("mountTriggered").boolValue = false; // stationary
            so.FindProperty("enableTilt").boolValue = true;
            so.FindProperty("maxTiltAngle").floatValue = 16f;
            so.FindProperty("idleTipBias").floatValue = 6f;
            so.FindProperty("idleSpeedThreshold").floatValue = 0.6f;
            if (ice != null) so.FindProperty("iceMaterial").objectReferenceValue = ice;
            so.ApplyModifiedProperties();
        }

        // --- Cluster C: leader/partner sandwich (F2) ---
        static void BuildSandwichCluster(Transform parent, Vector3 c, SurfaceMaterial ice)
        {
            CreateGround("C_Ground", parent, c + new Vector3(-4f, 0f, 0f), new Vector3(5f, 1f, 1f));

            // Leader = bottom platform the slime mounts; rises upward.
            var leader = CreateV2("C_Leader_Bottom", parent, c + new Vector3(0f, 0.8f, 0f), new Vector2(3f, 0.6f), LeaderColor);
            // Partner = top platform; descends.
            var partner = CreateV2("C_Partner_Top", parent, c + new Vector3(0f, 6f, 0f), new Vector2(4f, 0.8f), PartnerColor);

            var lso = new SerializedObject(leader);
            SetWaypoints(lso, Vector2.zero, new Vector2(0f, 2.6f));
            lso.FindProperty("travelSpeed").floatValue = 2.5f;
            lso.FindProperty("mountTriggered").boolValue = true;
            lso.FindProperty("isLeader").boolValue = true;
            lso.FindProperty("partner").objectReferenceValue = partner;
            lso.FindProperty("enableTilt").boolValue = false;
            lso.FindProperty("pinchPressDepth").floatValue = 0.45f;
            lso.FindProperty("openTiltMax").floatValue = 18f;
            lso.FindProperty("holdRange").floatValue = 2.5f;
            if (ice != null) lso.FindProperty("iceMaterial").objectReferenceValue = ice;
            lso.ApplyModifiedProperties();

            var pso = new SerializedObject(partner);
            SetWaypoints(pso, Vector2.zero, new Vector2(0f, -1.6f));
            pso.FindProperty("travelSpeed").floatValue = 2.5f;
            pso.FindProperty("mountTriggered").boolValue = true; // responds to TriggerFromLeader
            pso.FindProperty("isLeader").boolValue = false;
            pso.FindProperty("enableTilt").boolValue = false;
            pso.FindProperty("holdRange").floatValue = 2.5f;
            if (ice != null) pso.FindProperty("iceMaterial").objectReferenceValue = ice;
            pso.ApplyModifiedProperties();
        }

        // --- helpers ---
        static WaypointPlatformV2 CreateV2(string name, Transform parent, Vector3 pos, Vector2 deck, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.parent = parent;
            go.transform.position = pos;
            go.transform.localScale = new Vector3(deck.x, deck.y, 1f);
            go.layer = 7;

            Object.DestroyImmediate(go.GetComponent<BoxCollider>());

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            var col = go.AddComponent<BoxCollider2D>();
            col.size = Vector2.one;

            go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(color);

            return go.AddComponent<WaypointPlatformV2>();
        }

        static void SetWaypoints(SerializedObject so, Vector2 start, Vector2 dest)
        {
            var wp = so.FindProperty("localWaypoints");
            wp.arraySize = 2;
            wp.GetArrayElementAtIndex(0).vector2Value = start;
            wp.GetArrayElementAtIndex(1).vector2Value = dest;
        }

        static GameObject CreateGround(string name, Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.parent = parent;
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.layer = 7;

            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            var col = go.AddComponent<BoxCollider2D>();
            col.size = Vector2.one;

            go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(GroundColor);
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
