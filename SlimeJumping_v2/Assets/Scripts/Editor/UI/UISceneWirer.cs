using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SlimeJumping.InputHandling;
using SlimeJumping.UI.Framework;

namespace SlimeJumping.EditorTools.UI
{
    /// One-shot migration helpers: strip old per-scene UI Canvases and drop in
    /// the new UIRoot prefab + requested panel prefabs. Invoked via unity-cli
    /// exec once per scene during the UI framework rollout.
    public static class UISceneWirer
    {
        private const string MainMenuScenePath = "Assets/Scenes/MainMenuScene.unity";
        private const string MainScenePath = "Assets/Scenes/MainScene.unity";
        private const string EndScenePath = "Assets/Scenes/EndScene.unity";

        // Top-level GameObject names deleted when wiring a scene. Missing names
        // are silently skipped so the wirer is idempotent (safe to re-run).
        private static readonly string[] OldCanvasNames =
        {
            "MainMenuCanvas", "OptionsCanvas", "EndCanvas", "FullscreenEffectCanvas", "EventSystem",
            // Controller host GameObjects (legacy — standalone GOs hosting the now-deleted scripts).
            "MainMenuController", "DemoEndController",
        };

        public static string WireMainMenuScene()
        {
            var scene = EditorSceneManager.OpenScene(MainMenuScenePath);
            RemoveOldCanvases(scene);
            var uiRoot = InstantiateUIRoot();
            InstantiatePanel(uiRoot, MainMenuPanelSetup.PrefabPath);
            InstantiatePanel(uiRoot, OptionsPanelSetup.PrefabPath);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"Wired {scene.name}";
        }

        public static string WireMainScene()
        {
            var scene = EditorSceneManager.OpenScene(MainScenePath);
            RemoveOldCanvases(scene);
            var uiRoot = InstantiateUIRoot();
            InstantiatePanel(uiRoot, OptionsPanelSetup.PrefabPath);

            var manager = uiRoot.GetComponent<UIManager>();
            var reader = Object.FindFirstObjectByType<SlimeInputReader>(FindObjectsInactive.Include);
            if (manager != null && reader != null)
            {
                var so = new SerializedObject(manager);
                so.FindProperty("slimeInputReader").objectReferenceValue = reader;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"Wired {scene.name}";
        }

        public static string WireEndScene()
        {
            var scene = EditorSceneManager.OpenScene(EndScenePath);
            RemoveOldCanvases(scene);
            var uiRoot = InstantiateUIRoot();
            InstantiatePanel(uiRoot, EndPanelSetup.PrefabPath);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"Wired {scene.name}";
        }

        private static void RemoveOldCanvases(Scene scene)
        {
            var set = new HashSet<string>(OldCanvasNames);
            var doomed = new List<GameObject>();
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go == null) continue;
                if (set.Contains(go.name)) doomed.Add(go);
            }
            for (int i = 0; i < doomed.Count; i++)
                Object.DestroyImmediate(doomed[i]);
        }

        private static GameObject InstantiateUIRoot()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(UIRootSetup.UIRootPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[UISceneWirer] UIRoot prefab missing at {UIRootSetup.UIRootPrefabPath}. Run Tools → SlimeJumping → UI → Build or Rebuild UIRoot Prefab first.");
                return null;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = "UIRoot";
            return instance;
        }

        private static GameObject InstantiatePanel(GameObject uiRoot, string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[UISceneWirer] Panel prefab missing at {prefabPath}.");
                return null;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (uiRoot != null) instance.transform.SetParent(uiRoot.transform, worldPositionStays: false);
            return instance;
        }
    }
}
