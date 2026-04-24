using UnityEngine;
using UnityEditor;
using Unity.Cinemachine;
using SlimeJumping.Effects;
using SlimeJumping.Interaction;

namespace SlimeJumping.Editor
{
    public static class FireworkV2TestSetup
    {
        const int InteractableLayer = 10;
        const string SF1PrefabPath = "Assets/ThirdParty/NamuFX/Simiple Firework/Prefabs/SF_1.prefab";

        [MenuItem("SlimeJumping/Interaction/Setup Firework V2 Test")]
        public static void Setup()
        {
            var existing = GameObject.Find("FireworkV2Test");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject("FireworkV2Test");
            Undo.RegisterCreatedObjectUndo(root, "Create Firework V2 Test");

            float baseX = 4f;
            float baseY = 0f;

            CreatePlatform("Ground", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f),
                new Vector3(30f, 1f, 1f));

            // Left — vertical flight, straight-up slime launch
            CreateFireworkV2("FireworkV2_Up", root.transform,
                hostPos: new Vector3(baseX - 4f, baseY + 0.3f, 0f),
                flightDirection: Vector2.up,
                flightDistance: 5f,
                launchVelocity: new Vector2(0f, 18f),
                launchAwayFromExplosion: false);

            // Middle — diagonal flight
            CreateFireworkV2("FireworkV2_Angled", root.transform,
                hostPos: new Vector3(baseX, baseY + 0.3f, 0f),
                flightDirection: new Vector2(0.35f, 1f),
                flightDistance: 5.5f,
                launchVelocity: new Vector2(6f, 16f),
                launchAwayFromExplosion: false);

            // Right — radial fling away from explosion apex
            CreateFireworkV2("FireworkV2_Radial", root.transform,
                hostPos: new Vector3(baseX + 4.5f, baseY + 0.3f, 0f),
                flightDirection: Vector2.up,
                flightDistance: 4.5f,
                launchVelocity: new Vector2(0f, 14f),
                launchAwayFromExplosion: true);

            // Far right — VFX-sync mode: rocket mesh rides the SF_1 particle, SF_1 handles explosion
            var sf1 = AssetDatabase.LoadAssetAtPath<GameObject>(SF1PrefabPath);
            if (sf1 != null)
            {
                var vfxSync = CreateFireworkV2("FireworkV2_SF1Sync", root.transform,
                    hostPos: new Vector3(baseX + 9f, baseY + 0.3f, 0f),
                    flightDirection: Vector2.up,
                    flightDistance: 5f,
                    launchVelocity: new Vector2(0f, 18f),
                    launchAwayFromExplosion: false);
                var fw = vfxSync.GetComponent<FireworkInteractable_V2>();

                // Remove the local jet-flame trail + explosion — SF_1 handles explosion;
                // we'll attach a white smoke trail for the flight.
                var toRemove = vfxSync.transform.Find("ExplosionVFX");
                if (toRemove != null) Object.DestroyImmediate(toRemove.gameObject);
                var rocket = vfxSync.transform.Find("RocketVisual");
                if (rocket != null)
                {
                    var oldTrail = rocket.Find("TrailVFX");
                    if (oldTrail != null) Object.DestroyImmediate(oldTrail.gameObject);
                }
                var smokeTrail = rocket != null ? CreateSparkleTrail(rocket) : null;

                var so = new SerializedObject(fw);
                so.FindProperty("flightVfxPrefab").objectReferenceValue = sf1;
                so.FindProperty("trailVFX").objectReferenceValue = smokeTrail;
                so.FindProperty("explosionVFX").objectReferenceValue = null;
                so.ApplyModifiedProperties();
            }
            else
            {
                Debug.LogWarning($"[FireworkV2TestSetup] SF_1 prefab not found at {SF1PrefabPath} — skipping SF1Sync instance.");
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("Firework V2 Test setup complete!\n" +
                "- FireworkV2_Up: rocket flies straight up 5u, explodes, slime (0,18).\n" +
                "- FireworkV2_Angled: rocket flies (0.35,1) 5.5u, slime (6,16).\n" +
                "- FireworkV2_Radial: rocket flies up, slime launched away from apex (mag 14).\n" +
                "- FireworkV2_SF1Sync: mesh rides SF_1 particle (startSpeed×lifetime = ~15u up), SF_1 sub-emitters fire burst on death.\n" +
                "- Respawn after 3s with pop-in scale curve. Cooldown 0.5s (busy flag handles re-entry).");
        }

        static GameObject CreateFireworkV2(string name, Transform parent,
            Vector3 hostPos, Vector2 flightDirection, float flightDistance,
            Vector2 launchVelocity, bool launchAwayFromExplosion)
        {
            // Root — holds collider, audio, VFX (persistent) and the v2 component
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = hostPos;
            go.layer = InteractableLayer;

            // Rocket visual container (child — this is what moves/hides/respawns)
            var rocket = new GameObject("RocketVisual");
            rocket.transform.parent = go.transform;
            rocket.transform.localPosition = Vector3.zero;

            // Rocket body (cylinder)
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<CapsuleCollider>());
            body.transform.parent = rocket.transform;
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale = new Vector3(0.3f, 0.5f, 0.3f);
            body.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.9f, 0.3f, 0.2f));

            // Nose cone (sphere stand-in)
            var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tip.name = "Tip";
            Object.DestroyImmediate(tip.GetComponent<SphereCollider>());
            tip.transform.parent = rocket.transform;
            tip.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            tip.transform.localScale = Vector3.one * 0.25f;
            tip.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(1f, 0.85f, 0.1f));

            // Fin marker (small cube, purely visual)
            var fin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fin.name = "Fin";
            Object.DestroyImmediate(fin.GetComponent<BoxCollider>());
            fin.transform.parent = rocket.transform;
            fin.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            fin.transform.localScale = new Vector3(0.45f, 0.12f, 0.12f);
            fin.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.15f, 0.15f, 0.18f));

            // Trail VFX — childed to rocket visual so it follows during flight
            var trailGo = new GameObject("TrailVFX");
            trailGo.transform.parent = rocket.transform;
            trailGo.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            var trail = trailGo.AddComponent<ParticleSystem>();
            {
                var main = trail.main;
                main.duration = 2f;
                main.startLifetime = 0.35f;
                main.startSpeed = 1.5f;
                main.startSize = 0.12f;
                main.startColor = new Color(1f, 0.6f, 0.2f);
                main.loop = true;
                main.playOnAwake = false;
                var emission = trail.emission;
                emission.rateOverTime = 60;
                var shape = trail.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 8f;
                shape.radius = 0.05f;
                shape.rotation = new Vector3(90f, 0f, 0f); // point downward
            }

            // Explosion VFX — kept at root (NOT childed to rocket) so it survives rocket hide
            var explGo = new GameObject("ExplosionVFX");
            explGo.transform.parent = go.transform;
            explGo.transform.localPosition = Vector3.zero;
            var explosion = explGo.AddComponent<ParticleSystem>();
            {
                var main = explosion.main;
                main.duration = 1.2f;
                main.startLifetime = 0.9f;
                main.startSpeed = 9f;
                main.startSize = 0.18f;
                main.startColor = new Color(1f, 0.8f, 0.2f);
                main.loop = false;
                main.playOnAwake = false;
                var emission = explosion.emission;
                emission.rateOverTime = 0;
                emission.SetBurst(0, new ParticleSystem.Burst(0f, 80));
                var shape = explosion.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.05f;
            }

            // Trigger collider on root (visual/collider separation)
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1f, 1.3f);

            // Camera shake
            go.AddComponent<CinemachineImpulseSource>();
            var emitter = go.AddComponent<ImpulseEmitter>();

            // Component wiring
            var fw = go.AddComponent<FireworkInteractable_V2>();
            var so = new SerializedObject(fw);
            so.FindProperty("rocketVisual").objectReferenceValue = rocket.transform;
            so.FindProperty("flightDirection").vector2Value = flightDirection;
            so.FindProperty("flightDistance").floatValue = flightDistance;
            so.FindProperty("flightDuration").floatValue = 0.4f;
            so.FindProperty("launchVelocity").vector2Value = launchVelocity;
            so.FindProperty("launchAwayFromExplosion").boolValue = launchAwayFromExplosion;
            so.FindProperty("explosionVFX").objectReferenceValue = explosion;
            so.FindProperty("trailVFX").objectReferenceValue = trail;
            so.FindProperty("impulseEmitter").objectReferenceValue = emitter;
            so.FindProperty("impulseForce").floatValue = 0.3f;
            so.FindProperty("respawnDelay").floatValue = 3f;
            so.FindProperty("respawnDuration").floatValue = 0.35f;
            so.FindProperty("cooldown").floatValue = 0.5f;
            so.FindProperty("interactPrompt").stringValue = "Light Firework";
            so.ApplyModifiedProperties();

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
            // Warm bright colors — additive blend will make these pop as sparkles
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

            // Twinkle: size pops 0 → 1 → 0 so each sparkle fades in and out
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            var twinkle = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.4f, 1f),
                new Keyframe(1f, 0f)
            );
            size.size = new ParticleSystem.MinMaxCurve(1f, twinkle);

            // Alpha fades in/out so the additive glow pulses
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
                }
            );
            col.color = new ParticleSystem.MinMaxGradient(gradient);

            // Additive material saved as a project asset so prefab serialization survives
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                var mat = LoadOrCreateSparkleMaterial();
                if (mat != null) renderer.sharedMaterial = mat;
            }

            return ps;
        }

        const string SparkleMatPath = "Assets/Prebs/VFX/SparkleAdditive.mat";

        static Material LoadOrCreateSparkleMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(SparkleMatPath);
            if (existing != null) return existing;

            var shader = Shader.Find("Legacy Shaders/Particles/Additive");
            if (shader == null) shader = Shader.Find("Particles/Additive");
            if (shader == null)
            {
                Debug.LogWarning("[FireworkV2TestSetup] Additive particle shader not found — sparkle material cannot be built.");
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
            string cur = parts[0]; // "Assets"
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
