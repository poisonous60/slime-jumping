using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

namespace SlimeJumping.Editor
{
    public static class FullscreenCanvasSetupUtility
    {
        const string RootName = "FullscreenEffectCanvas";

        public static GameObject GetOrCreateRoot()
        {
            var existing = GameObject.Find(RootName);
            if (existing != null) return existing;

            var go = new GameObject(RootName,
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(go, "Create FullscreenEffectCanvas");

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9000;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            SceneVisibilityManager.instance.Hide(go, true);
            return go;
        }

        public static GameObject GetOrCreateEffectChild(GameObject root, string childName)
        {
            var existingTr = root.transform.Find(childName);
            if (existingTr != null) return existingTr.gameObject;

            var go = new GameObject(childName, typeof(RectTransform), typeof(CanvasGroup));
            Undo.RegisterCreatedObjectUndo(go, "Create " + childName);
            go.transform.SetParent(root.transform, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var cg = go.GetComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;

            return go;
        }

        public static Image AddFullscreenImage(GameObject effectChild, Color color)
        {
            var existing = effectChild.transform.Find("FullscreenImage");
            if (existing != null) return existing.GetComponent<Image>();

            var imgGo = new GameObject("FullscreenImage",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imgGo.transform.SetParent(effectChild.transform, false);
            var rt = imgGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = imgGo.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }
    }
}
