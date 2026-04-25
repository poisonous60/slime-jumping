using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    public static class GraniteBlastTestSetup
    {
        [MenuItem("SlimeJumping/Mechanism/Setup Granite Blast Test")]
        public static void Setup()
        {
            var existing = GameObject.Find("GraniteBlastTest");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject("GraniteBlastTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Granite Blast Test");

            float baseY = 0f;

            CreatePlatform("Ground", root.transform,
                new Vector3(0f, baseY - 0.5f, 0f),
                new Vector3(60f, 1f, 1f));

            // Three emitters along the ground, one per mode, aimed up-and-inward
            // so all three blasts converge on the slime if it hangs around the centre.
            CreateEmitter("Emitter_Branching", root.transform,
                hostPos: new Vector3(-10f, baseY + 1f, 0f),
                muzzleEulerZ: 70f,
                mode: GraniteBlastEmitter.FireMode.Branching);

            CreateEmitter("Emitter_Beam", root.transform,
                hostPos: new Vector3(0f, baseY + 1f, 0f),
                muzzleEulerZ: 90f,
                mode: GraniteBlastEmitter.FireMode.Beam);

            CreateEmitter("Emitter_Burst", root.transform,
                hostPos: new Vector3(10f, baseY + 1f, 0f),
                muzzleEulerZ: 110f,
                mode: GraniteBlastEmitter.FireMode.Burst);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("Granite Blast Test setup complete!\n" +
                "- Three emitters: Branching (left), Beam (centre), Burst (right).\n" +
                "- Each detects the slime, charges, then fires in its mode.\n" +
                "- Tune mode/spread/colors on the emitter inspector. AudioClips are unset by default.");
        }

        static GameObject CreateEmitter(string name, Transform parent, Vector3 hostPos, float muzzleEulerZ,
                                        GraniteBlastEmitter.FireMode mode)
        {
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = hostPos;

            // Pedestal mount
            var mount = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mount.name = "Mount";
            Object.DestroyImmediate(mount.GetComponent<BoxCollider>());
            mount.transform.parent = go.transform;
            mount.transform.localPosition = Vector3.zero;
            mount.transform.localScale = new Vector3(0.9f, 0.9f, 0.9f);
            mount.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.18f, 0.2f, 0.22f));

            // Head — rotates around Z so the muzzle's local +X aims along the chosen angle.
            var head = new GameObject("Head");
            head.transform.parent = go.transform;
            head.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            head.transform.localEulerAngles = new Vector3(0f, 0f, muzzleEulerZ);

            var headBody = GameObject.CreatePrimitive(PrimitiveType.Cube);
            headBody.name = "HeadBody";
            Object.DestroyImmediate(headBody.GetComponent<BoxCollider>());
            headBody.transform.parent = head.transform;
            headBody.transform.localPosition = new Vector3(0.25f, 0f, 0f);
            headBody.transform.localScale = new Vector3(0.7f, 0.55f, 0.55f);
            headBody.GetComponent<MeshRenderer>().sharedMaterial = MakeEmissiveMaterial(new Color(0.1f, 0.45f, 0.7f));

            var barrel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barrel.name = "Barrel";
            Object.DestroyImmediate(barrel.GetComponent<BoxCollider>());
            barrel.transform.parent = head.transform;
            barrel.transform.localPosition = new Vector3(0.85f, 0f, 0f);
            barrel.transform.localScale = new Vector3(0.7f, 0.18f, 0.18f);
            barrel.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.06f, 0.08f, 0.12f));

            var muzzle = new GameObject("Muzzle");
            muzzle.transform.parent = head.transform;
            muzzle.transform.localPosition = new Vector3(1.25f, 0f, 0f);
            muzzle.transform.localRotation = Quaternion.identity;

            // Detection indicator — small cyan sphere above the head.
            var indicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            indicator.name = "DetectIndicator";
            Object.DestroyImmediate(indicator.GetComponent<SphereCollider>());
            indicator.transform.parent = head.transform;
            indicator.transform.localPosition = new Vector3(0.1f, 0.55f, 0f);
            indicator.transform.localScale = Vector3.one * 0.18f;
            indicator.GetComponent<MeshRenderer>().sharedMaterial = MakeEmissiveMaterial(new Color(0.3f, 0.85f, 1f));
            indicator.SetActive(false);

            var chargeGlow = CreateChargeGlow(muzzle.transform);
            var muzzleFlash = CreateMuzzleFlash(muzzle.transform);
            var impactTemplate = CreateImpactTemplate(go.transform);

            var emitter = go.AddComponent<GraniteBlastEmitter>();
            {
                var so = new SerializedObject(emitter);
                so.FindProperty("mode").enumValueIndex = (int)mode;
                so.FindProperty("muzzle").objectReferenceValue = muzzle.transform;
                so.FindProperty("detectionRange").floatValue = 18f;
                so.FindProperty("detectionDelay").floatValue = 0.6f;
                so.FindProperty("preFireDuration").floatValue = 0.7f;
                so.FindProperty("fireStartDuration").floatValue = 0.08f;
                so.FindProperty("firingDuration").floatValue = 0.45f;
                so.FindProperty("postFireDuration").floatValue = 0.25f;
                so.FindProperty("cooldownDuration").floatValue = 1.2f;
                so.FindProperty("loseSightGrace").floatValue = 0.5f;
                so.FindProperty("maxRange").floatValue = 30f;
                // Default (0) | SlimeNode (6) | Environment (7) — same as TurretHazard.
                so.FindProperty("bulletHitMask").intValue = (1 << 0) | (1 << 6) | (1 << 7);
                // Snap branching tendril endpoints onto Environment (layer 7) only —
                // avoids snapping onto the slime itself.
                so.FindProperty("groundSnapMask").intValue = (1 << 7);
                so.FindProperty("knockbackForce").floatValue = 18f;
                so.FindProperty("stunDuration").floatValue = 0.5f;
                so.FindProperty("chargeGlow").objectReferenceValue = chargeGlow;
                so.FindProperty("muzzleFlash").objectReferenceValue = muzzleFlash;
                so.FindProperty("impactPrefab").objectReferenceValue = impactTemplate;
                so.FindProperty("detectionIndicator").objectReferenceValue = indicator;
                so.ApplyModifiedProperties();
            }

            return go;
        }

        static ParticleSystem CreateChargeGlow(Transform parent)
        {
            var go = new GameObject("ChargeGlow");
            go.transform.parent = parent;
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one * 0.6f;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 1.2f;
            main.loop = true;
            main.startLifetime = 0.4f;
            main.startSpeed = 0.4f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.6f, 0.9f, 1f, 1f), new Color(0.85f, 0.95f, 1f, 1f));
            main.stopAction = ParticleSystemStopAction.None;
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.rateOverTime = 30f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.18f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.7f, 0.95f, 1f), 0f), new GradientColorKey(new Color(0.4f, 0.7f, 1f), 1f) },
                new[] { new GradientAlphaKey(0.0f, 0f), new GradientAlphaKey(0.9f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.4f, 1f, 0f));

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            var mat = new Material(Shader.Find("Sprites/Default"));
            // Additive blend matches the line stack's halo/glow look.
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_ZWrite", 0);
            mat.mainTexture = Texture2D.whiteTexture;
            renderer.material = mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            return ps;
        }

        static ParticleSystem CreateMuzzleFlash(Transform parent)
        {
            var go = new GameObject("MuzzleFlash");
            go.transform.parent = parent;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 0.12f;
            main.loop = false;
            main.startLifetime = 0.18f;
            main.startSpeed = 5f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.28f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.97f, 1f, 1f));
            main.stopAction = ParticleSystemStopAction.None;
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 18f;
            shape.radius = 0.05f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.9f, 0.98f, 1f), 0f), new GradientColorKey(new Color(0.4f, 0.7f, 1f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.1f));

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_ZWrite", 0);
            mat.mainTexture = Texture2D.whiteTexture;
            renderer.material = mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            return ps;
        }

        static ParticleSystem CreateImpactTemplate(Transform parent)
        {
            var go = new GameObject("ImpactTemplate");
            go.transform.parent = parent;
            go.transform.localPosition = Vector3.zero;
            go.SetActive(false);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 0.4f;
            main.loop = false;
            main.startLifetime = 0.45f;
            main.startSpeed = 4f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.5f, 0.9f, 1f, 1f), new Color(0.95f, 0.99f, 1f, 1f));
            main.stopAction = ParticleSystemStopAction.None;
            main.playOnAwake = false;
            main.gravityModifier = 0.1f;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 22) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.12f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.9f, 0.99f, 1f), 0f), new GradientColorKey(new Color(0.3f, 0.6f, 1f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_ZWrite", 0);
            mat.mainTexture = Texture2D.whiteTexture;
            renderer.material = mat;
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
