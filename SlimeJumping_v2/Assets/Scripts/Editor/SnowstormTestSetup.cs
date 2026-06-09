using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    public static class SnowstormTestSetup
    {
        const string SnowPrefabPath = "Assets/Prebs/VFX/Snow.prefab";
        const string VolumeProfilePath = "Assets/_Project/VolumeProfiles/SnowstormProfile.asset";
        const string WindForceFieldLayerName = "WindForceField";

        [MenuItem("SlimeJumping/Mechanism/Setup Snowstorm Test")]
        public static void SetupSnowstormTest()
        {
            int windFFLayer = LayerMask.NameToLayer(WindForceFieldLayerName);
            if (windFFLayer < 0)
            {
                Debug.LogError($"Layer '{WindForceFieldLayerName}' missing from TagManager. Add it before running this setup.");
                return;
            }

            var root = new GameObject("SnowstormTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Snowstorm Test");

            float baseX = 0f;
            float baseY = 0f;
            float zoneWidth = 40f;
            float zoneHeight = 24f;

            // --- Test platforms ---
            CreatePlatform("Floor", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f),
                new Vector3(zoneWidth, 1f, 1f));

            CreatePlatform("LaunchStep", root.transform,
                new Vector3(baseX - 14f, baseY + 1f, 0f),
                new Vector3(3f, 0.5f, 1f));

            CreatePlatform("MiddlePerch", root.transform,
                new Vector3(baseX, baseY + 6f, 0f),
                new Vector3(2.5f, 0.5f, 1f));

            CreatePlatform("RightLanding", root.transform,
                new Vector3(baseX + 14f, baseY + 3f, 0f),
                new Vector3(3f, 0.5f, 1f));

            // --- Snowstorm Zone ---
            var stormGo = new GameObject("Snowstorm");
            stormGo.transform.parent = root.transform;
            stormGo.transform.position = new Vector3(baseX, baseY + zoneHeight * 0.5f, 0f);

            // PolygonCollider2D so designers can reshape the storm zone to non-rectangular
            // areas. Seeded as a rectangle matching the old box so the test scene is
            // unchanged until edited.
            var col = stormGo.AddComponent<PolygonCollider2D>();
            col.isTrigger = true;
            col.points = RectPoints(zoneWidth, zoneHeight);

            var storm = stormGo.AddComponent<Snowstorm2D>();

            // --- ForceField child (drives Snow.prefab particles inside zone bounds) ---
            var ffGo = new GameObject("SnowForceField");
            ffGo.transform.parent = stormGo.transform;
            ffGo.transform.localPosition = Vector3.zero;
            ffGo.transform.localRotation = Quaternion.identity;
            ffGo.transform.localScale = new Vector3(zoneWidth, zoneHeight, 100f);
            ffGo.layer = windFFLayer;
            var ff = ffGo.AddComponent<ParticleSystemForceField>();
            ff.shape = ParticleSystemForceFieldShape.Box;
            ff.endRange = 0.5f;
            ff.multiplyDragByParticleVelocity = false;
            ff.multiplyDragByParticleSize = false;
            ff.drag = new ParticleSystem.MinMaxCurve(1.5f);

            // --- Volume child (storm post-process: vignette + cool tint) ---
            var volumeGo = new GameObject("StormVolume");
            volumeGo.transform.parent = stormGo.transform;
            volumeGo.transform.localPosition = Vector3.zero;
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 50f;
            volume.weight = 0f;
            volume.sharedProfile = LoadOrCreateStormProfile();

            // --- Ambient Snow.prefab instance (optional baseline snowfall). Snowstorm2D
            // boosts this PS's emission rate + size during wind phases. Camera-follows
            // and culls outside its own polygon area (Snow.prefab's SnowCameraEmitter).
            var snowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SnowPrefabPath);
            ParticleSystem ambientSnowPs = null;
            Material reusedSnowMaterial = null;
            if (snowPrefab != null)
            {
                var ambientInstance = (GameObject)PrefabUtility.InstantiatePrefab(snowPrefab, root.transform);
                ambientInstance.name = "AmbientSnow";
                ambientInstance.transform.position = new Vector3(baseX, baseY + zoneHeight * 0.5f, 0f);
                ambientSnowPs = ambientInstance.GetComponentInChildren<ParticleSystem>();
                if (ambientSnowPs != null)
                {
                    var r = ambientSnowPs.GetComponent<ParticleSystemRenderer>();
                    if (r != null)
                    {
                        r.sortingOrder = 10;
                        reusedSnowMaterial = r.sharedMaterial;
                    }
                }
            }
            else
            {
                Debug.LogWarning($"Snow prefab missing at {SnowPrefabPath} — ambient snowfall won't render until added manually.");
            }

            // --- Storm-owned PS pair (top-strip + side-strip). Snowstorm2D owns both
            // directly and drives spawn position/emission/velocity per frame. Top-strip
            // covers the upper diagonal half of the viewport via fall+drift; side-strip
            // fills the lower diagonal half by drifting in from the upwind edge.
            var topPsGo = new GameObject("StormParticles_Top");
            topPsGo.transform.parent = stormGo.transform;
            topPsGo.transform.localPosition = Vector3.zero;
            var topPs = topPsGo.AddComponent<ParticleSystem>();
            ConfigureStormPs(topPs, reusedSnowMaterial);

            var sidePsGo = new GameObject("StormParticles_Side");
            sidePsGo.transform.parent = stormGo.transform;
            sidePsGo.transform.localPosition = Vector3.zero;
            var sidePs = sidePsGo.AddComponent<ParticleSystem>();
            ConfigureStormPs(sidePs, reusedSnowMaterial);

            // --- Wire references on Snowstorm2D ---
            var so = new SerializedObject(storm);
            so.FindProperty("snowForceField").objectReferenceValue = ff;
            so.FindProperty("snowParticles").objectReferenceValue = ambientSnowPs;
            so.FindProperty("stormParticles").objectReferenceValue = topPs;
            so.FindProperty("sideStormParticles").objectReferenceValue = sidePs;
            so.FindProperty("postVolume").objectReferenceValue = volume;
            so.ApplyModifiedProperties();

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("Snowstorm test setup complete. Phase cycle: WindLeft → CalmAfterLeft → WindRight → CalmAfterRight. Default starts in CalmAfterRight so the player has a quiet beat to read the rhythm.");
        }

        // Storm-owned PS. Local sim space anchored to the camera (Snowstorm2D moves the
        // PS transform to camera every LateUpdate); particles are visually glued to the
        // viewport regardless of camera motion. Snowstorm2D drives spawn strip position,
        // emission rate, and horizontal velocity per frame — this configures only the
        // static traits. Same config for both top + side strips.
        static void ConfigureStormPs(ParticleSystem ps, Material reusedSnowMaterial)
        {
            if (ps == null) return;

            var main = ps.main;
            // LOCAL space: particles move with the camera-anchored PS transform; fast
            // slime jumps no longer leave snow behind. Snowstorm2D asserts this at the
            // cull site — flipping back to World will break the design.
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
            // startSpeed=0 — Snowstorm2D drives both X (wind drift) and Y (fall) via
            // velocityOverLifetime. Box shape's default emit direction would otherwise
            // push particles upward (its +Y axis), the opposite of what we want.
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.18f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 1f, 1f, 0.85f),
                new Color(0.8f, 0.88f, 1f, 0.85f));
            // Sized to absorb peak emission without silent throttling. Peak rate 1800
            // × max lifetime 5s = 9000 steady-state. 12000 leaves headroom.
            main.maxParticles = 12000;
            main.gravityModifier = 0.6f;

            // Prewarm OFF on both strips — Snowstorm2D gates emission by slime presence
            // weight (0 outside zone, lerps to 1 on entry). Prewarm would pre-populate
            // particles at scene start regardless of slime location, producing a burst
            // of falling snow in scenes where the slime hasn't entered the storm yet.
            // The slime-entry fade-in (~0.66s) plus particle fall time fills the
            // viewport quickly enough that prewarm isn't needed.
            main.prewarm = false;
            main.loop = true;

            // Initial emission baseline; Snowstorm2D's ApplyStormParticles overwrites
            // rateOverTime every LateUpdate frame, so this value is only active for
            // the single frame before the first LateUpdate fires.
            var emis = ps.emission;
            emis.rateOverTime = new ParticleSystem.MinMaxCurve(300f);

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.boxThickness = Vector3.zero;
            // Initial above-viewport approximation. Prewarm runs at PS enable BEFORE
            // any LateUpdate, so without a sane default here the prewarmed cloud would
            // spawn at the PS center (camera origin) and be visible at viewport center
            // for the first ~3-5s. ApplyStormParticles overwrites this every frame
            // with the camera-accurate value (halfH + topStripOffset + thickness/2).
            shape.position = new Vector3(0f, 7f, 0f);
            shape.rotation = Vector3.zero;

            // sizeOverLifetime: short fade-in so newly-spawned particles become visible
            // almost immediately (they spawn above viewport so the fade doesn't matter
            // visually but keeps cap-hit behavior smooth). Long visible plateau, brief
            // fade-out at end of life.
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.6f),
                new Keyframe(0.05f, 1f),
                new Keyframe(0.9f, 1f),
                new Keyframe(1f, 0f)));

            // External forces off — storm PS drives its own velocity in local space.
            var ext = ps.externalForces;
            ext.enabled = false;

            // Noise module breaks the regular grid distribution that comes from a
            // uniform Box spawn + uniform velocityOverLifetime — without this the
            // snow tends to read as visible parallel "ruled lines" rather than
            // organic snowfall. damping=true keeps the motion subtle.
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(1.2f);
            noise.frequency = 0.6f;
            noise.scrollSpeed = 0.4f;
            noise.damping = true;
            noise.octaveCount = 2;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.sortingOrder = 20;
                renderer.sharedMaterial = ResolveStormMaterial(reusedSnowMaterial);
            }
        }

        // Prefer the snowflake material from Snow.prefab (round particle texture). Fall
        // back to a fresh URP Particles/Unlit white if the prefab isn't present or its
        // material can't be read — warn so the designer notices instead of silently
        // getting an invisible/magenta PS.
        static Material ResolveStormMaterial(Material reused)
        {
            if (reused != null) return reused;

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                Debug.LogWarning("URP Particles/Unlit shader not found — storm particles will use Unity's default magenta material.");
                return null;
            }
            var mat = new Material(shader) { name = "SnowstormParticleFallback" };
            mat.SetColor("_BaseColor", Color.white);
            return mat;
        }

        static VolumeProfile LoadOrCreateStormProfile()
        {
            var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (existing != null) return existing;

            EnsureFolder("Assets/_Project");
            EnsureFolder("Assets/_Project/VolumeProfiles");

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "SnowstormProfile";

            // Stronger defaults than the first pass — the previous tint was too subtle
            // to read as "you are inside a blizzard". Vignette pushed up + a colder/
            // darker color filter + a saturation drop combine into a noticeably colder
            // mood at peak weight=1.
            var vignette = profile.Add<Vignette>(true);
            vignette.color.overrideState = true;
            vignette.color.value = new Color(0.4f, 0.55f, 0.75f);
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.7f;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.6f;

            var color = profile.Add<ColorAdjustments>(true);
            color.colorFilter.overrideState = true;
            color.colorFilter.value = new Color(0.7f, 0.78f, 0.95f);
            color.saturation.overrideState = true;
            color.saturation.value = -25f;
            color.postExposure.overrideState = true;
            color.postExposure.value = -0.3f;

            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.overrideState = true;
            wb.temperature.value = -25f;

            AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            AssetDatabase.SaveAssets();
            return profile;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            var leaf = System.IO.Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static Vector2[] RectPoints(float width, float height)
        {
            float hx = width * 0.5f;
            float hy = height * 0.5f;
            return new[]
            {
                new Vector2(-hx, -hy),
                new Vector2(hx, -hy),
                new Vector2(hx, hy),
                new Vector2(-hx, hy),
            };
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

            var litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader != null)
            {
                var mat = new Material(litShader) { color = new Color(0.5f, 0.5f, 0.5f) };
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
            else
            {
                Debug.LogWarning("URP Lit shader not found — test platforms will use Unity's default magenta material.");
            }
            return go;
        }
    }
}
