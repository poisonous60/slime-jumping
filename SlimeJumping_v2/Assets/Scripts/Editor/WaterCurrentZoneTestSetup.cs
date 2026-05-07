using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    /// <summary>
    /// Builds a minimal scene to verify WaterCurrentZone end-to-end:
    ///   - Floor + a tall vertical wall the slime can charge into.
    ///   - A WaterCurrentZone whose PolygonCollider2D spans the upper half of
    ///     the wall surface plus a small slab of adjacent air, so the trigger
    ///     fires the moment the slime sticks.
    ///   - flowDirection set to local -Y, matching the wall surface tangent.
    /// User drops a slime prefab into the scene to test.
    /// </summary>
    public static class WaterCurrentZoneTestSetup
    {
        const int GroundLayer = 7;
        // Refraction-based material — matches WaterfallImpactVFX_Test's translucent
        // calm look (sees the wall through the water with a subtle blue tint) instead
        // of Water_FlowingV2's saturated rim glow. Designer can swap the zone's
        // flowMaterial slot back to M_WaterFlowingV2 if they want the bright version.
        static readonly string FlowMaterialPath = "Assets/Art/Materials/M_WaterFlowingRefraction.mat";
        // Water surface material — no stick / no friction / no crawl. Wired into the test
        // zone's waterSurfaceMaterial slot so the slime slips off the wall while inside.
        static readonly string WaterSurfaceMaterialPath = "Assets/Settings/SurfaceMaterials/Water.asset";

        [MenuItem("SlimeJumping/Mechanism/Setup WaterCurrent Zone Test")]
        public static void Setup()
        {
            var root = new GameObject("WaterCurrentZone_Test");
            Undo.RegisterCreatedObjectUndo(root, "Create WaterCurrentZone Test");

            // Ground + launch pads
            CreatePlatform(root.transform, "Floor",
                new Vector3(0f, -6f, 0f), new Vector3(30f, 1f, 1f));
            CreatePlatform(root.transform, "LaunchPad_Left",
                new Vector3(-6f, -3f, 0f), new Vector3(3f, 1f, 1f));

            // Vertical wall to climb / be pushed off
            CreatePlatform(root.transform, "WallToClimb",
                new Vector3(6f, 0f, 0f), new Vector3(1f, 10f, 1f));

            // Water current zone overlapping the upper half of the wall
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

        /// <summary>
        /// Creates a WaterCurrentZone with a 5-point vertical centerline spanning the requested
        /// height and a ribbonWidth = 2 * halfSize.x. SyncColliderToRibbonOutline will populate
        /// the PolygonCollider2D from the ribbon silhouette so the trigger area auto-matches the
        /// visual mesh.
        /// </summary>
        static GameObject CreateWaterCurrentZone(Transform parent, string name,
            Vector3 position, Vector2 halfSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;

            // RequireComponent auto-adds PolygonCollider2D + Reset() flips isTrigger on.
            var zone = go.AddComponent<WaterCurrentZone>();

            // Wire the shared flow material asset. If missing — e.g. user moved/deleted it —
            // fall back to a runtime-built material so the test scene at least shows SOMETHING
            // instead of a magenta placeholder.
            var mat = AssetDatabase.LoadAssetAtPath<Material>(FlowMaterialPath);
            if (mat == null)
            {
                var sh = Shader.Find("SlimeJumping/Water_FlowingRefraction");
                if (sh != null) mat = new Material(sh) { name = "M_WaterFlowingRefraction_Runtime" };
            }

            // Author centerline + ribbonWidth + flowDirection + flowMaterial + waterSurfaceMaterial
            // via SerializedObject, then mark `migrated` so OnValidate doesn't try to derive a
            // centerline from the (currently-default) PolygonCollider2D path.
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

            // Force a synchronous rebuild: OnEnable already ran with the unauthored centerline,
            // so the mesh + collider are stale. The OnValidate delayCall would only fire next
            // editor tick.
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
