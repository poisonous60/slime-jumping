using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Platforms.Snow.EditorScripts
{
    /// <summary>One-shot helper that wires SnowField.settings / hostCollider and adds
    /// a SnowStamper to the slime in MainScene + SampleScene. Reports a punch list to
    /// the console; saves dirty scenes. Idempotent — safe to re-run.</summary>
    public static class SnowSceneWirer
    {
        const string DefaultSettingsPath = "Assets/Settings/Snow_Default.asset";

        [MenuItem("Tools/SlimeJumping/Snow/Wire MainScene + SampleScene")]
        public static void WireBoth()
        {
            var report = new StringBuilder();
            WireScene("Assets/Scenes/MainScene.unity", report);
            WireScene("Assets/Scenes/SampleScene.unity", report);
            Debug.Log("[Snow Wire]\n" + report);
        }

        static void WireScene(string scenePath, StringBuilder report)
        {
            if (!System.IO.File.Exists(scenePath)) { report.AppendLine($"[skip] {scenePath} (not found)"); return; }
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            report.AppendLine($"=== {scene.name} ===");

            bool dirty = false;

            var slime = Object.FindFirstObjectByType<SlimeController>();
            if (slime != null)
            {
                var stamper = slime.GetComponentInChildren<SnowStamper>(true);
                if (stamper == null)
                {
                    stamper = slime.gameObject.AddComponent<SnowStamper>();
                    var soSt = new SerializedObject(stamper);
                    soSt.FindProperty("slimeController").objectReferenceValue = slime;
                    soSt.ApplyModifiedPropertiesWithoutUndo();
                    report.AppendLine($"  + Added SnowStamper to slime '{slime.name}'.");
                    dirty = true;
                }
                else
                {
                    var soSt = new SerializedObject(stamper);
                    var sp = soSt.FindProperty("slimeController");
                    if (sp.objectReferenceValue == null)
                    {
                        sp.objectReferenceValue = slime;
                        soSt.ApplyModifiedPropertiesWithoutUndo();
                        report.AppendLine("  ~ SnowStamper existed but slimeController null; wired.");
                        dirty = true;
                    }
                    else
                    {
                        report.AppendLine($"  = SnowStamper already on '{stamper.gameObject.name}'.");
                    }
                }
            }
            else
            {
                report.AppendLine("  ! No SlimeController in scene; skipping SnowStamper.");
            }

            var defaultSettings = AssetDatabase.LoadAssetAtPath<SnowSettings>(DefaultSettingsPath);

            var fields = Object.FindObjectsByType<SnowField>(FindObjectsSortMode.None);
            foreach (var f in fields)
            {
                var so = new SerializedObject(f);
                bool changed = false;

                if (f.settings == null && defaultSettings != null)
                {
                    so.FindProperty("settings").objectReferenceValue = defaultSettings;
                    changed = true;
                }

                if (f.hostCollider == null)
                {
                    var hostT = (f.sourceMeshFilter != null ? f.sourceMeshFilter.transform : f.transform);
                    Collider2D coll = hostT != null ? hostT.GetComponent<Collider2D>() : null;
                    if (coll == null && hostT != null) coll = hostT.GetComponentInParent<Collider2D>();
                    if (coll == null) coll = f.GetComponent<Collider2D>();
                    if (coll != null)
                    {
                        so.FindProperty("hostCollider").objectReferenceValue = coll;
                        changed = true;
                    }
                }

                if (changed)
                {
                    so.ApplyModifiedPropertiesWithoutUndo();
                    f.RebuildNow();
                    string set = f.settings != null ? f.settings.name : "NULL";
                    string col = f.hostCollider != null ? f.hostCollider.name : "NULL";
                    report.AppendLine($"  ~ {f.name}: settings={set}, hostCollider={col}");
                    dirty = true;
                }
                else
                {
                    string set = f.settings != null ? f.settings.name : "NULL";
                    string col = f.hostCollider != null ? f.hostCollider.name : "NULL";
                    report.AppendLine($"  = {f.name}: settings={set}, hostCollider={col}");
                }
            }

            if (fields.Length == 0) report.AppendLine("  (no SnowField in scene)");

            if (dirty)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                report.AppendLine("  [saved]");
            }
            else report.AppendLine("  [no changes]");
        }
    }
}
