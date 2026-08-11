using TMPro;
using UnityEditor;
using UnityEngine;
using SlimeJumping.UI;

namespace SlimeJumping.Editor
{
    /// 신전 왼쪽에 기록 석판을 세운다: 콘크리트 큐브 한 장 + 앞면에 각인 TMP.
    /// 각인은 TMP SDF 셰이더의 Inner Bevel 이 만든다 — 파인 것처럼 보이려면
    /// 글자가 얹힐 면이 있어야 해서 판때기가 필요하다.
    /// 슬라임이 다가오면 기록이 굴러 올라가 멈춘다. 루트 삭제 = 완전 롤백.
    public static class ShrineTimeSteleSetup
    {
        const string SceneName = "MainScene";
        const string RootName = "ShrineTimeStele";
        const string SlabMaterialPath = "Assets/ThirdParty/YughuesFreeConcreteMaterials/Materials/M_YFCM_Slab1x1.mat";
        const string EngravedMaterialPath = "Assets/Materials/M_ShrineTimeEngraved.mat";
        // Surface 변형은 빌트인 서피스 셰이더라 URP 에서 패스가 안 돈다 — 자체 광원각을 가진 평범한 SDF 를 쓴다.
        const string EngravedShader = "TextMeshPro/Distance Field";

        // 신전은 x 80.2~82.8 / y 495.3~498.0 (stage4 정상 상단면 495.3).
        static readonly Vector3 SlabCenter = new Vector3(78.3f, 496.15f, 0f);
        static readonly Vector3 SlabScale = new Vector3(3.0f, 1.7f, 0.3f);
        const float TextDepth = 0.02f;   // 앞면에서 앞으로 띄우는 거리.
        const float TextFontSize = 7.5f;
        const string LabelPrefix = "<size=55%>YOUR TIME</size>\n";

        // 판 알베도가 0.56 이라 각인은 그 그늘만큼 어두워야 읽힌다.
        static readonly Color FaceColor = new Color(0.11f, 0.105f, 0.10f);
        static readonly Color OutlineColor = new Color(0.04f, 0.04f, 0.04f);

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
            root.transform.position = SlabCenter;
            Undo.RegisterCreatedObjectUndo(root, "Create Shrine Time Stele");

            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "Slab";
            Object.DestroyImmediate(slab.GetComponent<BoxCollider>());
            slab.transform.SetParent(root.transform, false);
            slab.transform.localScale = SlabScale;
            var slabMat = AssetDatabase.LoadAssetAtPath<Material>(SlabMaterialPath);
            if (slabMat != null) slab.GetComponent<MeshRenderer>().sharedMaterial = slabMat;
            else Debug.LogWarning($"[TimeStele] 판 머티리얼 없음: {SlabMaterialPath} — 기본 머티리얼로 둔다.");

            var labelGo = new GameObject("Engraving");
            labelGo.transform.SetParent(root.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 0f, -(SlabScale.z * 0.5f + TextDepth));
            var tmp = labelGo.AddComponent<TextMeshPro>();
            tmp.autoSizeTextContainer = false;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = TextFontSize;
            tmp.text = LabelPrefix + "--:--";
            tmp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            tmp.rectTransform.sizeDelta = new Vector2(SlabScale.x, SlabScale.y);
            tmp.fontSharedMaterial = EnsureEngravedMaterial(tmp.fontSharedMaterial);

            var display = root.AddComponent<EndingTimeDisplay>();
            var so = new SerializedObject(display);
            so.FindProperty("label").objectReferenceValue = tmp;
            so.FindProperty("prefix").stringValue = LabelPrefix;
            so.ApplyModifiedPropertiesWithoutUndo();

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            Debug.Log($"[TimeStele] 석판 배치: 중심 {SlabCenter}, 크기 {SlabScale}. " +
                      $"각인 머티리얼 {EngravedMaterialPath} — Bevel 이 볼록해 보이면 _ShaderFlags 를 0/1 로 뒤집어라.", root);
        }

        /// 폰트 머티리얼을 복제해 Inner Bevel + 위쪽 광원각을 박은 에셋. 획 윗변이 그늘,
        /// 아랫변이 하이라이트가 되면서 파인 것처럼 읽힌다.
        static Material EnsureEngravedMaterial(Material fontMaterial)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(EngravedMaterialPath);
            if (existing != null) return existing;

            var shader = Shader.Find(EngravedShader);
            if (shader == null)
            {
                Debug.LogWarning($"[TimeStele] '{EngravedShader}' 못 찾음 — 폰트 기본 머티리얼로 둔다.");
                return fontMaterial;
            }
            var mat = new Material(fontMaterial) { shader = shader };
            // Bevel·Outline 은 shader_feature 라 키워드를 켜야 값이 먹는다.
            mat.EnableKeyword("BEVEL_ON");
            mat.EnableKeyword("OUTLINE_ON");
            mat.SetColor("_FaceColor", FaceColor);
            mat.SetFloat("_FaceDilate", 0.1f);
            mat.SetColor("_OutlineColor", OutlineColor);
            mat.SetFloat("_OutlineWidth", 0.08f);
            mat.SetFloat("_Bevel", 0.6f);
            mat.SetFloat("_BevelOffset", -0.05f);
            mat.SetFloat("_BevelWidth", 0.2f);
            mat.SetFloat("_BevelRoundness", 0.2f);
            mat.SetFloat("_BevelClamp", 0.8f);
            mat.SetFloat("_LightAngle", Mathf.PI); // 위에서 비추는 가짜 광원.
            mat.SetFloat("_Diffuse", 0.6f);
            mat.SetFloat("_Ambient", 0.4f);
            mat.SetFloat("_SpecularPower", 2f);
            mat.SetColor("_SpecularColor", new Color(0.7f, 0.7f, 0.68f));
            mat.SetFloat("_ShaderFlags", 1f); // Bevel Type = Inner.
            AssetDatabase.CreateAsset(mat, EngravedMaterialPath);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}
