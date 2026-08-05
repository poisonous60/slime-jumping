using UnityEngine;
using UnityEditor;
using SlimeJumping.Platforms;

namespace SlimeJumping.Editor
{
    public static class DriftingCloudLaneTestSetup
    {
        const string RootName = "DriftingCloudLaneTest";
        const string SourceRootName = "jumping_cloud";
        const string DriftingCloudConfigPath = "Assets/Resources/DriftingCloudPlatformConfig.asset";

        // 리그용 테스트 배치. 레인 중심 기준.
        static readonly Vector2[] CloudOffsets =
        {
            new Vector2(-6f, -4.57f), new Vector2(0f, -3.67f), new Vector2(6f, -2.77f),
            new Vector2(-6f, -0.90f), new Vector2(0f,  0.00f), new Vector2(6f,  0.90f),
            new Vector2(-6f,  2.77f), new Vector2(0f,  3.67f), new Vector2(6f,  4.57f),
        };

        [MenuItem("SlimeJumping/Platform/Setup Drifting Cloud Lane Test")]
        public static void SetupDriftingCloudLaneTest()
        {
            var template = FindCloudTemplate();
            if (template == null) return;

            var existing = GameObject.Find(RootName);
            if (existing != null) Object.DestroyImmediate(existing);

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Drifting Cloud Lane Test");

            float baseX = 0f;
            float baseY = 0f;

            CreatePlatform("Floor", root.transform,
                new Vector3(baseX, baseY - 6f, 0f),
                new Vector3(40f, 1f, 1f));

            CreatePlatform("LaunchPad", root.transform,
                new Vector3(baseX - 7f, baseY - 4f, 0f),
                new Vector3(3f, 1f, 1f));

            var lane = CreateLane("CloudLane", root.transform,
                new Vector3(baseX, baseY + 3f, 0f), template);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log($"Drifting Cloud Lane Test setup complete — {CloudOffsets.Length} clouds in a " +
                $"{lane.BandSize.x}x{lane.BandSize.y} band.\n" +
                "Launch off LaunchPad and climb upward while the clouds flow left to right.\n" +
                "\n" +
                "Check list:\n" +
                "1. Clouds drift left to right; each cloud's speed matches its own Speed field (gizmo arrows).\n" +
                "2. At the right edge a cloud shrinks away and grows back in at the left edge — the count never changes.\n" +
                "3. Growing / shrinking clouds cannot be landed on; only full-size ones are solid.\n" +
                "4. Ride a cloud: it carries the slime smoothly, with no 50Hz judder or stair-stepping.\n" +
                "5. Stomping a cloud still bursts it into chunks — visibly different from the edge shrink.\n" +
                "6. Ride a cloud all the way to the right edge: the slime must NOT teleport when the cloud wraps.\n" +
                "7. A stomped cloud never regrows in place — it stays gone until it wraps at the right edge.\n" +
                "\n" +
                "Select CloudLane to edit the band (bandSize / spawnFadeTime / despawnFadeTime, shared " +
                "DriftingCloudLaneConfig.asset) and every cloud's Speed in one inspector; the scene view shows " +
                "each cloud's speed arrow and fade boundaries. Stomp behaviour comes from " +
                $"{DriftingCloudConfigPath} (respawn off). Move CloudLane and the band and its clouds follow.", root);
        }

        static GameObject FindCloudTemplate()
        {
            var source = GameObject.Find(SourceRootName);
            if (source == null)
            {
                Debug.LogWarning($"[DriftingCloudLaneTest] '{SourceRootName}' not in the open scene — the cloud's " +
                    "silhouette PolygonCollider2D exists only in the scene, so there is nothing to clone.");
                return null;
            }

            for (int i = 0; i < source.transform.childCount; i++)
            {
                var child = source.transform.GetChild(i).gameObject;
                var cp = child.GetComponent<CloudPlatform>();
                if (cp == null) continue;

                var disp = cp.disperser != null ? cp.disperser : child.GetComponentInChildren<CloudDisperser>();
                if (disp != null && disp.sceneCloud != null && child.GetComponentInChildren<Collider2D>() != null)
                    return child;
            }

            Debug.LogWarning($"[DriftingCloudLaneTest] no CloudPlatform under '{SourceRootName}' has both a " +
                "sceneCloud visual and a collider.");
            return null;
        }

        static DriftingCloudLane CreateLane(string name, Transform parent, Vector3 pos, GameObject template)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;

            var lane = go.AddComponent<DriftingCloudLane>();

            for (int i = 0; i < CloudOffsets.Length; i++)
            {
                var clone = Object.Instantiate(template, go.transform);
                clone.name = $"Cloud_{i}";
                Convert(clone, lane);
                clone.transform.position = pos + (Vector3)CloudOffsets[i];
                CenterVisualOnRoot(clone);
            }

            return lane;
        }

        // The source cloud carries a large offset on its collider wrapper, so its root sits far from what
        // you see. Drift, wrap and fade all judge the band by the root, so the two have to coincide.
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

        // 흘러가는 구름은 전용 CloudPlatformConfig 를 쓴다 — 정지 구름과 달리 respawn 이 false 라
        // 밟힌 자리에서 되살아나지 않고, 오른쪽 끝 wrap 에서만 돌아온다.
        static void Convert(GameObject go, DriftingCloudLane lane)
        {
            var old = go.GetComponent<CloudPlatform>();
            var disp = old.disperser != null ? old.disperser : go.GetComponentInChildren<CloudDisperser>();
            var cfg = AssetDatabase.LoadAssetAtPath<CloudPlatformConfig>(DriftingCloudConfigPath);
            if (cfg == null)
                Debug.LogWarning($"[DriftingCloudLaneTest] {DriftingCloudConfigPath} not found — clouds fall back " +
                    "to the shared CloudPlatformConfig and will regrow in place after being stomped.");

            Object.DestroyImmediate(old);
            var drifting = go.AddComponent<DriftingCloudPlatform>();
            drifting.configOverride = cfg;
            drifting.disperser = disp;
            drifting.lane = lane;
            if (disp != null) disp.configOverride = cfg;
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

            go.GetComponent<MeshRenderer>().sharedMaterial =
                MakeMaterial(new Color(0.5f, 0.5f, 0.5f));

            return go;
        }

        static Material MakeMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            return mat;
        }
    }
}
