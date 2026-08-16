using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;
using SlimeJumping.UI;

namespace SlimeJumping.EditorTools.UI
{
    /// Adds the video backdrop root to MainMenuScene. Own root only, so UIRoot rewires leave
    /// it alone. Opens the scene additively when it is not already loaded, so the currently
    /// open scene (and its unsaved changes) stay untouched. Creates the shared config asset
    /// on first run; an existing config is never modified — it holds user tuning.
    public static class MenuVideoBackgroundSetup
    {
        private const string MainMenuScenePath = "Assets/Scenes/MainMenuScene.unity";
        private const string ClipFolder = "Assets/Video/Title";
        private const string ConfigAssetPath = "Assets/Resources/MenuBackgroundVideoConfig.asset";
        private const string RootName = "MenuVideoBackground";
        private const int SortingOrder = -100;

        [MenuItem("Tools/SlimeJumping/UI/Setup MainMenu Video Background")]
        public static void Setup()
        {
            EnsureConfigAsset();

            var scene = SceneManager.GetSceneByPath(MainMenuScenePath);
            bool openedHere = false;
            if (!scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Additive);
                openedHere = true;
            }

            foreach (var go in scene.GetRootGameObjects())
                if (go != null && go.name == RootName) Object.DestroyImmediate(go);

            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            var canvas = UISetupCommon.CreateOverlayCanvas(root.transform, "VideoLayer", SortingOrder);
            Object.DestroyImmediate(canvas.GetComponent<GraphicRaycaster>());

            var imgGO = UISetupCommon.CreateChild(canvas.transform, "VideoImage",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(AspectRatioFitter));
            var img = imgGO.GetComponent<RawImage>();
            img.color = Color.white;
            img.raycastTarget = false;
            var halftone = LoadOrCreateHalftoneMaterial();
            if (halftone != null) img.material = halftone;
            var fitter = imgGO.GetComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = 16f / 9f;

            // Vignette above the video; color stays clear until the component applies config.
            var vigGO = UISetupCommon.CreateChild(canvas.transform, "VignetteImage",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            UISetupCommon.FillParent(vigGO);
            var vig = vigGO.GetComponent<RawImage>();
            vig.color = Color.clear;
            vig.raycastTarget = false;

            root.AddComponent<VideoPlayer>().playOnAwake = false;
            var controller = root.AddComponent<MenuBackgroundVideo>();
            var so = new SerializedObject(controller);
            so.FindProperty("target").objectReferenceValue = img;
            so.FindProperty("vignette").objectReferenceValue = vig;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (openedHere) EditorSceneManager.CloseScene(scene, removeScene: true);
            Debug.Log($"[MenuVideoBackgroundSetup] {RootName} wired into {MainMenuScenePath}.");
        }

        /// Halftone look preset; existing asset = user tuning, untouched.
        private static Material LoadOrCreateHalftoneMaterial()
        {
            const string path = "Assets/Settings/UI/Mat_MenuVideoHalftone.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var shader = Shader.Find("SlimeJumping/UI/MenuVideoHalftone");
            if (shader == null) return null;
            var mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            return mat;
        }

        private static void EnsureConfigAsset()
        {
            if (AssetDatabase.LoadAssetAtPath<MenuBackgroundVideoConfig>(ConfigAssetPath) != null) return;

            var config = ScriptableObject.CreateInstance<MenuBackgroundVideoConfig>();
            var guids = AssetDatabase.FindAssets("t:VideoClip", new[] { ClipFolder });
            config.clips = new VideoClip[guids.Length];
            for (int i = 0; i < guids.Length; i++)
                config.clips[i] = AssetDatabase.LoadAssetAtPath<VideoClip>(AssetDatabase.GUIDToAssetPath(guids[i]));
            AssetDatabase.CreateAsset(config, ConfigAssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[MenuVideoBackgroundSetup] Created {ConfigAssetPath} with {guids.Length} clips.");
        }
    }
}
