using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    public static class TurretTestSetup
    {
        [MenuItem("SlimeJumping/Mechanism/Setup Turret Test")]
        public static void Setup()
        {
            var existing = GameObject.Find("TurretTest");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject("TurretTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Turret Test");

            float baseX = 4f;
            float baseY = 0f;

            CreatePlatform("Ground", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f),
                new Vector3(30f, 1f, 1f));

            CreateTurret("Turret_Left", root.transform,
                hostPos: new Vector3(baseX - 6f, baseY + 5f, 0f),
                headEuler: new Vector3(0f, 0f, -30f),
                detectionRange: 18f);

            CreateTurret("Turret_Right", root.transform,
                hostPos: new Vector3(baseX + 6f, baseY + 5f, 0f),
                headEuler: new Vector3(0f, 0f, 30f),
                detectionRange: 14f);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("Turret Test setup complete!\n" +
                "- Two turrets track the slime via CCTVTracker, then freeze aim and fire.\n" +
                "- Tune knockback/detection/telegraph timing in TurretHazard inspector.\n" +
                "- Hook up your own AudioClips and ParticleSystem prefabs for production FX.");
        }

        static GameObject CreateTurret(string name, Transform parent, Vector3 hostPos, Vector3 headEuler, float detectionRange)
        {
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = hostPos;

            var mount = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mount.name = "Mount";
            Object.DestroyImmediate(mount.GetComponent<BoxCollider>());
            mount.transform.parent = go.transform;
            mount.transform.localPosition = Vector3.zero;
            mount.transform.localScale = new Vector3(0.6f, 0.3f, 0.3f);
            mount.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.15f, 0.15f, 0.15f));

            // Barrel/lens convention: local +X, matching CCTVTracker.LensAxis.PositiveX.
            var head = new GameObject("Head");
            head.transform.parent = go.transform;
            head.transform.localPosition = new Vector3(0f, -0.3f, 0f);
            head.transform.localEulerAngles = headEuler;

            var headBody = GameObject.CreatePrimitive(PrimitiveType.Cube);
            headBody.name = "HeadBody";
            Object.DestroyImmediate(headBody.GetComponent<BoxCollider>());
            headBody.transform.parent = head.transform;
            headBody.transform.localPosition = new Vector3(0.25f, 0f, 0f);
            headBody.transform.localScale = new Vector3(0.7f, 0.45f, 0.45f);
            headBody.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.35f, 0.1f, 0.1f));

            var barrel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barrel.name = "Barrel";
            Object.DestroyImmediate(barrel.GetComponent<BoxCollider>());
            barrel.transform.parent = head.transform;
            barrel.transform.localPosition = new Vector3(0.85f, 0f, 0f);
            barrel.transform.localScale = new Vector3(0.7f, 0.15f, 0.15f);
            barrel.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.05f, 0.05f, 0.08f));

            var muzzle = new GameObject("Muzzle");
            muzzle.transform.parent = head.transform;
            muzzle.transform.localPosition = new Vector3(1.25f, 0f, 0f);
            muzzle.transform.localRotation = Quaternion.identity;

            var indicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            indicator.name = "DetectIndicator";
            Object.DestroyImmediate(indicator.GetComponent<SphereCollider>());
            indicator.transform.parent = head.transform;
            indicator.transform.localPosition = new Vector3(0.1f, 0.45f, 0f);
            indicator.transform.localScale = Vector3.one * 0.18f;
            indicator.GetComponent<MeshRenderer>().sharedMaterial = MakeEmissiveMaterial(new Color(1f, 0.15f, 0.1f));
            indicator.SetActive(false);

            // Laser sight + bullet tracer LineRenderers are auto-created by TurretHazard.Start().

            var muzzleFlash = CreateMuzzleFlash(muzzle.transform);

            var impactTemplate = CreateImpactTemplate(go.transform);

            var cctv = go.AddComponent<CCTVTracker>();
            {
                var so = new SerializedObject(cctv);
                so.FindProperty("headTransform").objectReferenceValue = head.transform;
                so.FindProperty("lensAxis").enumValueIndex = (int)CCTVTracker.LensAxis.PositiveX;
                so.FindProperty("trackingRange").floatValue = detectionRange;
                // Aim lag is owned by TurretHazard.trackingDelay, not by this angular limit.
                so.FindProperty("angularSpeed").floatValue = 360f;
                so.FindProperty("maxLeftDeg").floatValue = 90f;
                so.FindProperty("maxRightDeg").floatValue = 90f;
                so.FindProperty("maxUpDeg").floatValue = 60f;
                so.FindProperty("maxDownDeg").floatValue = 60f;
                so.ApplyModifiedProperties();
            }

            var turret = go.AddComponent<TurretHazard>();
            {
                var so = new SerializedObject(turret);
                so.FindProperty("detectionRange").floatValue = detectionRange;
                so.FindProperty("muzzle").objectReferenceValue = muzzle.transform;
                so.FindProperty("muzzleAxis").enumValueIndex = (int)CCTVTracker.LensAxis.PositiveX;
                so.FindProperty("detectionDelay").floatValue = 1.2f;
                so.FindProperty("telegraphDuration").floatValue = 0.6f;
                so.FindProperty("cooldown").floatValue = 0.9f;
                so.FindProperty("loseSightGrace").floatValue = 0.5f;
                so.FindProperty("trackingDelay").floatValue = 1.0f;
                so.FindProperty("maxShotDistance").floatValue = 30f;
                // Default(0) | SlimeNode(6) | Environment(7); Interactable/Sensor/Water are
                // excluded because those triggers would block the raycast at the muzzle.
                so.FindProperty("bulletHitMask").intValue = (1 << 0) | (1 << 6) | (1 << 7);
                so.FindProperty("knockbackForce").floatValue = 22f;
                so.FindProperty("stunDuration").floatValue = 0.4f;
                so.FindProperty("muzzleFlash").objectReferenceValue = muzzleFlash;
                so.FindProperty("impactPrefab").objectReferenceValue = impactTemplate;
                so.FindProperty("tracerDuration").floatValue = 0.06f;
                so.FindProperty("detectionIndicator").objectReferenceValue = indicator;
                so.ApplyModifiedProperties();
            }

            return go;
        }

        static ParticleSystem CreateMuzzleFlash(Transform parent)
        {
            var go = new GameObject("MuzzleFlash");
            go.transform.parent = parent;
            go.transform.localPosition = Vector3.zero;
            // Point along +X
            go.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 0.1f;
            main.loop = false;
            main.startLifetime = 0.08f;
            main.startSpeed = 4f;
            main.startSize = 0.25f;
            main.startColor = new Color(1f, 0.7f, 0.2f, 1f);
            main.stopAction = ParticleSystemStopAction.None;
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 12) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 15f;
            shape.radius = 0.05f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.9f, 0.4f), 0f), new GradientColorKey(new Color(1f, 0.3f, 0.05f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.1f));

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = new Material(Shader.Find("Sprites/Default"));
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            return ps;
        }

        static ParticleSystem CreateImpactTemplate(Transform parent)
        {
            // Kept disabled: TurretHazard Instantiates from it, the template must never play.
            var go = new GameObject("ImpactTemplate");
            go.transform.parent = parent;
            go.transform.localPosition = Vector3.zero;
            go.SetActive(false);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 0.4f;
            main.loop = false;
            main.startLifetime = 0.5f;
            main.startSpeed = 3f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.7f, 0.6f, 0.45f), new Color(0.9f, 0.85f, 0.7f));
            main.stopAction = ParticleSystemStopAction.None;
            main.playOnAwake = false;
            main.gravityModifier = 0.5f;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 20) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.1f;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = new Material(Shader.Find("Sprites/Default"));
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            return ps;
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
            go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.5f, 0.5f, 0.5f));
            return go;
        }

        static Material MakeMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            return mat;
        }

        static Material MakeEmissiveMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 2f);
            return mat;
        }
    }
}
