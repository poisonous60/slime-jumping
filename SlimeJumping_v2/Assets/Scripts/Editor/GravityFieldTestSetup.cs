using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    public static class GravityFieldTestSetup
    {
        [MenuItem("SlimeJumping/Mechanism/Setup GravityField Test")]
        public static void SetupGravityFieldTest()
        {
            var root = new GameObject("GravityFieldTest");
            Undo.RegisterCreatedObjectUndo(root, "Create GravityField Test");

            float baseX = 20f;
            float baseY = 0f;

            // --- Normal-gravity floor (launch area) ---
            CreatePlatform("Floor", root.transform,
                new Vector3(baseX - 6f, baseY - 0.5f, 0f),
                new Vector3(8f, 1f, 1f));

            // --- Test 1: Fixed mode — Inverted gravity ---
            CreatePlatform("InvertedCeiling", root.transform,
                new Vector3(baseX + 2f, baseY + 8.5f, 0f),
                new Vector3(6f, 1f, 1f));

            CreateGravityFieldFixed(
                "GravityField_Inverted", root.transform,
                new Vector3(baseX + 2f, baseY + 5f, 0f),
                new Vector2(8f, 8f),
                Vector2.up);

            // --- Test 2: Fixed mode — Sideways gravity ---
            CreatePlatform("SidewaysWall", root.transform,
                new Vector3(baseX + 14.5f, baseY + 4f, 0f),
                new Vector3(1f, 8f, 1f));

            CreateGravityFieldFixed(
                "GravityField_Sideways", root.transform,
                new Vector3(baseX + 11f, baseY + 4f, 0f),
                new Vector2(6f, 10f),
                Vector2.right);

            // --- Test 3: Fixed mode — 45 degree angle ---
            CreatePlatform("DiagonalPlatform", root.transform,
                new Vector3(baseX + 20f, baseY + 2f, 0f),
                new Vector3(6f, 1f, 1f));

            CreateGravityFieldFixed(
                "GravityField_Diagonal", root.transform,
                new Vector3(baseX + 20f, baseY + 5f, 0f),
                new Vector2(8f, 8f),
                new Vector2(1f, -1f).normalized);

            // --- Test 4: Radial mode — Circular planet ---
            float ringRadius = 3.5f;
            Vector3 ringCenter = new Vector3(baseX + 30f, baseY + 6f, 0f);
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI * 2f / 12f;
                Vector3 pos = ringCenter + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * ringRadius;
                float angleDeg = angle * Mathf.Rad2Deg;
                var platform = CreatePlatform($"RingPlatform_{i}", root.transform,
                    pos, new Vector3(2f, 0.5f, 1f));
                platform.transform.rotation = Quaternion.Euler(0f, 0f, angleDeg);
            }

            CreateGravityFieldRadial(
                "GravityField_Radial", root.transform,
                ringCenter,
                5.5f,
                Vector2.zero,
                false);

            // --- Transition bridge (walk from normal into inverted) ---
            CreatePlatform("TransitionBridge", root.transform,
                new Vector3(baseX - 1f, baseY - 0.5f, 0f),
                new Vector3(4f, 1f, 1f));

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("GravityField Test setup complete! 4 test zones: Inverted, Sideways, 45-degree, Radial ring.");
        }

        static GameObject CreateGravityFieldFixed(string name, Transform parent, Vector3 pos,
            Vector2 boxSize, Vector2 downDirection)
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
            so.FindProperty("gravityMode").enumValueIndex = 0; // Fixed
            so.FindProperty("localDownDirection").vector2Value = downDirection;
            so.ApplyModifiedProperties();

            AddFieldVisual(go, col, GravityField.GravityMode.Fixed, downDirection, Vector2.zero, false);

            return go;
        }

        static GameObject CreateGravityFieldRadial(string name, Transform parent, Vector3 pos,
            float radius, Vector2 localPivot, bool invert)
        {
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = pos;
            go.layer = LayerMask.NameToLayer("GravityField");

            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = radius;

            var gf = go.AddComponent<GravityField>();
            var so = new SerializedObject(gf);
            so.FindProperty("gravityMode").enumValueIndex = 1; // Radial
            so.FindProperty("localPivot").vector2Value = localPivot;
            so.FindProperty("invertDirection").boolValue = invert;
            so.ApplyModifiedProperties();

            AddFieldVisual(go, col, GravityField.GravityMode.Radial, Vector2.down, localPivot, invert);

            return go;
        }

        // --- Pogostuck-style visual ---
        // Translucent purple fill matching the collider shape + particle flow in gravity direction.
        // Kept as a child GameObject so visual transform never affects the collider.

        static readonly Color FieldFillColor   = new Color(0.55f, 0.25f, 0.95f, 0.22f);
        static readonly Color ParticleTint     = new Color(0.88f, 0.60f, 1.00f, 0.90f);

        static void AddFieldVisual(GameObject host, Collider2D col,
            GravityField.GravityMode mode, Vector2 fixedDown, Vector2 localPivot, bool invert)
        {
            var visual = new GameObject("Visual");
            visual.transform.SetParent(host.transform, false);

            var fillMesh = BuildFieldFillMesh(col);
            visual.AddComponent<MeshFilter>().sharedMesh = fillMesh;
            var mr = visual.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MakeFieldMaterial(FieldFillColor);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sortingOrder = -1;

            AddFlowParticles(visual, col, fillMesh, mode, fixedDown, localPivot, invert);
        }

        static Mesh BuildFieldFillMesh(Collider2D col)
        {
            var mesh = new Mesh { name = "GravityFieldFill" };

            if (col is CircleCollider2D c)
            {
                const int segs = 64;
                var verts = new Vector3[segs + 1];
                var uvs = new Vector2[segs + 1];
                verts[0] = c.offset;
                uvs[0] = new Vector2(0.5f, 0.5f);
                for (int i = 0; i < segs; i++)
                {
                    float a = i * Mathf.PI * 2f / segs;
                    float x = Mathf.Cos(a);
                    float y = Mathf.Sin(a);
                    verts[i + 1] = (Vector3)(c.offset + new Vector2(x, y) * c.radius);
                    uvs[i + 1] = new Vector2((x + 1f) * 0.5f, (y + 1f) * 0.5f);
                }
                var tris = new int[segs * 3];
                for (int i = 0; i < segs; i++)
                {
                    tris[i * 3 + 0] = 0;
                    tris[i * 3 + 1] = 1 + i;
                    tris[i * 3 + 2] = 1 + (i + 1) % segs;
                }
                mesh.vertices = verts;
                mesh.uv = uvs;
                mesh.triangles = tris;
            }
            else if (col is BoxCollider2D b)
            {
                Vector2 h = b.size * 0.5f;
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
            }
            else if (col is PolygonCollider2D p)
            {
                var generated = p.CreateMesh(false, false);
                if (generated != null) mesh = generated;
                mesh.name = "GravityFieldFill";
            }

            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }

        static Material MakeFieldMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = color;
            return mat;
        }

        static void AddFlowParticles(GameObject host, Collider2D col, Mesh fillMesh,
            GravityField.GravityMode mode, Vector2 fixedDown, Vector2 localPivot, bool invert)
        {
            var psGO = new GameObject("Flow");
            psGO.transform.SetParent(host.transform, false);

            var ps = psGO.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 5f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
            main.startColor = ParticleTint;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 400;
            main.gravityModifier = 0f;

            var emission = ps.emission;

            var shape = ps.shape;
            shape.alignToDirection = false;
            shape.randomDirectionAmount = 0f;

            const float FlowSpeed = 1.6f;

            if (mode == GravityField.GravityMode.Fixed)
            {
                // Emit across the whole area; velocityOverLifetime drives the flow.
                main.startSpeed = 0f;
                float extentAlongFlow = 1f; // default fallback
                if (col is BoxCollider2D b)
                {
                    shape.shapeType = ParticleSystemShapeType.Rectangle;
                    shape.scale = new Vector3(b.size.x, b.size.y, 0f);
                    shape.position = b.offset;
                    Vector2 dAbs = new Vector2(Mathf.Abs(fixedDown.x), Mathf.Abs(fixedDown.y)).normalized;
                    extentAlongFlow = b.size.x * dAbs.x + b.size.y * dAbs.y;
                }
                else if (col is CircleCollider2D c)
                {
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    shape.radius = c.radius;
                    shape.radiusThickness = 1f;
                    shape.position = c.offset;
                    extentAlongFlow = c.radius * 2f;
                }
                else if (col is PolygonCollider2D p)
                {
                    shape.shapeType = ParticleSystemShapeType.Mesh;
                    shape.mesh = fillMesh;
                    shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
                    var bb = p.bounds.size;
                    extentAlongFlow = Mathf.Max(bb.x, bb.y);
                }

                // Particles travel at FlowSpeed in the gravity direction.
                // Lifetime = half the zone extent → avg travel = quarter extent.
                // Most die of old age before reaching the boundary;
                // the TriggerModule below catches any stragglers.
                main.startLifetime = Mathf.Max(0.3f, extentAlongFlow * 0.5f / FlowSpeed);

                Vector2 dir = fixedDown.sqrMagnitude > 0.0001f ? fixedDown.normalized : Vector2.down;
                var vel = ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.Local;
                vel.x = dir.x * FlowSpeed;
                vel.y = dir.y * FlowSpeed;
            }
            else
            {
                // Radial: emit from circumference, stream toward (or away from) the pivot.
                float r = (col is CircleCollider2D cc) ? cc.radius : 3f;
                Vector2 center = (col is CircleCollider2D cc2) ? cc2.offset : Vector2.zero;
                center += localPivot; // emit relative to the pivot so flow stays centered

                if (invert)
                {
                    // Outward: emit near pivot, push out. Lifetime = radius / speed.
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    shape.radius = Mathf.Max(0.15f, r * 0.1f);
                    shape.radiusThickness = 1f;
                    shape.position = center;
                    main.startSpeed = FlowSpeed;
                    main.startLifetime = Mathf.Max(0.8f, r / FlowSpeed);
                }
                else
                {
                    // Inward: emit on ring, negative speed pulls toward pivot.
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    shape.radius = r * 0.95f;
                    shape.radiusThickness = 0f; // edge only
                    shape.position = center;
                    main.startSpeed = -FlowSpeed;
                    main.startLifetime = Mathf.Max(0.8f, r / FlowSpeed);
                }
            }

            // Rate scales with area and average lifetime so on-screen density stays comparable.
            float avgLifetime = main.startLifetime.constant;
            emission.rateOverTime = Mathf.Clamp(EstimateEmissionRate(col) / Mathf.Max(0.5f, avgLifetime), 8f, 60f);

            // Hard bound: kill any particle that leaves the field's collider.
            var trigger = ps.trigger;
            trigger.enabled = true;
            trigger.SetCollider(0, col);
            trigger.inside = ParticleSystemOverlapAction.Ignore;
            trigger.outside = ParticleSystemOverlapAction.Kill;
            trigger.enter = ParticleSystemOverlapAction.Ignore;
            trigger.exit = ParticleSystemOverlapAction.Ignore;

            // Alpha fade at birth/death.
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

            // Slight size swell.
            var sizeMod = ps.sizeOverLifetime;
            sizeMod.enabled = true;
            var sizeCurve = new AnimationCurve(
                new Keyframe(0f, 0.6f),
                new Keyframe(0.5f, 1f),
                new Keyframe(1f, 0.4f));
            sizeMod.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            // Renderer: additive-ish soft dots, behind gameplay but above the field fill.
            var psr = ps.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Billboard;
            psr.material = MakeParticleMaterial();
            psr.sortingOrder = 0;
            psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            psr.receiveShadows = false;
        }

        static float EstimateEmissionRate(Collider2D col)
        {
            float area;
            if (col is CircleCollider2D c) area = Mathf.PI * c.radius * c.radius;
            else if (col is BoxCollider2D b) area = Mathf.Abs(b.size.x * b.size.y);
            else if (col is PolygonCollider2D p) { var bb = p.bounds.size; area = bb.x * bb.y; }
            else area = 10f;
            return Mathf.Clamp(area * 1.5f, 10f, 120f);
        }

        static Material MakeParticleMaterial()
        {
            // Sprites/Default gives premultiplied-alpha billboards that blend cleanly over the fill.
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = Color.white;
            return mat;
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
