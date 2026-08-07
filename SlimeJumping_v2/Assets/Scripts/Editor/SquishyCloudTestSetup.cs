using UnityEngine;
using UnityEditor;
using SlimeJumping.Platforms;

namespace SlimeJumping.Editor
{
    // 말랑 구름 테스트 리그. 기존 씬 구름은 복제 소스로만 읽는다(비파괴).
    public static class SquishyCloudTestSetup
    {
        const string RootName = "SquishyCloudTest";
        const string SourceRootName = "jumping_cloud";
        const string SquishyConfigPath = "Assets/Resources/SquishyCloudConfig.asset";
        const string CanonicalCloudConfigPath = "Assets/Resources/CloudPlatformConfig.asset";
        const string NoVanishConfigPath = "Assets/Resources/CloudPlatformConfig_NoVanish.asset";

        [MenuItem("SlimeJumping/Platform/Setup Squishy Cloud Test")]
        public static void SetupSquishyCloudTest()
        {
            var template = FindCloudTemplate();
            if (template == null) return;

            EnsureSquishyConfig();
            var noVanish = EnsureNoVanishConfig();

            var existing = GameObject.Find(RootName);
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Squishy Cloud Test");

            CreatePlatform("Floor", root.transform, new Vector3(0f, -6f, 0f), new Vector3(24f, 1f, 1f));
            CreatePlatform("LaunchPad", root.transform, new Vector3(-8f, -4f, 0f), new Vector3(3f, 1f, 1f));

            CreateCloud(template, root.transform, new Vector3(-3f, -1.5f, 0f), "SquishyCloud_VanishON", null);
            CreateCloud(template, root.transform, new Vector3(3f, -1.5f, 0f), "SquishyCloud_VanishOFF", noVanish);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("Squishy Cloud Test setup complete — 왼쪽 = 소멸 ON(기존 disperse), 오른쪽 = 소멸 OFF(잔류+반동).\n" +
                "\n" +
                "Check list:\n" +
                "1. 착지 순간: 발밑이 움푹 들어가고, 그 눌림이 물결로 실루엣을 타고 퍼진다.\n" +
                "2. 착지 순간: 구름 전체가 살짝 주저앉고(전신 압축) 옆구리가 미세하게 부푼다.\n" +
                "3. 탑승 중: 발밑 눌림이 슬라임을 따라다니고, 굴러 이동하면 지나간 자리에 트레일 물결.\n" +
                "4. 왼쪽 구름: 발사하면 기존처럼 조각 puff 로 흩어지고 리스폰된다.\n" +
                "5. 오른쪽 구름: 발사해도 남는다 — 떠난 직후 구름이 출렁출렁 잔동(damped wobble) 후 원형 복귀.\n" +
                "6. 오른쪽 구름: 리바운드가 끝나면 다시 밟을 수 있고, 리바운드 중 재착지해도 정상 반응.\n" +
                "7. 콜라이더는 변형되지 않는다 — 물리 착지 지점은 기존과 동일, 변형은 비주얼만.\n" +
                "\n" +
                "튜닝: " + SquishyConfigPath + " (전신/파동/한계 노브). 소멸 옵션: CloudPlatformConfig.vanishOnUse.", root);
        }

        static GameObject FindCloudTemplate()
        {
            var source = GameObject.Find(SourceRootName);
            if (source == null)
            {
                Debug.LogWarning($"[SquishyCloudTest] '{SourceRootName}' 이 씬에 없어 복제 소스를 찾을 수 없다.");
                return null;
            }

            for (int i = 0; i < source.transform.childCount; i++)
            {
                var child = source.transform.GetChild(i).gameObject;
                var cp = child.GetComponent<CloudPlatform>();
                if (cp == null) continue;

                var disp = cp.disperser != null ? cp.disperser : child.GetComponentInChildren<CloudDisperser>();
                if (disp != null && disp.sceneCloud != null && child.GetComponentInChildren<PolygonCollider2D>() != null)
                    return child;
            }

            Debug.LogWarning($"[SquishyCloudTest] '{SourceRootName}' 아래에 sceneCloud + PolygonCollider2D 를 갖춘 " +
                "CloudPlatform 이 없다.");
            return null;
        }

        static void CreateCloud(GameObject template, Transform parent, Vector3 pos, string name,
            CloudPlatformConfig cloudCfg)
        {
            var clone = Object.Instantiate(template, parent);
            clone.name = name;
            clone.transform.position = pos;
            CenterVisualOnRoot(clone);

            var cp = clone.GetComponent<CloudPlatform>();
            cp.configOverride = cloudCfg;
            var disp = cp.disperser != null ? cp.disperser : clone.GetComponentInChildren<CloudDisperser>();
            if (disp != null) disp.configOverride = cloudCfg;

            clone.AddComponent<SquishyCloud>();
        }

        // 소스 구름은 콜라이더 래퍼에 큰 오프셋이 있어 루트와 비주얼이 어긋난다 — 배치 좌표를 보이는 위치로 맞춘다.
        static void CenterVisualOnRoot(GameObject go)
        {
            var rd = go.GetComponentInChildren<Renderer>();
            if (rd == null) return;

            Vector3 off = rd.bounds.center - go.transform.position;
            off.z = 0f;
            if (off.sqrMagnitude < 1e-6f) return;

            for (int i = 0; i < go.transform.childCount; i++)
                go.transform.GetChild(i).position -= off;
        }

        static void EnsureSquishyConfig()
        {
            if (AssetDatabase.LoadAssetAtPath<SquishyCloudConfig>(SquishyConfigPath) != null) return;
            var cfg = ScriptableObject.CreateInstance<SquishyCloudConfig>();
            AssetDatabase.CreateAsset(cfg, SquishyConfigPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SquishyCloudTest] canonical config 생성: {SquishyConfigPath}");
        }

        static CloudPlatformConfig EnsureNoVanishConfig()
        {
            var found = AssetDatabase.LoadAssetAtPath<CloudPlatformConfig>(NoVanishConfigPath);
            if (found != null) return found;

            var canonical = AssetDatabase.LoadAssetAtPath<CloudPlatformConfig>(CanonicalCloudConfigPath);
            var cfg = canonical != null
                ? Object.Instantiate(canonical)
                : ScriptableObject.CreateInstance<CloudPlatformConfig>();
            cfg.vanishOnUse = false;
            AssetDatabase.CreateAsset(cfg, NoVanishConfigPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SquishyCloudTest] vanish OFF 변종 config 생성: {NoVanishConfigPath}");
            return cfg;
        }

        static GameObject CreatePlatform(string name, Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.parent = parent;
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.layer = 7;

            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            var col2d = go.AddComponent<BoxCollider2D>();
            col2d.size = Vector2.one;

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = new Color(0.5f, 0.5f, 0.5f);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;

            return go;
        }
    }
}
