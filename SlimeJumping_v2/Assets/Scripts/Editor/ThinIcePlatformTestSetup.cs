using UnityEngine;
using UnityEditor;
using SlimeJumping.Platforms;

namespace SlimeJumping.Editor
{
    public static class ThinIcePlatformTestSetup
    {
        const int GroundLayer = 7;

        [MenuItem("SlimeJumping/Platform/Setup ThinIcePlatform Test")]
        public static void Setup()
        {
            var root = new GameObject("ThinIcePlatformTest");
            Undo.RegisterCreatedObjectUndo(root, "Create ThinIcePlatform Test");

            // --- Support geometry ---
            CreateBoxPlatform("Floor", root.transform,
                new Vector3(0f, -4f, 0f), new Vector3(24f, 1f, 1f));

            CreateBoxPlatform("CatchFloor", root.transform,
                new Vector3(0f, -14f, 0f), new Vector3(30f, 1f, 1f));

            CreateBoxPlatform("LeftWall", root.transform,
                new Vector3(-11f, 1f, 0f), new Vector3(1f, 11f, 1f));

            CreateBoxPlatform("RightWall", root.transform,
                new Vector3(11f, 1f, 0f), new Vector3(1f, 11f, 1f));

            // --- ThinIce variants ---
            CreateThinIce("ThinIce_Default", root.transform,
                new Vector3(-6f, 0f, 0f), new Vector3(3f, 0.5f, 1f),
                triggerDelay: 1.0f, crumbleDuration: 0.15f,
                respawnTime: 3.0f, regenDuration: 0.6f);

            CreateThinIce("ThinIce_Fast", root.transform,
                new Vector3(-2f, 0f, 0f), new Vector3(3f, 0.5f, 1f),
                triggerDelay: 0.3f, crumbleDuration: 0.1f,
                respawnTime: 2.0f, regenDuration: 0.4f);

            CreateThinIce("ThinIce_Slow", root.transform,
                new Vector3(2f, 0f, 0f), new Vector3(3f, 0.5f, 1f),
                triggerDelay: 2.0f, crumbleDuration: 0.2f,
                respawnTime: 4.0f, regenDuration: 1.2f);

            // respawnTime < 0 = never regenerates
            CreateThinIce("ThinIce_Permanent", root.transform,
                new Vector3(6f, 0f, 0f), new Vector3(3f, 0.5f, 1f),
                triggerDelay: 1.0f, crumbleDuration: 0.15f,
                respawnTime: -1f, regenDuration: 0.6f);

            // stacked pair: upper tile must not regen while the slime rests on it
            CreateThinIce("ThinIce_Upper", root.transform,
                new Vector3(-2f, 3.5f, 0f), new Vector3(4f, 0.5f, 1f),
                triggerDelay: 1.0f, crumbleDuration: 0.15f,
                respawnTime: 3.0f, regenDuration: 0.6f);

            // Shared audio manager — broadcasts one clip set to every tile on Awake.
            var audioMgrGo = new GameObject("ThinIcePlatformAudioManager");
            audioMgrGo.transform.SetParent(root.transform, false);
            audioMgrGo.AddComponent<ThinIcePlatformAudioManager>();

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("ThinIcePlatform test setup complete.\n" +
                      "- Assign crack / shatter / regen AudioClips on ThinIcePlatformAudioManager " +
                      "— they broadcast to every tile on Awake.\n" +
                      "- ShatterParticles slot is pre-wired per tile with a simple burst PS.\n" +
                      "- ThinIce_Permanent never regenerates (respawnTime = -1).");
        }

        static GameObject CreateThinIce(string name, Transform parent, Vector3 pos, Vector3 scale,
            float triggerDelay, float crumbleDuration, float respawnTime, float regenDuration)
        {
            // Visual is a child so shake/scale on it never feeds back into the collider.
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.layer = GroundLayer;

            var col = go.AddComponent<BoxCollider2D>();
            col.size = Vector2.one;

            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            Object.DestroyImmediate(visual.GetComponent<BoxCollider>());
            visual.transform.SetParent(go.transform, worldPositionStays: false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localScale = Vector3.one;
            visual.layer = GroundLayer;

            var renderer = visual.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = EnsureIceMaterial();

            var psGo = new GameObject("ShatterParticles");
            psGo.transform.SetParent(visual.transform, worldPositionStays: false);
            psGo.transform.localPosition = Vector3.zero;
            var ps = psGo.AddComponent<ParticleSystem>();
            ConfigureShatterParticles(ps);

            var ice = go.AddComponent<ThinIcePlatform>();
            var so = new SerializedObject(ice);
            so.FindProperty("triggerDelay").floatValue = triggerDelay;
            so.FindProperty("crumbleDuration").floatValue = crumbleDuration;
            so.FindProperty("respawnTime").floatValue = respawnTime;
            so.FindProperty("regenDuration").floatValue = regenDuration;
            so.FindProperty("visualRoot").objectReferenceValue = visual.transform;
            so.FindProperty("visualRenderer").objectReferenceValue = renderer;
            so.FindProperty("maxShakeOffset").floatValue = 0.05f;
            so.FindProperty("crackingTint").colorValue = new Color(0.55f, 0.8f, 1f, 1f);
            so.FindProperty("soundVolume").floatValue = 1f;
            so.FindProperty("shatterParticles").objectReferenceValue = ps;
            so.FindProperty("blockingLayers").intValue = ~0;
            so.ApplyModifiedProperties();

            return go;
        }

        static void ConfigureShatterParticles(ParticleSystem ps)
        {
            var main = ps.main;
            main.duration = 0.5f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = 0.6f;
            main.startSpeed = 4f;
            main.startSize = 0.12f;
            main.startColor = new Color(0.7f, 0.9f, 1f, 1f);
            main.gravityModifier = 1.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 64;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 24) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1f, 0.2f, 0.2f);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                var mat = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");
                if (mat != null) renderer.sharedMaterial = mat;
                renderer.sortingOrder = 1;
            }

            // Start stopped — the ThinIcePlatform calls Play() on shatter.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        static Material EnsureIceMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader);
            mat.name = "ThinIce_TestMat";
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", new Color(0.75f, 0.9f, 1f, 1f));
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", new Color(0.75f, 0.9f, 1f, 1f));
            return mat;
        }

        static GameObject CreateBoxPlatform(string name, Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.parent = parent;
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.layer = GroundLayer;

            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            go.AddComponent<BoxCollider2D>().size = Vector2.one;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            go.GetComponent<MeshRenderer>().sharedMaterial =
                new Material(shader) { color = new Color(0.45f, 0.45f, 0.5f) };
            return go;
        }
    }
}
