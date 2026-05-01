using System.IO;
using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    /// <summary>
    /// V2 stretch-warp comparison rig. Spawns four jump pads side-by-side, each
    /// driving a different SpaceWarp_BulbShatter material so shape (Bulb-on-Neck /
    /// Inverted Teardrop / Dumbbell) and crack (Voronoi / Radial Wedges) variants
    /// can be tried back-to-back during play. Materials auto-create on first run.
    /// </summary>
    public static class SpaceWarpV2TestSetup
    {
        const string ShaderName  = "SlimeJumping/SpaceWarp_BulbShatter";
        const string MatFolder   = "Assets/Art/Shader";

        // Quad y-scale stays CONSTANT — the bulb radius is sized for the slime
        // and shouldn't grow with stretch (slime size is constant). The BOTTOM
        // (anchor) widens with stretch via the shader's _AnchorScale instead,
        // animated by SpaceWarpEffect.cs from 0.3 → 1.0 across stretchT.
        // QuadHeight ≈ 2 × bulb-world-radius so the bulb fills the quad cross-section.
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

        // Two pads — both Horn shape, comparing crack modes (Voronoi vs Radial
        // wedges). Existing .mat names are reused so prior asset references stay
        // valid; the unused Teardrop / Dumbbell mat files are now leftover but
        // harmless (no pad references them).
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

            // Warp quad — constant Y-scale (= QuadHeight). The bulb stays sized
            // to the slime; the bottom widens via shader _AnchorScale instead.
            var warpGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            warpGo.name = "SpaceWarp";
            Object.DestroyImmediate(warpGo.GetComponent<MeshCollider>());
            warpGo.transform.SetParent(padRoot.transform, worldPositionStays: false);
            warpGo.transform.localPosition = new Vector3(0f, 0.2f, -0.1f);
            warpGo.transform.localScale = new Vector3(QuadHeight, QuadHeight, 1f);
            warpGo.GetComponent<MeshRenderer>().sharedMaterial = mat;

            var warp = warpGo.AddComponent<SpaceWarpEffect>();

            // Wire references through SerializedObject so they survive variants/undo.
            var soWarp = new SerializedObject(warp);
            soWarp.FindProperty("targetRenderer").objectReferenceValue = warpGo.GetComponent<MeshRenderer>();
            soWarp.FindProperty("baseWidth").floatValue   = QuadHeight;
            soWarp.FindProperty("tipWidth").floatValue    = QuadHeight;
            soWarp.FindProperty("flareScale").floatValue  = 1f;
            // tipLead — distance the quad tip extends past the slime. Sized so the
            // hemispherical cap lands cleanly past the slime: ~bulb-world-radius.
            soWarp.FindProperty("tipLead").floatValue     = 0.7f;
            // Generous max stretch so the comparison spans a meaningful arc.
            soWarp.FindProperty("maxStretchLength").floatValue   = 9f;
            soWarp.FindProperty("maxStretchDuration").floatValue = 0.55f;
            soWarp.FindProperty("popDuration").floatValue        = 0.35f;
            soWarp.ApplyModifiedProperties();

            var soPad = new SerializedObject(jumpPad);
            soPad.FindProperty("warpEffect").objectReferenceValue = warp;
            soPad.ApplyModifiedProperties();

            // World-space label so each pad is identifiable in the scene view.
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

            // Cyan tint, kept consistent across variants.
            mat.SetColor("_BaseColor",         new Color(0.7f, 0.85f, 1.0f, 1.0f));
            mat.SetFloat("_TintStrength",      0.04f);

            mat.SetFloat("_ShapeMode",         v.shapeMode);
            mat.SetFloat("_CrackMode",         v.crackMode);

            // Shape — defaults tuned per mode. v-axis is normalized (-1..1 with
            // mid at 0), so radii here are in fractions of the half-quad height.
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
                    // _NeckWidth is the FULL-STRETCH anchor half-width fraction.
                    // Driver scales it down by _AnchorScale (0.3..1.0) at runtime
                    // so early stretch shows a thinner back, full stretch a wider one.
                    mat.SetFloat("_NeckWidth",        0.45f);
                    mat.SetFloat("_BulbCenter",       0.78f);  // where flare ends, hemisphere begins
                    mat.SetFloat("_BulbRadius",       0.65f);  // bulb v-fraction (round in world via _QuadAspect)
                    mat.SetFloat("_BulbAspect",       1.0f);
                    break;
            }

            // Persistent warp — domain noise + swirl on by default so the
            // membrane reads as "warped space" rather than a single-direction
            // concave mirror. Tune down per-mat in the inspector if too busy.
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

            // Crack — defaults differ per crack mode.
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
