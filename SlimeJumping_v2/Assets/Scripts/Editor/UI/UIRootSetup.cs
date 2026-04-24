using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem.UI;
using SlimeJumping.UI;
using SlimeJumping.UI.Framework;

namespace SlimeJumping.EditorTools.UI
{
    /// Builds Assets/Prefabs/UI/UIRoot.prefab: 4 layer canvases, EventSystem,
    /// UIManager, click-sound installer, and the two fullscreen-effect children
    /// (Flash + Jumpscare) under the FullscreenLayer canvas.
    public static class UIRootSetup
    {
        public const string UIRootPrefabPath = "Assets/Prefabs/UI/UIRoot.prefab";

        [MenuItem("Tools/SlimeJumping/UI/Build or Rebuild UIRoot Prefab")]
        public static void BuildUIRootMenu()
        {
            var path = Build();
            Debug.Log($"[UISetup] UIRoot prefab saved: {path}");
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        public static string Build()
        {
            UISetupCommon.EnsureFolder(UISetupCommon.PrefabRoot);

            var root = new GameObject("UIRoot", typeof(RectTransform));
            var rootRT = root.GetComponent<RectTransform>();
            rootRT.anchorMin = Vector2.zero;
            rootRT.anchorMax = Vector2.one;
            rootRT.offsetMin = Vector2.zero;
            rootRT.offsetMax = Vector2.zero;

            // EventSystem
            var eventSystemGO = UISetupCommon.CreateChild(root.transform, "EventSystem",
                typeof(EventSystem), typeof(InputSystemUIInputModule));

            // Layer canvases (sortingOrder enforces z stacking)
            var hud = UISetupCommon.CreateOverlayCanvas(root.transform, "HudLayer", 100);
            var menu = UISetupCommon.CreateOverlayCanvas(root.transform, "MenuLayer", 500);
            var modal = UISetupCommon.CreateOverlayCanvas(root.transform, "ModalLayer", 800);
            var fullscreen = UISetupCommon.CreateOverlayCanvas(root.transform, "FullscreenLayer", 9000);

            // Fullscreen effect children (Flash + Jumpscare) inside FullscreenLayer
            BuildFullscreenEffect(fullscreen.transform, "FlashCanvas", addFlash: true);
            BuildFullscreenEffect(fullscreen.transform, "JumpscareCanvas", addFlash: false);

            // UIManager on root, wired to the layer canvases
            var manager = root.AddComponent<UIManager>();
            var managerSO = new SerializedObject(manager);
            managerSO.FindProperty("hudLayer").objectReferenceValue = hud;
            managerSO.FindProperty("menuLayer").objectReferenceValue = menu;
            managerSO.FindProperty("modalLayer").objectReferenceValue = modal;
            managerSO.FindProperty("fullscreenLayer").objectReferenceValue = fullscreen;
            managerSO.ApplyModifiedPropertiesWithoutUndo();

            // Click-sound installer on root covers all buttons under UIRoot
            var clickInstaller = root.AddComponent<UIClickSoundInstaller>();
            var clickSO = new SerializedObject(clickInstaller);
            var clip = UISetupCommon.LoadByGuid<AudioClip>(UISetupCommon.UIClickClipGuid);
            var mixer = UISetupCommon.LoadByGuid<UnityEngine.Audio.AudioMixerGroup>(UISetupCommon.UIMixerGroupGuid);
            if (clip != null) clickSO.FindProperty("clickClip").objectReferenceValue = clip;
            if (mixer != null) clickSO.FindProperty("outputGroup").objectReferenceValue = mixer;
            clickSO.FindProperty("volume").floatValue = 0.8f;
            clickSO.FindProperty("pitch").floatValue = 1f;
            clickSO.ApplyModifiedPropertiesWithoutUndo();

            var saved = PrefabUtility.SaveAsPrefabAsset(root, UIRootPrefabPath, out bool success);
            if (!success) Debug.LogError($"[UISetup] Failed to save UIRoot at {UIRootPrefabPath}");
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return UIRootPrefabPath;
        }

        private static void BuildFullscreenEffect(Transform parent, string name, bool addFlash)
        {
            var go = UISetupCommon.CreateChild(parent, name, typeof(RectTransform), typeof(CanvasGroup));
            UISetupCommon.FillParent(go);
            var cg = go.GetComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;

            var imgGO = UISetupCommon.CreateChild(go.transform, "FullscreenImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            UISetupCommon.FillParent(imgGO);
            var img = imgGO.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 1f);
            img.raycastTarget = false;

            if (addFlash)
            {
                var controller = go.AddComponent<FlashCanvasController>();
                var so = new SerializedObject(controller);
                so.FindProperty("canvasGroup").objectReferenceValue = cg;
                so.FindProperty("fullscreenImage").objectReferenceValue = img;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                var controller = go.AddComponent<JumpscareCanvasController>();
                var so = new SerializedObject(controller);
                so.FindProperty("canvasGroup").objectReferenceValue = cg;
                so.FindProperty("fullscreenImage").objectReferenceValue = img;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
