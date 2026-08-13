using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LeTai.Asset.TranslucentImage;
using SlimeJumping.UI.Framework;
using SlimeJumping.UI.Panels;

namespace SlimeJumping.EditorTools.UI
{
    /// Builds OptionsPanel.prefab: single flat list over the dimmed screen — right-aligned
    /// label column, left-aligned control column around a center gutter, action buttons below.
    public static class OptionsPanelSetup
    {
        public const string PrefabPath = UISetupCommon.PanelPrefabRoot + "/OptionsPanel.prefab";

        private const float BoxWidth = 900f;
        private const float BoxHeight = 880f;
        private const float RowHeight = 56f;
        private const float LabelWidth = 340f;
        private const float ControlWidth = 430f;
        private const float CycleButtonWidth = 60f;

        [MenuItem("Tools/SlimeJumping/UI/Build or Rebuild OptionsPanel Prefab")]
        public static void BuildMenu()
        {
            var path = Build();
            Debug.Log($"[UISetup] OptionsPanel prefab saved: {path}");
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        public static string Build()
        {
            var root = UISetupCommon.CreatePanelRoot("OptionsPanel", new Vector2(UISetupCommon.CanvasRefWidth, UISetupCommon.CanvasRefHeight));
            var panel = root.AddComponent<OptionsPanel>();

            var backdrop = UISetupCommon.CreateChild(root.transform, "Backdrop",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            UISetupCommon.FillParent(backdrop);
            // Darker than the shared dim so menu-layer UI outside the plate stays muted.
            backdrop.GetComponent<Image>().color = new Color(0f, 0.06f, 0.04f, 0.75f);

            // Frosted plate — blur source auto-acquired at Start; alpha 1 blocks menu-layer UI bleed.
            var box = UISetupCommon.CreateChild(root.transform, "Box", typeof(RectTransform), typeof(CanvasRenderer));
            var boxImg = box.AddComponent<TranslucentImage>();
            boxImg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            boxImg.type = Image.Type.Sliced;
            boxImg.color = new Color(0.04f, 0.11f, 0.09f, 1f);
            boxImg.foregroundOpacity = 0.5f;
            boxImg.vibrancy = 1f;
            var boxRT = box.GetComponent<RectTransform>();
            boxRT.anchorMin = new Vector2(0.5f, 0.5f);
            boxRT.anchorMax = new Vector2(0.5f, 0.5f);
            boxRT.pivot = new Vector2(0.5f, 0.5f);
            boxRT.sizeDelta = new Vector2(BoxWidth, BoxHeight);
            boxRT.anchoredPosition = Vector2.zero;
            UISetupCommon.AddVerticalLayout(box, padding: 32, spacing: 10, childAlign: TextAnchor.UpperCenter);
            UISetupCommon.TagSlot(box, UISlotType.ContentRoot);

            var title = UISetupCommon.CreateText(box.transform, "Title", "Options",
                UISetupCommon.FontTitle, TextAlignmentOptions.Center);
            title.color = UISetupCommon.ColorTitle;
            title.fontStyle = TMPro.FontStyles.Bold;
            UISetupCommon.AddLayoutElement(title.gameObject, minHeight: 88f, preferredHeight: 96f);
            UISetupCommon.TagSlot(title.gameObject, UISlotType.Title);

            var resolutionRow = CreateDropdownRow(box.transform, "ResolutionRow", "Resolution",
                out TMP_Dropdown resolutionDropdown);
            UISetupCommon.TagSlot(resolutionRow, UISlotType.Row_Resolution);

            var windowModeRow = CreateCyclerRow(box.transform, "WindowModeRow", "Window Mode",
                out Button windowModePrev, out Button windowModeNext, out TextMeshProUGUI windowModeValue);
            UISetupCommon.TagSlot(windowModeRow, UISlotType.Row_WindowMode);

            var frameLimitRow = CreateCyclerRow(box.transform, "FrameLimitRow", "Frame Limit",
                out Button frameLimitPrev, out Button frameLimitNext, out TextMeshProUGUI frameLimitValue);
            UISetupCommon.TagSlot(frameLimitRow, UISlotType.Row_FrameLimit);

            var vsyncRow = CreateCyclerRow(box.transform, "VSyncRow", "VSync",
                out Button vsyncPrev, out Button vsyncNext, out TextMeshProUGUI vsyncValue);
            UISetupCommon.TagSlot(vsyncRow, UISlotType.Row_VSync);

            var bgCameraRow = CreateCyclerRow(box.transform, "BackgroundCameraRow", "Background Camera",
                out Button bgCameraPrev, out Button bgCameraNext, out TextMeshProUGUI bgCameraValue);
            UISetupCommon.TagSlot(bgCameraRow, UISlotType.Row_BackgroundCamera);

            var bgmRow = CreateSliderRow(box.transform, "BgmRow", "BGM",
                out Slider bgmSlider, out TextMeshProUGUI bgmValue);
            UISetupCommon.TagSlot(bgmRow, UISlotType.Row_Bgm);

            var playerSfxRow = CreateSliderRow(box.transform, "PlayerSfxRow", "Player SFX",
                out Slider playerSfxSlider, out TextMeshProUGUI playerSfxValue);
            UISetupCommon.TagSlot(playerSfxRow, UISlotType.Row_PlayerSfx);

            var worldSfxRow = CreateSliderRow(box.transform, "WorldSfxRow", "World SFX",
                out Slider worldSfxSlider, out TextMeshProUGUI worldSfxValue);
            UISetupCommon.TagSlot(worldSfxRow, UISlotType.Row_WorldSfx);

            var uiRow = CreateSliderRow(box.transform, "UiRow", "UI",
                out Slider uiSlider, out TextMeshProUGUI uiValue);
            UISetupCommon.TagSlot(uiRow, UISlotType.Row_Ui);

            var spacer = UISetupCommon.CreateChild(box.transform, "Spacer", typeof(RectTransform));
            UISetupCommon.AddLayoutElement(spacer, minHeight: 12f, preferredHeight: 16f);

            var actions = UISetupCommon.CreateChild(box.transform, "Actions", typeof(RectTransform));
            UISetupCommon.AddHorizontalLayout(actions, padding: 0, spacing: 16, childAlign: TextAnchor.MiddleCenter, expandWidth: true);
            UISetupCommon.AddLayoutElement(actions, minHeight: 56f, preferredHeight: 60f);
            var closeBtn = UISetupCommon.CreateButton(actions.transform, "CloseButton", "Close");
            UISetupCommon.TagSlot(closeBtn.gameObject, UISlotType.CloseButton);
            UISetupCommon.AddLayoutElement(closeBtn.gameObject, preferredWidth: 300f);
            var menuBtn = UISetupCommon.CreateButton(actions.transform, "MenuButton", "Return to Menu");
            UISetupCommon.TagSlot(menuBtn.gameObject, UISlotType.ReturnButton);
            UISetupCommon.AddLayoutElement(menuBtn.gameObject, preferredWidth: 300f);

            // Wire OptionsPanel's [SerializeField] fields
            var so = new SerializedObject(panel);
            so.FindProperty("canvasGroup").objectReferenceValue = root.GetComponent<CanvasGroup>();
            so.FindProperty("layer").enumValueIndex = (int)UILayer.Modal;
            so.FindProperty("isModal").boolValue = true;
            so.FindProperty("openOnStart").boolValue = false;
            so.FindProperty("openOnEscape").boolValue = true;
            so.FindProperty("instantTransitions").boolValue = true;
            so.FindProperty("juiceProfile").objectReferenceValue = UISetupCommon.LoadOrCreateDefaultJuiceProfile();

            so.FindProperty("frameLimitPrevButton").objectReferenceValue = frameLimitPrev;
            so.FindProperty("frameLimitNextButton").objectReferenceValue = frameLimitNext;
            so.FindProperty("frameLimitValueLabel").objectReferenceValue = UISetupCommon.MakeBumpable(frameLimitValue);

            so.FindProperty("vsyncPrevButton").objectReferenceValue = vsyncPrev;
            so.FindProperty("vsyncNextButton").objectReferenceValue = vsyncNext;
            so.FindProperty("vsyncValueLabel").objectReferenceValue = UISetupCommon.MakeBumpable(vsyncValue);

            so.FindProperty("playerSfxVolumeSlider").objectReferenceValue = playerSfxSlider;
            so.FindProperty("playerSfxVolumeValueLabel").objectReferenceValue = UISetupCommon.MakeBumpable(playerSfxValue);
            so.FindProperty("worldSfxVolumeSlider").objectReferenceValue = worldSfxSlider;
            so.FindProperty("worldSfxVolumeValueLabel").objectReferenceValue = UISetupCommon.MakeBumpable(worldSfxValue);
            so.FindProperty("bgmVolumeSlider").objectReferenceValue = bgmSlider;
            so.FindProperty("bgmVolumeValueLabel").objectReferenceValue = UISetupCommon.MakeBumpable(bgmValue);
            so.FindProperty("uiVolumeSlider").objectReferenceValue = uiSlider;
            so.FindProperty("uiVolumeValueLabel").objectReferenceValue = UISetupCommon.MakeBumpable(uiValue);

            so.FindProperty("windowModePrevButton").objectReferenceValue = windowModePrev;
            so.FindProperty("windowModeNextButton").objectReferenceValue = windowModeNext;
            so.FindProperty("windowModeValueLabel").objectReferenceValue = UISetupCommon.MakeBumpable(windowModeValue);
            so.FindProperty("resolutionDropdown").objectReferenceValue = resolutionDropdown;
            so.FindProperty("backgroundCameraPrevButton").objectReferenceValue = bgCameraPrev;
            so.FindProperty("backgroundCameraNextButton").objectReferenceValue = bgCameraNext;
            so.FindProperty("backgroundCameraValueLabel").objectReferenceValue = UISetupCommon.MakeBumpable(bgCameraValue);

            so.FindProperty("closeButton").objectReferenceValue = closeBtn;
            so.FindProperty("menuButton").objectReferenceValue = menuBtn;
            so.ApplyModifiedPropertiesWithoutUndo();

            return UISetupCommon.SavePanelPrefab(root, "OptionsPanel");
        }

        // Reference layout: label right-aligned to the center gutter, control column left-aligned after it.
        private static GameObject CreateRowShell(Transform parent, string name, string labelText, out GameObject controlArea)
        {
            var row = UISetupCommon.CreateChild(parent, name, typeof(RectTransform));
            UISetupCommon.AddHorizontalLayout(row, padding: 0, spacing: 24, childAlign: TextAnchor.MiddleCenter);
            UISetupCommon.AddLayoutElement(row, minHeight: RowHeight, preferredHeight: RowHeight);

            var label = UISetupCommon.CreateText(row.transform, "Label", labelText,
                UISetupCommon.FontRowLabel, TextAlignmentOptions.MidlineRight);
            label.color = UISetupCommon.ColorRowLabel;
            UISetupCommon.AddLayoutElement(label.gameObject, preferredWidth: LabelWidth, flexibleWidth: 0f);

            controlArea = UISetupCommon.CreateChild(row.transform, "Control", typeof(RectTransform));
            UISetupCommon.AddHorizontalLayout(controlArea, padding: 0, spacing: 8, childAlign: TextAnchor.MiddleLeft);
            UISetupCommon.AddLayoutElement(controlArea, preferredWidth: ControlWidth, flexibleWidth: 0f);
            return row;
        }

        private static GameObject CreateCyclerRow(Transform parent, string name, string labelText,
            out Button prev, out Button next, out TextMeshProUGUI valueLabel)
        {
            var row = CreateRowShell(parent, name, labelText, out GameObject control);

            prev = UISetupCommon.CreateButton(control.transform, "PrevButton", "<");
            UISetupCommon.AddLayoutElement(prev.gameObject, preferredWidth: CycleButtonWidth, preferredHeight: RowHeight, flexibleWidth: 0f);
            UISetupCommon.TagSlot(prev.gameObject, UISlotType.PrevButton);

            valueLabel = UISetupCommon.CreateText(control.transform, "Value", "--",
                UISetupCommon.FontRowValue, TextAlignmentOptions.Center);
            valueLabel.color = UISetupCommon.ColorSection;
            UISetupCommon.AddLayoutElement(valueLabel.gameObject, flexibleWidth: 1f);
            UISetupCommon.TagSlot(valueLabel.gameObject, UISlotType.ValueLabel);

            next = UISetupCommon.CreateButton(control.transform, "NextButton", ">");
            UISetupCommon.AddLayoutElement(next.gameObject, preferredWidth: CycleButtonWidth, preferredHeight: RowHeight, flexibleWidth: 0f);
            UISetupCommon.TagSlot(next.gameObject, UISlotType.NextButton);

            return row;
        }

        private static GameObject CreateSliderRow(Transform parent, string name, string labelText,
            out Slider slider, out TextMeshProUGUI valueLabel)
        {
            var row = CreateRowShell(parent, name, labelText, out GameObject control);

            slider = BuildSlider(control.transform, "Slider");
            UISetupCommon.AddLayoutElement(slider.gameObject, minWidth: 260f, flexibleWidth: 1f, preferredHeight: 32f);
            UISetupCommon.TagSlot(slider.gameObject, UISlotType.Slider);

            valueLabel = UISetupCommon.CreateText(control.transform, "Value", "0%",
                UISetupCommon.FontRowValue, TextAlignmentOptions.Right);
            valueLabel.color = UISetupCommon.ColorSection;
            UISetupCommon.AddLayoutElement(valueLabel.gameObject, preferredWidth: 90f, flexibleWidth: 0f);
            UISetupCommon.TagSlot(valueLabel.gameObject, UISlotType.ValueLabel);

            return row;
        }

        private static GameObject CreateDropdownRow(Transform parent, string name, string labelText,
            out TMP_Dropdown dropdown)
        {
            var row = CreateRowShell(parent, name, labelText, out GameObject control);

            dropdown = BuildDropdown(control.transform, "Dropdown");
            UISetupCommon.AddLayoutElement(dropdown.gameObject, minWidth: 260f, flexibleWidth: 1f, preferredHeight: 44f);
            UISetupCommon.TagSlot(dropdown.gameObject, UISlotType.Dropdown);

            return row;
        }

        private static Slider BuildSlider(Transform parent, string name)
        {
            var go = UISetupCommon.CreateChild(parent, name, typeof(RectTransform));

            var background = UISetupCommon.CreateChild(go.transform, "Background",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var bgRT = background.GetComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0f, 0.25f);
            bgRT.anchorMax = new Vector2(1f, 0.75f);
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;
            var bgImg = background.GetComponent<Image>();
            bgImg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            bgImg.type = Image.Type.Sliced;
            bgImg.color = UISetupCommon.ColorSliderBG;

            var fillArea = UISetupCommon.CreateChild(go.transform, "Fill Area", typeof(RectTransform));
            var faRT = fillArea.GetComponent<RectTransform>();
            faRT.anchorMin = new Vector2(0f, 0.25f);
            faRT.anchorMax = new Vector2(1f, 0.75f);
            faRT.offsetMin = new Vector2(5f, 0f);
            faRT.offsetMax = new Vector2(-15f, 0f);

            var fill = UISetupCommon.CreateChild(fillArea.transform, "Fill",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var fillRT = fill.GetComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;
            var fillImg = fill.GetComponent<Image>();
            fillImg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            fillImg.type = Image.Type.Sliced;
            fillImg.color = UISetupCommon.ColorSliderFill;

            var handleArea = UISetupCommon.CreateChild(go.transform, "Handle Slide Area", typeof(RectTransform));
            var haRT = handleArea.GetComponent<RectTransform>();
            haRT.anchorMin = new Vector2(0f, 0f);
            haRT.anchorMax = new Vector2(1f, 1f);
            haRT.offsetMin = new Vector2(10f, 0f);
            haRT.offsetMax = new Vector2(-10f, 0f);

            var handle = UISetupCommon.CreateChild(handleArea.transform, "Handle",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var handleRT = handle.GetComponent<RectTransform>();
            handleRT.sizeDelta = new Vector2(20f, 0f);
            var handleImg = handle.GetComponent<Image>();
            handleImg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            handleImg.color = UISetupCommon.ColorSection;

            var slider = go.AddComponent<Slider>();
            slider.targetGraphic = handleImg;
            slider.fillRect = fillRT;
            slider.handleRect = handleRT;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 0.8f;
            return slider;
        }

        private static TMP_Dropdown BuildDropdown(Transform parent, string name)
        {
            var go = UISetupCommon.CreateChild(parent, name,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var bgImg = go.GetComponent<Image>();
            bgImg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            bgImg.type = Image.Type.Sliced;
            bgImg.color = UISetupCommon.ColorDropdownBG;

            var dd = go.AddComponent<TMP_Dropdown>();
            dd.targetGraphic = bgImg;

            var labelGO = UISetupCommon.CreateChild(go.transform, "Label", typeof(RectTransform));
            var lbl = labelGO.AddComponent<TextMeshProUGUI>();
            lbl.fontSize = UISetupCommon.FontRowValue;
            lbl.alignment = TextAlignmentOptions.MidlineLeft;
            lbl.enableAutoSizing = false;
            var font = UISetupCommon.LoadSharedFont();
            if (font != null) lbl.font = font;
            lbl.color = UISetupCommon.ColorBody;
            var lblRT = labelGO.GetComponent<RectTransform>();
            lblRT.anchorMin = Vector2.zero;
            lblRT.anchorMax = Vector2.one;
            lblRT.offsetMin = new Vector2(14f, 4f);
            lblRT.offsetMax = new Vector2(-28f, -4f);
            dd.captionText = lbl;

            var arrowGO = UISetupCommon.CreateChild(go.transform, "Arrow",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var arrowImg = arrowGO.GetComponent<Image>();
            arrowImg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd");
            arrowImg.color = UISetupCommon.ColorSection;
            var arrowRT = arrowGO.GetComponent<RectTransform>();
            arrowRT.anchorMin = new Vector2(1f, 0.5f);
            arrowRT.anchorMax = new Vector2(1f, 0.5f);
            arrowRT.pivot = new Vector2(1f, 0.5f);
            arrowRT.anchoredPosition = new Vector2(-8f, 0f);
            arrowRT.sizeDelta = new Vector2(20f, 20f);

            var templateGO = UISetupCommon.CreateChild(go.transform, "Template",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            var tRT = templateGO.GetComponent<RectTransform>();
            tRT.anchorMin = new Vector2(0f, 0f);
            tRT.anchorMax = new Vector2(1f, 0f);
            tRT.pivot = new Vector2(0.5f, 1f);
            tRT.anchoredPosition = new Vector2(0f, 2f);
            tRT.sizeDelta = new Vector2(0f, 180f);
            templateGO.SetActive(false);

            var tImg = templateGO.GetComponent<Image>();
            tImg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            tImg.type = Image.Type.Sliced;
            tImg.color = UISetupCommon.ColorDropdownBG;

            var viewport = UISetupCommon.CreateChild(templateGO.transform, "Viewport",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
            var vRT = viewport.GetComponent<RectTransform>();
            vRT.anchorMin = new Vector2(0f, 0f);
            vRT.anchorMax = new Vector2(1f, 1f);
            vRT.offsetMin = Vector2.zero;
            vRT.offsetMax = new Vector2(-18f, 0f);
            var vImg = viewport.GetComponent<Image>();
            vImg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd");
            vImg.type = Image.Type.Sliced;
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = UISetupCommon.CreateChild(viewport.transform, "Content", typeof(RectTransform));
            var cRT = content.GetComponent<RectTransform>();
            cRT.anchorMin = new Vector2(0f, 1f);
            cRT.anchorMax = new Vector2(1f, 1f);
            cRT.pivot = new Vector2(0.5f, 1f);
            cRT.anchoredPosition = Vector2.zero;
            cRT.sizeDelta = new Vector2(0f, 28f);

            var item = UISetupCommon.CreateChild(content.transform, "Item", typeof(RectTransform), typeof(Toggle));
            var iRT = item.GetComponent<RectTransform>();
            iRT.anchorMin = new Vector2(0f, 0.5f);
            iRT.anchorMax = new Vector2(1f, 0.5f);
            iRT.sizeDelta = new Vector2(0f, 28f);

            var itemBg = UISetupCommon.CreateChild(item.transform, "Item Background",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var ibRT = itemBg.GetComponent<RectTransform>();
            ibRT.anchorMin = Vector2.zero; ibRT.anchorMax = Vector2.one;
            ibRT.offsetMin = Vector2.zero; ibRT.offsetMax = Vector2.zero;
            var ibImg = itemBg.GetComponent<Image>();
            ibImg.color = UISetupCommon.ColorSliderBG;

            var itemCheck = UISetupCommon.CreateChild(item.transform, "Item Checkmark",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var icRT = itemCheck.GetComponent<RectTransform>();
            icRT.anchorMin = new Vector2(0f, 0.5f);
            icRT.anchorMax = new Vector2(0f, 0.5f);
            icRT.anchoredPosition = new Vector2(10f, 0f);
            icRT.sizeDelta = new Vector2(20f, 20f);
            var icImg = itemCheck.GetComponent<Image>();
            icImg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd");
            icImg.color = UISetupCommon.ColorSection;

            var itemLabelGO = UISetupCommon.CreateChild(item.transform, "Item Label", typeof(RectTransform));
            var il = itemLabelGO.AddComponent<TextMeshProUGUI>();
            il.fontSize = 22;
            il.alignment = TextAlignmentOptions.MidlineLeft;
            il.enableAutoSizing = false;
            if (font != null) il.font = font;
            il.color = UISetupCommon.ColorBody;
            var ilRT = itemLabelGO.GetComponent<RectTransform>();
            ilRT.anchorMin = Vector2.zero; ilRT.anchorMax = Vector2.one;
            ilRT.offsetMin = new Vector2(36f, 2f);
            ilRT.offsetMax = new Vector2(-10f, -2f);
            dd.itemText = il;

            var toggle = item.GetComponent<Toggle>();
            toggle.targetGraphic = ibImg;
            toggle.graphic = icImg;
            toggle.isOn = true;

            var scrollbarGO = UISetupCommon.CreateChild(templateGO.transform, "Scrollbar",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Scrollbar));
            var sbRT = scrollbarGO.GetComponent<RectTransform>();
            sbRT.anchorMin = new Vector2(1f, 0f);
            sbRT.anchorMax = new Vector2(1f, 1f);
            sbRT.pivot = new Vector2(1f, 1f);
            sbRT.sizeDelta = new Vector2(18f, 0f);
            sbRT.anchoredPosition = new Vector2(0f, 0f);
            var sbImg = scrollbarGO.GetComponent<Image>();
            sbImg.color = UISetupCommon.ColorSliderBG;
            var scrollbar = scrollbarGO.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            var sbSliding = UISetupCommon.CreateChild(scrollbarGO.transform, "Sliding Area", typeof(RectTransform));
            var sbSRT = sbSliding.GetComponent<RectTransform>();
            sbSRT.anchorMin = Vector2.zero; sbSRT.anchorMax = Vector2.one;
            sbSRT.offsetMin = new Vector2(4f, 4f); sbSRT.offsetMax = new Vector2(-4f, -4f);

            var sbHandle = UISetupCommon.CreateChild(sbSliding.transform, "Handle",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var sbHRT = sbHandle.GetComponent<RectTransform>();
            sbHRT.sizeDelta = new Vector2(10f, 10f);
            var sbHImg = sbHandle.GetComponent<Image>();
            sbHImg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            sbHImg.type = Image.Type.Sliced;
            sbHImg.color = UISetupCommon.ColorSliderFill;
            scrollbar.targetGraphic = sbHImg;
            scrollbar.handleRect = sbHRT;

            var sr = templateGO.GetComponent<ScrollRect>();
            sr.content = cRT;
            sr.viewport = vRT;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.verticalScrollbar = scrollbar;
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            sr.verticalScrollbarSpacing = -3f;

            dd.template = tRT;

            return dd;
        }
    }
}
