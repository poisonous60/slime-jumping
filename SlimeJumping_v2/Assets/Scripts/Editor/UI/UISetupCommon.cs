using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SlimeJumping.UI.Framework;

namespace SlimeJumping.EditorTools.UI
{
    /// Shared helpers for procedural UI prefab construction. Mirrors the pattern
    /// of FullscreenCanvasSetupUtility and MemoSetup: each builder uses these
    /// primitives to assemble a prefab, then PrefabUtility saves the result.
    internal static class UISetupCommon
    {
        public const string PrefabRoot = "Assets/Prefabs/UI";
        public const string PanelPrefabRoot = "Assets/Prefabs/UI/Panels";

        public const float CanvasRefWidth = 1920f;
        public const float CanvasRefHeight = 1080f;

        // Pinned font sizes.
        public const int FontTitle = 72;
        public const int FontSection = 28;
        public const int FontRowLabel = 24;
        public const int FontRowValue = 24;
        public const int FontButton = 30;
        public const int FontBody = 24;

        // Mint / green palette.
        public static readonly Color ColorTitle       = new Color(0.82f, 0.96f, 0.89f, 1f); // pale mint
        public static readonly Color ColorSection     = new Color(0.56f, 0.88f, 0.74f, 1f); // mint accent
        public static readonly Color ColorBody        = new Color(0.91f, 0.97f, 0.94f, 1f); // off-white mint
        public static readonly Color ColorRowLabel    = new Color(0.91f, 0.97f, 0.94f, 1f);
        public static readonly Color ColorButtonBG    = new Color(0.56f, 0.87f, 0.72f, 1f); // mint button base
        public static readonly Color ColorButtonText  = new Color(0.05f, 0.18f, 0.13f, 1f); // deep green text
        public static readonly Color ColorBoxBG       = new Color(0.04f, 0.11f, 0.09f, 0.92f); // deep green panel bg
        public static readonly Color ColorBackdropDim = new Color(0f, 0.06f, 0.04f, 0.55f);
        public static readonly Color ColorSliderBG    = new Color(0.10f, 0.22f, 0.17f, 1f);
        public static readonly Color ColorSliderFill  = new Color(0.46f, 0.84f, 0.68f, 1f);
        public static readonly Color ColorDropdownBG  = new Color(0.08f, 0.17f, 0.13f, 1f);

        // Assets with stable guids already used elsewhere in the project.
        public const string UIClickClipGuid = "a2ba3a3e7e55dfa469a4c562510044f1";
        public const string UIMixerGroupGuid = "f5c4b4cbe33287f46a2b3ebe1665485f";
        public const string TMPFontGuid = "8f586378b4e144a9851e7b34d9b748ee";

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            var leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        public static T LoadByGuid<T>(string guid) where T : Object
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        public static TMP_FontAsset LoadSharedFont()
        {
            return LoadByGuid<TMP_FontAsset>(TMPFontGuid);
        }

        public static GameObject CreateChild(Transform parent, string name, params System.Type[] components)
        {
            var go = new GameObject(name, components);
            go.transform.SetParent(parent, worldPositionStays: false);
            return go;
        }

        public static RectTransform FillParent(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
            return rt;
        }

        public static Canvas CreateOverlayCanvas(Transform parent, string name, int sortingOrder)
        {
            // These ScreenSpaceOverlay canvases are nested under UIRoot, which has
            // no parent Canvas. That makes them root-level canvases at render time
            // (Unity treats ScreenSpaceOverlay canvases as roots regardless of
            // hierarchy), so sortingOrder applies directly without overrideSorting.
            var go = CreateChild(parent, name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            ConfigureScaler(go.GetComponent<CanvasScaler>());
            return canvas;
        }

        public static void ConfigureScaler(CanvasScaler scaler)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(CanvasRefWidth, CanvasRefHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = 100f;
        }

        public static void TagSlot(GameObject go, UISlotType type)
        {
            var slot = go.GetComponent<UISlot>();
            if (slot == null) slot = go.AddComponent<UISlot>();
            slot.slotType = type;
        }

        public static TextMeshProUGUI CreateText(Transform parent, string name, string text, int fontSize,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var go = CreateChild(parent, name, typeof(RectTransform));
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = align;
            tmp.enableAutoSizing = false;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Overflow;
            var font = LoadSharedFont();
            if (font != null) tmp.font = font;
            tmp.color = ColorBody;
            return tmp;
        }

        public static Button CreateButton(Transform parent, string name, string label, int fontSize = FontButton)
        {
            var go = CreateChild(parent, name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var img = go.GetComponent<Image>();
            img.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            img.type = Image.Type.Sliced;
            img.color = ColorButtonBG;
            var button = go.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(0.80f, 0.92f, 0.86f, 1f);
            colors.selectedColor = new Color(1.05f, 1.05f, 1.05f, 1f);
            button.colors = colors;

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 56f;
            le.preferredHeight = 64f;

            var labelTmp = CreateText(go.transform, "Label", label, fontSize, TextAlignmentOptions.Center);
            labelTmp.color = ColorButtonText;
            labelTmp.fontStyle = FontStyles.Bold;
            var lrt = labelTmp.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(12f, 4f);
            lrt.offsetMax = new Vector2(-12f, -4f);

            return button;
        }

        public static GameObject CreatePanelRoot(string name, Vector2 preferredSize)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            var rt = root.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var cg = root.GetComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;
            return root;
        }

        public static GameObject CreateCenteredBox(Transform parent, string name, Vector2 size)
        {
            var go = CreateChild(parent, name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            img.type = Image.Type.Sliced;
            img.color = ColorBoxBG;
            return go;
        }

        public static VerticalLayoutGroup AddVerticalLayout(GameObject go, int padding, int spacing,
            TextAnchor childAlign = TextAnchor.UpperCenter, bool expandHeight = false)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(padding, padding, padding, padding);
            v.spacing = spacing;
            v.childAlignment = childAlign;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = expandHeight;
            v.childScaleWidth = false;
            v.childScaleHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup AddHorizontalLayout(GameObject go, int padding, int spacing,
            TextAnchor childAlign = TextAnchor.MiddleLeft, bool expandWidth = false)
        {
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(padding, padding, padding, padding);
            h.spacing = spacing;
            h.childAlignment = childAlign;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = expandWidth;
            h.childForceExpandHeight = false;
            h.childScaleWidth = false;
            h.childScaleHeight = false;
            return h;
        }

        public static LayoutElement AddLayoutElement(GameObject go, float minWidth = -1, float minHeight = -1,
            float preferredWidth = -1, float preferredHeight = -1, float flexibleWidth = -1)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            if (minWidth >= 0) le.minWidth = minWidth;
            if (minHeight >= 0) le.minHeight = minHeight;
            if (preferredWidth >= 0) le.preferredWidth = preferredWidth;
            if (preferredHeight >= 0) le.preferredHeight = preferredHeight;
            if (flexibleWidth >= 0) le.flexibleWidth = flexibleWidth;
            return le;
        }

        public static string SavePanelPrefab(GameObject root, string fileName)
        {
            EnsureFolder(PanelPrefabRoot);
            var path = $"{PanelPrefabRoot}/{fileName}.prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            if (!success) Debug.LogError($"[UISetup] Failed to save prefab at {path}");
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return path;
        }
    }
}
