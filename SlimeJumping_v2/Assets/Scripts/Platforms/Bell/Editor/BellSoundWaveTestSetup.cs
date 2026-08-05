using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;
using SlimeJumping.Platforms.Bell;
using SlimeJumping.Rendering;

namespace SlimeJumping.Editor
{
    // Spawns a self-contained bell sound-wave test set at the scene-view pivot: clones of the
    // scene's bell and the two hidden platforms (original relative offsets preserved), reveal
    // materials, trail receivers, and the wave broadcaster with its distortion quads.
    // Scene originals are used as clone sources only and never modified.
    public static class BellSoundWaveTestSetup
    {
        const string ConfigAssetPath        = "Assets/Resources/BellSoundWaveConfig.asset";
        const string RevealShaderName       = "Quibli/Stylized Lit (Trail Receiver Local Reveal)";
        const string DistortionShaderName   = "SlimeJumping/BellWave_Distortion";
        const string RevealMatPath          = "Assets/Art/Shader/Mat_BellReveal_Platform.mat";
        const string DistortionMatPath      = "Assets/Art/Shader/Mat_BellWave_Distortion.mat";
        const string SourceAtlasMatPath     = "Assets/ThirdParty/Platformer_9_Heaven/Materials/Color.mat";
        const string BellRootName           = "bell_Collider2D";
        static readonly string[] PlatformWrapperNames =
        {
            "platform_018 (1)_Collider2D",
            "platform_017 (1)_Collider2D",
        };
        const int DistortionQuadCount = 4;

        [MenuItem("SlimeJumping/Bell/Spawn Sound Wave Test Set")]
        public static void SpawnTestSet()
        {
            var bellRoot = GameObject.Find(BellRootName);
            if (bellRoot == null || bellRoot.GetComponent<SwingingBell>() == null)
            {
                EditorUtility.DisplayDialog("Bell Sound Wave Test",
                    $"No '{BellRootName}' with a SwingingBell in the open scene.", "OK");
                return;
            }

            var wrappers = new GameObject[PlatformWrapperNames.Length];
            for (int i = 0; i < PlatformWrapperNames.Length; i++)
            {
                var go = GameObject.Find(PlatformWrapperNames[i]);
                if (go == null || go.GetComponent<PolygonCollider2D>() == null ||
                    go.GetComponentInChildren<MeshRenderer>() == null)
                {
                    EditorUtility.DisplayDialog("Bell Sound Wave Test",
                        $"'{PlatformWrapperNames[i]}' (with PolygonCollider2D + child MeshRenderer) not found in the open scene.", "OK");
                    return;
                }
                wrappers[i] = go;
            }

            if (Object.FindFirstObjectByType<WorldTrailManager>() == null)
                Debug.LogWarning("[BellSoundWave] No WorldTrailManager in scene — trail (and its z-fade globals) inactive; goo will not appear on the hidden platforms.");
            if (AssetDatabase.FindAssets("t:SpaceWarpSourceFeature").Length == 0)
                Debug.LogWarning("[BellSoundWave] No SpaceWarpSourceFeature asset found — if it is missing from the active URP renderer, the distortion ring will be invisible.");

            var cfg = EnsureConfig();
            var revealMat = EnsureRevealMaterial();
            var distortionMat = EnsureDistortionMaterial(cfg);
            if (revealMat == null || distortionMat == null) return;

            var root = new GameObject("BellSoundWave_TestSet");
            Undo.RegisterCreatedObjectUndo(root, "Spawn Bell Sound Wave Test Set");
            root.transform.position = GetSpawnPosition();

            // Clone the bell at the root's XY but on the original gameplay Z; platform clones keep
            // their original offsets from the bell so the test geometry matches the real placement.
            Vector3 bellOrigPos = bellRoot.transform.position;
            Vector3 bellPos = new Vector3(root.transform.position.x, root.transform.position.y, bellOrigPos.z);
            var bellCopy = Object.Instantiate(bellRoot, root.transform);
            bellCopy.name = BellRootName + " (TestSet)";
            bellCopy.transform.position = bellPos;

            var receivers = new LocalTrailReceiver[wrappers.Length];
            for (int i = 0; i < wrappers.Length; i++)
            {
                var copy = Object.Instantiate(wrappers[i], root.transform);
                copy.name = PlatformWrapperNames[i] + " (TestSet)";
                copy.transform.position = bellPos + (wrappers[i].transform.position - bellOrigPos);

                var renderer = copy.GetComponentInChildren<MeshRenderer>();
                renderer.sharedMaterial = revealMat;
                receivers[i] = AddReceiver(copy, renderer, cfg);
            }

            var bell = bellCopy.GetComponent<SwingingBell>();
            var broadcaster = CreateBroadcaster(root.transform, bellPos, bell, cfg, distortionMat);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = root;

            Debug.Log($"[BellSoundWave] Test set spawned at {root.transform.position}. Slam the test bell: " +
                      "audible strikes send a wave that reveals the two hidden platforms in passing order. " +
                      $"Tuning lives in {ConfigAssetPath}.", broadcaster);
        }

        // -------- platform clone wiring --------

        static LocalTrailReceiver AddReceiver(GameObject wrapper, MeshRenderer renderer, BellSoundWaveConfig cfg)
        {
            var receiver = wrapper.AddComponent<LocalTrailReceiver>();
            var col = wrapper.GetComponent<PolygonCollider2D>();

            // Receiver transform is the paint plane — the wrapper is unrotated, so its local
            // frame stays world-XY even though the FBX visual child may carry import rotation.
            var b = col.bounds;
            Vector3 lmin3 = wrapper.transform.InverseTransformPoint(b.min);
            Vector3 lmax3 = wrapper.transform.InverseTransformPoint(b.max);
            Vector2 lmin = Vector2.Min(lmin3, lmax3);
            Vector2 lmax = Vector2.Max(lmin3, lmax3);

            // 0.2 world-unit padding, converted to local via lossyScale.
            Vector3 ls = wrapper.transform.lossyScale;
            float padX = 0.2f / Mathf.Max(1e-4f, Mathf.Abs(ls.x));
            float padY = 0.2f / Mathf.Max(1e-4f, Mathf.Abs(ls.y));
            lmin -= new Vector2(padX, padY);
            lmax += new Vector2(padX, padY);

            var so = new SerializedObject(receiver);
            so.FindProperty("localMin").vector2Value  = lmin;
            so.FindProperty("localSize").vector2Value = lmax - lmin;
            so.FindProperty("pixelsPerUnit").intValue = 20;
            so.FindProperty("contactCollider").objectReferenceValue = col;
            so.FindProperty("targetRenderer").objectReferenceValue  = renderer;
            so.FindProperty("overrideId").stringValue = System.Guid.NewGuid().ToString("N");
            so.FindProperty("stampStrengthMultiplier").floatValue = cfg.hiddenPlatformTrailStrength;
            so.ApplyModifiedProperties();
            return receiver;
        }

        // -------- broadcaster + distortion quads --------

        static BellSoundWave CreateBroadcaster(Transform parent, Vector3 bellPos, SwingingBell bell,
                                               BellSoundWaveConfig cfg, Material distortionMat)
        {
            var go = new GameObject("BellSoundWave");
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.position = bellPos;

            var quads = new SpaceWarpEffect[DistortionQuadCount];
            for (int i = 0; i < DistortionQuadCount; i++)
            {
                var quadGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quadGo.name = $"WaveDistortion_{i}";
                Object.DestroyImmediate(quadGo.GetComponent<MeshCollider>());
                quadGo.transform.SetParent(go.transform, worldPositionStays: false);
                quadGo.transform.position = new Vector3(bellPos.x, bellPos.y, bellPos.z - 0.5f);
                // Square scale — the distortion shader's radial math assumes square UV space.
                float size = 2f * cfg.maxRadius;
                quadGo.transform.localScale = new Vector3(size, size, 1f);
                quadGo.GetComponent<MeshRenderer>().sharedMaterial = distortionMat;

                var warp = quadGo.AddComponent<SpaceWarpEffect>();
                var soWarp = new SerializedObject(warp);
                soWarp.FindProperty("targetRenderer").objectReferenceValue = quadGo.GetComponent<MeshRenderer>();
                // Ring speed matches the reveal wavefront: full radius over maxRadius / waveSpeed.
                soWarp.FindProperty("pulseDuration").floatValue = Mathf.Max(0.05f, cfg.maxRadius / cfg.waveSpeed);
                soWarp.FindProperty("pulseStrength").floatValue = cfg.distortionStrength;
                soWarp.ApplyModifiedProperties();
                quads[i] = warp;
            }

            var wave = go.AddComponent<BellSoundWave>();
            var so = new SerializedObject(wave);
            so.FindProperty("bell").objectReferenceValue = bell;
            // Same asset Resolve() would load — wired explicitly so the inspector shows it.
            so.FindProperty("configOverride").objectReferenceValue = cfg;
            so.FindProperty("distortionMaterial").objectReferenceValue = distortionMat;
            var quadsProp = so.FindProperty("distortionQuads");
            quadsProp.arraySize = quads.Length;
            for (int i = 0; i < quads.Length; i++)
                quadsProp.GetArrayElementAtIndex(i).objectReferenceValue = quads[i];
            so.ApplyModifiedProperties();
            return wave;
        }

        // -------- shared assets --------

        static Material EnsureRevealMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(RevealMatPath);
            if (mat != null) return mat;

            var shader = Shader.Find(RevealShaderName);
            if (shader == null)
            {
                EditorUtility.DisplayDialog("Bell Sound Wave Test",
                    $"Shader '{RevealShaderName}' not found (compile errors?).", "OK");
                return null;
            }

            mat = new Material(shader);

            // Same atlas the platforms already use — read off Color.mat, never hardcode the texture path.
            var sourceMat = AssetDatabase.LoadAssetAtPath<Material>(SourceAtlasMatPath);
            if (sourceMat != null && sourceMat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", sourceMat.GetTexture("_BaseMap"));
            else
                Debug.LogWarning($"[BellSoundWave] Could not read the atlas from {SourceAtlasMatPath} — assign _BaseMap on {RevealMatPath} manually.");
            mat.SetFloat("_TextureImpact", 1f);

            // Transparent surface state, mirroring QuibliEditor's Alpha branch.
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            AssetDatabase.CreateAsset(mat, RevealMatPath);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static Material EnsureDistortionMaterial(BellSoundWaveConfig cfg)
        {
            var shader = Shader.Find(DistortionShaderName);
            if (shader == null)
            {
                EditorUtility.DisplayDialog("Bell Sound Wave Test",
                    $"Shader '{DistortionShaderName}' not found.", "OK");
                return null;
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(DistortionMatPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, DistortionMatPath);
            }

            // Config-derived values — re-applied on every run so retuning resyncs them.
            Undo.RecordObject(mat, "Bell Sound Wave Distortion Material");
            if (mat.shader != shader) mat.shader = shader;
            mat.SetFloat("_PulseMaxRadius", 1f);
            mat.SetFloat("_PulseRippleWidth",
                Mathf.Clamp(cfg.distortionRingWidthWorld / Mathf.Max(0.1f, cfg.maxRadius), 0.01f, 0.6f));
            mat.SetFloat("_BlurSize", cfg.distortionBlurSize);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static BellSoundWaveConfig EnsureConfig()
        {
            var existing = AssetDatabase.LoadAssetAtPath<BellSoundWaveConfig>(ConfigAssetPath);
            if (existing != null) return existing;

            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");

            var config = ScriptableObject.CreateInstance<BellSoundWaveConfig>();
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
