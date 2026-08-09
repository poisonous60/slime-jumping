using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    public static class GravityFieldV2TestSetup
    {
        [MenuItem("SlimeJumping/Mechanism/Setup GravityFieldV2 Test")]
        public static void SetupGravityFieldV2Test()
        {
            var root = new GameObject("GravityFieldV2Test");
            Undo.RegisterCreatedObjectUndo(root, "Create GravityFieldV2 Test");

            float baseX = 220f;
            float baseY = 0f;

            // --- Test 1: 단일 점 — 궤도 휘어짐 + 점 근처 저중력 ---
            CreatePlatform("T1_LaunchPad", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f), new Vector3(6f, 1f, 1f));

            var region1 = CreateRegion("T1_SinglePoint", root.transform,
                new Vector3(baseX, baseY + 10f, 0f), new Vector2(16f, 14f), 2f);
            CreatePoint(region1.transform, new Vector3(baseX, baseY + 12f, 0f), 6f, 25f);

            // --- Test 2: 두 점 상쇄 — 중간 데드존에서 방향 freeze 확인 ---
            var region2 = CreateRegion("T2_TwinDeadzone", root.transform,
                new Vector3(baseX + 26f, baseY + 9f, 0f), new Vector2(18f, 12f), 2f);
            CreatePoint(region2.transform, new Vector3(baseX + 21f, baseY + 9f, 0f), 5.5f, 20f);
            CreatePoint(region2.transform, new Vector3(baseX + 31f, baseY + 9f, 0f), 5.5f, 20f);
            CreateMarker("T2_SaddleMarker", root.transform, new Vector3(baseX + 26f, baseY + 9f, 0f));
            CreatePlatform("T2_LaunchPad", root.transform,
                new Vector3(baseX + 26f, baseY - 0.5f, 0f), new Vector3(6f, 1f, 1f));

            // --- Test 3: 경계 페더 — 관통 비행으로 진입/이탈 부드러움 확인 ---
            CreatePlatform("T3_LaunchPad", root.transform,
                new Vector3(baseX + 44f, baseY - 0.5f, 0f), new Vector3(5f, 1f, 1f));

            var region3 = CreateRegion("T3_FeatherBand", root.transform,
                new Vector3(baseX + 54f, baseY + 8f, 0f), new Vector2(9f, 16f), 3f);
            CreatePoint(region3.transform, new Vector3(baseX + 54f, baseY + 10f, 0f), 4.5f, 18f);

            CreatePlatform("T3_LandingPad", root.transform,
                new Vector3(baseX + 64f, baseY - 0.5f, 0f), new Vector3(6f, 1f, 1f));

            // --- Test 4: 벽 착지 — 점이 수직벽 쪽으로 끌어 벽면이 "바닥"이 되는지 ---
            CreatePlatform("T4_LaunchPad", root.transform,
                new Vector3(baseX + 76f, baseY - 0.5f, 0f), new Vector3(5f, 1f, 1f));

            var region4 = CreateRegion("T4_WallAttract", root.transform,
                new Vector3(baseX + 84f, baseY + 8f, 0f), new Vector2(16f, 14f), 2f);
            CreatePoint(region4.transform, new Vector3(baseX + 88f, baseY + 8f, 0f), 7f, 30f);

            CreatePlatform("T4_Wall", root.transform,
                new Vector3(baseX + 90f, baseY + 8f, 0f), new Vector3(1f, 10f, 1f));

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("GravityFieldV2 Test setup complete! 4 cases: single-point orbit, twin deadzone freeze, feather band, wall attract.");
        }

        static GameObject CreateRegion(string name, Transform parent, Vector3 pos, Vector2 boxSize, float feather)
        {
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = pos;
            go.layer = LayerMask.NameToLayer("GravityField");

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = boxSize;

            var zone = go.AddComponent<GravityFieldV2>();
            var so = new SerializedObject(zone);
            so.FindProperty("featherWidth").floatValue = feather;
            so.ApplyModifiedProperties();

            AddRegionVisual(go, col);
            return go;
        }

        static GameObject CreatePoint(Transform region, Vector3 pos, float radius, float peakAccel)
        {
            var go = new GameObject("GravityPoint");
            go.transform.parent = region;
            go.transform.position = pos;

            var point = go.AddComponent<GravityPoint2D>();
            var so = new SerializedObject(point);
            so.FindProperty("radius").floatValue = radius;
            so.FindProperty("peakAccel").floatValue = peakAccel;
            so.ApplyModifiedProperties();

            // 게임 뷰 표식 (기즈모는 씬 뷰 전용)
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Marker";
            marker.transform.SetParent(go.transform, false);
            marker.transform.localScale = Vector3.one * 0.5f;
            Object.DestroyImmediate(marker.GetComponent<SphereCollider>());
            marker.GetComponent<MeshRenderer>().sharedMaterial =
                MakeUnlitMaterial(new Color(1f, 0.6f, 0.2f, 0.9f));
            return go;
        }

        static readonly Color RegionFillColor = new Color(0.70f, 0.30f, 1.00f, 0.10f);

        static void AddRegionVisual(GameObject host, BoxCollider2D col)
        {
            var visual = new GameObject("Visual");
            visual.transform.SetParent(host.transform, false);

            visual.AddComponent<MeshFilter>().sharedMesh = BuildQuad(col);
            var mr = visual.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MakeUnlitMaterial(RegionFillColor);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sortingOrder = -1;
        }

        static Mesh BuildQuad(BoxCollider2D b)
        {
            Vector2 h = b.size * 0.5f;
            var mesh = new Mesh { name = "GravityFieldV2Fill" };
            mesh.vertices = new[]
            {
                (Vector3)(b.offset + new Vector2(-h.x, -h.y)),
                (Vector3)(b.offset + new Vector2(-h.x,  h.y)),
                (Vector3)(b.offset + new Vector2( h.x,  h.y)),
                (Vector3)(b.offset + new Vector2( h.x, -h.y)),
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(1f, 0f),
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }

        static GameObject CreateMarker(string name, Transform parent, Vector3 pos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.parent = parent;
            go.transform.position = pos;
            go.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);

            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = MakeLitMaterial(new Color(1f, 0.85f, 0.2f));
            return go;
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
            go.AddComponent<BoxCollider2D>().size = Vector2.one;

            go.GetComponent<MeshRenderer>().sharedMaterial = MakeLitMaterial(new Color(0.5f, 0.5f, 0.5f));
            return go;
        }

        static Material MakeLitMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            return mat;
        }

        static Material MakeUnlitMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = color;
            return mat;
        }
    }
}
