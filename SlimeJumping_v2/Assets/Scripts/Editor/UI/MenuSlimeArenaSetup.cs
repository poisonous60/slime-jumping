using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SlimeJumping.UI;
using SlimeJumping.UI.Framework;

namespace SlimeJumping.EditorTools.UI
{
    /// Places the playable slime pen into MainMenuScene: clones the Slime and light from
    /// MainScene (scene-tuned instances, not prefab defaults), pulls the camera back so
    /// world objects are visible, and drops the video canvas onto the camera plane so it
    /// renders behind the slime. Scenes already open in the editor are used in place and
    /// never closed; only MainMenuScene is saved. Root deletion = full rollback.
    public static class MenuSlimeArenaSetup
    {
        private const string MainMenuScenePath = "Assets/Scenes/MainMenuScene.unity";
        private const string MainScenePath = "Assets/Scenes/MainScene.unity";
        private const string ConfigAssetPath = "Assets/Resources/MenuSlimeArenaConfig.asset";
        private const string RootName = "MenuSlimeArena";
        private const float CameraZ = -10f;
        private const float VideoPlaneDistance = 20f;
        private const float SlimeDropMargin = 10f;
        private const float SlimeDropX = -0.52f;

        [MenuItem("Tools/SlimeJumping/UI/Setup MainMenu Slime Arena")]
        public static void Setup()
        {
            EnsureConfigAsset();

            var menuScene = SceneManager.GetSceneByPath(MainMenuScenePath);
            bool openedMenuHere = false;
            if (!menuScene.isLoaded)
            {
                menuScene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Additive);
                openedMenuHere = true;
            }
            var mainScene = SceneManager.GetSceneByPath(MainScenePath);
            bool openedMainHere = false;
            if (!mainScene.isLoaded)
            {
                mainScene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Additive);
                openedMainHere = true;
            }

            var previousActive = SceneManager.GetActiveScene();
            try
            {
                foreach (var go in menuScene.GetRootGameObjects())
                    if (go != null && go.name == RootName) Object.DestroyImmediate(go);

                UnityEngine.Camera cam = null;
                GameObject uiRoot = null, videoRoot = null;
                foreach (var go in menuScene.GetRootGameObjects())
                {
                    if (go.name == "Main Camera") cam = go.GetComponent<UnityEngine.Camera>();
                    else if (go.name == "UIRoot") uiRoot = go;
                    else if (go.name == "MenuVideoBackground") videoRoot = go;
                }
                if (cam == null)
                {
                    Debug.LogError("[MenuSlimeArenaSetup] MainMenuScene 에 Main Camera 없음.");
                    return;
                }

                // 슬라임(z=0)이 카메라 앞에 오도록 뒤로.
                cam.transform.position = new Vector3(0f, 0f, CameraZ);

                // 오버레이 캔버스는 월드 오브젝트를 항상 덮는다 — 영상만 카메라 평면으로 내린다.
                if (videoRoot != null)
                {
                    var videoCanvas = videoRoot.GetComponentInChildren<Canvas>(true);
                    if (videoCanvas != null)
                    {
                        videoCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                        videoCanvas.worldCamera = cam;
                        videoCanvas.planeDistance = VideoPlaneDistance;
                    }
                }

                GameObject srcSlime = null, srcLight = null;
                foreach (var go in mainScene.GetRootGameObjects())
                {
                    if (go.name == "Slime") srcSlime = go;
                    else if (go.name == "Directional Light") srcLight = go;
                }
                if (srcSlime == null)
                {
                    Debug.LogError("[MenuSlimeArenaSetup] MainScene 에서 Slime 루트를 못 찾았다.");
                    return;
                }

                SceneManager.SetActiveScene(mainScene);
                var sky = RenderSettings.skybox;
                var ambientMode = RenderSettings.ambientMode;
                float ambientIntensity = RenderSettings.ambientIntensity;

                // Instantiate 는 활성 씬에 생성된다 — 메뉴 씬으로 전환 후 복제.
                SceneManager.SetActiveScene(menuScene);
                RenderSettings.skybox = sky;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientIntensity = ambientIntensity;

                var root = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(root, menuScene);

                var slime = Object.Instantiate(srcSlime);
                slime.name = "Slime";
                slime.transform.SetParent(root.transform, true);
                slime.transform.position = new Vector3(SlimeDropX, cam.orthographicSize + SlimeDropMargin, 0f);

                if (srcLight != null)
                {
                    var light = Object.Instantiate(srcLight);
                    light.name = "Directional Light";
                    light.transform.SetParent(root.transform, true);
                }

                var arena = root.AddComponent<MenuSlimeArena>();
                var so = new SerializedObject(arena);
                so.FindProperty("arenaCamera").objectReferenceValue = cam;
                so.ApplyModifiedPropertiesWithoutUndo();

                // 모달이 슬라임 입력을 잠글 수 있게 리더 배선 (EndScene 와이어러와 동일 패턴).
                if (uiRoot != null)
                {
                    var manager = uiRoot.GetComponent<UIManager>();
                    var reader = slime.GetComponentInChildren<SlimeJumping.InputHandling.SlimeInputReader>(true);
                    if (manager != null && reader != null)
                    {
                        var mso = new SerializedObject(manager);
                        mso.FindProperty("slimeInputReader").objectReferenceValue = reader;
                        mso.ApplyModifiedPropertiesWithoutUndo();
                    }
                }

                EditorSceneManager.MarkSceneDirty(menuScene);
                EditorSceneManager.SaveScene(menuScene);
                Debug.Log("[MenuSlimeArenaSetup] 슬라임 아레나 배치 완료.");
            }
            finally
            {
                if (previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
                if (openedMainHere && mainScene.isLoaded) EditorSceneManager.CloseScene(mainScene, true);
                if (openedMenuHere && menuScene.isLoaded) EditorSceneManager.CloseScene(menuScene, true);
            }
        }

        private static void EnsureConfigAsset()
        {
            if (AssetDatabase.LoadAssetAtPath<MenuSlimeArenaConfig>(ConfigAssetPath) != null) return;
            var config = ScriptableObject.CreateInstance<MenuSlimeArenaConfig>();
            AssetDatabase.CreateAsset(config, ConfigAssetPath);
            AssetDatabase.SaveAssets();
        }
    }
}
