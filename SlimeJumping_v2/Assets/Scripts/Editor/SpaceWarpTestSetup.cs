using System.IO;
using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    /// <summary>
    /// Spawns test objects for SlimeJumping/SpaceWarp_Distortion:
    ///   • "Setup Space Warp Zone"     — persistent warp anomaly area (always on).
    ///   • "Setup Space Warp Jump Pad" — JumpPad with a one-shot shockwave child.
    /// Both create reusable .mat assets next to the shader on first run.
    /// </summary>
    public static class SpaceWarpTestSetup
    {
        const string ShaderName       = "SlimeJumping/SpaceWarp_Distortion";
        const string MatFolder        = "Assets/Art/Shader";
        const string MatZonePath      = MatFolder + "/Mat_SpaceWarpZone.mat";
        const string MatZoneBgPath    = MatFolder + "/Mat_SpaceWarpZone_Background.mat";
        const string MatJumpPadPath   = MatFolder + "/Mat_SpaceWarpJumpPad.mat";

        // Foreground = covers gameplay (only the warped image shows). Background = drawn behind
        // gameplay, useful as an ambient backdrop. Material settings drive the difference:
        //   queue 3500 + ZTest Always (8) → foreground
        //   queue 3000 + ZTest LEqual (4) → background
        const int   QueueForeground = 3500;
        const int   QueueBackground = 3000;
        const float ZTestAlways     = 8f;
        const float ZTestLEqual     = 4f;

        [MenuItem("SlimeJumping/Mechanism/Setup Space Warp Zone (Foreground)")]
        public static void SetupZoneForeground()
        {
            CreateZone(LoadOrCreateZoneMaterial(), "SpaceWarpZone_Test",
                "Foreground zone — quad covers everything inside it; only the warped image shows.");
        }

        [MenuItem("SlimeJumping/Mechanism/Setup Space Warp Zone (Background)")]
        public static void SetupZoneBackground()
        {
            CreateZone(LoadOrCreateZoneBackgroundMaterial(), "SpaceWarpZone_Background_Test",
                "Background zone — gameplay objects (slime, platforms) draw on top; the warp acts as a backdrop. " +
                "Place behind gameplay (z larger than gameplay z).");
        }

        static void CreateZone(Material mat, string goName, string log)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = goName;
            Object.DestroyImmediate(go.GetComponent<MeshCollider>());
            go.transform.localScale = new Vector3(6f, 6f, 1f);
            go.transform.position = GetSpawnPosition();
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;

            Undo.RegisterCreatedObjectUndo(go, "Create Space Warp Zone");
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = go;
            Debug.Log(log);
        }

        [MenuItem("SlimeJumping/Mechanism/Setup Space Warp Jump Pad")]
        public static void SetupJumpPad()
        {
            var mat = LoadOrCreateJumpPadMaterial();

            var root = new GameObject("JumpPad_SpaceWarp_Test");
            Undo.RegisterCreatedObjectUndo(root, "Create Space Warp Jump Pad");
            root.transform.position = GetSpawnPosition();

            // Pad body — visible cube + 2D collider so the slime can land on it.
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "Pad";
            pad.transform.SetParent(root.transform, worldPositionStays: false);
            pad.transform.localScale = new Vector3(2.5f, 0.4f, 1f);
            pad.layer = 7; // ground layer
            Object.DestroyImmediate(pad.GetComponent<BoxCollider>());
            var col = pad.AddComponent<BoxCollider2D>();
            col.size = Vector2.one;
            pad.GetComponent<MeshRenderer>().sharedMaterial = MakeLitMaterial(new Color(0.25f, 0.9f, 0.45f));

            var jumpPad = pad.AddComponent<JumpPad>();

            // Warp visual — quad above the pad. Slightly toward camera so it sits in front of scenery.
            var warpGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            warpGo.name = "SpaceWarp";
            Object.DestroyImmediate(warpGo.GetComponent<MeshCollider>());
            warpGo.transform.SetParent(root.transform, worldPositionStays: false);
            warpGo.transform.localPosition = new Vector3(0f, 0.2f, -0.1f);
            warpGo.transform.localScale = new Vector3(5f, 5f, 1f);
            warpGo.GetComponent<MeshRenderer>().sharedMaterial = mat;

            var warp = warpGo.AddComponent<SpaceWarpEffect>();

            // Wire warp.targetRenderer + jumpPad.warpEffect via SerializedObject so the inspector
            // shows the references — surviving prefab variants and undo cleanly.
            var soWarp = new SerializedObject(warp);
            soWarp.FindProperty("targetRenderer").objectReferenceValue = warpGo.GetComponent<MeshRenderer>();
            soWarp.ApplyModifiedProperties();

            var soPad = new SerializedObject(jumpPad);
            soPad.FindProperty("warpEffect").objectReferenceValue = warp;
            soPad.ApplyModifiedProperties();

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = root;

            Debug.Log("Space Warp Jump Pad created. Land the slime on the green pad — each launch " +
                      "fires one shockwave (_PulsePhase animates 0→1 over the SpaceWarpEffect's " +
                      "pulseDuration). The warp quad auto-hides between hits.");
        }

        // -------- material loaders --------

        static Material LoadOrCreateZoneMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatZonePath);
            if (mat != null) return mat;

            EnsureFolder(MatFolder);
            mat = new Material(Shader.Find(ShaderName));
            ApplyZoneCommon(mat);
            mat.SetFloat("_ZTest", ZTestAlways);
            mat.renderQueue = QueueForeground;
            AssetDatabase.CreateAsset(mat, MatZonePath);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static Material LoadOrCreateZoneBackgroundMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatZoneBgPath);
            if (mat != null) return mat;

            EnsureFolder(MatFolder);
            mat = new Material(Shader.Find(ShaderName));
            ApplyZoneCommon(mat);
            mat.SetFloat("_ZTest", ZTestLEqual);
            mat.renderQueue = QueueBackground;
            AssetDatabase.CreateAsset(mat, MatZoneBgPath);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static void ApplyZoneCommon(Material mat)
        {
            mat.SetColor("_BaseColor",       new Color(0.55f, 0.7f, 1.0f, 1.0f));
            mat.SetFloat("_TintStrength",    0.05f);
            mat.SetFloat("_FalloffStart",    0.45f);
            mat.SetFloat("_FalloffEnd",      1.0f);
            mat.SetFloat("_PersistStrength", 0.04f);
            mat.SetFloat("_PersistScale",    3.0f);
            mat.SetFloat("_DomainWarp",      1.6f);
            mat.SetFloat("_SwirlAmount",     0.9f);
            mat.SetFloat("_SwirlAnim",       0.4f);
            mat.SetFloat("_PulseStrength",   0f);
        }

        static Material LoadOrCreateJumpPadMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatJumpPadPath);
            if (mat != null) return mat;

            EnsureFolder(MatFolder);
            mat = new Material(Shader.Find(ShaderName));
            mat.SetColor("_BaseColor",         new Color(0.7f, 0.85f, 1.0f, 1.0f));
            mat.SetFloat("_TintStrength",      0.04f);
            mat.SetFloat("_FalloffStart",      0.6f);
            mat.SetFloat("_FalloffEnd",        1.0f);
            mat.SetFloat("_PersistStrength",   0f);   // jump pad = pulse only
            mat.SetFloat("_SwirlAmount",       0f);
            mat.SetFloat("_PulseRippleWidth",  0.22f);
            mat.SetFloat("_PulseMaxRadius",    1.1f);
            mat.SetFloat("_Taper",             0.55f);
            mat.SetFloat("_PopFizz",           0.06f);
            mat.SetFloat("_FalloffStart",      0.45f);
            mat.SetFloat("_FalloffEnd",        1.0f);
            mat.SetFloat("_ZTest",             ZTestAlways);
            mat.renderQueue                  = QueueForeground;
            AssetDatabase.CreateAsset(mat, MatJumpPadPath);
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
            // Place near the SceneView pivot so the test object is visible immediately.
            var view = SceneView.lastActiveSceneView;
            if (view != null) return view.pivot;
            return Vector3.zero;
        }
    }
}
