using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    public static class WindZoneTestSetup
    {
        [MenuItem("SlimeJumping/Mechanism/Setup Wind Zone Test")]
        public static void SetupWindZoneTest()
        {
            var root = new GameObject("WindZoneTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Wind Zone Test");

            float baseX = 4f;
            float baseY = 0f;

            // --- Floor ---
            CreatePlatform("Floor", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f),
                new Vector3(20f, 1f, 1f));

            // --- Left wall (wind pushes slime toward this) ---
            CreatePlatform("LeftWall", root.transform,
                new Vector3(baseX - 10.5f, baseY + 4f, 0f),
                new Vector3(1f, 9f, 1f));

            // --- Right wall ---
            CreatePlatform("RightWall", root.transform,
                new Vector3(baseX + 10.5f, baseY + 4f, 0f),
                new Vector3(1f, 9f, 1f));

            // --- Ceiling above wind zone (test attached + wind) ---
            CreatePlatform("Ceiling", root.transform,
                new Vector3(baseX + 3f, baseY + 8.5f, 0f),
                new Vector3(8f, 1f, 1f));

            // --- Step platform (launch pad before wind zone) ---
            CreatePlatform("LaunchStep", root.transform,
                new Vector3(baseX - 6f, baseY + 2f, 0f),
                new Vector3(3f, 0.5f, 1f));

            // --- Wind Zone (horizontal, rightward) ---
            var windZone = new GameObject("WindZone_Right");
            windZone.transform.parent = root.transform;
            windZone.transform.position = new Vector3(baseX + 3f, baseY + 4f, 0f);

            var windCol = windZone.AddComponent<BoxCollider2D>();
            windCol.isTrigger = true;
            windCol.size = new Vector2(8f, 8f);

            var wind = windZone.AddComponent<WindZone2D>();
            var soWind = new SerializedObject(wind);
            soWind.FindProperty("windAcceleration").vector2Value = new Vector2(20f, 0f);
            soWind.FindProperty("terminalSpeed").floatValue = 12f;
            soWind.FindProperty("airborneMultiplier").floatValue = 1f;
            soWind.FindProperty("attachedMultiplier").floatValue = 0.3f;
            soWind.ApplyModifiedProperties();

            // Wind visual zone indicator (SpriteRenderer tint)
            var windVis = windZone.AddComponent<SpriteRenderer>();
            windVis.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            windVis.drawMode = SpriteDrawMode.Sliced;
            windVis.size = new Vector2(8f, 8f);
            windVis.color = new Color(0.4f, 0.7f, 1f, 0.08f);
            windVis.sortingOrder = -1;

            // --- Particle System (child of wind zone) ---
            var particleGo = new GameObject("WindParticles");
            particleGo.transform.parent = windZone.transform;
            particleGo.transform.localPosition = Vector3.zero;

            var ps = particleGo.AddComponent<ParticleSystem>();
            var psRenderer = particleGo.GetComponent<ParticleSystemRenderer>();

            // Visual-only settings (geometry/timing is auto-synced by WindZone2D.SyncParticles)
            var main = ps.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
            main.startColor = new Color(1f, 1f, 1f, 0.3f);
            main.maxParticles = 300;

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            var sizeCurve = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.2f, 1f),
                new Keyframe(0.8f, 1f),
                new Keyframe(1f, 0f));
            sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.4f;
            noise.frequency = 0.5f;
            noise.scrollSpeed = 0.3f;

            psRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            psRenderer.lengthScale = 3f;
            psRenderer.velocityScale = 0.1f;

            // Wire particle reference
            soWind = new SerializedObject(wind);
            soWind.FindProperty("windParticles").objectReferenceValue = ps;
            soWind.ApplyModifiedProperties();

            // --- Landing platform (destination after wind push) ---
            CreatePlatform("LandingPlatform", root.transform,
                new Vector3(baseX + 9f, baseY + 2f, 0f),
                new Vector3(3f, 0.5f, 1f));

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("Wind Zone Test setup complete! Launch slime from the left step into the wind zone.");
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
