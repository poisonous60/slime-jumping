using UnityEngine;
using UnityEditor;
using SlimeJumping.Platforms;
using SlimeJumping.Settings;

namespace SlimeJumping.Editor
{
    public static class BambooPlatformV3TestSetup
    {
        const string RootName = "BambooPlatformV3Test";
        const string SettingsAssetPath = "Assets/Settings/BambooV3Settings.asset";
        // Pushed onto the slime's SurfaceMaterial override stack by PlatformCarrier while it
        // rides a stalk, popped on detach. Kills gravity slide so the slime stays where it
        // landed instead of running down to the base — at the base the moment arm is ~0, which
        // zeroes both the throw velocity and the launch carrier bonus.
        const string SurfaceAssetPath = "Assets/Settings/SurfaceMaterials/BambooV3_Surface.asset";

        // Floor cube sits at y = -6 with half-height 0.5.
        const float FloorTop = -5.5f;

        [MenuItem("SlimeJumping/Platform/Setup BambooPlatform V3 Test")]
        public static void SetupBambooV3Test()
        {
            // Remove any stale test root left over from a previous menu run — those still
            // reference last-build's material/shader and won't pick up the latest defaults
            // until the GameObject is recreated.
            var existing = GameObject.Find(RootName);
            if (existing != null) Object.DestroyImmediate(existing);

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create BambooPlatformV3 Test");
            root.transform.position = Vector3.zero;
            var settings = AssetDatabase.LoadAssetAtPath<BambooV3Settings>(SettingsAssetPath);
            if (settings == null)
                Debug.LogWarning($"[BambooPlatformV3TestSetup] {SettingsAssetPath} not found — stalks will fall back to runtime class defaults.", root);
            var surface = AssetDatabase.LoadAssetAtPath<SlimeJumping.Physics.SurfaceMaterial>(SurfaceAssetPath);
            if (surface == null)
                Debug.LogWarning($"[BambooPlatformV3TestSetup] {SurfaceAssetPath} not found — the slime will slide down the stalks.", root);

            CreateStaticPlatform("Floor", root.transform,
                new Vector3(0f, -6f, 0f), new Vector3(56f, 1f, 1f));
            CreateStaticPlatform("Wall_Left", root.transform,
                new Vector3(-26f, 2f, 0f), new Vector3(1f, 20f, 1f));
            CreateStaticPlatform("Wall_Right", root.transform,
                new Vector3(26f, 2f, 0f), new Vector3(1f, 20f, 1f));
            CreateStaticPlatform("LaunchPad", root.transform,
                new Vector3(-24f, -4f, 0f), new Vector3(3f, 1f, 1f));

            // The whole point of V3 is being hit from BELOW on a tilted stalk, so most of
            // these are raised on pedestals and lean out over open floor. A stalk based on
            // the floor has no reachable underside near the base, which is exactly the setup
            // that made v1's failure hard to see.
            //
            // Tilt is the root Transform's Z rotation and nothing else — negative Z leans the
            // stalk's local +Y toward +X, so a slime launched from the left pad flies up-right
            // into the underside.

            // Baseline: vertical. Rider weight torque is ZERO here (arm.x = 0), so this stalk
            // only ever moves from an impact. Compare against the tilted ones to see the
            // static sag that only a tilt produces.
            CreateBamboo(root.transform, "V3_Vertical",
                new Vector3(-20f, FloorTop, 0f), tiltZ: 0f, length: 5f, segments: 12, settings, surface);

            CreatePedestal(root.transform, "Pedestal_Tilt30", -12f, topY: 1f);
            CreateBamboo(root.transform, "V3_Tilt30",
                new Vector3(-12f, 1f, 0f), tiltZ: -30f, length: 6f, segments: 14, settings, surface);

            CreatePedestal(root.transform, "Pedestal_Tilt50", -2f, topY: 3f);
            CreateBamboo(root.transform, "V3_Tilt50",
                new Vector3(-2f, 3f, 0f), tiltZ: -50f, length: 7f, segments: 16, settings, surface);

            // Nearly horizontal — the strongest "hit the underside straight on" case, and the
            // largest rider-weight sag.
            CreatePedestal(root.transform, "Pedestal_Tilt70", 9f, topY: 1f);
            CreateBamboo(root.transform, "V3_Tilt70",
                new Vector3(9f, 1f, 0f), tiltZ: -70f, length: 6f, segments: 14, settings, surface);

            // Long stalk on the same settings asset. V3 has no length-normalised curve, so
            // this should read consistently with the short ones instead of barely bending —
            // that mismatch across a 4 m..20 m grove is what broke v1's tuning.
            CreatePedestal(root.transform, "Pedestal_Long", 19f, topY: 0f);
            CreateBamboo(root.transform, "V3_Long",
                new Vector3(19f, 0f, 0f), tiltZ: -40f, length: 13f, segments: 26, settings, surface);

            // The chain diagnostic now lives on the SLIME: it follows the slime and resolves,
            // each fixed step, whichever V3 stalk the slime is on or nearest to. This way it
            // also sees the impulse the rider injects while charging — the "pressing jump kicks
            // the stalk" signal — regardless of which stalk is involved.
            AttachChainDebugToSlime();

            EditorSceneManager_MarkActiveDirty();

            Selection.activeGameObject = root;
            Debug.Log(
                "BambooPlatformV3 Test setup complete — 5 stalks (1 vertical baseline + 4 tilted on pedestals).\n" +
                "Tilt is the root Transform Z rotation; there is no tilt field.\n" +
                "\n" +
                "Check list:\n" +
                "1. Hit a tilted stalk's underside hard from below → it visibly bends. (v1 produced literally zero here: it used only the impact component perpendicular to the stalk.)\n" +
                "2. Stay off Space and wait → the slime is thrown off automatically as the stalk springs back.\n" +
                "3. Land on one gently → it sags a little and does NOT throw; the stalk is just a perch.\n" +
                "4. While attached, charge and launch → the stalk KEEPS its natural swing; pressing jump must NOT kick it.\n" +
                "5. Return speed → back to rest inside ~0.5 s, two or three swings, then still.\n" +
                "6. Right after being thrown → it does not immediately re-stick to the same stalk.\n" +
                "\n" +
                "Tuning: if 3 throws when it should not, RAISE ejectBendDeg; if 2 refuses to throw, LOWER it. " +
                "The other four knobs should not need touching — that is the point of the design.\n" +
                "V3_Vertical is the control: rider weight makes no torque on a vertical stalk, so any sag there means something else is acting.\n" +
                "V3_Long shares the same settings asset — it should feel consistent with the short stalks, not dead.\n" +
                "\n" +
                "DIAGNOSTIC: BambooV3ChainDebug is on the SLIME. Its on-screen panel names the FIRST broken link of\n" +
                "  overlap a segment -> SurfaceProbe picks it -> slime Attached -> carrier on this stalk -> ApplyRiderInfluence ran -> bend.\n" +
                "It also shows the impulse the rider injects each step (kind: landing / coupled) and whether the rider is charging,\n" +
                "so a coupled impulse during Charging is the 'jump kicks the stalk' smoking gun. F7 dumps the last ~90 FixedUpdates.\n" +
                "\n" +
                "Right-click the BambooPlatformV3 header → 'Rebuild Segments' after changing length / segmentCount / segmentRadius / colliderShape / visual tessellation.");
        }

        static GameObject CreateBamboo(Transform parent, string name, Vector3 basePos, float tiltZ,
                                 float length, int segments, BambooV3Settings settings,
                                 SlimeJumping.Physics.SurfaceMaterial surface)
        {
            // returns the stalk GameObject so the caller can bolt a diagnostic onto one of them
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = basePos;
            go.transform.rotation = Quaternion.Euler(0f, 0f, tiltZ);
            go.layer = 7;

            // The platform adds Rigidbody2D itself in RebuildSegments, but the serialized
            // fields have to be in place first so RebuildSegments builds to them.
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;

            var bamboo = go.AddComponent<BambooPlatformV3>();

            var so = new SerializedObject(bamboo);
            so.FindProperty("settings").objectReferenceValue = settings;
            so.FindProperty("bambooSurfaceMaterial").objectReferenceValue = surface;
            so.FindProperty("bambooLength").floatValue = length;
            so.FindProperty("segmentCount").intValue = segments;
            // Set the stalk colours explicitly so leftover serialized data from any prior
            // component layout cannot leak into the new material.
            so.FindProperty("stalkColorBase").colorValue = new Color(0.30f, 0.52f, 0.20f, 1f);
            so.FindProperty("stalkColorTip").colorValue = new Color(0.50f, 0.68f, 0.28f, 1f);
            so.FindProperty("shadowDarken").floatValue = 0.60f;
            so.FindProperty("scarColor").colorValue = new Color(0.22f, 0.26f, 0.14f, 1f);
            so.FindProperty("scarHalfWidth").floatValue = 0.004f;
            so.FindProperty("scarDepth").floatValue = 0.03f;
            so.ApplyModifiedProperties();

            bamboo.RebuildSegments();
            return go;
        }

        static void AttachChainDebugToSlime()
        {
            var slime = Object.FindFirstObjectByType<SlimeJumping.Controller.SlimeController>();
            if (slime == null)
            {
                Debug.LogWarning("[BambooPlatformV3TestSetup] No SlimeController in the scene — chain diagnostic not attached. " +
                                 "Add SlimeJumping/Debug/Bamboo V3 Chain Debug to the slime manually.");
                return;
            }
            if (slime.GetComponent<SlimeJumping.Debugging.BambooV3ChainDebug>() == null)
                Undo.AddComponent<SlimeJumping.Debugging.BambooV3ChainDebug>(slime.gameObject);
        }

        // Thin column from the floor up to the stalk's base, so the tilted stalk has open
        // air underneath it to be hit from.
        static void CreatePedestal(Transform parent, string name, float x, float topY)
        {
            float height = topY - FloorTop;
            CreateStaticPlatform(name, parent,
                new Vector3(x, (FloorTop + topY) * 0.5f, 0f),
                new Vector3(1.2f, height, 1f));
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

        static void EditorSceneManager_MarkActiveDirty()
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        }
    }
}
