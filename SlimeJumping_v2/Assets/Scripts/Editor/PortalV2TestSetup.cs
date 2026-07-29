using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    /// <summary>Builds a Portal_v2 verification scene: a vertical fling shaft plus a fast-cycle floor pair.</summary>
    public static class PortalV2TestSetup
    {
        [MenuItem("SlimeJumping/Mechanism/Setup Portal_v2 Fling Test")]
        public static void SetupPortalV2FlingTest()
        {
            var root = new GameObject("PortalV2FlingTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Portal v2 Fling Test");

            // --- Vertical shaft (two walls + floor) ---
            float shaftX = -12f;
            float shaftWidth = 4f;
            float wallHeight = 14f;
            float wallY = 6f;

            CreatePlatform("ShaftLeftWall", root.transform,
                new Vector3(shaftX - shaftWidth / 2f - 0.5f, wallY, 0f),
                new Vector3(1f, wallHeight, 1f));

            CreatePlatform("ShaftRightWall", root.transform,
                new Vector3(shaftX + shaftWidth / 2f + 0.5f, wallY, 0f),
                new Vector3(1f, wallHeight, 1f));

            CreatePlatform("ShaftFloor", root.transform,
                new Vector3(shaftX, wallY - wallHeight / 2f - 0.5f, 0f),
                new Vector3(shaftWidth + 2f, 1f, 1f));

            float portalY_top = wallY + wallHeight / 2f - 2f;
            float portalY_bottom = wallY - wallHeight / 2f + 2f;

            // --- Portal Blue (top of shaft, left wall, OutNormal = +X) ---
            var portalBlueGo = BuildPortalGo(
                "Portal_v2_Blue",
                root.transform,
                new Vector3(shaftX - shaftWidth / 2f + 0.1f, portalY_top, 0f),
                Quaternion.Euler(0f, 0f, 0f),
                new Color(0.2f, 0.4f, 1f, 1f));

            // --- Portal Orange (bottom of shaft, right wall, OutNormal = -X) ---
            var portalOrangeGo = BuildPortalGo(
                "Portal_v2_Orange",
                root.transform,
                new Vector3(shaftX + shaftWidth / 2f - 0.1f, portalY_bottom, 0f),
                Quaternion.Euler(0f, 0f, 180f),
                new Color(1f, 0.5f, 0.1f, 1f));

            var bluePortal = portalBlueGo.GetComponent<Portal_v2>();
            var orangePortal = portalOrangeGo.GetComponent<Portal_v2>();

            // Wire pair on one side; Portal_v2.Awake() will auto-mirror to the other.
            var soBlue = new SerializedObject(bluePortal);
            soBlue.FindProperty("pair").objectReferenceValue = orangePortal;
            soBlue.ApplyModifiedProperties();

            // --- Third pair: custom colors + faster cycle ---
            float pairBX = shaftX + 18f;
            var portalGreenGo = BuildPortalGo(
                "Portal_v2_Green",
                root.transform,
                new Vector3(pairBX, wallY + 1f, 0f),
                Quaternion.Euler(0f, 0f, 90f),  // OutNormal = +Y (slime flings up)
                new Color(0.3f, 1f, 0.4f, 1f));

            var portalMagentaGo = BuildPortalGo(
                "Portal_v2_Magenta",
                root.transform,
                new Vector3(pairBX + 6f, wallY + 1f, 0f),
                Quaternion.Euler(0f, 0f, 90f),
                new Color(1f, 0.3f, 0.8f, 1f));

            var greenPortal = portalGreenGo.GetComponent<Portal_v2>();
            var magentaPortal = portalMagentaGo.GetComponent<Portal_v2>();

            var soGreen = new SerializedObject(greenPortal);
            soGreen.FindProperty("pair").objectReferenceValue = magentaPortal;
            soGreen.FindProperty("consumeDuration").floatValue = 0.2f;
            soGreen.FindProperty("respawnDelay").floatValue = 1.0f;
            soGreen.FindProperty("respawnDuration").floatValue = 0.25f;
            soGreen.ApplyModifiedProperties();

            var soMagenta = new SerializedObject(magentaPortal);
            soMagenta.FindProperty("consumeDuration").floatValue = 0.2f;
            soMagenta.FindProperty("respawnDelay").floatValue = 1.0f;
            soMagenta.FindProperty("respawnDuration").floatValue = 0.25f;
            soMagenta.ApplyModifiedProperties();

            // Floor under the green/magenta horizontal pair so the slime can stand and walk in.
            CreatePlatform("GreenMagentaFloor", root.transform,
                new Vector3(pairBX + 3f, wallY - 1f, 0f),
                new Vector3(10f, 1f, 1f));

            // --- Access ramp into the shaft area ---
            CreatePlatform("Ramp", root.transform,
                new Vector3(shaftX + 4f, wallY + wallHeight / 2f + 1f, 0f),
                new Vector3(6f, 0.5f, 1f));

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("Portal_v2 Fling Test setup complete!\n" +
                      "  • Blue↔Orange shaft loop (default cycle): drop slime above the shaft.\n" +
                      "  • Green↔Magenta floor pair (fast cycle): walk slime into a portal to see it disappear/reappear faster.");
        }

        // --- Builders ---

        static GameObject BuildPortalGo(string name, Transform parent, Vector3 pos, Quaternion rot, Color tint)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.SetPositionAndRotation(pos, rot);

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(0.3f, 1.5f);

            // Visual child: a Quad (matches PortalTestSetup convention so MaterialPropertyBlock path is exercised).
            var vis = GameObject.CreatePrimitive(PrimitiveType.Quad);
            vis.name = "Visual";
            vis.transform.SetParent(go.transform, worldPositionStays: false);
            vis.transform.localPosition = Vector3.zero;
            vis.transform.localRotation = Quaternion.identity;
            vis.transform.localScale = new Vector3(0.2f, 1.5f, 1f);
            Object.DestroyImmediate(vis.GetComponent<MeshCollider>());
            vis.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(tint);

            var portal = go.AddComponent<Portal_v2>();

            // Wire visual + renderer + color via SerializedObject so the inspector reflects the values.
            var so = new SerializedObject(portal);
            so.FindProperty("visual").objectReferenceValue = vis.transform;
            so.FindProperty("visualRenderer").objectReferenceValue = vis.GetComponent<MeshRenderer>();
            so.FindProperty("portalColor").colorValue = tint;
            so.ApplyModifiedProperties();

            return go;
        }

        static GameObject CreatePlatform(string name, Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.layer = 7; // ground layer

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
