using UnityEditor;
using UnityEngine;
using SlimeJumping.Platforms;

namespace SlimeJumping.EditorTools
{
    // Spawns a TautLine test layout in the active scene: floor + a SpawnPad
    // (used to reproduce the "rope → bounce off another surface → rope" case
    // that broke RopeV3) + one TautLine. Independent of RopeTestSetup.
    public static class TautLineSetup
    {
        const string RopeMaterialPath = "Assets/Prebs/Platform/Materials/RopeMaterial.mat";
        const int GroundLayer = 7;

        [MenuItem("SlimeJumping/Mechanism/Setup TautLine Test")]
        public static void SetupTautLineMenu()
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
                "TautLine test scene:\n" +
                "  TautLine_Default — static parabolic baseline + per-rider spring dip\n" +
                "                     across the whole rope (smoothstep bell, peaks under slime).\n" +
                "\n" +
                "Verification (bug fix targets):\n" +
                "  1. Land on TautLine: should attach with no Attached/Airborne flicker.\n" +
                "  2. Charge → release: rope visibly deepens during charge, then returns to\n" +
                "     baseline smoothly (no snap-back oscillation that catches slime).\n" +
                "  3. SUSPECT repro: launch up → bounce off SpawnPad_Soft → fall back to rope.\n" +
                "     Attach should be clean (no oscillation). RopeV3 oscillated here.\n" +
                "  4. Jump from below: slime moving up should pass through the rope\n" +
                "     without attaching. Slime keeps full velocity.\n" +
                "\n" +
                "Tuning:\n" +
                "  jumpBoost (m/s) — full-charge bonus speed at launch.\n" +
                "  maxCap (m/s)    — hard cap on bonus speed.\n" +
                "  baselineSag, baseEquilibrium, chargeExtraDepth — visual sag knobs.\n" +
                "  springStiffness, attachedDampingRatio — bounce feel during attached.\n" +
                "  detachedDampingRatio — keep ≥1 so post-launch rope doesn't oscillate.");
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

            // SurfaceProbe Multi-mode is required: TautLine writes rb.position
            // absolutely each FU, and Multi mode's teleport branch zeroes
            // rb.linearVelocity so the manual write isn't fought by Unity's
            // contact integrator.
            var probeOverride = body.AddComponent<SlimeJumping.Physics.SurfaceProbeModeOverride>();
            probeOverride.mode = SlimeJumping.Physics.SurfaceProbeMode.Multi;

            // Polygon (trigger) ribbon — TautLine populates points each FU.
            var poly = body.AddComponent<PolygonCollider2D>();
            poly.isTrigger = true;
            // Seed with a 1-segment dummy so the inspector and serialization
            // doesn't complain before TautLine.Rebuild runs.
            poly.points = new Vector2[] {
                Vector2.zero, new Vector2(0.1f, 0f),
                new Vector2(0.1f, -0.05f), new Vector2(0f, -0.05f)
            };

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
            so.FindProperty("polygon").objectReferenceValue = poly;
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
