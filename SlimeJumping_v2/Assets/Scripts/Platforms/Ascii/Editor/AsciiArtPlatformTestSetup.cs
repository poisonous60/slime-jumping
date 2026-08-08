using System.IO;
using UnityEditor;
using UnityEngine;
using SlimeJumping.Platforms;

namespace SlimeJumping.Editor
{
    // 아스키아트 발판 테스트 셋업: AssetVault 의 1.txt 를 프로젝트로 복사(1회)하고,
    // 씬뷰 피벗에 발판 + 접근용 바닥을 새 루트로 배치한다. 기존 씬 오브젝트는 건드리지 않는다.
    public static class AsciiArtPlatformTestSetup
    {
        const int GroundLayer = 7;
        const string ConfigAssetPath = "Assets/Resources/AsciiArtPlatformConfig.asset";
        const string ArtFolder = "Assets/AsciiArt";
        const string ArtAssetPath = "Assets/AsciiArt/1.txt";
        const string VaultRelativePath = "../../SlimeJumping_AssetVault/ascii/1.txt";

        [MenuItem("SlimeJumping/Platform/Setup Ascii Art Platform Test")]
        public static void Setup()
        {
            var art = EnsureArtAsset();
            if (art == null) return;
            var cfg = EnsureConfig();

            var root = new GameObject("AsciiArtPlatformTest");
            Undo.RegisterCreatedObjectUndo(root, "Setup Ascii Art Platform Test");
            root.transform.position = GetSpawnPosition();

            var platformGo = new GameObject("AsciiArt_1") { layer = GroundLayer };
            platformGo.transform.SetParent(root.transform, false);
            var platform = platformGo.AddComponent<AsciiArtPlatform>();

            var so = new SerializedObject(platform);
            so.FindProperty("configOverride").objectReferenceValue = cfg;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (!AsciiArtPlatformBuilder.Build(platform, art, cfg))
            {
                Object.DestroyImmediate(root);
                return;
            }

            // 아트 좌상단이 로컬 원점 — 가로 중앙 정렬 + 바닥변을 루트 y=0 에 올린다.
            float artW = platform.Columns * platform.CellWidth;
            float artH = platform.Rows * platform.CellHeight;
            platformGo.transform.localPosition = new Vector3(-artW * 0.5f, artH, 0f);

            CreateFloor(root.transform, artW);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = root;
            Debug.Log($"[AsciiArt] Test set spawned at {root.transform.position} (art {artW:F1}×{artH:F1}). " +
                      $"슬라임을 바닥에서 아트 위로 발사해 표면 부착·주행을 확인. 튜닝은 {ConfigAssetPath}.", root);
        }

        static void CreateFloor(Transform parent, float artWidth)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Floor";
            go.layer = GroundLayer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, -0.25f, 0f);
            go.transform.localScale = new Vector3(artWidth + 8f, 0.5f, 1f);
            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            go.AddComponent<BoxCollider2D>();
            go.GetComponent<MeshRenderer>().sharedMaterial =
                new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.35f, 0.35f, 0.38f) };
        }

        static TextAsset EnsureArtAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TextAsset>(ArtAssetPath);
            if (existing != null) return existing;

            string src = Path.GetFullPath(Path.Combine(Application.dataPath, VaultRelativePath));
            if (!File.Exists(src))
            {
                EditorUtility.DisplayDialog("Ascii Art Platform Test",
                    $"Art source not found:\n{src}", "OK");
                return null;
            }
            if (!AssetDatabase.IsValidFolder(ArtFolder))
                AssetDatabase.CreateFolder("Assets", "AsciiArt");
            File.Copy(src, Path.GetFullPath(Path.Combine(Application.dataPath, "AsciiArt/1.txt")));
            AssetDatabase.ImportAsset(ArtAssetPath);
            return AssetDatabase.LoadAssetAtPath<TextAsset>(ArtAssetPath);
        }

        static AsciiArtPlatformConfig EnsureConfig()
        {
            var existing = AssetDatabase.LoadAssetAtPath<AsciiArtPlatformConfig>(ConfigAssetPath);
            if (existing != null) return existing;

            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            var config = ScriptableObject.CreateInstance<AsciiArtPlatformConfig>();
            AssetDatabase.CreateAsset(config, ConfigAssetPath);
            AssetDatabase.SaveAssets();
            return config;
        }

        static Vector3 GetSpawnPosition()
        {
            var view = SceneView.lastActiveSceneView;
            if (view != null) return view.pivot;
            return Vector3.zero;
        }
    }
}
