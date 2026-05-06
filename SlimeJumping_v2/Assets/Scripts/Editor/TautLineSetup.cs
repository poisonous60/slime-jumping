using UnityEditor;
using UnityEngine;
using SlimeJumping.Platforms;

namespace SlimeJumping.EditorTools
{
    // Spawns a SampleScene-style TautLine test layout: floor, a SpawnPad
    // (used to reproduce the "rope → bounce off another surface → rope"
    // SUSPECT case that broke RopeV3), and one TautLine instance.
    // Independent of RopeTestSetup — duplicates a few small helpers
    // intentionally so RopeTestSetup.cs stays untouched.
    public static class TautLineSetup
    {
        const string RopeMaterialPath = "Assets/Prebs/Platform/Materials/RopeMaterial.mat";
        const int GroundLayer = 7;

        [MenuItem("SlimeJumping/Mechanism/Setup TautLine Test")]
        public static void SetupTautLine()
        {
            var root = new GameObject("TautLineTest");
            Undo.RegisterCreatedObjectUndo(root, "Create TautLine Test");

            CreatePlatform("Floor", root.transform,
                new Vector3(0f, -6f, 0f), new Vector3(60f, 1f, 1f));
            CreatePlatform("SpawnPad_Soft", root.transform,
                new Vector3(-6f, -2f, 0f), new Vector3(3f, 1f, 1f));

            const float anchorY = 3f;
            const float halfSpan = 4f;

            BuildTautLine(root.transform, "TautLine_Default",
                new Vector3(-halfSpan, anchorY, 0f),
                new Vector3(halfSpan, anchorY, 0f));

            Finish(root,
                "TautLine test:\n" +
                "  TautLine_Default — static parabolic baseline + local spring dip.\n" +
                "\n" +
                "How to evaluate the bug fix (SUSPECT repro):\n" +
                "  1. Drop slime onto TautLine. Should attach with no Attached/Airborne flicker.\n" +
                "  2. Charge-launch upward → land on SpawnPad_Soft → bounce/walk → fall back\n" +
                "     onto TautLine. Should attach cleanly. RopeV3 oscillated here; TautLine\n" +
                "     should not.\n" +
                "  3. Tune baselineSag in inspector (0 = perfectly straight power-line look).");
        }

        static GameObject BuildTautLine(Transform parent, string name,
            Vector3 anchorAPos, Vector3 anchorBPos)
        {
            var rootGo = new GameObject(name);
            rootGo.transform.SetParent(parent, false);

            var anchorA = new GameObject("AnchorA");
            anchorA.transform.SetParent(rootGo.transform, false);
            anchorA.transform.position = anchorAPos;

            var anchorB = new GameObject("AnchorB");
            anchorB.transform.SetParent(rootGo.transform, false);
            anchorB.transform.position = anchorBPos;

            CreateAnchorMarker(anchorA.transform);
            CreateAnchorMarker(anchorB.transform);

            var body = new GameObject("Body");
            body.transform.SetParent(rootGo.transform, false);
            body.layer = GroundLayer;

            var probeOverride = body.AddComponent<SlimeJumping.Physics.SurfaceProbeModeOverride>();
            probeOverride.mode = SlimeJumping.Physics.SurfaceProbeMode.Multi;

            var edge = body.AddComponent<EdgeCollider2D>();
            edge.edgeRadius = 0f;

            var lineGo = new GameObject("LineRenderer");
            lineGo.transform.SetParent(body.transform, false);
            var line = lineGo.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.startWidth = line.endWidth = 0.08f;
            line.textureMode = LineTextureMode.Tile;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(RopeMaterialPath);
            if (mat != null) line.sharedMaterial = mat;

            var taut = body.AddComponent<TautLine>();
            var so = new SerializedObject(taut);
            so.FindProperty("anchorA").objectReferenceValue = anchorA.transform;
            so.FindProperty("anchorB").objectReferenceValue = anchorB.transform;
            so.FindProperty("edge").objectReferenceValue = edge;
            so.FindProperty("line").objectReferenceValue = line;
            so.ApplyModifiedProperties();

            taut.Rebuild();
            return rootGo;
        }

        static GameObject CreatePlatform(string name, Transform parent, Vector3 pos, Vector3 scale)
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

        static GameObject CreateAnchorMarker(Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Marker";
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);
            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.9f, 0.3f, 0.3f));
            return go;
        }

        static Material MakeMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            return mat;
        }

        static void Finish(GameObject root, string message)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = root;
            Debug.Log("[TautLineSetup] " + message);
        }
    }
}
