using UnityEngine;
using UnityEditor;
using Unity.Cinemachine;
using SlimeJumping.Effects;
using SlimeJumping.Interaction;

namespace SlimeJumping.Editor
{
    public static class FireworkTestSetup
    {
        const int InteractableLayer = 10;

        [MenuItem("SlimeJumping/Interaction/Setup Firework Test")]
        public static void Setup()
        {
            var existing = GameObject.Find("FireworkTest");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject("FireworkTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Firework Test");

            float baseX = 4f;
            float baseY = 0f;

            CreatePlatform("Ground", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f),
                new Vector3(20f, 1f, 1f));

            CreateFirework("Firework_Up", root.transform,
                hostPos: new Vector3(baseX - 2f, baseY + 0.3f, 0f),
                launchVelocity: new Vector2(0f, 18f),
                launchAwayFromCenter: false);

            CreateFirework("Firework_Radial", root.transform,
                hostPos: new Vector3(baseX + 4f, baseY + 0.3f, 0f),
                launchVelocity: new Vector2(0f, 14f),
                launchAwayFromCenter: true);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("Firework Test setup complete!\n" +
                "- Firework_Up: W → launches slime straight up (0,18).\n" +
                "- Firework_Radial: W → launches away from firework center (magnitude 14).\n" +
                "- Camera shake via ImpulseEmitter force 0.3. Cooldown 1.5s.");
        }

        static GameObject CreateFirework(string name, Transform parent,
            Vector3 hostPos, Vector2 launchVelocity, bool launchAwayFromCenter)
        {
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = hostPos;
            go.layer = InteractableLayer;

            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<CapsuleCollider>());
            body.transform.parent = go.transform;
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale = new Vector3(0.3f, 0.5f, 0.3f);
            body.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.9f, 0.3f, 0.2f));

            var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tip.name = "Tip";
            Object.DestroyImmediate(tip.GetComponent<SphereCollider>());
            tip.transform.parent = go.transform;
            tip.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            tip.transform.localScale = Vector3.one * 0.2f;
            tip.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(1f, 0.85f, 0.1f));

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1f, 1.2f);

            var vfxGo = new GameObject("BurstVFX");
            vfxGo.transform.parent = go.transform;
            vfxGo.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            var ps = vfxGo.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 0.6f;
            main.startLifetime = 0.5f;
            main.startSpeed = 6f;
            main.startSize = 0.15f;
            main.startColor = new Color(1f, 0.8f, 0.2f);
            main.loop = false;
            main.playOnAwake = false;
            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBurst(0, new ParticleSystem.Burst(0f, 40));
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;

            var audio = go.AddComponent<AudioSource>();
            audio.spatialBlend = 1f;
            audio.playOnAwake = false;

            var impulseSrc = go.AddComponent<CinemachineImpulseSource>();
            var emitter = go.AddComponent<ImpulseEmitter>();

            var fw = go.AddComponent<FireworkInteractable>();
            var so = new SerializedObject(fw);
            so.FindProperty("launchVelocity").vector2Value = launchVelocity;
            so.FindProperty("launchAwayFromCenter").boolValue = launchAwayFromCenter;
            so.FindProperty("fireworkVFX").objectReferenceValue = ps;
            so.FindProperty("audioSource").objectReferenceValue = audio;
            so.FindProperty("impulseEmitter").objectReferenceValue = emitter;
            so.FindProperty("impulseForce").floatValue = 0f; // camera shake off by default — VFX is the focus
            so.FindProperty("cooldown").floatValue = 1.5f;
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
    }
}
