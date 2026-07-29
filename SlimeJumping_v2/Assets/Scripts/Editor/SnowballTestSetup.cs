using System.IO;
using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;
using SlimeJumping.Rendering;

namespace SlimeJumping.Editor
{
    public static class SnowballTestSetup
    {
        const string TestRootName = "SnowballTest";
        const string SnowballMaterialPath = "Assets/Art/Materials/Snowball.mat";
        const string FragmentPrefabPath   = "Assets/Art/Prefabs/SnowballFragment.prefab";
        const string PrefabDir            = "Assets/Art/Prefabs";
        const string RollingPrefabPath    = "Assets/Art/Prefabs/Snowball_Rolling.prefab";
        const string FlyingPrefabPath     = "Assets/Art/Prefabs/Snowball_Flying.prefab";
        const string GradientRampPath     = "Assets/Art/Textures/SnowballGradientRamp.png";
        const string SurfaceNormalPath    = "Assets/Art/Textures/SnowballNormal.png";

        [MenuItem("SlimeJumping/Mechanism/Setup Snowball Test")]
        public static void Setup()
        {
            var existing = GameObject.Find(TestRootName);
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject(TestRootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Snowball Test");

            CreatePlatform("FlatGround",  root.transform,
                new Vector3(0f, -0.5f, 0f),
                new Vector3(20f, 1f, 1f));

            // +30° — 굴러 올라갈 수 있어야 하는 각
            CreatePlatform("Slope_30",    root.transform,
                new Vector3(11.5f, 0.5f, 0f),
                new Vector3(4f, 1f, 1f),
                rotationDeg: 30f);

            // +60° — 앞 슬로프 대비 각도 급변 → 부서짐
            CreatePlatform("Slope_Break", root.transform,
                new Vector3(14f, 1.6f, 0f),
                new Vector3(2f, 1f, 1f),
                rotationDeg: 60f);

            CreatePlatform("LowerLanding", root.transform,
                new Vector3(-12f, -3f, 0f),
                new Vector3(8f, 1f, 1f));

            CreatePlatform("Wall_FlyTarget", root.transform,
                new Vector3(-8f, 4f, 0f),
                new Vector3(1f, 4f, 1f));

            CreateSnowball("Snowball_Rolling", root.transform,
                pos: new Vector3(-3f, 0.5f, 0f),
                mode: Snowball.Mode.Rolling,
                direction: 1,
                flyDir: Vector2.left);

            CreateSnowball("Snowball_Flying", root.transform,
                pos: new Vector3(6f, 5f, 0f),
                mode: Snowball.Mode.Flying,
                direction: 1,
                flyDir: new Vector2(-1f, -0.2f));

            var rollingPrefab = RebuildSnowballPrefab(RollingPrefabPath, Snowball.Mode.Rolling, 1, Vector2.left);

            CreateSpawner("Spawner_LeftRolling", root.transform,
                pos: new Vector3(-9f, 0.5f, 0f),
                initialAngleDegrees: 0f,
                prefab: rollingPrefab);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("Snowball Test setup complete!\n" +
                "- Snowball_Rolling: 평지에서 굴러가다가 +60° 슬로프에서 부서짐.\n" +
                "- Snowball_Flying: 비스듬히 날아가다가 벽 만나면 부서짐.\n" +
                "- Spawner_LeftRolling: 3초 간격으로 Rolling 눈덩이 spawn (maxAlive=3).\n" +
                "- 슬라임이 위에서 밟으면 자동 점프, 옆에서 부딪히면 밀려남, 어느 쪽이든 부서짐.\n" +
                "- 셰이더/머티리얼은 디자이너가 수동 설정 필요 (Snowball_Quibli.shader 사용한 .mat 만들고 SnowballMaterialBinder 에 할당).");
        }

        // ---------- helpers ----------

        static GameObject CreatePlatform(string name, Transform parent, Vector3 pos, Vector3 scale, float rotationDeg = 0f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.parent = parent;
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, rotationDeg);
            go.layer = 7;
            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            var col2d = go.AddComponent<BoxCollider2D>();
            col2d.size = Vector2.one;
            go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.5f, 0.5f, 0.5f));
            return go;
        }

        static GameObject CreateSnowball(string name, Transform parent, Vector3 pos,
            Snowball.Mode mode, int direction, Vector2 flyDir)
        {
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = pos;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;

            var trigger = go.AddComponent<CircleCollider2D>();
            trigger.isTrigger = true;
            trigger.radius = 0.5f;

            // Visual 자식 — 회전이 collider 에 영향 없도록 분리
            var visualGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visualGo.name = "Visual";
            Object.DestroyImmediate(visualGo.GetComponent<SphereCollider>());
            visualGo.transform.parent = go.transform;
            visualGo.transform.localPosition = Vector3.zero;
            visualGo.transform.localScale = Vector3.one * 1f;  // sphere = diameter 1 = radius 0.5

            var snowballMat = AssetDatabase.LoadAssetAtPath<Material>(SnowballMaterialPath);
            if (snowballMat != null)
            {
                var binder = visualGo.AddComponent<SnowballMaterialBinder>();
                var binderSo = new SerializedObject(binder);
                binderSo.FindProperty("baseMaterial").objectReferenceValue = snowballMat;
                binderSo.FindProperty("baseColor").colorValue = Color.white;  // 톤은 ramp 가 결정
                var ramp = AssetDatabase.LoadAssetAtPath<Texture2D>(GradientRampPath);
                var bump = AssetDatabase.LoadAssetAtPath<Texture2D>(SurfaceNormalPath);
                if (ramp != null) binderSo.FindProperty("shadowRamp").objectReferenceValue = ramp;
                if (bump != null) binderSo.FindProperty("bumpMap").objectReferenceValue = bump;
                binderSo.FindProperty("fresnelEnabled").boolValue = true;
                binderSo.FindProperty("fresnelColor").colorValue = new Color(0.7f, 0.85f, 1.0f, 1f);
                binderSo.FindProperty("glitterEnabled").boolValue = false;
                binderSo.ApplyModifiedProperties();
            }
            else
            {
                visualGo.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(Color.white);
            }

            var snowball = go.AddComponent<Snowball>();
            var so = new SerializedObject(snowball);
            so.FindProperty("mode").enumValueIndex = (int)mode;
            so.FindProperty("speed").floatValue = 6f;
            so.FindProperty("radius").floatValue = 0.5f;
            so.FindProperty("terrainMask").intValue = 1 << 7;
            so.FindProperty("direction").intValue = direction;
            so.FindProperty("breakSlopeDelta").floatValue = 30f;
            so.FindProperty("lookahead").floatValue = 0.3f;
            so.FindProperty("rayDepth").floatValue = 0.5f;
            so.FindProperty("fallGravity").floatValue = 30f;
            so.FindProperty("horizontalDecay").floatValue = 0f;
            so.FindProperty("maxFallTime").floatValue = 3f;
            so.FindProperty("maxLandingAngle").floatValue = 45f;
            so.FindProperty("flyDirection").vector2Value = flyDir;
            so.FindProperty("maxLifetime").floatValue = 5f;
            so.FindProperty("flightSpinSpeed").floatValue = 360f;
            so.FindProperty("fixedJumpSpeed").floatValue = 18f;
            so.FindProperty("incomingSpeedPercent").floatValue = 0.7f;
            so.FindProperty("bonusSpeed").floatValue = 5f;
            so.FindProperty("topNormalThreshold").floatValue = 0.5f;
            so.FindProperty("preserveHorizontalOnStomp").boolValue = true;
            so.FindProperty("knockbackSpeed").floatValue = 12f;
            so.FindProperty("knockbackUpBias").floatValue = 0.3f;
            so.FindProperty("respawnTime").floatValue = 3f;
            so.FindProperty("blockingLayers").intValue = 1 << 7;
            so.FindProperty("crumbleDuration").floatValue = 0.2f;
            so.FindProperty("fragmentBurstCount").intValue = 6;
            so.FindProperty("fragmentSpeed").floatValue = 4f;
            so.FindProperty("fragmentLifetime").floatValue = 1.5f;
            var fragmentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FragmentPrefabPath);
            if (fragmentPrefab != null)
                so.FindProperty("crumbleFragmentPrefab").objectReferenceValue = fragmentPrefab;
            so.FindProperty("cameraShakeForce").floatValue = 0.5f;
            so.FindProperty("sfxVolume").floatValue = 1f;
            so.FindProperty("visualRoot").objectReferenceValue = visualGo.transform;
            so.FindProperty("visualRenderer").objectReferenceValue = visualGo.GetComponent<MeshRenderer>();
            so.ApplyModifiedProperties();

            return go;
        }

        static GameObject CreateSpawner(string name, Transform parent, Vector3 pos, float initialAngleDegrees, Snowball prefab)
        {
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = pos;
            var spawner = go.AddComponent<SnowballSpawner>();

            var so = new SerializedObject(spawner);
            if (prefab != null)
                so.FindProperty("snowballPrefab").objectReferenceValue = prefab;
            so.FindProperty("initialAngleDegrees").floatValue = initialAngleDegrees;
            so.FindProperty("interval").floatValue = 3f;
            so.FindProperty("startDelay").floatValue = 0.5f;
            so.FindProperty("intervalJitter").floatValue = 0.2f;
            so.FindProperty("maxAlive").intValue = 3;
            so.ApplyModifiedProperties();
            return go;
        }

        // 매 invocation 재빌드 — CreateSnowball 의 현재 config 와 prefab 자산을 동기화.
        static Snowball RebuildSnowballPrefab(string assetPath, Snowball.Mode mode, int direction, Vector2 flyDir)
        {
            if (!Directory.Exists(PrefabDir)) Directory.CreateDirectory(PrefabDir);

            var temp = CreateSnowball("__SnowballPrefabBuild", null, Vector3.zero, mode, direction, flyDir);
            var prefabGo = PrefabUtility.SaveAsPrefabAsset(temp, assetPath);
            Object.DestroyImmediate(temp);
            return prefabGo != null ? prefabGo.GetComponent<Snowball>() : null;
        }

        static Material MakeMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            return mat;
        }
    }
}
