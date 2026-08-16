using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SlimeJumping.UI.Framework;
using SlimeJumping.UI.Panels;

namespace SlimeJumping.EditorTools.UI
{
    /// Builds Assets/Prefabs/UI/Panels/MainMenuPanel.prefab.
    public static class MainMenuPanelSetup
    {
        public const string PrefabPath = UISetupCommon.PanelPrefabRoot + "/MainMenuPanel.prefab";

        [MenuItem("Tools/SlimeJumping/UI/Build or Rebuild MainMenuPanel Prefab")]
        public static void BuildMenu()
        {
            var path = Build();
            Debug.Log($"[UISetup] MainMenuPanel prefab saved: {path}");
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        public static string Build()
        {
            var root = UISetupCommon.CreatePanelRoot("MainMenuPanel", new Vector2(UISetupCommon.CanvasRefWidth, UISetupCommon.CanvasRefHeight));
            var panel = root.AddComponent<MainMenuPanel>();

            var content = UISetupCommon.CreateChild(root.transform, "Content", typeof(RectTransform));
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.5f, 0.5f);
            crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(880f, 620f);
            crt.anchoredPosition = new Vector2(0f, -40f);
            UISetupCommon.AddVerticalLayout(content, padding: 24, spacing: 22, childAlign: TextAnchor.MiddleCenter);
            UISetupCommon.TagSlot(content, UISlotType.ContentRoot);

            // 공용 FontTitle(72)보다 확대 + 자간 — 젤리 가시성, 착지면 확보, 글자 사이 낙하 틈.
            var title = UISetupCommon.CreateText(content.transform, "Title", "Slime Jumping", 260,
                TextAlignmentOptions.Center);
            title.characterSpacing = -10f;
            // 영상 배경 위라 공용 pale mint 는 떠 보인다 — 이 타이틀만 한 단계 어두운 민트.
            title.color = new Color(0.45f, 0.72f, 0.58f, 1f);
            var glowMat = LoadOrCreateTitleGlowMaterial(title);
            if (glowMat != null) title.fontSharedMaterial = glowMat;
            title.fontStyle = TMPro.FontStyles.Bold;
            title.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            title.overflowMode = TMPro.TextOverflowModes.Overflow;
            UISetupCommon.TagSlot(title.gameObject, UISlotType.Title);
            UISetupCommon.AddLayoutElement(title.gameObject, minHeight: 355f, preferredHeight: 390f);

            var spacer = UISetupCommon.CreateChild(content.transform, "Spacer", typeof(RectTransform));
            UISetupCommon.AddLayoutElement(spacer, minHeight: 20f, preferredHeight: 24f);

            var start = UISetupCommon.CreateButton(content.transform, "StartButton", "Start New Game");
            UISetupCommon.TagSlot(start.gameObject, UISlotType.StartButton);

            // 세이브가 있을 때만 MainMenuPanel 이 런타임에 켠다.
            var load = UISetupCommon.CreateButton(content.transform, "LoadButton", "Load Game");
            UISetupCommon.TagSlot(load.gameObject, UISlotType.LoadButton);

            var options = UISetupCommon.CreateButton(content.transform, "OptionsButton", "Options");
            UISetupCommon.TagSlot(options.gameObject, UISlotType.OptionsButton);

            var quit = UISetupCommon.CreateButton(content.transform, "QuitButton", "Quit");
            UISetupCommon.TagSlot(quit.gameObject, UISlotType.QuitButton);

            var panelSO = new SerializedObject(panel);
            panelSO.FindProperty("canvasGroup").objectReferenceValue = root.GetComponent<CanvasGroup>();
            panelSO.FindProperty("layer").enumValueIndex = (int)UILayer.Menu;
            panelSO.FindProperty("isModal").boolValue = false;
            panelSO.FindProperty("openOnStart").boolValue = true;
            panelSO.FindProperty("openOnEscape").boolValue = false;
            panelSO.FindProperty("instantTransitions").boolValue = true;
            panelSO.FindProperty("juiceProfile").objectReferenceValue = UISetupCommon.LoadOrCreateDefaultJuiceProfile();
            panelSO.ApplyModifiedPropertiesWithoutUndo();

            return UISetupCommon.SavePanelPrefab(root, "MainMenuPanel");
        }

        /// SDF Glow preset for the title — overlay canvases sit outside post-processing, so the
        /// bloom look comes from the glyph shader itself. Existing asset = user tuning, untouched.
        private static Material LoadOrCreateTitleGlowMaterial(TMPro.TextMeshProUGUI title)
        {
            const string path = UISetupCommon.SettingsRoot + "/TitleGlow.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            if (title.font == null) return null;

            var mat = new Material(title.font.material);
            // 폰트 기본은 Mobile 셰이더라 Glow 프로퍼티가 없다 — 풀 셰이더로 스왑(아틀라스 참조 유지).
            var fullShader = Shader.Find("TextMeshPro/Distance Field");
            if (fullShader != null) mat.shader = fullShader;
            mat.EnableKeyword("GLOW_ON");
            mat.SetColor("_GlowColor", new Color(0.45f, 0.9f, 0.65f, 0.85f));
            mat.SetFloat("_GlowOuter", 0.35f);
            mat.SetFloat("_GlowInner", 0.05f);
            mat.SetFloat("_GlowPower", 0.75f);
            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}
