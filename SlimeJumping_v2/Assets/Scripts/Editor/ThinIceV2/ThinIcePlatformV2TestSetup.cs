using UnityEditor;
using UnityEngine;
using SlimeJumping.Platforms.ThinIceV2;

namespace SlimeJumping.Platforms.ThinIceV2.EditorOnly
{
    /// One-click ThinIceV2 test scene generator; shared assets all come from ThinIceV2Config.
    public static class ThinIcePlatformV2TestSetup
    {
        const int    GroundLayer = 7;
        const string MaterialPath = "Assets/Shaders/ThinIceV2.mat";
        const string ConfigPath   = "Assets/Resources/ThinIceV2Config.asset";

        [MenuItem("SlimeJumping/Platform/Setup ThinIcePlatformV2 Test")]
        public static void Setup()
        {
            // Config + VFX prefabs must exist before any tile is added.
            if (AssetDatabase.LoadAssetAtPath<ThinIceV2Config>(ConfigPath) == null)
            {
                ThinIceV2ConfigGenerator.GenerateMenu();
            }
            var config = AssetDatabase.LoadAssetAtPath<ThinIceV2Config>(ConfigPath);

            var root = new GameObject("ThinIcePlatformV2Test");
            Undo.RegisterCreatedObjectUndo(root, "Create ThinIcePlatformV2 Test");

            // --- Support geometry ---
            CreateBoxPlatform("Floor", root.transform,
                new Vector3(0f, -4f, 0f), new Vector3(28f, 1f, 1f));
            CreateBoxPlatform("CatchFloor", root.transform,
                new Vector3(0f, -16f, 0f), new Vector3(36f, 1f, 1f));
            CreateBoxPlatform("LeftWall", root.transform,
                new Vector3(-13f, 1f, 0f), new Vector3(1f, 11f, 1f));
            CreateBoxPlatform("RightWall", root.transform,
                new Vector3(13f, 1f, 0f), new Vector3(1f, 11f, 1f));

            // --- Respawn group container ---
            var zoneGo = new GameObject("ThinIceV2_RespawnZone");
            zoneGo.transform.SetParent(root.transform, false);
            zoneGo.transform.position = new Vector3(0f, 0f, 0f);
            var zoneBox = zoneGo.AddComponent<BoxCollider2D>();
            zoneGo.AddComponent<Rigidbody2D>();
            zoneGo.AddComponent<ThinIceRespawnZone>();
            zoneBox.isTrigger = true;
            zoneBox.size = new Vector2(20f, 7f);

            // --- In-zone variants (children of zone for auto-discovery) ---
            CreateThinIcePlatform("ThinIce_Default", zoneGo.transform,
                new Vector3(-7f, 0f, 0f), new Vector3(3f, 0.6f, 0.5f),
                crackingDuration: 2.0f);
            CreateThinIcePlatform("ThinIce_Fast", zoneGo.transform,
                new Vector3(-2.5f, 0f, 0f), new Vector3(3f, 0.6f, 0.5f),
                crackingDuration: 0.8f);
            CreateThinIcePlatform("ThinIce_Large", zoneGo.transform,
                new Vector3(2.5f, 0f, 0f), new Vector3(4.5f, 0.7f, 0.5f),
                crackingDuration: 2.5f);
            CreateThinIcePlatform("ThinIce_Upper", zoneGo.transform,
                new Vector3(0f, 3.5f, 0f), new Vector3(3.5f, 0.6f, 0.5f),
                crackingDuration: 1.5f);

            // --- Orphan (NOT in-zone): permanent one-shot ---
            CreateThinIcePlatform("ThinIce_Orphan", root.transform,
                new Vector3(9f, 0f, 0f), new Vector3(3f, 0.6f, 0.5f),
                crackingDuration: 2.0f);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = root;
            int shardCount = (config != null && config.shardSet != null) ? config.shardSet.shards.Count : 0;
            string configName = config != null ? config.name : "missing";
            Debug.Log("ThinIcePlatformV2 test setup complete.\n" +
                      $"- Shared config: {configName} ({shardCount} shards).\n" +
                      "- 4 in-zone variants regenerate when slime exits the zone box.\n" +
                      "- 'ThinIce_Orphan' is outside the zone and shatters permanently.");
        }

        static GameObject CreateThinIcePlatform(string name, Transform parent,
                                                Vector3 pos, Vector3 scale, float crackingDuration)
        {
            // Cube, not Quad: side faces give the Voronoi shader UV coverage from any camera angle.
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.parent = parent;
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.layer = GroundLayer;

            // Cube primitive ships with a 3D BoxCollider; swap it for a 2D one.
            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            var col = go.AddComponent<BoxCollider2D>();
            col.size = Vector2.one;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = LoadIceMaterial();

            var plat = go.AddComponent<ThinIcePlatformV2>();
            ConfigureThinIcePlatformV2(plat, mr, col, crackingDuration);

            return go;
        }

        static void ConfigureThinIcePlatformV2(ThinIcePlatformV2 plat, MeshRenderer mr, Collider2D col,
                                               float crackingDuration)
        {
            var so = new SerializedObject(plat);
            so.FindProperty("visualRenderer").objectReferenceValue   = mr;
            so.FindProperty("platformCollider").objectReferenceValue = col;

            // Per-instance crackingDuration only applies with overrideBehavior on; the rest
            // stays on ThinIceV2Config defaults.
            so.FindProperty("overrideBehavior").boolValue  = true;
            so.FindProperty("crackingDuration").floatValue = crackingDuration;
            so.FindProperty("regenDuration").floatValue    = 0.6f;

            so.ApplyModifiedProperties();
        }

        static Material LoadIceMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat != null) return mat;
            var sh = Shader.Find("SlimeJumping/ThinIceV2");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            return new Material(sh) { name = "ThinIceV2_Fallback" };
        }

        static GameObject CreateBoxPlatform(string name, Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.parent = parent;
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
