using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    public static class GravityFieldV2TestSetup
    {
        [MenuItem("SlimeJumping/Mechanism/Setup GravityFieldV2 Test")]
        public static void SetupGravityFieldV2Test()
        {
            var root = new GameObject("GravityFieldV2Test");
            Undo.RegisterCreatedObjectUndo(root, "Create GravityFieldV2 Test");

            float baseX = 220f;
            float baseY = 0f;

            // canonical 기본 + 케이스별 변종 config (재실행 시 값 갱신)
            EnsureConfig("GravityPointConfig", 6f, 20f);
            var cfgT1 = EnsureConfig("GravityPointConfig_T1", 6f, 25f);
            var cfgT2 = EnsureConfig("GravityPointConfig_T2", 5.5f, 20f);
            var cfgT3 = EnsureConfig("GravityPointConfig_T3", 4.5f, 18f);
            var cfgT4 = EnsureConfig("GravityPointConfig_T4", 7f, 30f);

            // --- Test 1: 단일 점 — 궤도 휘어짐 + 점 근처 저중력 ---
            CreatePlatform("T1_LaunchPad", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f), new Vector3(6f, 1f, 1f));

            var region1 = CreateRegion("T1_SinglePoint", root.transform,
                new Vector3(baseX, baseY + 10f, 0f), new Vector2(16f, 14f), 2f);
            CreatePoint(region1.transform, new Vector3(baseX, baseY + 12f, 0f), cfgT1);

            // --- Test 2: 두 점 상쇄 — 중간 데드존에서 방향 freeze 확인 ---
            var region2 = CreateRegion("T2_TwinDeadzone", root.transform,
                new Vector3(baseX + 26f, baseY + 9f, 0f), new Vector2(18f, 12f), 2f);
            CreatePoint(region2.transform, new Vector3(baseX + 21f, baseY + 9f, 0f), cfgT2);
            CreatePoint(region2.transform, new Vector3(baseX + 31f, baseY + 9f, 0f), cfgT2);
            CreateMarker("T2_SaddleMarker", root.transform, new Vector3(baseX + 26f, baseY + 9f, 0f));
            CreatePlatform("T2_LaunchPad", root.transform,
                new Vector3(baseX + 26f, baseY - 0.5f, 0f), new Vector3(6f, 1f, 1f));

            // --- Test 3: 경계 페더 — 관통 비행으로 진입/이탈 부드러움 확인 ---
            CreatePlatform("T3_LaunchPad", root.transform,
                new Vector3(baseX + 44f, baseY - 0.5f, 0f), new Vector3(5f, 1f, 1f));

            var region3 = CreateRegion("T3_FeatherBand", root.transform,
                new Vector3(baseX + 54f, baseY + 8f, 0f), new Vector2(9f, 16f), 3f);
            CreatePoint(region3.transform, new Vector3(baseX + 54f, baseY + 10f, 0f), cfgT3);

            CreatePlatform("T3_LandingPad", root.transform,
                new Vector3(baseX + 64f, baseY - 0.5f, 0f), new Vector3(6f, 1f, 1f));

            // --- Test 4: 벽 착지 — 점이 수직벽 쪽으로 끌어 벽면이 "바닥"이 되는지 ---
            CreatePlatform("T4_LaunchPad", root.transform,
                new Vector3(baseX + 76f, baseY - 0.5f, 0f), new Vector3(5f, 1f, 1f));

            var region4 = CreateRegion("T4_WallAttract", root.transform,
                new Vector3(baseX + 84f, baseY + 8f, 0f), new Vector2(16f, 14f), 2f);
            CreatePoint(region4.transform, new Vector3(baseX + 88f, baseY + 8f, 0f), cfgT4);

            CreatePlatform("T4_Wall", root.transform,
                new Vector3(baseX + 90f, baseY + 8f, 0f), new Vector3(1f, 10f, 1f));

            AssetDatabase.SaveAssets();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("GravityFieldV2 Test setup complete! 4 cases: single-point orbit, twin deadzone freeze, feather band, wall attract.");
        }

        static GameObject CreateRegion(string name, Transform parent, Vector3 pos, Vector2 boxSize, float feather)
        {
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = pos;
            go.layer = LayerMask.NameToLayer("GravityField");

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = boxSize;

            var zone = go.AddComponent<GravityFieldV2>();
            var so = new SerializedObject(zone);
            so.FindProperty("featherWidth").floatValue = feather;
            so.ApplyModifiedProperties();
            return go;
        }

        static GravityPointConfig EnsureConfig(string assetName, float radius, float peakAccel)
        {
            string path = $"Assets/Resources/{assetName}.asset";
            var cfg = AssetDatabase.LoadAssetAtPath<GravityPointConfig>(path);
            if (cfg == null)
            {
                cfg = ScriptableObject.CreateInstance<GravityPointConfig>();
                cfg.radius = radius;
                cfg.peakAccel = peakAccel;
                AssetDatabase.CreateAsset(cfg, path);
            }
            else
            {
                cfg.radius = radius;
                cfg.peakAccel = peakAccel;
                EditorUtility.SetDirty(cfg);
            }
            return cfg;
        }

        static GameObject CreatePoint(Transform region, Vector3 pos, GravityPointConfig cfg)
        {
            float radius = cfg.radius;
            var go = new GameObject("GravityPoint");
            go.transform.parent = region;
            go.transform.position = pos;

            var point = go.AddComponent<GravityPoint2D>();
            var so = new SerializedObject(point);
            so.FindProperty("configOverride").objectReferenceValue = cfg;
            so.ApplyModifiedProperties();

            // 임시 리그 비주얼(힘 곡선 비례 글로우+펄스) — 실전 비주얼 확정 전 메커닉 테스트용.
            var rim = GameObject.CreatePrimitive(PrimitiveType.Quad);
            rim.name = "FieldRim";
            rim.transform.SetParent(go.transform, false);
            rim.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
            Object.DestroyImmediate(rim.GetComponent<MeshCollider>());
            var rimRenderer = rim.GetComponent<MeshRenderer>();
            var rimMat = new Material(Shader.Find("SlimeJumping/GravityPointField"));
            var profileTex = BakeProfileTexture(point.ForceProfile);
            Undo.RegisterCreatedObjectUndo(rimMat, "Create GravityFieldV2 Test");
            Undo.RegisterCreatedObjectUndo(profileTex, "Create GravityFieldV2 Test");
            rimMat.SetTexture("_ProfileTex", profileTex);
            rimRenderer.sharedMaterial = rimMat;
            rimRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rimRenderer.receiveShadows = false;
            rimRenderer.sortingOrder = -1;

            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Marker";
            marker.transform.SetParent(go.transform, false);
            marker.transform.localScale = Vector3.one * 0.2f;
            Object.DestroyImmediate(marker.GetComponent<SphereCollider>());
            marker.GetComponent<MeshRenderer>().sharedMaterial =
                MakeUnlitMaterial(new Color(1f, 0.6f, 0.2f, 0.45f));
            return go;
        }

        static Texture2D BakeProfileTexture(AnimationCurve profile)
        {
            const int width = 128;
            var tex = new Texture2D(width, 1, TextureFormat.R8, false)
            {
                name = "GravityPointProfile",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            for (int x = 0; x < width; x++)
            {
                float v = Mathf.Clamp01(profile.Evaluate(x / (float)(width - 1)));
                tex.SetPixel(x, 0, new Color(v, v, v, 1f));
            }
            tex.Apply(false, true);
            return tex;
        }

        static GameObject CreateMarker(string name, Transform parent, Vector3 pos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.parent = parent;
            go.transform.position = pos;
            go.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);

            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = MakeLitMaterial(new Color(1f, 0.85f, 0.2f));
            return go;
        }

        static GameObject CreatePlatform(string name, Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.parent = parent;
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.layer = 7;

            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            go.AddComponent<BoxCollider2D>().size = Vector2.one;

            go.GetComponent<MeshRenderer>().sharedMaterial = MakeLitMaterial(new Color(0.5f, 0.5f, 0.5f));
            return go;
        }

        static Material MakeLitMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            return mat;
        }

        static Material MakeUnlitMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = color;
            return mat;
        }
    }
}
