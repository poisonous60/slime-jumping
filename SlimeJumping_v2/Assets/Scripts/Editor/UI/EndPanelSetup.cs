using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SlimeJumping.UI.Framework;
using SlimeJumping.UI.Panels;

namespace SlimeJumping.EditorTools.UI
{
    /// Builds Assets/Prefabs/UI/Panels/EndPanel.prefab.
    public static class EndPanelSetup
    {
        public const string PrefabPath = UISetupCommon.PanelPrefabRoot + "/EndPanel.prefab";

        [MenuItem("Tools/SlimeJumping/UI/Build or Rebuild EndPanel Prefab")]
        public static void BuildMenu()
        {
            var path = Build();
            Debug.Log($"[UISetup] EndPanel prefab saved: {path}");
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        public static string Build()
        {
            var root = UISetupCommon.CreatePanelRoot("EndPanel", new Vector2(UISetupCommon.CanvasRefWidth, UISetupCommon.CanvasRefHeight));
            var panel = root.AddComponent<EndPanel>();

            var backdrop = UISetupCommon.CreateChild(root.transform, "Backdrop",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            UISetupCommon.FillParent(backdrop);
            var bImg = backdrop.GetComponent<Image>();
            bImg.color = new Color(0.02f, 0.08f, 0.05f, 0.88f);
            bImg.raycastTarget = false;

            var content = UISetupCommon.CreateChild(root.transform, "Content", typeof(RectTransform));
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.5f, 0.5f);
            crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(960f, 560f);
            crt.anchoredPosition = Vector2.zero;
            UISetupCommon.AddVerticalLayout(content, padding: 32, spacing: 28, childAlign: TextAnchor.MiddleCenter);
            UISetupCommon.TagSlot(content, UISlotType.ContentRoot);
            UISetupCommon.AddContainerStagger(content);

            var title = UISetupCommon.CreateText(content.transform, "Title", "Thanks for Playing",
                UISetupCommon.FontTitle, TextAlignmentOptions.Center);
            title.color = UISetupCommon.ColorTitle;
            title.fontStyle = TMPro.FontStyles.Bold;
            UISetupCommon.TagSlot(title.gameObject, UISlotType.Title);
            UISetupCommon.AddLayoutElement(title.gameObject, minHeight: 96f, preferredHeight: 110f);

            var body = UISetupCommon.CreateText(content.transform, "Body",
                "Demo complete. Press any key to return to the main menu.",
                UISetupCommon.FontBody, TextAlignmentOptions.Center);
            body.color = UISetupCommon.ColorBody;
            UISetupCommon.TagSlot(body.gameObject, UISlotType.Body);
            UISetupCommon.AddLayoutElement(body.gameObject, minHeight: 60f, preferredHeight: 140f);

            var returnBtn = UISetupCommon.CreateButton(content.transform, "ReturnButton", "Return to Menu");
            UISetupCommon.AddLayoutElement(returnBtn.gameObject, preferredWidth: 360f, preferredHeight: 68f);
            UISetupCommon.TagSlot(returnBtn.gameObject, UISlotType.ReturnButton);

            var panelSO = new SerializedObject(panel);
            panelSO.FindProperty("canvasGroup").objectReferenceValue = root.GetComponent<CanvasGroup>();
            panelSO.FindProperty("layer").enumValueIndex = (int)UILayer.Menu;
            panelSO.FindProperty("isModal").boolValue = false;
            panelSO.FindProperty("openOnStart").boolValue = true;
            panelSO.FindProperty("openOnEscape").boolValue = false;
            panelSO.FindProperty("juiceProfile").objectReferenceValue = UISetupCommon.LoadOrCreateDefaultJuiceProfile();
            panelSO.ApplyModifiedPropertiesWithoutUndo();

            return UISetupCommon.SavePanelPrefab(root, "EndPanel");
        }
    }
}
