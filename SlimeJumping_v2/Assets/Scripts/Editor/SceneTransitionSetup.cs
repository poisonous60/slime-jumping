using System.IO;
using SlimeJumping.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SlimeJumping.EditorTools
{
    /// One-shot generator for the SceneTransition prefab + iris material.
    public static class SceneTransitionSetup
    {
        private const string ResourcesFolder = "Assets/Resources";
        private const string PrefabPath = "Assets/Resources/SceneTransition.prefab";
        private const string MaterialPath = "Assets/Resources/SceneTransitionIris.mat";
        private const string ShaderName = "SlimeJumping/UI/IrisMask";

        // Clip picks — name prefixes, matched by AssetDatabase search.
        private const string FadeOutClipName = "Ambient_Game_Bubble_UI_1";
        private const string IrisOpenClipName = "Fantasy_Game_UI_Material_Liquid_Bubble_Pop_1";
        private const string IrisExpandClipName = "Ambient_Game_Bubble_Potion_Liquid_Buff_Item_14";

        [MenuItem("Tools/SlimeJumping/Setup Scene Transition")]
        public static void Setup()
        {
            if (!AssetDatabase.IsValidFolder(ResourcesFolder))
                AssetDatabase.CreateFolder("Assets", "Resources");

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[SceneTransitionSetup] Shader '{ShaderName}' not found. " +
                               "Make sure IrisMask.shader compiled without errors.");
                return;
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            else
            {
                mat.shader = shader;
            }
            mat.SetColor("_Color", Color.black);
            mat.SetVector("_Center", new Vector4(0.5f, 0.5f, 0f, 0f));
            mat.SetFloat("_Radius", 0f);
            mat.SetFloat("_Edge", 0.008f);
            mat.SetFloat("_Aspect", 16f / 9f);
            EditorUtility.SetDirty(mat);

            var root = new GameObject("SceneTransition");
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();

            var group = root.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            group.blocksRaycasts = true;
            group.interactable = false;

            var fadeGo = new GameObject("Fade");
            fadeGo.transform.SetParent(root.transform, false);
            var fadeRt = fadeGo.AddComponent<RectTransform>();
            StretchFill(fadeRt);
            var fadeImg = fadeGo.AddComponent<Image>();
            fadeImg.color = new Color(0f, 0f, 0f, 0f);
            fadeImg.raycastTarget = true;

            var irisGo = new GameObject("Iris");
            irisGo.transform.SetParent(root.transform, false);
            var irisRt = irisGo.AddComponent<RectTransform>();
            StretchFill(irisRt);
            var irisImg = irisGo.AddComponent<Image>();
            irisImg.material = mat;
            irisImg.color = Color.white;
            irisImg.raycastTarget = true;
            irisGo.SetActive(false);

            var audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.loop = false;
            audio.spatialBlend = 0f;
            audio.volume = 1f;

            var ctrl = root.AddComponent<SceneTransitionController>();
            var so = new SerializedObject(ctrl);
            so.FindProperty("canvas").objectReferenceValue = canvas;
            so.FindProperty("canvasGroup").objectReferenceValue = group;
            so.FindProperty("irisImage").objectReferenceValue = irisImg;
            so.FindProperty("fadeImage").objectReferenceValue = fadeImg;
            so.FindProperty("audioSource").objectReferenceValue = audio;
            so.FindProperty("fadeOutClip").objectReferenceValue = FindClip(FadeOutClipName);
            so.FindProperty("irisOpenClip").objectReferenceValue = FindClip(IrisOpenClipName);
            so.FindProperty("irisExpandClip").objectReferenceValue = FindClip(IrisExpandClipName);
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[SceneTransitionSetup] Prefab created at {PrefabPath} and material at {MaterialPath}.");
        }

        private static void StretchFill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        private static AudioClip FindClip(string namePrefix)
        {
            string[] guids = AssetDatabase.FindAssets($"t:AudioClip {namePrefix}");
            foreach (string g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(path).StartsWith(namePrefix))
                    return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }
            Debug.LogWarning($"[SceneTransitionSetup] Audio clip '{namePrefix}*' not found — leaving unset.");
            return null;
        }
    }
}
