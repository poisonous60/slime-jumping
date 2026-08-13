using TMPro;
using UnityEditor;
using UnityEngine;
using SlimeJumping.UI;
using SlimeJumping.VFX;

namespace SlimeJumping.Editor
{
    /// 신전 옆에 기록 비석을 세운다: 울퉁불퉁한 바위 메시 + 각인 라벨.
    /// 각인은 RockEngraving 이 라벨을 마스크 RT 로 구워 바위 셰이더
    /// ("Quibli/Stylized Lit (Rock Engraving)")에 투영한다 — 바위 픽셀 자체가
    /// 파이므로 라벨 렌더러는 화면에 안 나온다. 슬라임이 존에 들어오면 기록 확정.
    /// 루트 삭제 = 완전 롤백.
    public static class ShrineTimeSteleSetup
    {
        const string SceneName = "MainScene";
        const string RootName = "ShrineTimeStele";
        const string RockMeshPath = "Assets/ThirdParty/Synty/PolygonGeneric/Models/SM_Gen_Env_Rock_09.fbx";
        const string RockSourceMaterialPath = "Assets/Art/Materials/Stage4_sky/Mat_pillor.mat";
        const string EngraveMaterialPath = "Assets/Materials/M_ShrineRockEngraved.mat";
        const string EngraveShaderName = "Quibli/Stylized Lit (Rock Engraving)";

        // 씬 실측값 (2026-08-13, 유저 배치).
        static readonly Vector3 RootPosition = new Vector3(77.55f, 496.15f, 1.34f);
        static readonly Vector3 RockOffset = new Vector3(0.01f, -0.96f, -0.13f);
        static readonly Vector3 RockScale = new Vector3(1.5f, 1f, 1f);
        static readonly Vector3 LabelOffset = new Vector3(0.31f, 0.73f, -0.71f);
        static readonly Vector2 LabelSize = new Vector2(3.0f, 1.7f);
        const float TextFontSize = 7.5f;
        const string LabelPrefix = "<size=55%>YOUR TIME</size>\n";

        const int ZoneLayer = 2; // Ignore Raycast — Environment 마스크 밖이면 뭐든 된다.
        static readonly Vector2 ZoneSize = new Vector2(12f, 8f);

        [MenuItem("SlimeJumping/Mechanism/Create Shrine Time Stele (MainScene)")]
        public static void Setup()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (scene.name != SceneName)
            {
                Debug.LogError($"[TimeStele] 활성 씬이 {SceneName} 이 아니다 ('{scene.name}').");
                return;
            }
            if (GameObject.Find(RootName) != null)
            {
                Debug.LogError($"[TimeStele] '{RootName}' 루트가 이미 있다. 삭제 후 다시 실행.");
                return;
            }

            var root = new GameObject(RootName);
            root.transform.position = RootPosition;
            Undo.RegisterCreatedObjectUndo(root, "Create Shrine Time Stele");

            var rockGo = CreateRock(root.transform);

            var labelGo = new GameObject("Engraving");
            labelGo.transform.SetParent(root.transform, false);
            labelGo.transform.localPosition = LabelOffset;
            var tmp = labelGo.AddComponent<TextMeshPro>();
            tmp.autoSizeTextContainer = false;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = TextFontSize;
            tmp.text = LabelPrefix + "--:--";
            tmp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            tmp.rectTransform.sizeDelta = LabelSize;

            var zone = CreateCaptureZone(root.transform);

            var display = root.AddComponent<EndingTimeDisplay>();
            var displaySo = new SerializedObject(display);
            displaySo.FindProperty("label").objectReferenceValue = tmp;
            displaySo.FindProperty("prefix").stringValue = LabelPrefix;
            displaySo.FindProperty("captureZone").objectReferenceValue = zone;
            displaySo.ApplyModifiedPropertiesWithoutUndo();

            var engraving = root.AddComponent<RockEngraving>();
            var engravingSo = new SerializedObject(engraving);
            engravingSo.FindProperty("label").objectReferenceValue = tmp;
            engravingSo.FindProperty("rock").objectReferenceValue = rockGo.GetComponent<MeshRenderer>();
            engravingSo.ApplyModifiedPropertiesWithoutUndo();

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            Debug.Log($"[TimeStele] 비석 배치: 루트 {RootPosition}. 각인은 Engraving 사각형을 " +
                      "옮기고 늘려서 자리를 잡는다. 튜닝은 RockEngravingConfig.asset.", root);
        }

        static GameObject CreateRock(Transform parent)
        {
            var go = new GameObject("SM_Gen_Env_Rock_09");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = RockOffset;
            go.transform.localScale = RockScale;

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(RockMeshPath);
            if (mesh == null) Debug.LogError($"[TimeStele] 바위 메시 없음: {RockMeshPath}");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = EnsureEngraveMaterial();
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        /// 도착 판정 존. Environment(7) 밖 레이어라 슬라임 표면 탐색이 안 집는다.
        static Collider2D CreateCaptureZone(Transform parent)
        {
            var go = new GameObject("CaptureZone") { layer = ZoneLayer };
            go.transform.SetParent(parent, false);
            var box = go.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = ZoneSize;
            return box;
        }

        /// 바위 원본 머티리얼을 복제해 각인 셰이더를 입힌 에셋.
        static Material EnsureEngraveMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(EngraveMaterialPath);
            if (existing != null) return existing;

            var source = AssetDatabase.LoadAssetAtPath<Material>(RockSourceMaterialPath);
            var shader = Shader.Find(EngraveShaderName);
            if (source == null || shader == null)
            {
                Debug.LogError($"[TimeStele] 원본 머티리얼({source}) 또는 셰이더({EngraveShaderName}) 없음.");
                return source;
            }
            var mat = new Material(source) { shader = shader };
            AssetDatabase.CreateAsset(mat, EngraveMaterialPath);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}
