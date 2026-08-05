using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    public static class LowGravityZoneTestSetup
    {
        [MenuItem("SlimeJumping/Mechanism/Setup LowGravityZone Test")]
        public static void SetupLowGravityZoneTest()
        {
            var root = new GameObject("LowGravityZoneTest");
            Undo.RegisterCreatedObjectUndo(root, "Create LowGravityZone Test");

            float baseX = 80f;
            float baseY = 0f;

            // --- Test 1: 발사 높이 비교 (밖 / 안 나란히) ---
            CreatePlatform("LaunchPad_Normal", root.transform,
                new Vector3(baseX - 6f, baseY - 0.5f, 0f), new Vector3(6f, 1f, 1f));

            CreatePlatform("LaunchPad_LowG", root.transform,
                new Vector3(baseX + 4f, baseY - 0.5f, 0f), new Vector3(6f, 1f, 1f));

            CreateZone("LowGravity_Launch", root.transform,
                new Vector3(baseX + 4f, baseY + 9f, 0f), new Vector2(9f, 20f), 0.35f);

            // 도달 높이 눈금
            for (int i = 1; i <= 4; i++)
                CreateMarker($"Height_{i * 4}m", root.transform,
                    new Vector3(baseX - 1f, baseY + i * 4f, 0f));

            // --- Test 2: 동일 각도 슬로프 2개 (슬라이드 속도) ---
            var slopeNormal = CreatePlatform("Slope_Normal", root.transform,
                new Vector3(baseX + 18f, baseY + 2f, 0f), new Vector3(10f, 0.6f, 1f));
            slopeNormal.transform.rotation = Quaternion.Euler(0f, 0f, -30f);

            var slopeLow = CreatePlatform("Slope_LowG", root.transform,
                new Vector3(baseX + 32f, baseY + 2f, 0f), new Vector3(10f, 0.6f, 1f));
            slopeLow.transform.rotation = Quaternion.Euler(0f, 0f, -30f);

            CreateZone("LowGravity_Slope", root.transform,
                new Vector3(baseX + 32f, baseY + 3f, 0f), new Vector2(14f, 12f), 0.35f);

            // --- Test 3: 볼록 코너 (friction detach) ---
            var corner = CreatePlatform("ConvexCorner", root.transform,
                new Vector3(baseX + 46f, baseY + 3f, 0f), new Vector3(6f, 6f, 1f));
            corner.transform.rotation = Quaternion.Euler(0f, 0f, 45f);

            CreateZone("LowGravity_Corner", root.transform,
                new Vector3(baseX + 46f, baseY + 4f, 0f), new Vector2(12f, 12f), 0.2f);

            // --- Test 4: GravityField 합성 (방향 = 필드, 크기 = 존) ---
            CreatePlatform("InvertedCeiling", root.transform,
                new Vector3(baseX + 62f, baseY + 8.5f, 0f), new Vector3(6f, 1f, 1f));

            CreateInvertedGravityField("GravityField_Inverted", root.transform,
                new Vector3(baseX + 62f, baseY + 5f, 0f), new Vector2(8f, 8f));

            CreateZone("LowGravity_Composed", root.transform,
                new Vector3(baseX + 62f, baseY + 5f, 0f), new Vector2(8f, 8f), 0.35f);

            // --- Test 5: 경계 관통 아치 ---
            CreatePlatform("ArchLaunchPad", root.transform,
                new Vector3(baseX + 74f, baseY - 0.5f, 0f), new Vector3(5f, 1f, 1f));

            CreateZone("LowGravity_Band", root.transform,
                new Vector3(baseX + 86f, baseY + 7f, 0f), new Vector2(12f, 8f), 0.25f);

            CreatePlatform("ArchLanding", root.transform,
                new Vector3(baseX + 98f, baseY - 0.5f, 0f), new Vector3(6f, 1f, 1f));

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("LowGravityZone Test setup complete! 5 cases: launch height, slope slide, convex corner, GravityField compose, boundary arch.");
        }

        static GameObject CreateZone(string name, Transform parent, Vector3 pos, Vector2 boxSize, float scale)
        {
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = pos;
            go.layer = LayerMask.NameToLayer("GravityField");

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = boxSize;

            var zone = go.AddComponent<LowGravityZone>();
            var so = new SerializedObject(zone);
            so.FindProperty("gravityScale").floatValue = scale;
            so.ApplyModifiedProperties();

            AddZoneVisual(go, col, scale);
            return go;
        }

        static GameObject CreateInvertedGravityField(string name, Transform parent, Vector3 pos, Vector2 boxSize)
        {
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = pos;
            go.layer = LayerMask.NameToLayer("GravityField");

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = boxSize;

            var gf = go.AddComponent<GravityField>();
            var so = new SerializedObject(gf);
            so.FindProperty("gravityMode").enumValueIndex = 0;
            so.FindProperty("localDownDirection").vector2Value = Vector2.up;
            so.ApplyModifiedProperties();

            return go;
        }

        // 채움 + 위로 흐르는 입자는 자식에 둬서 콜라이더에 영향을 안 준다.

        static readonly Color ZoneFillColor = new Color(0.25f, 0.90f, 0.85f, 0.16f);
        static readonly Color ParticleTint = new Color(0.65f, 1.00f, 0.98f, 0.90f);

        static void AddZoneVisual(GameObject host, BoxCollider2D col, float scale)
        {
            var visual = new GameObject("Visual");
            visual.transform.SetParent(host.transform, false);

            visual.AddComponent<MeshFilter>().sharedMesh = BuildQuad(col);
            var mr = visual.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MakeUnlitMaterial(ZoneFillColor);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sortingOrder = -1;

            AddDriftParticles(visual, col, scale);
        }

        static Mesh BuildQuad(BoxCollider2D b)
        {
            Vector2 h = b.size * 0.5f;
            var mesh = new Mesh { name = "LowGravityZoneFill" };
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

        // 배율이 낮을수록 더 천천히 떠오른다 = 강도를 눈으로 읽는 단서
        static void AddDriftParticles(GameObject host, BoxCollider2D col, float scale)
        {
            var psGO = new GameObject("Drift");
            psGO.transform.SetParent(host.transform, false);

            var ps = psGO.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            float driftSpeed = Mathf.Lerp(0.25f, 1.2f, scale);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 5f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
            main.startColor = ParticleTint;
            main.startSpeed = 0f;
            main.startLifetime = Mathf.Max(1f, col.size.y * 0.5f / driftSpeed);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 400;
            main.gravityModifier = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Rectangle;
            shape.scale = new Vector3(col.size.x, col.size.y, 0f);
            shape.position = col.offset;
            shape.alignToDirection = false;
            shape.randomDirectionAmount = 0f;

            // x/y/z MinMaxCurve 는 mode 가 전부 같아야 한다 — 하나만 TwoConstants 로 두면 런타임 에러
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
            vel.y = new ParticleSystem.MinMaxCurve(driftSpeed * 0.85f, driftSpeed * 1.15f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var emission = ps.emission;
            float area = Mathf.Abs(col.size.x * col.size.y);
            emission.rateOverTime = Mathf.Clamp(area * 1.2f / Mathf.Max(0.5f, main.startLifetime.constant), 8f, 60f);

            var trigger = ps.trigger;
            trigger.enabled = true;
            trigger.SetCollider(0, col);
            trigger.inside = ParticleSystemOverlapAction.Ignore;
            trigger.outside = ParticleSystemOverlapAction.Kill;
            trigger.enter = ParticleSystemOverlapAction.Ignore;
            trigger.exit = ParticleSystemOverlapAction.Ignore;

            var colMod = ps.colorOverLifetime;
            colMod.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.25f),
                    new GradientAlphaKey(1f, 0.75f),
                    new GradientAlphaKey(0f, 1f),
                });
            colMod.color = grad;

            var psr = ps.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Billboard;
            psr.material = MakeUnlitMaterial(Color.white);
            psr.sortingOrder = 0;
            psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            psr.receiveShadows = false;
        }

        static GameObject CreateMarker(string name, Transform parent, Vector3 pos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.parent = parent;
            go.transform.position = pos;
            go.transform.localScale = new Vector3(1.2f, 0.06f, 1f);

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
