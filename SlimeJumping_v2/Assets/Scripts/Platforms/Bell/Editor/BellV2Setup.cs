using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using SlimeJumping.Platforms;
using SlimeJumping.Platforms.Bell;

namespace SlimeJumping.Editor
{
    // Places SwingingBellV2: either a fresh clone of the scene's bell at the scene-view pivot, or an
    // in-place upgrade of a bell already placed. Scene originals are clone sources only.
    // The old SwingingBell is never removed — BellSoundWave and BellRevealCamera hold typed
    // references to it, so V2 keeps it around switched off and drives its ring surface instead.
    public static class BellV2Setup
    {
        const string ConfigAssetPath = "Assets/Resources/BellV2Config.asset";
        const string V1ConfigAssetPath = "Assets/Resources/BellConfig.asset";
        const string BellRootName = "bell_Collider2D";
        const string BellVisualName = "bell";
        const string ClapperName = "bell_clapper";

        [MenuItem("SlimeJumping/Mechanism/Setup Swinging Bell V2 Test")]
        public static void SpawnTestSet()
        {
            var source = GameObject.Find(BellRootName);
            if (source == null || source.GetComponent<Rigidbody2D>() == null)
            {
                EditorUtility.DisplayDialog("Swinging Bell V2",
                    $"No '{BellRootName}' with a Rigidbody2D in the open scene to clone.", "OK");
                return;
            }

            var cfg = EnsureConfig();

            var root = new GameObject("SwingingBellV2_TestSet");
            Undo.RegisterCreatedObjectUndo(root, "Spawn Swinging Bell V2 Test Set");
            root.transform.position = SpawnPosition();

            // Cloned at the pivot's XY but the original gameplay Z, so it sits in the play plane.
            var copy = Object.Instantiate(source, root.transform);
            copy.name = BellRootName + " (V2 TestSet)";
            copy.transform.position = new Vector3(root.transform.position.x, root.transform.position.y,
                                                  source.transform.position.z);

            var bell = Install(copy, cfg);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(copy.scene);
            Selection.activeGameObject = root;
            ReportThrow(cfg, bell);
        }

        [MenuItem("SlimeJumping/Mechanism/Convert Selected Bell to Swinging Bell V2")]
        public static void UpgradeSelected()
        {
            var go = Selection.activeGameObject;
            if (go == null || go.GetComponent<Rigidbody2D>() == null)
            {
                EditorUtility.DisplayDialog("Swinging Bell V2",
                    "Select the bell's collider object (the one with the Rigidbody2D) first.", "OK");
                return;
            }

            var cfg = EnsureConfig();
            var bell = Install(go, cfg);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;
            ReportThrow(cfg, bell);
        }

        [MenuItem("SlimeJumping/Mechanism/Convert Selected Bell to Swinging Bell V2", validate = true)]
        static bool UpgradeSelectedValidate() =>
            Selection.activeGameObject != null &&
            Selection.activeGameObject.GetComponent<Rigidbody2D>() != null;

        // -------- wiring --------

        // Idempotent: re-running only re-resolves the references and the ordering.
        static SwingingBellV2 Install(GameObject go, BellV2Config cfg)
        {
            var bell = go.GetComponent<SwingingBellV2>();
            if (bell == null) bell = Undo.AddComponent<SwingingBellV2>(go);

            var visual = go.transform.Find(BellVisualName);
            if (visual == null && go.transform.childCount > 0) visual = go.transform.GetChild(0);
            Transform clapper = visual == null ? null : visual.Find(ClapperName);

            var so = new SerializedObject(bell);
            so.FindProperty("bellVisual").objectReferenceValue = visual;
            so.FindProperty("clapperVisual").objectReferenceValue = clapper;
            so.FindProperty("revealRelay").objectReferenceValue = go.GetComponent<SwingingBell>();
            so.ApplyModifiedProperties();

            HoistAboveOtherMotors(go, bell);

            if (clapper == null)
                Debug.LogWarning($"[SwingingBellV2] '{go.name}': no '{ClapperName}' under the bell visual — " +
                                 "the clapper will not swing. Assign Clapper Visual by hand.", bell);
            if (cfg.strikeSound == null)
                Debug.LogWarning($"[SwingingBellV2] {ConfigAssetPath} has no strike sound — the bell will be " +
                                 "silent. Assign a SoundDef from Assets/Audio/Sounds/.", cfg);

            EditorUtility.SetDirty(bell);
            return bell;
        }

        // Riders bind to the FIRST PlatformMotor on the object, and a switched-off one still counts.
        static void HoistAboveOtherMotors(GameObject go, SwingingBellV2 bell)
        {
            // Bounded by every component, not just the motors — MoveComponentUp steps past all of them.
            int steps = go.GetComponents<Component>().Length;
            for (int i = 0; i < steps && go.GetComponents<PlatformMotor>()[0] != bell; i++)
                ComponentUtility.MoveComponentUp(bell);

            if (go.GetComponents<PlatformMotor>()[0] == bell) return;
            Debug.LogError($"[SwingingBellV2] '{go.name}': could not move the component above " +
                           "the other PlatformMotor — drag it up in the Inspector by hand.", bell);
        }

        // -------- config --------

        // Seeded from the physics bell's tuned asset: script defaults are not what the scene uses.
        static BellV2Config EnsureConfig()
        {
            var existing = AssetDatabase.LoadAssetAtPath<BellV2Config>(ConfigAssetPath);
            if (existing != null) return existing;

            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");

            var cfg = ScriptableObject.CreateInstance<BellV2Config>();
            var v1 = AssetDatabase.LoadAssetAtPath<BellConfig>(V1ConfigAssetPath);
            if (v1 != null)
            {
                cfg.pivotLocalY = v1.pivotLocalY;
                cfg.bellComLocalY = v1.bellComLocalY;
                cfg.clapperAxisLocalY = v1.clapperAxisLocalY;
                cfg.clapperComLocalY = v1.clapperComLocalY;
                cfg.bellMass = v1.bellMass;
                cfg.bellSwingPeriod = v1.bellSwingPeriod;
                cfg.bellDamping = v1.bellDamping;
                cfg.maxBellAngle = v1.maxBellAngle;
                cfg.swingDegrees = v1.hardHitSwingDegrees;
                cfg.clapperMass = v1.clapperMass;
                cfg.clapperInertiaSpread = v1.clapperInertiaSpread;
                cfg.clapperDamping = v1.clapperDamping;
                cfg.strikeAngle = v1.strikeAngle;
                cfg.strikeRestitution = v1.strikeRestitution;
                cfg.clapperSeparationAngle = v1.clapperSeparationAngle;
                cfg.substeps = v1.substeps;
            }

            AssetDatabase.CreateAsset(cfg, ConfigAssetPath);
            AssetDatabase.SaveAssets();
            return cfg;
        }

        // -------- report --------

        // Only the author knows the gap they want, so the arithmetic is printed rather than applied.
        static void ReportThrow(BellV2Config cfg, SwingingBellV2 bell)
        {
            float g = SlimeFallGravity();
            float e = cfg.bounceRestitution;

            Debug.Log($"[SwingingBellV2] Installed on '{bell.name}'. The slime bounces off elastically and " +
                      $"keeps {e * 100f:F0}% of the speed it brought, so how far it goes is how hard it came " +
                      $"in — there is no fixed arc to lay the level out against. Arrive at v and you leave " +
                      $"the flank at {e:F2}*v, floored at {cfg.minSeparationSpeed:F1} u/s so a drift-in still " +
                      $"comes off. A flat bounce at v covers v*sqrt(2*h/{g:F0}) units before dropping h " +
                      $"(e.g. leaving at 6 u/s and falling 3u = {6f * Mathf.Sqrt(6f / g):F2}u across).\n" +
                      $"Below {cfg.minRingSpeed:F1} u/s the bell moves but stays silent; the swing ramps " +
                      $"from {cfg.softSwingDegrees:F0}deg to {cfg.swingDegrees:F0}deg over " +
                      $"{cfg.minRingSpeed:F1}-{cfg.fullSwingSpeed:F1} u/s and never goes past that. " +
                      $"Tune in {ConfigAssetPath}.", bell);
        }

        // The slime falls on its own gravity, not Physics2D's, so the arithmetic has to use that one.
        static float SlimeFallGravity()
        {
            var slime = GameObject.Find("Slime");
            var controller = slime == null ? null : slime.GetComponent<Controller.SlimeController>();
            if (controller != null)
            {
                var field = typeof(Controller.SlimeController)
                    .GetField("settings", System.Reflection.BindingFlags.NonPublic |
                                          System.Reflection.BindingFlags.Instance);
                var settings = field == null ? null : field.GetValue(controller);
                if (settings != null)
                {
                    var g = settings.GetType().GetField("gravity");
                    var scale = settings.GetType().GetField("fallingGravityScale");
                    if (g != null && scale != null)
                        return Mathf.Abs((float)g.GetValue(settings)) * (float)scale.GetValue(settings);
                }
            }
            return Mathf.Abs(Physics2D.gravity.y);
        }

        static Vector3 SpawnPosition()
        {
            var view = SceneView.lastActiveSceneView;
            return view != null ? view.pivot : Vector3.zero;
        }
    }
}
