using UnityEditor;
using UnityEngine;
using SlimeJumping.Interaction;
using SlimeJumping.Platforms;

namespace SlimeJumping.Editor
{
    /// EndScene 아스키 엔딩 셋업: THANKS/FOR/PLAYING 낱글자 발판 계단 + 아스키 지면/벽 +
    /// RETURN 물리 버튼 + 슬라임 프리팹 + 고정 카메라를 AsciiEnding 루트 하나로 배치하고
    /// 구형 EndPanel 을 비활성화한다. 루트 삭제 + EndPanel 재활성화 = 완전 롤백.
    public static class AsciiEndingSceneSetup
    {
        const string SceneName = "EndScene";
        const string RootName = "AsciiEnding";
        const int GroundLayer = 7;
        const string ArtFolder = "Assets/AsciiArt/Ending";
        const string ConfigAssetPath = "Assets/Resources/AsciiArtPlatformConfig.asset";
        const string MainScenePath = "Assets/Scenes/MainScene.unity";

        static readonly string[] WordsBottomUp = { "PLAYING", "FOR", "THANKS" };

        // Web 데모는 1스테이지에서 끝나므로 완주 문구를 쓰지 않는다.
        const string WebSceneName = "EndScene_Web";
        static readonly string[] DemoWordsBottomUp = { "ENDING", "DEMO" };
        const float LetterGap = 0.6f;
        const float RowClearance = 1.6f;
        const float ButtonMargin = 1.2f;
        const float SlimeDropHeight = 4f;
        const float CameraMargin = 0.8f;
        const float CameraAspect = 16f / 9f;
        const float BackdropZ = 8f;
        const float BackdropWidth = 40f;
        const float BackdropHeight = 26f;
        const float CeilingThickness = 0.5f;
        const float CeilingWidthPad = 1f;
        const float SlimeSpawnXMargin = 1.5f;
        const float CameraZ = -10f;
        static readonly Color CameraBackground = new Color(0.02f, 0.06f, 0.045f);

        [MenuItem("SlimeJumping/Platform/Setup Ascii Ending (EndScene)")]
        public static void Setup() => Build(SceneName, WordsBottomUp);

        [MenuItem("SlimeJumping/Platform/Setup Ascii Ending (Web Demo)")]
        public static void SetupWebDemo() => Build(WebSceneName, DemoWordsBottomUp);

        static void Build(string targetScene, string[] wordsBottomUp)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (scene.name != targetScene)
            {
                Debug.LogError($"[AsciiEnding] 활성 씬이 {targetScene} 이 아니다 ('{scene.name}'). {targetScene} 을 열고 다시 실행.");
                return;
            }
            if (GameObject.Find(RootName) != null)
            {
                Debug.LogError($"[AsciiEnding] '{RootName}' 루트가 이미 있다. 삭제 후 다시 실행.");
                return;
            }
            var cfg = EnsureConfig();

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Setup Ascii Ending");

            var ground = BuildArt(root.transform, "Ground", "ground", cfg, out float groundW, out float groundH);
            if (ground == null) { Object.DestroyImmediate(root); return; }
            ground.transform.localPosition = new Vector3(-groundW * 0.5f, groundH, 0f);
            float groundTop = groundH;
            float halfArena = groundW * 0.5f;

            float baseY = groundTop + RowClearance;
            float maxRowHalf = 0f;
            float topY = groundTop;
            foreach (var word in wordsBottomUp)
            {
                var row = new GameObject("Row_" + word);
                row.transform.SetParent(root.transform, false);
                float cursor = 0f, rowH = 0f;
                foreach (char ch in word)
                {
                    var letter = BuildArt(row.transform, $"Letter_{ch}", $"letter_{ch}", cfg, out float w, out float h);
                    if (letter == null) { Object.DestroyImmediate(root); return; }
                    letter.transform.localPosition = new Vector3(cursor, h, 0f);
                    cursor += w + LetterGap;
                    rowH = Mathf.Max(rowH, h);
                }
                float rowW = cursor - LetterGap;
                row.transform.localPosition = new Vector3(-rowW * 0.5f, baseY, 0f);
                maxRowHalf = Mathf.Max(maxRowHalf, rowW * 0.5f);
                topY = baseY + rowH;
                baseY += rowH + RowClearance;
            }

            var button = BuildArt(root.transform, "ReturnButton", "button_return", cfg, out float btnW, out float btnH);
            if (button == null) { Object.DestroyImmediate(root); return; }
            button.transform.localPosition = new Vector3(maxRowHalf + ButtonMargin, groundTop + btnH, 0f);
            button.AddComponent<EndingReturnButton>();

            var wallL = BuildArt(root.transform, "WallLeft", "wall", cfg, out float wallW, out float wallH);
            if (wallL == null) { Object.DestroyImmediate(root); return; }
            wallL.transform.localPosition = new Vector3(-halfArena, wallH, 0f);
            var wallR = BuildArt(root.transform, "WallRight", "wall", cfg, out _, out _);
            if (wallR == null) { Object.DestroyImmediate(root); return; }
            wallR.transform.localPosition = new Vector3(halfArena - wallW, wallH, 0f);
            CreateInvisibleBox(root.transform, "Ceiling", new Vector2(0f, wallH + CeilingThickness * 0.5f), new Vector2(groundW + CeilingWidthPad, CeilingThickness));

            if (!CloneMainSceneParts(root.transform, new Vector3(-(maxRowHalf + SlimeSpawnXMargin), groundTop + SlimeDropHeight, 0f)))
            {
                Object.DestroyImmediate(root);
                return;
            }
            CreateBackdrop(root.transform, topY);

            var cam = UnityEngine.Camera.main;
            if (cam != null)
            {
                Undo.RecordObject(cam, "Setup Ascii Ending");
                Undo.RecordObject(cam.transform, "Setup Ascii Ending");
                float halfH = topY * 0.5f + CameraMargin;
                float halfW = halfArena + CameraMargin;
                cam.orthographic = true;
                cam.orthographicSize = Mathf.Max(halfH, halfW / CameraAspect);
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = CameraBackground;
                cam.transform.position = new Vector3(0f, topY * 0.5f, CameraZ);
            }
            else Debug.LogWarning("[AsciiEnding] Main Camera 없음 — 프레이밍 생략.");

            var endPanel = FindUnder("UIRoot", "EndPanel");
            if (endPanel != null)
            {
                Undo.RecordObject(endPanel, "Setup Ascii Ending");
                endPanel.SetActive(false);
            }
            else Debug.LogWarning("[AsciiEnding] EndPanel 못 찾음 — any-key 메뉴 복귀가 살아있을 수 있다.");

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            Debug.Log($"[AsciiEnding] 배치 완료: arena {groundW:F1}u, top {topY:F1}u, button x {maxRowHalf + ButtonMargin:F1}. " +
                      "END 버튼을 밟거나 클릭하면 아이리스 전환 후 메인 메뉴.", root);
        }

        static GameObject BuildArt(Transform parent, string name, string artFile,
                                   AsciiArtPlatformConfig cfg, out float w, out float h)
        {
            w = h = 0f;
            var art = AssetDatabase.LoadAssetAtPath<TextAsset>($"{ArtFolder}/{artFile}.txt");
            if (art == null)
            {
                Debug.LogError($"[AsciiEnding] 아트 없음: {ArtFolder}/{artFile}.txt");
                return null;
            }
            var go = new GameObject(name) { layer = GroundLayer };
            go.transform.SetParent(parent, false);
            var platform = go.AddComponent<AsciiArtPlatform>();
            var so = new SerializedObject(platform);
            so.FindProperty("configOverride").objectReferenceValue = cfg;
            so.ApplyModifiedPropertiesWithoutUndo();
            if (!AsciiArtPlatformBuilder.Build(platform, art, cfg)) return null;
            w = platform.Columns * platform.CellWidth;
            h = platform.Rows * platform.CellHeight;
            return go;
        }

        /// MainScene 을 additive 로 열어 슬라임/라이트/사운드/PostFX 인스턴스와 환경 설정을 복제한다.
        /// 씬 인스턴스 복제 = 프리팹 기본값이 아니라 실제 튜닝 상태를 가져오기 위함.
        static bool CloneMainSceneParts(Transform root, Vector3 slimeSpawn)
        {
            var end = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            var main = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                MainScenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
            GameObject srcSlime = null, srcLight = null, srcSound = null, srcPostFx = null;
            Material sky = null;
            float ambientIntensity = 1f;
            UnityEngine.Rendering.AmbientMode ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            try
            {
                try
                {
                    UnityEditor.SceneManagement.EditorSceneManager.SetActiveScene(main);
                    sky = RenderSettings.skybox;
                    ambientIntensity = RenderSettings.ambientIntensity;
                    ambientMode = RenderSettings.ambientMode;
                }
                finally
                {
                    // Instantiate 는 활성 씬에 생성되므로 복제 전에 반드시 EndScene 으로 복귀.
                    UnityEditor.SceneManagement.EditorSceneManager.SetActiveScene(end);
                }
                foreach (var go in main.GetRootGameObjects())
                {
                    if (go == null) continue;
                    if (go.name == "Slime") srcSlime = go;
                    else if (go.name == "Directional Light") srcLight = go;
                    else if (go.name == "PostFX_Stage1") srcPostFx = go;
                    else if (go.name == "Manager")
                    {
                        var svc = go.GetComponentInChildren<SlimeJumping.Audio.SoundService>(true);
                        if (svc != null) srcSound = svc.gameObject;
                    }
                }
                if (srcSlime == null)
                {
                    Debug.LogError("[AsciiEnding] MainScene 에서 Slime 루트를 못 찾았다.");
                    return false;
                }
                var slime = Object.Instantiate(srcSlime);
                slime.name = "Slime";
                slime.transform.SetParent(root, true);
                slime.transform.position = slimeSpawn;
                if (srcLight != null)
                {
                    var light = Object.Instantiate(srcLight);
                    light.name = "Directional Light";
                    light.transform.SetParent(root, true);
                }
                if (srcSound != null)
                {
                    var sound = Object.Instantiate(srcSound);
                    sound.name = "SoundService";
                    sound.transform.SetParent(root, false);
                }
                if (srcPostFx != null)
                {
                    var fx = Object.Instantiate(srcPostFx);
                    fx.name = "PostFX_Ending";
                    fx.transform.SetParent(root, true);
                }
            }
            finally
            {
                if (main.isLoaded)
                    UnityEditor.SceneManagement.EditorSceneManager.CloseScene(main, true);
            }
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientIntensity = ambientIntensity;
            DynamicGI.UpdateEnvironment();
            return true;
        }

        static void CreateBackdrop(Transform parent, float topY)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Backdrop";
            Object.DestroyImmediate(quad.GetComponent<MeshCollider>());
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = new Vector3(0f, topY * 0.5f, BackdropZ);
            quad.transform.localScale = new Vector3(BackdropWidth, BackdropHeight, 1f);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = Color.black };
            quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static void CreateInvisibleBox(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name) { layer = GroundLayer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var box = go.AddComponent<BoxCollider2D>();
            box.size = size;
        }

        static AsciiArtPlatformConfig EnsureConfig()
        {
            var existing = AssetDatabase.LoadAssetAtPath<AsciiArtPlatformConfig>(ConfigAssetPath);
            if (existing != null) return existing;
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            var config = ScriptableObject.CreateInstance<AsciiArtPlatformConfig>();
            AssetDatabase.CreateAsset(config, ConfigAssetPath);
            AssetDatabase.SaveAssets();
            return config;
        }

        static GameObject FindUnder(string rootName, string childName)
        {
            var rootGo = GameObject.Find(rootName);
            if (rootGo == null) return null;
            var t = FindChildRecursive(rootGo.transform, childName);
            return t == null ? null : t.gameObject;
        }

        static Transform FindChildRecursive(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var r = FindChildRecursive(c, name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
