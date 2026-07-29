using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    /// <summary>Test scene for WaterCurrentZone: floor, climbable wall, and a zone over the wall's upper half.</summary>
    public static class WaterCurrentZoneTestSetup
    {
        const int GroundLayer = 7;
        // Translucent refraction look; swap to M_WaterFlowingV2 for the saturated rim glow.
        static readonly string FlowMaterialPath = "Assets/Art/Materials/M_WaterFlowingRefraction.mat";
        // No stick / no friction / no crawl — the slime slips off the wall while inside.
        static readonly string WaterSurfaceMaterialPath = "Assets/Settings/SurfaceMaterials/Water.asset";

        [MenuItem("SlimeJumping/Mechanism/Setup WaterCurrent Zone Test")]
        public static void Setup()
        {
            var root = new GameObject("WaterCurrentZone_Test");
            Undo.RegisterCreatedObjectUndo(root, "Create WaterCurrentZone Test");

            CreatePlatform(root.transform, "Floor",
                new Vector3(0f, -6f, 0f), new Vector3(30f, 1f, 1f));
            CreatePlatform(root.transform, "LaunchPad_Left",
                new Vector3(-6f, -3f, 0f), new Vector3(3f, 1f, 1f));

            CreatePlatform(root.transform, "WallToClimb",
                new Vector3(6f, 0f, 0f), new Vector3(1f, 10f, 1f));

            CreateWaterCurrentZone(root.transform, "WaterCurrent_UpperHalf",
                position: new Vector3(6f, 1f, 0f),
                halfSize:  new Vector2(1.2f, 3.5f));

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = root;
            Debug.Log("[WaterCurrentZoneTest] Wall at x=6 with water current covering its " +
                      "upper half. Drop a slime prefab in and charge into the wall to test " +
                      "the downward push.");
        }

        static GameObject CreatePlatform(Transform parent, string name,
            Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.layer = GroundLayer;
            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            go.AddComponent<BoxCollider2D>().size = Vector2.one;
            go.GetComponent<MeshRenderer>().sharedMaterial = MakeUnlitMaterial(new Color(0.5f, 0.5f, 0.5f));
            return go;
        }

        /// <summary>Zone with a 5-point vertical centerline; the collider is synced from the ribbon silhouette.</summary>
        static GameObject CreateWaterCurrentZone(Transform parent, string name,
            Vector3 position, Vector2 halfSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;

            // RequireComponent auto-adds PolygonCollider2D + Reset() flips isTrigger on.
            var zone = go.AddComponent<WaterCurrentZone>();

            // Runtime-built fallback when the asset is missing, else the zone renders magenta.
            var mat = AssetDatabase.LoadAssetAtPath<Material>(FlowMaterialPath);
            if (mat == null)
            {
                var sh = Shader.Find("SlimeJumping/Water_FlowingRefraction");
                if (sh != null) mat = new Material(sh) { name = "M_WaterFlowingRefraction_Runtime" };
            }

            // `migrated` is pre-set so OnValidate skips deriving a centerline from the still-default
            // PolygonCollider2D path.
            var waterMat = AssetDatabase.LoadAssetAtPath<SlimeJumping.Physics.SurfaceMaterial>(
                WaterSurfaceMaterialPath);
            var so = new SerializedObject(zone);
            if (mat != null)
                so.FindProperty("flowMaterial").objectReferenceValue = mat;
            if (waterMat != null)
                so.FindProperty("waterSurfaceMaterial").objectReferenceValue = waterMat;
            so.FindProperty("flowDirection").vector2Value = new Vector2(0f, -1f);
            so.FindProperty("ribbonWidth").floatValue = halfSize.x * 2f;
            so.FindProperty("migrated").boolValue = true;

            var centerlineProp = so.FindProperty("centerlineLocal");
            centerlineProp.arraySize = 5;
            for (int i = 0; i < 5; i++)
            {
                float y = halfSize.y * (1f - 2f * i / 4f);  // +halfY → -halfY
                centerlineProp.GetArrayElementAtIndex(i).vector2Value = new Vector2(0f, y);
            }
            so.ApplyModifiedProperties();

            // Synchronous rebuild: OnEnable ran before the centerline existed and OnValidate's
            // delayCall fires only next editor tick, leaving mesh + collider stale.
            zone.Rebuild();
            EditorUtility.SetDirty(zone);

            return go;
        }

        static Material MakeUnlitMaterial(Color color)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Standard");
            var mat = new Material(sh);
            mat.color = color;
            return mat;
        }
    }
}
