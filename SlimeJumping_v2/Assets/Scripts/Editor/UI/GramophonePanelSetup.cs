using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SlimeJumping.UI.Animation;
using SlimeJumping.UI.Framework;
using SlimeJumping.UI.Panels;

namespace SlimeJumping.EditorTools.UI
{
    /// Builds Assets/Prefabs/UI/Panels/GramophonePanel.prefab.
    /// Layout: dim backdrop + centered box → Title / Subtitle / ScrollRect of
    /// track rows / Stop+Cancel action row. The ScrollRect uses a
    /// VerticalLayoutGroup + ContentSizeFitter on its Content so an arbitrary
    /// number of track rows scrolls cleanly.
    public static class GramophonePanelSetup
    {
        public const string PrefabPath = UISetupCommon.PanelPrefabRoot + "/GramophonePanel.prefab";

        private const float BoxWidth = 720f;
        private const float BoxHeight = 820f;
        private const float ScrollHeight = 480f;
        private const float RowHeight = 64f;
        private const float ActionButtonWidth = 260f;
        private const float ScrollbarWidth = 14f;

        [MenuItem("Tools/SlimeJumping/UI/Build or Rebuild GramophonePanel Prefab")]
        public static void BuildMenu()
        {
            var path = Build();
            Debug.Log($"[UISetup] GramophonePanel prefab saved: {path}");
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        public static string Build()
        {
            var root = UISetupCommon.CreatePanelRoot("GramophonePanel",
                new Vector2(UISetupCommon.CanvasRefWidth, UISetupCommon.CanvasRefHeight));
            var panel = root.AddComponent<GramophonePanel>();

            // Backdrop
            var backdrop = UISetupCommon.CreateChild(root.transform, "Backdrop",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            UISetupCommon.FillParent(backdrop);
            backdrop.GetComponent<Image>().color = UISetupCommon.ColorBackdropDim;

            // Centered box
            var box = UISetupCommon.CreateCenteredBox(root.transform, "Box", new Vector2(BoxWidth, BoxHeight));
            UISetupCommon.AddVerticalLayout(box, padding: 28, spacing: 12, childAlign: TextAnchor.UpperCenter);
            UISetupCommon.TagSlot(box, UISlotType.ContentRoot);
            UISetupCommon.AddContainerStagger(box);

            // Second stagger lives on the panel root and is used by
            // GramophonePanel.RebuildList to animate dynamically spawned track
            // rows via PlayForList(). manualTriggerOnly so UIPanel.Open() does
            // not also fire it against the panel root's structural children.
            var rowStagger = UISetupCommon.AddContainerStagger(root,
                UIContainerStagger.StaggerEffect.ScalePop, manualTriggerOnly: true);

            // Title
            var title = UISetupCommon.CreateText(box.transform, "Title", "Phonograph",
                UISetupCommon.FontTitle, TextAlignmentOptions.Center);
            title.color = UISetupCommon.ColorTitle;
            title.fontStyle = FontStyles.Bold;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            UISetupCommon.AddLayoutElement(title.gameObject, minHeight: 82f, preferredHeight: 92f);
            UISetupCommon.TagSlot(title.gameObject, UISlotType.Title);

            // Subtitle
            var subtitle = UISetupCommon.CreateText(box.transform, "Subtitle", "Select a track",
                UISetupCommon.FontSection, TextAlignmentOptions.Center);
            subtitle.color = UISetupCommon.ColorSection;
            UISetupCommon.AddLayoutElement(subtitle.gameObject, minHeight: 40f, preferredHeight: 44f);
            UISetupCommon.TagSlot(subtitle.gameObject, UISlotType.Subtitle);

            // Scroll view
            var scrollGO = UISetupCommon.CreateChild(box.transform, "Scroll",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            var scrollImg = scrollGO.GetComponent<Image>();
            scrollImg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            scrollImg.type = Image.Type.Sliced;
            scrollImg.color = UISetupCommon.ColorSliderBG;
            UISetupCommon.AddLayoutElement(scrollGO, minHeight: ScrollHeight, preferredHeight: ScrollHeight);

            // RectMask2D instead of Mask + Image — Mask's stencil setup is
            // fragile when children are instantiated from an inactive template
            // at runtime (rows render blank). RectMask2D is CPU-clipped and
            // works regardless of activation order.
            var viewport = UISetupCommon.CreateChild(scrollGO.transform, "Viewport",
                typeof(RectTransform), typeof(RectMask2D));
            var viewportRT = viewport.GetComponent<RectTransform>();
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.offsetMin = new Vector2(8f, 8f);
            viewportRT.offsetMax = new Vector2(-(ScrollbarWidth + 8f), -8f);
            UISetupCommon.TagSlot(viewport, UISlotType.ScrollViewport);

            // Content — holds dynamic track rows. VerticalLayoutGroup sizes each
            // row, ContentSizeFitter tracks overall height so the scrollbar works.
            var content = UISetupCommon.CreateChild(viewport.transform, "Content", typeof(RectTransform));
            var contentRT = content.GetComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0f, 1f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot = new Vector2(0.5f, 1f);
            contentRT.anchoredPosition = Vector2.zero;
            contentRT.sizeDelta = new Vector2(0f, 0f);
            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.spacing = 8;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            UISetupCommon.TagSlot(content, UISlotType.TrackListContent);

            // Scrollbar (vertical)
            var scrollbarGO = UISetupCommon.CreateChild(scrollGO.transform, "Scrollbar",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Scrollbar));
            var sbRT = scrollbarGO.GetComponent<RectTransform>();
            sbRT.anchorMin = new Vector2(1f, 0f);
            sbRT.anchorMax = new Vector2(1f, 1f);
            sbRT.pivot = new Vector2(1f, 0.5f);
            sbRT.sizeDelta = new Vector2(ScrollbarWidth, -16f);
            sbRT.anchoredPosition = new Vector2(-4f, 0f);
            var sbImg = scrollbarGO.GetComponent<Image>();
            sbImg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            sbImg.type = Image.Type.Sliced;
            sbImg.color = UISetupCommon.ColorSliderBG;
            var scrollbar = scrollbarGO.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            var sbSlide = UISetupCommon.CreateChild(scrollbarGO.transform, "Sliding Area", typeof(RectTransform));
            var sbSlideRT = sbSlide.GetComponent<RectTransform>();
            sbSlideRT.anchorMin = Vector2.zero; sbSlideRT.anchorMax = Vector2.one;
            sbSlideRT.offsetMin = new Vector2(2f, 2f); sbSlideRT.offsetMax = new Vector2(-2f, -2f);

            var sbHandle = UISetupCommon.CreateChild(sbSlide.transform, "Handle",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var sbHandleRT = sbHandle.GetComponent<RectTransform>();
            sbHandleRT.sizeDelta = new Vector2(10f, 10f);
            var sbHandleImg = sbHandle.GetComponent<Image>();
            sbHandleImg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            sbHandleImg.type = Image.Type.Sliced;
            sbHandleImg.color = UISetupCommon.ColorSliderFill;
            scrollbar.targetGraphic = sbHandleImg;
            scrollbar.handleRect = sbHandleRT;

            var scrollRect = scrollGO.GetComponent<ScrollRect>();
            scrollRect.viewport = viewportRT;
            scrollRect.content = contentRT;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            // Track row template — inactive prefab child copied per entry at runtime.
            var rowTemplate = UISetupCommon.CreateButton(content.transform, "TrackRowTemplate", "Track");
            UISetupCommon.AddLayoutElement(rowTemplate.gameObject,
                minHeight: RowHeight, preferredHeight: RowHeight);
            rowTemplate.gameObject.SetActive(false);
            UISetupCommon.TagSlot(rowTemplate.gameObject, UISlotType.TrackRowTemplate);

            // Action row (Stop + Cancel)
            var actions = UISetupCommon.CreateChild(box.transform, "Actions", typeof(RectTransform));
            UISetupCommon.AddHorizontalLayout(actions, padding: 0, spacing: 16,
                childAlign: TextAnchor.MiddleCenter, expandWidth: true);
            UISetupCommon.AddLayoutElement(actions, minHeight: 64f, preferredHeight: 68f);

            var stopBtn = UISetupCommon.CreateButton(actions.transform, "StopButton", "Stop music");
            UISetupCommon.AddLayoutElement(stopBtn.gameObject, preferredWidth: ActionButtonWidth);
            UISetupCommon.TagSlot(stopBtn.gameObject, UISlotType.StopButton);

            var cancelBtn = UISetupCommon.CreateButton(actions.transform, "CancelButton", "Cancel");
            UISetupCommon.AddLayoutElement(cancelBtn.gameObject, preferredWidth: ActionButtonWidth);
            UISetupCommon.TagSlot(cancelBtn.gameObject, UISlotType.CancelButton);

            // Wire panel fields
            var so = new SerializedObject(panel);
            so.FindProperty("canvasGroup").objectReferenceValue = root.GetComponent<CanvasGroup>();
            so.FindProperty("layer").enumValueIndex = (int)UILayer.Modal;
            so.FindProperty("isModal").boolValue = true;
            so.FindProperty("openOnStart").boolValue = false;
            so.FindProperty("openOnEscape").boolValue = false;
            so.FindProperty("juiceProfile").objectReferenceValue = UISetupCommon.LoadOrCreateDefaultJuiceProfile();
            so.FindProperty("trackListContent").objectReferenceValue = contentRT;
            so.FindProperty("trackRowTemplate").objectReferenceValue = rowTemplate.gameObject;
            so.FindProperty("titleLabel").objectReferenceValue = title;
            so.FindProperty("subtitleLabel").objectReferenceValue = subtitle;
            so.FindProperty("stopButton").objectReferenceValue = stopBtn;
            so.FindProperty("cancelButton").objectReferenceValue = cancelBtn;
            so.FindProperty("rowStagger").objectReferenceValue = rowStagger;
            so.ApplyModifiedPropertiesWithoutUndo();

            return UISetupCommon.SavePanelPrefab(root, "GramophonePanel");
        }
    }
}
