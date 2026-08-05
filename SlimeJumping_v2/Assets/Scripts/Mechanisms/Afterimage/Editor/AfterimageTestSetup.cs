using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;
using SlimeJumping.Mechanisms.Afterimage;

namespace SlimeJumping.Editor
{
    // Spawns a self-contained afterimage-zone test set at the scene-view pivot: the trigger
    // zone, two ripple distortion quads, and primitive platforms forming a crossing gap plus
    // a vertical shaft (stamp-ladder ratchet check). Creates the config asset and the ghost
    // material on first run.
    public static class AfterimageTestSetup
    {
        const string ConfigAssetPath     = "Assets/Resources/AfterimageConfig.asset";
        const string GhostShaderName     = "SlimeJumping/SlimeAfterimage";
        const string GhostMatPath        = "Assets/Art/Shader/Mat_SlimeAfterimage.mat";
        const string SourceBodyMatPath   = "Assets/Art/Shader/Mat_SlimeBody3D_Quibli.mat";
        const string DistortionShaderName = "SlimeJumping/BellWave_Distortion";
        const string DistortionMatPath   = "Assets/Art/Shader/Mat_BellWave_Distortion.mat";
        const int GroundLayer = 7;
        const int RippleQuadCount = 2;

        [MenuItem("SlimeJumping/Afterimage/Spawn Afterimage Zone Test Set")]
        public static void SpawnTestSet()
        {
            var cfg = EnsureConfig();
            var ghostMat = EnsureGhostMaterial(cfg);
            if (ghostMat == null) return;
            var distortionMat = EnsureDistortionMaterial();

            var root = new GameObject("AfterimageZone_TestSet");
            Undo.RegisterCreatedObjectUndo(root, "Spawn Afterimage Zone Test Set");
            root.transform.position = GetSpawnPosition();
            Vector3 origin = root.transform.position;

            // Trigger volume (default layer — a ground-layer trigger would read as a surface).
            var zoneGo = new GameObject("AfterimageZone");
            zoneGo.transform.SetParent(root.transform, false);
            var box = zoneGo.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = new Vector2(18f, 12f);
            var zone = zoneGo.AddComponent<AfterimageZone>();

            var quads = new SpaceWarpEffect[RippleQuadCount];
            for (int i = 0; i < RippleQuadCount; i++)
                quads[i] = CreateRippleQuad(root.transform, origin, cfg, distortionMat, i);

            var shapeGo = new GameObject("HardenBlockShape");
            shapeGo.transform.SetParent(root.transform, false);
            var shape = shapeGo.AddComponent<PolygonCollider2D>();
            shape.isTrigger = true;
            shape.SetPath(0, new[]
            {
                new Vector2(-2f, -1.5f), new Vector2(2f, -1.5f),
                new Vector2(2f, 3f), new Vector2(-2f, 3f),
            });

            var so = new SerializedObject(zone);
            so.FindProperty("hardenBlockShape").objectReferenceValue = shape;
            so.FindProperty("configOverride").objectReferenceValue = cfg;
            var quadsProp = so.FindProperty("rippleQuads");
            quadsProp.arraySize = quads.Length;
            for (int i = 0; i < quads.Length; i++)
                quadsProp.GetArrayElementAtIndex(i).objectReferenceValue = quads[i];
            so.ApplyModifiedProperties();

            // Crossing gap: start platform left, goal platform higher right — fall into the
            // gap, then land on your own trail. Vertical shaft in the middle for the
            // ratchet check (launching off stamps must erase the ladder).
            SpawnPlatform(root.transform, "Platform_Start", new Vector3(-7f, -4.5f), new Vector3(4f, 0.5f, 1f));
            SpawnPlatform(root.transform, "Platform_Goal", new Vector3(7f, 0.5f), new Vector3(3f, 0.5f, 1f));
            SpawnPlatform(root.transform, "Shaft_WallL", new Vector3(-1.5f, -0.5f), new Vector3(0.5f, 8f, 1f));
            SpawnPlatform(root.transform, "Shaft_WallR", new Vector3(1.5f, -0.5f), new Vector3(0.5f, 8f, 1f));

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = root;

            Debug.Log($"[Afterimage] Test set spawned at {origin}. Move through the zone to leave " +
                      "stamps (0.4s ghost → hardened, 5s lifetime), fall and land on your own trail, " +
                      "then launch off a stamp: ripple + nearby stamps erased + brief record silence. " +
                      $"Tuning lives in {ConfigAssetPath}.", root);
        }

        static SpaceWarpEffect CreateRippleQuad(Transform parent, Vector3 origin, AfterimageConfig cfg,
                                                Material distortionMat, int index)
        {
            var quadGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quadGo.name = $"AfterimageRipple_{index}";
            Object.DestroyImmediate(quadGo.GetComponent<MeshCollider>());
            quadGo.transform.SetParent(parent, false);
            quadGo.transform.position = new Vector3(origin.x, origin.y, origin.z - 0.5f);
            // Square scale — the distortion shader's radial math assumes square UV space.
            float size = 2f * cfg.shockwaveRadius;
            quadGo.transform.localScale = new Vector3(size, size, 1f);
            var renderer = quadGo.GetComponent<MeshRenderer>();
            if (distortionMat != null) renderer.sharedMaterial = distortionMat;

            var warp = quadGo.AddComponent<SpaceWarpEffect>();
            var so = new SerializedObject(warp);
            so.FindProperty("targetRenderer").objectReferenceValue = renderer;
            so.FindProperty("pulseDuration").floatValue = cfg.rippleDurationSeconds;
            so.FindProperty("pulseStrength").floatValue = cfg.rippleStrength;
            so.ApplyModifiedProperties();
            return warp;
        }

        static void SpawnPlatform(Transform parent, string name, Vector3 localPos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<MeshCollider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.layer = GroundLayer;
            go.AddComponent<BoxCollider2D>();
        }

        // -------- shared assets --------

        static Material EnsureGhostMaterial(AfterimageConfig cfg)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(GhostMatPath);
            if (mat == null)
            {
                var shader = Shader.Find(GhostShaderName);
                if (shader == null)
                {
                    EditorUtility.DisplayDialog("Afterimage Zone Test",
                        $"Shader '{GhostShaderName}' not found (compile errors?).", "OK");
                    return null;
                }

                mat = new Material(shader);
                // Inherit the gradient ramp and tuning from the live body material.
                var sourceMat = AssetDatabase.LoadAssetAtPath<Material>(SourceBodyMatPath);
                if (sourceMat != null)
                    mat.CopyPropertiesFromMaterial(sourceMat);
                else
                    Debug.LogWarning($"[Afterimage] {SourceBodyMatPath} not found — ghost material starts from shader defaults.");
                mat.shader = shader;
                mat.SetFloat("_GhostAlpha", cfg.ghostAlpha);
                AssetDatabase.CreateAsset(mat, GhostMatPath);
                AssetDatabase.SaveAssets();
            }

            if (cfg.ghostMaterial != mat)
            {
                var so = new SerializedObject(cfg);
                so.FindProperty("ghostMaterial").objectReferenceValue = mat;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(cfg);
                AssetDatabase.SaveAssets();
            }
            return mat;
        }

        static Material EnsureDistortionMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(DistortionMatPath);
            if (mat != null) return mat;

            var shader = Shader.Find(DistortionShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[Afterimage] Shader '{DistortionShaderName}' not found — ripple quads spawn without a material.");
                return null;
            }
            mat = new Material(shader);
            mat.SetFloat("_PulseMaxRadius", 1f);
            AssetDatabase.CreateAsset(mat, DistortionMatPath);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static AfterimageConfig EnsureConfig()
        {
            var existing = AssetDatabase.LoadAssetAtPath<AfterimageConfig>(ConfigAssetPath);
            if (existing != null) return existing;

            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");

            var config = ScriptableObject.CreateInstance<AfterimageConfig>();
            AssetDatabase.CreateAsset(config, ConfigAssetPath);
            AssetDatabase.SaveAssets();
            return config;
        }

        static Vector3 GetSpawnPosition()
        {
            var view = SceneView.lastActiveSceneView;
            if (view != null) return view.pivot;
            return Vector3.zero;
        }
    }
}
