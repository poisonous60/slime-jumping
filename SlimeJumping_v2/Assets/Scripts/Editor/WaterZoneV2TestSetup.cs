using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    public static class WaterZoneV2TestSetup
    {
        const int GroundLayer = 7;

        [MenuItem("SlimeJumping/Mechanism/Setup WaterZone v2 Test")]
        public static void Setup()
        {
            var root = new GameObject("WaterZoneV2_Test");
            Undo.RegisterCreatedObjectUndo(root, "Create WaterZone v2 Test");

            CreatePlatform(root.transform, "Floor",
                new Vector3(0f, -6f, 0f), new Vector3(30f, 1f, 1f));
            CreatePlatform(root.transform, "LaunchPad_Left",
                new Vector3(-6f, -2f, 0f), new Vector3(3f, 1f, 1f));
            CreatePlatform(root.transform, "LaunchPad_Right",
                new Vector3(6f, -2f, 0f), new Vector3(3f, 1f, 1f));

            CreateWaterZoneV2(root.transform, "Water_Circle",
                position: new Vector3(0f, 2f, 0f),
                radius: 2.5f,
                pointCount: 32);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = root;
            Debug.Log("[WaterZoneV2Test] Floating circular water created at (0, 2). " +
                      "Charge the slime into it to see the surface deform.");
        }

        static GameObject CreateWaterZoneV2(Transform parent, string name,
            Vector3 position, float radius, int pointCount)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;

            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MakeWaterMaterial(new Color(0.2f, 0.6f, 1f, 0.6f));
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var col = go.AddComponent<PolygonCollider2D>();
            col.isTrigger = true;
            // points will be synced from restPointsLocal after GenerateCirclePoints() below.

            var lineGo = new GameObject("Outline");
            lineGo.transform.SetParent(go.transform, false);
            var line = lineGo.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.startWidth = line.endWidth = 0.08f;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = MakeWaterMaterial(new Color(0.3f, 0.8f, 1f, 1f));

            var wz = go.AddComponent<WaterZone_v2>();
            var so = new SerializedObject(wz);
            so.FindProperty("pointCount").intValue = pointCount;
            so.FindProperty("radius").floatValue = radius;
            so.FindProperty("outlineLine").objectReferenceValue = line;
            so.ApplyModifiedProperties();

            // Pre-populate outline points so the user sees them in the scene view
            // before entering Play mode.
            wz.GenerateCirclePoints();
            wz.SyncColliderToRestShape();
            EditorUtility.SetDirty(wz);

            return go;
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
            go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.5f, 0.5f, 0.5f));
            return go;
        }

        static Material MakeMaterial(Color color)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Standard");
            var mat = new Material(sh);
            mat.color = color;
            return mat;
        }

        /// <summary>
        /// Sprites/Default: Cull Off (so winding doesn't matter), supports vertex colors,
        /// premultiplied alpha, no lighting required. Works in built-in and URP 2D Renderer.
        /// </summary>
        static Material MakeWaterMaterial(Color color)
        {
            var sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Standard");
            var mat = new Material(sh);
            mat.color = color;
            return mat;
        }
    }
}
