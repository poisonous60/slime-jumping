using UnityEditor;
using UnityEngine;
using SlimeJumping.VFX;

namespace SlimeJumping.Editor
{
    /// 신전 기록 석판 앞에 팡파레를 세운다: 트리거 존 + 좌우 색종이 포퍼 2문(영상 레시피 —
    /// 복제해 반대편에 미러). 렌더러(쿼드 메시·머티리얼)는 여기서 굽고, 파티클 모듈 값은
    /// 런타임에 ConfettiFanfareConfig 가 덮어쓴다. 루트 삭제 = 완전 롤백.
    public static class ConfettiFanfareSetup
    {
        const string SceneName = "MainScene";
        const string RootName = "ShrineFanfare";
        const string SteleName = "ShrineTimeStele";
        const string ConfigPath = "Assets/Resources/ConfettiFanfareConfig.asset";
        const string MaterialPath = "Assets/Materials/M_ConfettiFanfare.mat";
        const string LitShader = "Universal Render Pipeline/Particles/Simple Lit";
        const string UnlitShader = "Universal Render Pipeline/Particles/Unlit";

        const int ZoneLayer = 2; // Ignore Raycast — CaptureZone 과 같은 이유로 Environment 밖.
        static readonly Vector2 ZoneSize = new Vector2(12f, 8f);
        const float PopperSpacing = 2.2f;  // 석판(폭 3) 바닥 모서리 바깥.
        const float PopperHeight = -0.6f;
        const float PopperTiltDeg = 30f;   // 영상 "슬쩍 기울임" — 안쪽 위로 교차 발사.

        [MenuItem("SlimeJumping/Mechanism/Create Shrine Fanfare (MainScene)")]
        public static void Setup()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (scene.name != SceneName)
            {
                Debug.LogError($"[Fanfare] 활성 씬이 {SceneName} 이 아니다 ('{scene.name}').");
                return;
            }
            if (GameObject.Find(RootName) != null)
            {
                Debug.LogError($"[Fanfare] '{RootName}' 루트가 이미 있다. 삭제 후 다시 실행.");
                return;
            }
            var stele = GameObject.Find(SteleName);
            if (stele == null)
            {
                Debug.LogError($"[Fanfare] '{SteleName}' 이 씬에 없다 — 석판부터 세워라.");
                return;
            }

            var config = EnsureConfig();
            var material = EnsureMaterial();
            var quad = GetQuadMesh();

            var root = new GameObject(RootName) { layer = ZoneLayer };
            var stelePos = stele.transform.position;
            root.transform.position = new Vector3(stelePos.x, stelePos.y, 0f);
            Undo.RegisterCreatedObjectUndo(root, "Create Shrine Fanfare");

            var zone = root.AddComponent<BoxCollider2D>();
            zone.isTrigger = true;
            zone.size = ZoneSize;

            float tilt = PopperTiltDeg * Mathf.Deg2Rad;
            var poppers = new[]
            {
                CreatePopper(root.transform, "PopperLeft", -PopperSpacing, new Vector3(Mathf.Sin(tilt), Mathf.Cos(tilt), 0f), material, quad, config),
                CreatePopper(root.transform, "PopperRight", PopperSpacing, new Vector3(-Mathf.Sin(tilt), Mathf.Cos(tilt), 0f), material, quad, config),
            };

            var fanfare = root.AddComponent<ConfettiFanfare>();
            var so = new SerializedObject(fanfare);
            var prop = so.FindProperty("poppers");
            prop.arraySize = poppers.Length;
            for (int i = 0; i < poppers.Length; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = poppers[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            Debug.Log($"[Fanfare] 팡파레 배치: 석판 {stelePos} 좌우 포퍼 2문. 존 {ZoneSize} 안에 슬라임이 처음 들어오면 한 번 터진다.\n" +
                      "튜닝: " + ConfigPath + " (개수/속도/퍼짐/크기/팔레트/커브).", root);
        }

        static ParticleSystem CreatePopper(Transform parent, string name, float offsetX, Vector3 fireDir, Material material, Mesh quad, ConfettiFanfareConfig config)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(offsetX, PopperHeight, 0f);
            go.transform.localRotation = Quaternion.LookRotation(fireDir); // 원뿔 +Z = 발사 방향.

            // 모듈 값을 씬에 구워야 에디터 프리뷰가 실제 룩과 일치한다 (Awake 는 재적용만).
            var ps = go.AddComponent<ParticleSystem>();
            ConfettiFanfare.Configure(ps, config);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = quad;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        static ConfettiFanfareConfig EnsureConfig()
        {
            var existing = AssetDatabase.LoadAssetAtPath<ConfettiFanfareConfig>(ConfigPath);
            if (existing != null) return existing;
            var config = ScriptableObject.CreateInstance<ConfettiFanfareConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            AssetDatabase.SaveAssets();
            return config;
        }

        /// 조각 머티리얼: 파티클 Simple Lit — 텀블할 때 면 밝기가 뒤집혀야 종이로 읽힌다.
        /// Cull Off 로 뒷면도 그리고, 알파 블렌드로 페이드를 받는다.
        static Material EnsureMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing != null) return existing;

            var shader = Shader.Find(LitShader);
            if (shader == null) shader = Shader.Find(UnlitShader);

            var mat = new Material(shader);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            AssetDatabase.CreateAsset(mat, MaterialPath);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static Mesh GetQuadMesh()
        {
            var temp = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(temp);
            return mesh;
        }
    }
}
