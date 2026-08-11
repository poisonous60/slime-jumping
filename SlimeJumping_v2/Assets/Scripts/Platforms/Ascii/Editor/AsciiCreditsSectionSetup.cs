using TMPro;
using UnityEditor;
using UnityEngine;
using SlimeJumping.Interaction;
using SlimeJumping.Platforms;
using SlimeJumping.UI;

namespace SlimeJumping.Editor
{
    /// 엔딩 방 생성기. 배치는 하지 않는다 — 한 덩어리로 뽑아 통째로 옮긴다.
    /// 기존 오브젝트는 건드리지 않는다. 루트 삭제 = 완전 롤백.
    public static class AsciiCreditsSectionSetup
    {
        const string SceneName = "MainScene";
        const string RoomRootName = "AsciiEndRoom";
        const int GroundLayer = 7;
        const string EndingFolder = "Assets/AsciiArt/Ending";
        const string ConfigAssetPath = "Assets/Resources/AsciiArtPlatformConfig.asset";

        // 뽑아두는 자리. 어차피 옮겨 쓰는 물건이라 stage4(y ~495) 위 빈 공간이면 된다.
        static readonly Vector3 RoomWorld = new Vector3(20f, 560f, 0f);

        const float LetterGap = 0.6f;
        const float RowClearance = 1.6f;
        const float ButtonMargin = 1.2f;
        const float TimeLabelRise = 1.5f;
        const float TimeLabelFontScale = 2f;

        static readonly string[] WordsBottomUp = { "PLAYING", "FOR", "THANKS" };

        [MenuItem("SlimeJumping/Platform/Create Ascii End Room (MainScene)")]
        public static void CreateEndRoom()
        {
            if (!CheckScene(RoomRootName, out var scene)) return;
            var cfg = EnsureConfig();

            // 로컬 y=0 이 바닥 윗면, 오른쪽은 입구로 뚫려 있다.
            var room = new GameObject(RoomRootName);
            room.transform.position = RoomWorld;
            Undo.RegisterCreatedObjectUndo(room, "Create Ascii End Room");

            var ground = BuildArt(room.transform, "Ground", EndingFolder, "ground", cfg, out float groundW, out _);
            if (ground == null) { Object.DestroyImmediate(room); return; }
            ground.transform.localPosition = new Vector3(-groundW * 0.5f, 0f, 0f);

            var wall = BuildArt(room.transform, "WallLeft", EndingFolder, "wall", cfg, out _, out float wallH);
            if (wall == null) { Object.DestroyImmediate(room); return; }
            wall.transform.localPosition = new Vector3(-groundW * 0.5f, wallH, 0f);

            float baseY = RowClearance;
            float maxRowHalf = 0f;
            float topY = 0f;
            foreach (var word in WordsBottomUp)
            {
                var row = new GameObject("Row_" + word);
                row.transform.SetParent(room.transform, false);
                float cursor = 0f, rowH = 0f;
                foreach (char ch in word)
                {
                    var letter = BuildArt(row.transform, $"Letter_{ch}", EndingFolder, $"letter_{ch}",
                                          cfg, out float w, out float h);
                    if (letter == null) { Object.DestroyImmediate(room); return; }
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

            var button = BuildArt(room.transform, "ReturnButton", EndingFolder, "button_return",
                                  cfg, out _, out float btnH);
            if (button == null) { Object.DestroyImmediate(room); return; }
            button.transform.localPosition = new Vector3(maxRowHalf + ButtonMargin, btnH, 0f);
            button.AddComponent<EndingReturnButton>();

            var label = CreateTimeLabel(room.transform, cfg, topY + TimeLabelRise, groundW);
            var display = room.AddComponent<EndingTimeDisplay>();
            var so = new SerializedObject(display);
            so.FindProperty("label").objectReferenceValue = label;
            so.FindProperty("prefix").stringValue = "TIME  ";
            so.ApplyModifiedPropertiesWithoutUndo();

            Finish(scene, room, $"[AsciiCredits] 엔딩 방 생성: 바닥 {groundW:F1}u, 글자 최상단 로컬 y {topY:F1}. " +
                                "루트 y 가 바닥 윗면이자 플레이 타임 기록 높이다.");
        }

        static bool CheckScene(string rootName, out UnityEngine.SceneManagement.Scene scene)
        {
            scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (scene.name != SceneName)
            {
                Debug.LogError($"[AsciiCredits] 활성 씬이 {SceneName} 이 아니다 ('{scene.name}').");
                return false;
            }
            if (GameObject.Find(rootName) != null)
            {
                Debug.LogError($"[AsciiCredits] '{rootName}' 루트가 이미 있다. 삭제 후 다시 실행.");
                return false;
            }
            return true;
        }

        static void Finish(UnityEngine.SceneManagement.Scene scene, GameObject root, string message)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            Debug.Log(message, root);
        }

        static TMP_Text CreateTimeLabel(Transform parent, AsciiArtPlatformConfig cfg, float localY, float width)
        {
            var go = new GameObject("TimeLabel");
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.autoSizeTextContainer = false;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = cfg.fontSize * TimeLabelFontScale;
            tmp.color = cfg.textColor;
            tmp.text = "TIME  --:--";
            var rt = tmp.rectTransform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, cfg.fontSize * TimeLabelFontScale * 0.2f);
            rt.localPosition = new Vector3(0f, localY, 0f);
            return tmp;
        }

        static GameObject BuildArt(Transform parent, string name, string folder, string artFile,
                                   AsciiArtPlatformConfig cfg, out float w, out float h)
        {
            w = h = 0f;
            var art = AssetDatabase.LoadAssetAtPath<TextAsset>($"{folder}/{artFile}.txt");
            if (art == null)
            {
                Debug.LogError($"[AsciiCredits] 아트 없음: {folder}/{artFile}.txt");
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
    }
}
