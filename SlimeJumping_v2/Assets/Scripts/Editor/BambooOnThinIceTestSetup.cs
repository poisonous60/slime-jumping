using UnityEngine;
using UnityEditor;
using SlimeJumping.Platforms;
using SlimeJumping.Platforms.ThinIceV2;
using SlimeJumping.Platforms.ThinIceV2.EditorOnly;
using SlimeJumping.Settings;

namespace SlimeJumping.Editor
{
    /// One-click test scene for the BambooOnThinIce coupling: bamboo stalks standing on thin ice tiles.
    public static class BambooOnThinIceTestSetup
    {
        const string RootName = "BambooOnThinIceTest";
        const string SettingsAssetPath = "Assets/Settings/BambooV3Settings.asset";
        const string SurfaceAssetPath = "Assets/Settings/SurfaceMaterials/BambooV3_Surface.asset";
        const string IceMaterialPath = "Assets/Shaders/ThinIceV2.mat";
        const string IceConfigPath = "Assets/Resources/ThinIceV2Config.asset";
        const string LinkConfigPath = "Assets/Resources/BambooOnThinIceConfig.asset";
        const int GroundLayer = 7;

        [MenuItem("SlimeJumping/Platform/Setup Bamboo On ThinIce Test")]
        public static void Setup()
        {
            // Shared ice config + VFX prefabs must exist before any tile is added.
            if (AssetDatabase.LoadAssetAtPath<ThinIceV2Config>(IceConfigPath) == null)
            {
                ThinIceV2ConfigGenerator.GenerateMenu();
            }

            // Canonical corpse-fall config (idempotent — never stomps tuned values).
            if (AssetDatabase.LoadAssetAtPath<BambooOnThinIceConfig>(LinkConfigPath) == null)
            {
                AssetDatabase.CreateAsset(
                    ScriptableObject.CreateInstance<BambooOnThinIceConfig>(), LinkConfigPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[BambooOnThinIceTestSetup] Created {LinkConfigPath}.");
            }
            var linkConfig = AssetDatabase.LoadAssetAtPath<BambooOnThinIceConfig>(LinkConfigPath);

            // Poof puff prefab: reuse the landing-dust puff (idempotent — only fills null).
            if (linkConfig.corpsePoofPrefab == null)
            {
                linkConfig.corpsePoofPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prebs/VFX/Dust_Landing.prefab");
                EditorUtility.SetDirty(linkConfig);
                AssetDatabase.SaveAssets();
            }

            var existing = GameObject.Find(RootName);
            if (existing != null) Object.DestroyImmediate(existing);

            // The stock BambooV3 / ThinIceV2 test roots occupy the SAME coordinates — leaving them
            // means landings hit the wrong ice, probes flip between stalks, two respawn zones fight.
            foreach (var staleName in new[] { "BambooPlatformV3Test", "ThinIcePlatformV2Test" })
            {
                var stale = GameObject.Find(staleName);
                if (stale != null)
                {
                    Object.DestroyImmediate(stale);
                    Debug.Log($"[BambooOnThinIceTestSetup] Removed overlapping stale test root '{staleName}'.");
                }
            }

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create BambooOnThinIce Test");

            var settings = AssetDatabase.LoadAssetAtPath<BambooV3Settings>(SettingsAssetPath);
            if (settings == null)
                Debug.LogWarning($"[BambooOnThinIceTestSetup] {SettingsAssetPath} not found — stalks fall back to class defaults.", root);
            var surface = AssetDatabase.LoadAssetAtPath<SlimeJumping.Physics.SurfaceMaterial>(SurfaceAssetPath);
            if (surface == null)
                Debug.LogWarning($"[BambooOnThinIceTestSetup] {SurfaceAssetPath} not found — the slime will slide down the stalks.", root);

            // --- Support geometry ---
            CreateBoxPlatform("Floor", root.transform,
                new Vector3(0f, -4f, 0f), new Vector3(28f, 1f, 1f));
            CreateBoxPlatform("CatchFloor", root.transform,
                new Vector3(0f, -16f, 0f), new Vector3(36f, 1f, 1f));
            CreateBoxPlatform("LeftWall", root.transform,
                new Vector3(-13f, 1f, 0f), new Vector3(1f, 11f, 1f));
            CreateBoxPlatform("RightWall", root.transform,
                new Vector3(13f, 1f, 0f), new Vector3(1f, 11f, 1f));

            // --- Respawn zone: combos under it regenerate when the slime exits ---
            var zoneGo = new GameObject("ThinIceV2_RespawnZone");
            zoneGo.transform.SetParent(root.transform, false);
            var zoneBox = zoneGo.AddComponent<BoxCollider2D>();
            zoneGo.AddComponent<Rigidbody2D>();
            zoneGo.AddComponent<ThinIceRespawnZone>();
            zoneBox.isTrigger = true;
            zoneBox.size = new Vector2(20f, 9f);

            // In-zone: vertical baseline.
            CreateCombo(zoneGo.transform, "Combo_Vertical",
                icePos: new Vector3(-6f, 0f, 0f), tiltZ: 0f, length: 5f, segments: 12,
                settings, surface, linkConfig);

            // In-zone: tilted stalk.
            CreateCombo(zoneGo.transform, "Combo_Tilt30",
                icePos: new Vector3(0.5f, 0f, 0f), tiltZ: -30f, length: 6f, segments: 14,
                settings, surface, linkConfig);

            // In-zone: third variant. All combos are zone members.
            CreateCombo(zoneGo.transform, "Combo_Tilt20",
                icePos: new Vector3(8.5f, 0f, 0f), tiltZ: -20f, length: 5f, segments: 12,
                settings, surface, linkConfig);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = root;

            Debug.Log(
                "BambooOnThinIce test setup complete — 3 stalks on thin ice, all zone members.\n" +
                "\n" +
                "Check list:\n" +
                "1. Land on a stalk → the ICE under it cracks one step (crack sound + shader progress).\n" +
                "2. Nth landing → ice shatters (shards fly) and the stalk topples under physics, keeping its current swing.\n" +
                "3. Ride it down: stay attached while it falls — the slime should be carried, not frozen mid-air.\n" +
                "4. Fallen pole comes to rest → becomes solid walkable debris (no sliding when pushed).\n" +
                "5. Step on the ice DIRECTLY and let the touch timer shatter it → the stalk must fall the same way.\n" +
                "6. Leave the zone area → ice regenerates AND its stalk stands back up (corpse poofs into dust puffs).\n" +
                "   The re-standing stalk may swing briefly — it resumes its spring from the collapse pose.\n" +
                "\n" +
                "Knob: jumpsToShatter lives on BambooOnThinIceConfig (shared asset).");
        }

        static void CreateCombo(Transform parent, string name, Vector3 icePos, float tiltZ,
                                float length, int segments,
                                BambooV3Settings settings, SlimeJumping.Physics.SurfaceMaterial surface,
                                BambooOnThinIceConfig linkConfig)
        {
            var comboRoot = new GameObject(name);
            comboRoot.transform.SetParent(parent, false);
            comboRoot.transform.position = icePos;

            var iceScale = new Vector3(3f, 0.6f, 0.5f);
            var iceGo = CreateIceTile(name + "_Ice", comboRoot.transform, icePos, iceScale);

            // Stalk base sits on the ice's top face.
            var basePos = icePos + new Vector3(0f, iceScale.y * 0.5f, 0f);
            var bambooGo = CreateBamboo(name + "_Bamboo", comboRoot.transform, basePos,
                tiltZ, length, segments, settings, surface);

            // The link lives on the ICE GameObject (it must keep polling while the
            // bamboo is deactivated during collapse).
            var link = iceGo.AddComponent<BambooOnThinIce>();
            var so = new SerializedObject(link);
            so.FindProperty("bamboo").objectReferenceValue = bambooGo.GetComponent<BambooPlatformV3>();
            so.FindProperty("ice").objectReferenceValue = iceGo.GetComponent<ThinIcePlatformV2>();
            // Wire the canonical config into the visible slot — Resolve() finds it via Resources anyway.
            so.FindProperty("configOverride").objectReferenceValue = linkConfig;
            so.ApplyModifiedProperties();
        }

        static GameObject CreateIceTile(string name, Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, true);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.layer = GroundLayer;

            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            var col = go.AddComponent<BoxCollider2D>();
            col.size = Vector2.one;

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = LoadIceMaterial();

            var plat = go.AddComponent<ThinIcePlatformV2>();
            var so = new SerializedObject(plat);
            so.FindProperty("visualRenderer").objectReferenceValue = mr;
            so.FindProperty("platformCollider").objectReferenceValue = col;
            so.ApplyModifiedProperties();

            return go;
        }

        static GameObject CreateBamboo(string name, Transform parent, Vector3 basePos, float tiltZ,
                                       float length, int segments, BambooV3Settings settings,
                                       SlimeJumping.Physics.SurfaceMaterial surface)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, true);
            go.transform.position = basePos;
            go.transform.rotation = Quaternion.Euler(0f, 0f, tiltZ);
            go.layer = GroundLayer;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;

            var bamboo = go.AddComponent<BambooPlatformV3>();
            var so = new SerializedObject(bamboo);
            so.FindProperty("settings").objectReferenceValue = settings;
            so.FindProperty("bambooSurfaceMaterial").objectReferenceValue = surface;
            so.FindProperty("bambooLength").floatValue = length;
            so.FindProperty("segmentCount").intValue = segments;
            so.ApplyModifiedProperties();

            bamboo.RebuildSegments();
            return go;
        }

        static Material LoadIceMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(IceMaterialPath);
            if (mat != null) return mat;
            var sh = Shader.Find("SlimeJumping/ThinIceV2");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            return new Material(sh) { name = "ThinIceV2_Fallback" };
        }

        static GameObject CreateBoxPlatform(string name, Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.layer = GroundLayer;

            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            go.AddComponent<BoxCollider2D>().size = Vector2.one;

            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            go.GetComponent<MeshRenderer>().sharedMaterial =
                new Material(sh) { color = new Color(0.45f, 0.45f, 0.5f) };
            return go;
        }
    }
}
