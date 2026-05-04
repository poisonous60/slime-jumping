using System.IO;
using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;
using SlimeJumping.Controller;

namespace SlimeJumping.Editor
{
    /// <summary>
    /// Drops a single Beam-mode emitter that the slime can destroy by jumping into
    /// it. Wires SpaceWarpEffect (BulbShatter material) + GraniteBlastEmitterAttackable
    /// + trigger collider so a fresh scene gets a fully-functional attackable emitter
    /// with one menu click.
    ///
    /// Re-runnable: deletes any prior "GraniteBlast_Attackable_Test" root before building.
    /// </summary>
    public static class GraniteBlastAttackableTestSetup
    {
        const string ShaderName = "SlimeJumping/SpaceWarp_BulbShatter";
        const string MatFolder  = "Assets/Art/Shader";
        const string MatName    = "Mat_SWPad_v2_BulbNeck_Voronoi";
        const float QuadHeight  = 2.0f;
        const int   QueueForeground = 3500;
        const float ZTestAlways     = 8f;

        [MenuItem("SlimeJumping/Mechanism/Setup Granite Blast Attackable Test")]
        public static void Setup()
        {
            var slime = Object.FindFirstObjectByType<SlimeController>(FindObjectsInactive.Exclude);
            if (slime == null)
            {
                EditorUtility.DisplayDialog("Granite Blast Attackable Test",
                    "No SlimeController found in the active scene. Open SampleScene or MainScene first, then re-run this menu.",
                    "OK");
                return;
            }

            var existing = GameObject.Find("GraniteBlast_Attackable_Test");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            Vector3 slimePos = slime.transform.position;
            Vector3 anchor = new Vector3(slimePos.x + 6f, slimePos.y + 3f, slimePos.z);

            var root = new GameObject("GraniteBlast_Attackable_Test");
            root.transform.position = anchor;
            Undo.RegisterCreatedObjectUndo(root, "Create Granite Blast Attackable Test");

            // Single beam-mode emitter so the "in-flight beam halts on contact" behavior
            // is visible. Long firing window so the slime has time to jump in mid-fire.
            var emitterGO = new GameObject("Emitter_Beam_Attackable");
            emitterGO.transform.parent = root.transform;
            emitterGO.transform.position = anchor;

            // Visual head (just so the emitter is visible — no gameplay role).
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            Object.DestroyImmediate(visual.GetComponent<BoxCollider>());
            visual.transform.parent = emitterGO.transform;
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localScale = new Vector3(1.2f, 1.2f, 1.2f);
            var visualMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            visualMat.color = new Color(0.15f, 0.5f, 0.75f);
            visualMat.EnableKeyword("_EMISSION");
            visualMat.SetColor("_EmissionColor", new Color(0.15f, 0.5f, 0.75f) * 1.4f);
            visual.GetComponent<MeshRenderer>().sharedMaterial = visualMat;

            // Muzzle aimed back at the slime.
            var muzzle = new GameObject("Muzzle");
            muzzle.transform.parent = emitterGO.transform;
            muzzle.transform.localPosition = new Vector3(-0.8f, 0f, 0f);
            Vector2 toSlime = (Vector2)(slime.transform.position - muzzle.transform.position);
            float aimZ = Mathf.Atan2(toSlime.y, toSlime.x) * Mathf.Rad2Deg;
            muzzle.transform.localRotation = Quaternion.Euler(0f, 0f, aimZ);

            var emitter = emitterGO.AddComponent<GraniteBlastEmitter>();
            {
                var so = new SerializedObject(emitter);
                so.FindProperty("mode").enumValueIndex = (int)GraniteBlastEmitter.FireMode.Beam;
                so.FindProperty("muzzle").objectReferenceValue = muzzle.transform;
                so.FindProperty("aimAtTarget").boolValue = true;
                so.FindProperty("detectionRange").floatValue = 30f;
                so.FindProperty("detectionDelay").floatValue = 0.4f;
                so.FindProperty("preFireDuration").floatValue = 0.8f;
                so.FindProperty("fireStartDuration").floatValue = 0.05f;
                so.FindProperty("firingDuration").floatValue = 2.5f;
                so.FindProperty("postFireDuration").floatValue = 0.2f;
                so.FindProperty("cooldownDuration").floatValue = 1.5f;
                so.FindProperty("sustainedHit").boolValue = true;
                so.FindProperty("beamCoreWidth").floatValue = 0.7f;
                so.FindProperty("knockbackImpulse").floatValue = 12f;
                so.FindProperty("knockbackAcceleration").floatValue = 50f;
                so.ApplyModifiedProperties();
            }

            // Warp quad — child of the emitter, anchored at the emitter origin so the
            // BulbShatter membrane spans emitter→slime when TriggerStretch fires.
            var warpGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
            warpGO.name = "SpaceWarp";
            Object.DestroyImmediate(warpGO.GetComponent<MeshCollider>());
            warpGO.transform.parent = emitterGO.transform;
            warpGO.transform.localPosition = new Vector3(0f, 0f, -0.1f);
            warpGO.transform.localScale = new Vector3(QuadHeight, QuadHeight, 1f);
            var warpRenderer = warpGO.GetComponent<MeshRenderer>();
            warpRenderer.sharedMaterial = LoadOrCreateBulbShatterMaterial();

            var warp = warpGO.AddComponent<SpaceWarpEffect>();
            {
                var so = new SerializedObject(warp);
                so.FindProperty("targetRenderer").objectReferenceValue = warpRenderer;
                // Anchor on the emitter root so the membrane base sits on the emitter.
                so.FindProperty("anchor").objectReferenceValue = emitterGO.transform;
                so.FindProperty("baseWidth").floatValue   = QuadHeight;
                so.FindProperty("tipWidth").floatValue    = QuadHeight;
                so.FindProperty("flareScale").floatValue  = 1f;
                so.FindProperty("tipLead").floatValue     = 0.7f;
                so.FindProperty("maxStretchLength").floatValue   = 9f;
                so.FindProperty("maxStretchDuration").floatValue = 0.55f;
                so.FindProperty("popDuration").floatValue        = 0.35f;
                so.FindProperty("hideWhenIdle").boolValue        = true;
                so.ApplyModifiedProperties();
            }

            // Trigger volume + Attackable component on the emitter root.
            var trigger = emitterGO.AddComponent<CircleCollider2D>();
            trigger.isTrigger = true;
            trigger.radius = 0.85f;

            var attackable = emitterGO.AddComponent<GraniteBlastEmitterAttackable>();
            {
                var so = new SerializedObject(attackable);
                so.FindProperty("emitter").objectReferenceValue = emitter;
                so.FindProperty("warpEffect").objectReferenceValue = warp;
                so.FindProperty("visualToShrink").objectReferenceValue = visual.transform;
                so.FindProperty("targetToDeactivate").objectReferenceValue = emitterGO;
                so.FindProperty("worldFreezeDuration").floatValue = 0.55f;
                so.FindProperty("shrinkDuration").floatValue = 0.45f;
                so.ApplyModifiedProperties();
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = emitterGO;

            Debug.Log("[GraniteBlastAttackableTest] Built.\n" +
                      "- Beam-mode emitter placed near the slime.\n" +
                      "- Jump into the emitter while it's firing — the BulbShatter warp will stretch + shatter, " +
                      "the in-flight beam will be destroyed, and the emitter will shrink + deactivate.\n" +
                      "- Other GraniteBlastBeam/Tendril/JumpBall objects in the scene will pause for the freeze window.");
        }

        static Material LoadOrCreateBulbShatterMaterial()
        {
            string path = $"{MatFolder}/{MatName}.mat";
            EnsureFolder(MatFolder);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[GraniteBlastAttackableTest] Shader '{ShaderName}' not found. " +
                               "Expected the BulbShatter shader from the v2 SpaceWarp set.");
                return null;
            }
            mat = new Material(shader);
            mat.SetColor("_BaseColor",         new Color(0.7f, 0.85f, 1.0f, 1.0f));
            mat.SetFloat("_TintStrength",      0.04f);
            mat.SetFloat("_ShapeMode",         3); // Horn
            mat.SetFloat("_CrackMode",         0); // Voronoi
            mat.SetFloat("_NeckWidth",         0.45f);
            mat.SetFloat("_BulbCenter",        0.78f);
            mat.SetFloat("_BulbRadius",        0.65f);
            mat.SetFloat("_BulbAspect",        1.0f);
            mat.SetFloat("_PersistStrength",   0.06f);
            mat.SetFloat("_PersistScale",      4.0f);
            mat.SetVector("_PersistScroll",    new Vector4(0.06f, 0.10f, 0f, 0f));
            mat.SetFloat("_DomainWarp",        1.6f);
            mat.SetFloat("_SwirlAmount",       1.0f);
            mat.SetFloat("_SwirlAnim",         0.4f);
            mat.SetFloat("_PulseStrength",     0.10f);
            mat.SetFloat("_PulseRippleWidth",  0.2f);
            mat.SetFloat("_PulseMaxRadius",    1.0f);
            mat.SetFloat("_ShardScale",        14f);
            mat.SetFloat("_ShardThrow",        0.22f);
            mat.SetFloat("_ShardStagger",      0.40f);
            mat.SetFloat("_CrackEdgeWidth",    0.020f);
            mat.SetFloat("_CrackBoost",        3.0f);
            mat.SetFloat("_PopFlashStrength",  1.6f);
            mat.SetFloat("_WedgeCount",        14f);
            mat.SetFloat("_ZTest",             ZTestAlways);
            mat.renderQueue                  = QueueForeground;

            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            var leaf   = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
