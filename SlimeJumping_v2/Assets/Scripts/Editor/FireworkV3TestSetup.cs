using UnityEngine;
using UnityEditor;
using Unity.Cinemachine;
using SlimeJumping.Effects;
using SlimeJumping.Interaction;

namespace SlimeJumping.Editor
{
    public static class FireworkV3TestSetup
    {
        const int InteractableLayer = 10;
        const int GroundLayer = 7;
        const string SF1PrefabPath = "Assets/ThirdParty/NamuFX/Simiple Firework/Prefabs/SF_1.prefab";
        const string RocketModelPath = "Assets/ThirdParty/_Use/firework_rocket.glb";
        const string IgniteSfxPath = "Assets/Sound/firework_new_260424192822.wav";
        const string BurstSfxPath = "Assets/Sound/firework_new_260424192832.wav";
        const string SparkleMatPath = "Assets/Prebs/VFX/SparkleAdditive.mat";

        // Stage 1 (MainScene FireworkV2_SF1Sync) rocket pose — reused verbatim so V3 shells look identical.
        static readonly Quaternion RocketUpright = new Quaternion(-0.7071068f, 0f, 0f, 0.7071068f);
        static readonly Vector2 RocketPivotOffset = new Vector2(-0.3f, 0.29f);
        static readonly Vector3 TrailLocalPos = new Vector3(-0.58f, 0f, 0f);
        static readonly Quaternion TrailLocalRot = new Quaternion(0.7071068f, 0f, 0f, 0.7071068f);
        const float TrailLocalScale = 1.6666664f;

        [MenuItem("SlimeJumping/Interaction/Setup Firework V3 Test")]
        public static void Setup()
        {
            var existing = GameObject.Find("FireworkV3Test");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject("FireworkV3Test");
            Undo.RegisterCreatedObjectUndo(root, "Create Firework V3 Test");

            var sf1 = AssetDatabase.LoadAssetAtPath<GameObject>(SF1PrefabPath);
            if (sf1 == null)
                Debug.LogWarning($"[FireworkV3TestSetup] SF_1 prefab not found at {SF1PrefabPath} — shells will have no burst VFX.");
            var rocketModel = AssetDatabase.LoadAssetAtPath<GameObject>(RocketModelPath);
            if (rocketModel == null)
                Debug.LogWarning($"[FireworkV3TestSetup] Rocket model not found at {RocketModelPath} — shells will have no mesh.");

            Vector3 basePos = new Vector3(4f, 0f, 0f);
            CreatePlatform("Ground", root.transform, basePos + new Vector3(0f, -0.5f, 0f), new Vector3(30f, 1f, 1f));

            var host = new GameObject("FireworkV3");
            host.transform.parent = root.transform;
            host.transform.position = basePos;
            host.layer = InteractableLayer;

            var col = host.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.offset = new Vector2(0f, 0.5f);
            col.size = new Vector2(2.4f, 1.4f);

            host.AddComponent<CinemachineImpulseSource>();
            var emitter = host.AddComponent<ImpulseEmitter>();

            // Three diverging shells, bursts staggered at 1.00 / 1.05 / 1.15 s.
            var anchorC = CreateAnchor("Anchor_C", host.transform, new Vector3(0f, 8f, 0f), new Color(1f, 0.45f, 0.1f));
            var anchorR = CreateAnchor("Anchor_R", host.transform, new Vector3(4.5f, 6.5f, 0f), new Color(1f, 0.85f, 0.3f));
            var anchorL = CreateAnchor("Anchor_L", host.transform, new Vector3(-4.5f, 6.5f, 0f), new Color(1f, 0.85f, 0.3f));

            // Rockets stand upright on the ground; the component tilts each into its path at lift-off.
            float groundTopY = basePos.y;
            var rocketC = CreateRocket("Rocket_C", host.transform, 0f, groundTopY, rocketModel);
            var rocketR = CreateRocket("Rocket_R", host.transform, 0.8f, groundTopY, rocketModel);
            var rocketL = CreateRocket("Rocket_L", host.transform, -0.8f, groundTopY, rocketModel);

            var fw = host.AddComponent<FireworkInteractable_V3>();
            var so = new SerializedObject(fw);

            var shells = so.FindProperty("shells");
            shells.arraySize = 3;
            SetShell(shells.GetArrayElementAtIndex(0), rocketC, anchorC, 0f, 1f);
            SetShell(shells.GetArrayElementAtIndex(1), rocketR, anchorR, 0.15f, 0.9f);
            SetShell(shells.GetArrayElementAtIndex(2), rocketL, anchorL, 0.3f, 0.85f);

            so.FindProperty("shellVfxPrefab").objectReferenceValue = sf1;
            so.FindProperty("visualPivotOffset").vector2Value = RocketPivotOffset;
            so.FindProperty("shellVfxCleanupDelay").floatValue = 5f;
            so.FindProperty("launchVelocity").vector2Value = new Vector2(0f, 22f);
            so.FindProperty("launchAwayFromExplosion").boolValue = true;
            so.FindProperty("launchRadius").floatValue = 10f;
            so.FindProperty("respawnDelay").floatValue = 3f;
            so.FindProperty("respawnDuration").floatValue = 0.35f;
            so.FindProperty("igniteSfx").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>(IgniteSfxPath);
            so.FindProperty("burstSfx").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>(BurstSfxPath);
            so.FindProperty("sfxVolume").floatValue = 1f;
            so.FindProperty("impulseEmitter").objectReferenceValue = emitter;
            so.FindProperty("impulseForce").floatValue = 0.3f;
            so.FindProperty("cooldown").floatValue = 0.5f;
            so.FindProperty("interactPrompt").stringValue = "Light Firework";
            so.ApplyModifiedProperties();

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = host;
            Debug.Log("Firework V3 Test setup complete!\n" +
                "- 3 rockets stand on the launcher, each already aimed at its own anchor.\n" +
                "- E lifts them off in turn: centre at 0.00s, right at 0.15s, left at 0.30s (bursts 1.00 / 1.05 / 1.15s).\n" +
                "- Each burst shoves the slime radially away from its anchor (18 at the centre, 0 at radius 6).\n" +
                "- Rockets hide at their burst and pop back onto the launcher 3s later.\n" +
                "- Select the host to see shell lines + push radius circles.");
        }

        static void SetShell(SerializedProperty element, GameObject visual, GameObject anchor,
                             float igniteDelay, float flightDuration)
        {
            element.FindPropertyRelative("visual").objectReferenceValue = visual != null ? visual.transform : null;
            element.FindPropertyRelative("targetAnchor").objectReferenceValue = anchor != null ? anchor.transform : null;
            element.FindPropertyRelative("igniteDelay").floatValue = igniteDelay;
            element.FindPropertyRelative("flightDuration").floatValue = flightDuration;
        }

        // Same object stage 1 flies: the glb instance itself is the shell, with the sparkle trail hung
        // off its inner "root" node in stage 1's hand-tuned pose (that pose is what puts the sparks at
        // the rocket's tail instead of at its pivot).
        static GameObject CreateRocket(string name, Transform parent, float localX, float groundTopY, GameObject rocketModel)
        {
            if (rocketModel == null) return null;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(rocketModel);
            go.name = name;
            go.transform.parent = parent;
            go.transform.localPosition = new Vector3(localX, 0f, 0f);
            go.transform.localRotation = RocketUpright;

            var trailParent = go.transform.Find("root");
            if (trailParent == null) trailParent = go.transform;
            var trail = CreateSparkleTrail(trailParent);
            trail.transform.localPosition = TrailLocalPos;
            trail.transform.localRotation = TrailLocalRot;
            trail.transform.localScale = Vector3.one * TrailLocalScale;

            // The glb pivot isn't at the model's feet, so sit it on the ground by its mesh bounds.
            bool any = false;
            Bounds bounds = new Bounds();
            foreach (var rend in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!any) { bounds = rend.bounds; any = true; }
                else bounds.Encapsulate(rend.bounds);
            }
            if (any)
                go.transform.position += new Vector3(0f, groundTopY - bounds.min.y, 0f);

            return go;
        }

        static GameObject CreateAnchor(string name, Transform parent, Vector3 localPos, Color markerColor)
        {
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.localPosition = localPos;

            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Marker";
            Object.DestroyImmediate(marker.GetComponent<SphereCollider>());
            marker.transform.parent = go.transform;
            marker.transform.localPosition = Vector3.zero;
            marker.transform.localScale = Vector3.one * 0.25f;
            marker.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(markerColor);
            return go;
        }

        static GameObject CreatePlatform(string name, Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.parent = parent;
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.layer = GroundLayer;
            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            var col2d = go.AddComponent<BoxCollider2D>();
            col2d.size = Vector2.one;
            go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.5f, 0.5f, 0.5f));
            return go;
        }

        static Material MakeMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            return mat;
        }

        // Same sparkle trail stage 1's shell carries (FireworkV2TestSetup); the component plays it at lift-off.
        static ParticleSystem CreateSparkleTrail(Transform parent)
        {
            var go = new GameObject("SparkleTrail");
            go.transform.parent = parent;
            go.transform.localPosition = Vector3.zero;
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 1f, 0.9f, 1f),
                new Color(1f, 0.8f, 0.3f, 1f));
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 140f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.08f;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            var twinkle = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.4f, 1f),
                new Keyframe(1f, 0f));
            size.size = new ParticleSystem.MinMaxCurve(1f, twinkle);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(1f, 0.7f, 0.2f), 1f),
                },
                new[] {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.3f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                var mat = LoadOrCreateSparkleMaterial();
                if (mat != null) renderer.sharedMaterial = mat;
            }

            return ps;
        }

        // Same asset FireworkV2TestSetup builds, created on demand so a clean project still gets stage 1's trail.
        static Material LoadOrCreateSparkleMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(SparkleMatPath);
            if (existing != null) return existing;

            var shader = Shader.Find("Legacy Shaders/Particles/Additive");
            if (shader == null) shader = Shader.Find("Particles/Additive");
            if (shader == null)
            {
                Debug.LogWarning("[FireworkV3TestSetup] Additive particle shader not found — sparkle material cannot be built.");
                return null;
            }

            var mat = new Material(shader) { name = "SparkleAdditive" };
            var tex = Resources.GetBuiltinResource<Texture2D>("Default-Particle.psd");
            if (tex != null) mat.mainTexture = tex;
            if (mat.HasProperty("_TintColor"))
                mat.SetColor("_TintColor", new Color(1f, 0.85f, 0.5f, 0.5f));

            EnsureFolderExists(System.IO.Path.GetDirectoryName(SparkleMatPath).Replace('\\', '/'));
            AssetDatabase.CreateAsset(mat, SparkleMatPath);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static void EnsureFolderExists(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;
            var parts = folderPath.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
