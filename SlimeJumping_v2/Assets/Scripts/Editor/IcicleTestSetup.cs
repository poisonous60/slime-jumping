using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    public static class IcicleTestSetup
    {
        const int GroundLayer = 7;
        // Slime can be on Default (0) or SlimeNode (6) depending on setup.
        static readonly int SlimeDetectMask = (1 << 0) | (1 << 6);
        // Environment only — including slime layers would shatter the icicle the slime rides.
        static readonly int BreakOnMask = (1 << GroundLayer);

        [MenuItem("SlimeJumping/Mechanism/Setup Icicle Test")]
        public static void Setup()
        {
            var existing = GameObject.Find("IcicleTest");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject("IcicleTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Icicle Test");

            float baseX = 4f;
            float baseY = 0f;

            // --- Support geometry ---
            CreateBoxPlatform("Floor", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f),
                new Vector3(24f, 1f, 1f));

            CreateBoxPlatform("LeftWall", root.transform,
                new Vector3(baseX - 11f, baseY + 4f, 0f),
                new Vector3(1f, 10f, 1f));

            CreateBoxPlatform("RightWall", root.transform,
                new Vector3(baseX + 11f, baseY + 4f, 0f),
                new Vector3(1f, 10f, 1f));

            CreateBoxPlatform("Ceiling", root.transform,
                new Vector3(baseX, baseY + 9f, 0f),
                new Vector3(22f, 0.5f, 1f));

            // --- Icicle variants ---
            CreateIcicle("Icicle_Default", root.transform,
                new Vector3(baseX - 6f, baseY + 8.25f, 0f),
                detectionOffset: new Vector2(0f, -3.5f),
                detectionSize: new Vector2(1.5f, 7f),
                shakeDuration: 0.8f,
                fallSpeed: 6f,
                respawnTime: 3f,
                regenDuration: 0.6f);

            CreateIcicle("Icicle_Fast", root.transform,
                new Vector3(baseX - 2f, baseY + 8.25f, 0f),
                detectionOffset: new Vector2(0f, -3.5f),
                detectionSize: new Vector2(1.5f, 7f),
                shakeDuration: 0.3f,
                fallSpeed: 12f,
                respawnTime: 1.5f,
                regenDuration: 0.3f);

            CreateIcicle("Icicle_Slow", root.transform,
                new Vector3(baseX + 2f, baseY + 8.25f, 0f),
                detectionOffset: new Vector2(0f, -4f),
                detectionSize: new Vector2(1.5f, 8f),
                shakeDuration: 1.6f,
                fallSpeed: 3f,
                respawnTime: 5f,
                regenDuration: 1.2f);

            CreateIcicle("Icicle_Permanent", root.transform,
                new Vector3(baseX + 6f, baseY + 8.25f, 0f),
                detectionOffset: new Vector2(0f, -3f),
                detectionSize: new Vector2(5f, 6f),
                shakeDuration: 0.8f,
                fallSpeed: 6f,
                respawnTime: -1f,
                regenDuration: 0.6f);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("Icicle Test setup complete.\n" +
                      "- Icicle_Default: standard cadence, respawns after 3 s (0.6 s regen).\n" +
                      "- Icicle_Fast: quick shake/drop, 1.5 s respawn, 0.3 s regen.\n" +
                      "- Icicle_Slow: long shake + slow drift, 5 s respawn, 1.2 s regen.\n" +
                      "- Icicle_Permanent: one-shot (respawnTime = -1), 5-unit wide detect.\n" +
                      "- AudioClips are empty — assign shake/falling/impact on the component to hear sound.\n" +
                      "- BreakOnLayers is pre-wired to Environment (layer 7); regenBlockingLayers includes slime layers so respawn waits when slime stands at the hang point.");
        }

        static GameObject CreateIcicle(string name, Transform parent, Vector3 pos,
            Vector2 detectionOffset, Vector2 detectionSize,
            float shakeDuration, float fallSpeed,
            float respawnTime, float regenDuration)
        {
            // Collider stays put while the Visual child shakes — the ride surface must stay stable.
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = pos;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            // Triangular silhouette in root-local space, CCW, apex at the bottom.
            var poly = go.AddComponent<PolygonCollider2D>();
            poly.points = new Vector2[]
            {
                new Vector2( 0.2f,  0.4f),
                new Vector2(-0.2f,  0.4f),
                new Vector2( 0f,   -1.2f),
            };

            // --- Visual child (procedural cone mesh, apex pointing down) ---
            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, worldPositionStays: false);
            visual.transform.localPosition = new Vector3(0f, -0.4f, 0f);
            visual.transform.localScale = new Vector3(0.4f, 1.6f, 0.4f);
            visual.layer = 0;

            var meshFilter = visual.AddComponent<MeshFilter>();
            var renderer = visual.AddComponent<MeshRenderer>();
            meshFilter.sharedMesh = GetConeMesh();
            var mat = EnsureIcicleMaterial();
            renderer.sharedMaterial = mat;

            // --- Break particles (child of Visual) ---
            var psGo = new GameObject("BreakParticles");
            psGo.transform.SetParent(visual.transform, worldPositionStays: false);
            psGo.transform.localPosition = Vector3.zero;
            psGo.transform.localScale = Vector3.one;
            var ps = psGo.AddComponent<ParticleSystem>();
            ConfigureBreakParticles(ps);

            // --- IcicleHazard component wiring ---
            var icicle = go.AddComponent<IcicleHazard>();
            var so = new SerializedObject(icicle);
            so.FindProperty("detectionOffset").vector2Value = detectionOffset;
            so.FindProperty("detectionSize").vector2Value = detectionSize;
            so.FindProperty("slimeLayer").intValue = SlimeDetectMask;
            so.FindProperty("shakeDuration").floatValue = shakeDuration;
            so.FindProperty("shakeAmplitude").floatValue = 0.08f;
            so.FindProperty("shakeFrequency").floatValue = 40f;
            so.FindProperty("fallSpeed").floatValue = fallSpeed;
            so.FindProperty("breakOnLayers").intValue = BreakOnMask;
            so.FindProperty("visualRoot").objectReferenceValue = visual.transform;
            so.FindProperty("meshFilter").objectReferenceValue = meshFilter;
            so.FindProperty("meshRenderer").objectReferenceValue = renderer;
            so.FindProperty("mesh").objectReferenceValue = meshFilter.sharedMesh;
            so.FindProperty("material").objectReferenceValue = mat;
            so.FindProperty("solidCollider").objectReferenceValue = poly;
            so.FindProperty("breakParticles").objectReferenceValue = ps;
            so.FindProperty("respawnTime").floatValue = respawnTime;
            so.FindProperty("regenDuration").floatValue = regenDuration;
            // Anything under the hang point blocks regen — else the icicle pops back on top of the slime.
            so.FindProperty("regenBlockingLayers").intValue = BreakOnMask | SlimeDetectMask;
            so.FindProperty("soundVolume").floatValue = 1f;
            so.ApplyModifiedProperties();

            return go;
        }

        static void ConfigureBreakParticles(ParticleSystem ps)
        {
            var main = ps.main;
            main.duration = 0.5f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = 0.5f;
            main.startSpeed = 5f;
            main.startSize = 0.1f;
            main.startColor = new Color(0.75f, 0.9f, 1f, 1f);
            main.gravityModifier = 1.5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 30) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 60f;
            shape.radius = 0.15f;
            shape.rotation = new Vector3(90f, 0f, 0f); // cone opens downward

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                var mat = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");
                if (mat != null) renderer.sharedMaterial = mat;
                renderer.sortingOrder = 1;
            }

            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        // --- Procedural cone mesh ---

        static Mesh cachedConeMesh;

        static Mesh GetConeMesh()
        {
            // Mesh is not an asset — scene serialization drops it, so the visual goes blank
            // after a project reload and the menu has to be re-run.
            if (cachedConeMesh != null) return cachedConeMesh;
            cachedConeMesh = BuildConeMesh(16);
            return cachedConeMesh;
        }

        static Mesh BuildConeMesh(int segments)
        {
            // Unit cone, apex down. Layout: [0] apex, [1..segments] base ring, [segments+1] base center.
            var mesh = new Mesh { name = "Icicle_TestCone" };

            int vertCount = segments + 2;
            var verts = new Vector3[vertCount];
            var norms = new Vector3[vertCount];

            verts[0] = new Vector3(0f, -0.5f, 0f);
            norms[0] = Vector3.down;

            for (int i = 0; i < segments; i++)
            {
                float a = (i / (float)segments) * Mathf.PI * 2f;
                float x = Mathf.Cos(a) * 0.5f;
                float z = Mathf.Sin(a) * 0.5f;
                verts[i + 1] = new Vector3(x, 0.5f, z);
                // Outward radial with a downward tilt matching the cone slope.
                norms[i + 1] = new Vector3(x, -0.25f, z).normalized;
            }

            verts[segments + 1] = new Vector3(0f, 0.5f, 0f);
            norms[segments + 1] = Vector3.up;

            var tris = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                // Winding chosen so the side normal points away from the cone axis.
                tris[i * 6 + 0] = 0;
                tris[i * 6 + 1] = i + 1;
                tris[i * 6 + 2] = next + 1;
                tris[i * 6 + 3] = segments + 1;
                tris[i * 6 + 4] = next + 1;
                tris[i * 6 + 5] = i + 1;
            }

            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        static Material EnsureIcicleMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader);
            mat.name = "Icicle_TestMat";
            Color c = new Color(0.7f, 0.88f, 1f, 1f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
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
