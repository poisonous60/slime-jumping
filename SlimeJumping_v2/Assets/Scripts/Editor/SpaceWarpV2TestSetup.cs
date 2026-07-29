using System.IO;
using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    /// <summary>
    /// Spawns a row of jump pads, each with its own SpaceWarp_BulbShatter variant material.
    /// </summary>
    public static class SpaceWarpV2TestSetup
    {
        const string ShaderName  = "SlimeJumping/SpaceWarp_BulbShatter";
        const string MatFolder   = "Assets/Art/Shader";

        // Quad y-scale stays CONSTANT (≈ 2 × bulb-world-radius); stretch widens the
        // anchor end via the shader's _AnchorScale, not the quad.
        const float QuadHeight      = 2.0f;
        const float JumpPadSpacing  = 6.0f;
        const int   QueueForeground = 3500;
        const float ZTestAlways     = 8f;

        struct Variant
        {
            public string label;
            public string matName;
            public int    shapeMode;   // 0=BulbOnNeck, 1=Teardrop, 2=Dumbbell, 3=Horn
            public int    crackMode;   // 0=Voronoi, 1=Radial wedges
        }

        // .mat names intentionally don't match the shape — reused so existing asset
        // references stay valid.
        static readonly Variant[] Variants =
        {
            new Variant { label = "Horn_Voronoi", matName = "Mat_SWPad_v2_BulbNeck_Voronoi", shapeMode = 3, crackMode = 0 },
            new Variant { label = "Horn_Radial",  matName = "Mat_SWPad_v2_BulbNeck_Radial",  shapeMode = 3, crackMode = 1 },
        };

        [MenuItem("SlimeJumping/Mechanism/Setup Space Warp v2 Comparison Row")]
        public static void SetupComparisonRow()
        {
            EnsureFolder(MatFolder);

            var root = new GameObject("JumpPad_SpaceWarp_v2_Comparison");
            Undo.RegisterCreatedObjectUndo(root, "Create Space Warp v2 Comparison");
            root.transform.position = GetSpawnPosition();

            for (int i = 0; i < Variants.Length; i++)
            {
                var v   = Variants[i];
                var mat = LoadOrCreateVariantMaterial(v);
                CreateOnePad(root.transform, v.label, mat, i * JumpPadSpacing);
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = root;

            Debug.Log("Space Warp v2 comparison row created. Each pad fires its own variant — " +
                      "land on each green pad to see Bulb-on-Neck, Inverted Teardrop, Dumbbell, " +
                      "and Bulb+Radial-Wedges back-to-back.");
        }

        // -------- per-pad construction --------

        static void CreateOnePad(Transform parent, string label, Material mat, float xOffset)
        {
            var padRoot = new GameObject($"Pad_{label}");
            padRoot.transform.SetParent(parent, worldPositionStays: false);
            padRoot.transform.localPosition = new Vector3(xOffset, 0f, 0f);

            // Pad body (green cube + 2D collider). Ground layer 7.
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "Pad";
            pad.transform.SetParent(padRoot.transform, worldPositionStays: false);
            pad.transform.localScale = new Vector3(2.5f, 0.4f, 1f);
            pad.layer = 7;
            Object.DestroyImmediate(pad.GetComponent<BoxCollider>());
            var col = pad.AddComponent<BoxCollider2D>();
            col.size = Vector2.one;
            pad.GetComponent<MeshRenderer>().sharedMaterial = MakeLitMaterial(new Color(0.25f, 0.9f, 0.45f));

            var jumpPad = pad.AddComponent<JumpPad>();

            var warpGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            warpGo.name = "SpaceWarp";
            Object.DestroyImmediate(warpGo.GetComponent<MeshCollider>());
            warpGo.transform.SetParent(padRoot.transform, worldPositionStays: false);
            warpGo.transform.localPosition = new Vector3(0f, 0.2f, -0.1f);
            warpGo.transform.localScale = new Vector3(QuadHeight, QuadHeight, 1f);
            warpGo.GetComponent<MeshRenderer>().sharedMaterial = mat;

            var warp = warpGo.AddComponent<SpaceWarpEffect>();

            // SerializedObject, not direct field writes — private fields + undo support.
            var soWarp = new SerializedObject(warp);
            soWarp.FindProperty("targetRenderer").objectReferenceValue = warpGo.GetComponent<MeshRenderer>();
            soWarp.FindProperty("baseWidth").floatValue   = QuadHeight;
            soWarp.FindProperty("tipWidth").floatValue    = QuadHeight;
            soWarp.FindProperty("flareScale").floatValue  = 1f;
            // tipLead ≈ bulb-world-radius, else the hemisphere cap clips the slime.
            soWarp.FindProperty("tipLead").floatValue     = 0.7f;
            soWarp.FindProperty("maxStretchLength").floatValue   = 9f;
            soWarp.FindProperty("maxStretchDuration").floatValue = 0.55f;
            soWarp.FindProperty("popDuration").floatValue        = 0.35f;
            soWarp.ApplyModifiedProperties();

            var soPad = new SerializedObject(jumpPad);
            soPad.FindProperty("warpEffect").objectReferenceValue = warp;
            soPad.ApplyModifiedProperties();

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(padRoot.transform, worldPositionStays: false);
            labelGo.transform.localPosition = new Vector3(0f, -1.2f, 0f);
            var tm = labelGo.AddComponent<TextMesh>();
            tm.text = label;
            tm.characterSize = 0.12f;
            tm.fontSize = 32;
            tm.alignment = TextAlignment.Center;
            tm.anchor = TextAnchor.UpperCenter;
            tm.color = Color.white;
        }

        // -------- material creation --------

        static Material LoadOrCreateVariantMaterial(Variant v)
        {
            string path = $"{MatFolder}/{v.matName}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            mat = new Material(Shader.Find(ShaderName));

            mat.SetColor("_BaseColor",         new Color(0.7f, 0.85f, 1.0f, 1.0f));
            mat.SetFloat("_TintStrength",      0.04f);

            mat.SetFloat("_ShapeMode",         v.shapeMode);
            mat.SetFloat("_CrackMode",         v.crackMode);

            // Radii are fractions of the half-quad height (shader v-axis is -1..1).
            switch (v.shapeMode)
            {
                case 0: // Bulb on Neck
                    mat.SetFloat("_NeckWidth",        0.10f);
                    mat.SetFloat("_NeckEnd",          0.55f);
                    mat.SetFloat("_BulbCenter",       0.82f);
                    mat.SetFloat("_BulbRadius",       0.55f);
                    mat.SetFloat("_BulbAspect",       1.0f);
                    break;
                case 1: // Inverted Teardrop
                    mat.SetFloat("_NeckWidth",        0.06f);
                    mat.SetFloat("_BulbCenter",       0.95f);
                    mat.SetFloat("_BulbRadius",       0.55f);
                    mat.SetFloat("_BulbAspect",       1.0f);
                    break;
                case 2: // Dumbbell
                    mat.SetFloat("_NeckWidth",        0.08f);
                    mat.SetFloat("_BulbCenter",       0.82f);
                    mat.SetFloat("_BulbRadius",       0.50f);
                    mat.SetFloat("_BulbAspect",       1.0f);
                    mat.SetFloat("_AnchorBulbRadius", 0.20f);
                    mat.SetFloat("_AnchorBulbAspect", 1.0f);
                    break;
                case 3: // Horn — flat anchor edge, flare to bulb cap
                    // _NeckWidth = FULL-STRETCH anchor half-width; runtime scales it
                    // down by _AnchorScale (0.3..1.0).
                    mat.SetFloat("_NeckWidth",        0.45f);
                    mat.SetFloat("_BulbCenter",       0.78f);  // where flare ends, hemisphere begins
                    mat.SetFloat("_BulbRadius",       0.65f);  // bulb v-fraction (round in world via _QuadAspect)
                    mat.SetFloat("_BulbAspect",       1.0f);
                    break;
            }

            // Domain noise + swirl on by default; without them the membrane reads as
            // a single-direction concave mirror, not warped space.
            mat.SetFloat("_PersistStrength",   0.06f);
            mat.SetFloat("_PersistScale",      4.0f);
            mat.SetVector("_PersistScroll",    new Vector4(0.06f, 0.10f, 0f, 0f));
            mat.SetFloat("_DomainWarp",        1.6f);
            mat.SetFloat("_SwirlAmount",       1.0f);
            mat.SetFloat("_SwirlAnim",         0.4f);

            // Pulse — secondary radial shockwave. Keep modest.
            mat.SetFloat("_PulseStrength",     0.10f);
            mat.SetFloat("_PulseRippleWidth",  0.2f);
            mat.SetFloat("_PulseMaxRadius",    1.0f);

            if (v.crackMode == 0) // Voronoi
            {
                mat.SetFloat("_ShardScale",        14f);
                mat.SetFloat("_ShardThrow",        0.22f);
                mat.SetFloat("_ShardStagger",      0.40f);
                mat.SetFloat("_CrackEdgeWidth",    0.020f);
                mat.SetFloat("_CrackBoost",        3.0f);
                mat.SetFloat("_PopFlashStrength",  1.6f);
                mat.SetFloat("_WedgeCount",        14f); // unused
            }
            else // Radial wedges
            {
                mat.SetFloat("_ShardScale",        14f);  // unused
                mat.SetFloat("_ShardThrow",        0.25f);
                mat.SetFloat("_ShardStagger",      0.45f);
                mat.SetFloat("_CrackEdgeWidth",    0.018f);
                mat.SetFloat("_CrackBoost",        3.5f);
                mat.SetFloat("_PopFlashStrength",  1.8f);
                mat.SetFloat("_WedgeCount",        16f);
            }

            mat.SetFloat("_ZTest",             ZTestAlways);
            mat.renderQueue                  = QueueForeground;

            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static Material MakeLitMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            return mat;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            var leaf   = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static Vector3 GetSpawnPosition()
        {
            var view = SceneView.lastActiveSceneView;
            if (view != null) return view.pivot;
            return Vector3.zero;
        }
    }
}
