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

            var title = UISetupCommon.CreateText(content.transform, "Title", "Slime Jumping", UISetupCommon.FontTitle,
                TextAlignmentOptions.Center);
            title.color = UISetupCommon.ColorTitle;
            title.fontStyle = TMPro.FontStyles.Bold;
            title.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            title.overflowMode = TMPro.TextOverflowModes.Overflow;
            UISetupCommon.TagSlot(title.gameObject, UISlotType.Title);
            UISetupCommon.AddLayoutElement(title.gameObject, minHeight: 96f, preferredHeight: 110f);

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
    }
}
