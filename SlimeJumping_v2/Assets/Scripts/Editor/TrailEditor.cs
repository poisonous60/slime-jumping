#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using SlimeJumping.Rendering;

namespace SlimeJumping.EditorTools
{
    /// Tools > Trail menu: material migration, scene validation, save-folder access.
    public static class TrailEditorMenu
    {
        private const string MenuRoot = "Tools/Trail/";
        private const string StockShaderName    = "Quibli/Stylized Lit";
        private const string WorldShaderName    = "Quibli/Stylized Lit (Trail Receiver)";
        private const string LocalShaderName    = "Quibli/Stylized Lit (Trail Receiver Local)";

        [MenuItem(MenuRoot + "Migrate Selected Materials → World Variant")]
        private static void MigrateWorld() => MigrateSelected(StockShaderName, WorldShaderName);

        [MenuItem(MenuRoot + "Migrate Selected Materials → Local Variant")]
        private static void MigrateLocal() => MigrateSelected(StockShaderName, LocalShaderName);

        [MenuItem(MenuRoot + "Revert Selected Materials to Stock")]
        private static void MigrateRevert()
        {
            int done = 0, skipped = 0;
            var stock = Shader.Find(StockShaderName);
            if (stock == null) { Debug.LogError("[Trail] Stock Quibli shader not found."); return; }

            foreach (var obj in Selection.objects)
            {
                var mat = obj as Material;
                if (mat == null) { skipped++; continue; }
                if (mat.shader != null && (mat.shader.name == WorldShaderName || mat.shader.name == LocalShaderName))
                {
                    Undo.RecordObject(mat, "Trail: revert to stock");
                    mat.shader = stock;
                    EditorUtility.SetDirty(mat);
                    done++;
                }
                else skipped++;
            }
            Debug.Log($"[Trail] Reverted {done} material(s) to stock StylizedLit. {skipped} skipped.");
        }

        private static void MigrateSelected(string fromShaderName, string toShaderName)
        {
            var from = Shader.Find(fromShaderName);
            var to   = Shader.Find(toShaderName);
            if (to == null) { Debug.LogError($"[Trail] Target shader '{toShaderName}' not found. Is the project compiled?"); return; }

            int done = 0, skipped = 0, alreadyOnTrail = 0;
            foreach (var obj in Selection.objects)
            {
                var mat = obj as Material;
                if (mat == null) { skipped++; continue; }
                if (mat.shader == to) { alreadyOnTrail++; continue; }
                if (mat.shader != from)
                {
                    string shaderName = mat.shader != null ? mat.shader.name : "(null)";
                    Debug.LogWarning($"[Trail] Skipping '{mat.name}': shader is '{shaderName}', expected '{fromShaderName}'.", mat);
                    skipped++;
                    continue;
                }
                Undo.RecordObject(mat, "Trail: migrate material");
                mat.shader = to;
                EditorUtility.SetDirty(mat);
                done++;
            }
            Debug.Log($"[Trail] Migrated {done} material(s) to '{toShaderName}'. Already on trail variant: {alreadyOnTrail}. Skipped: {skipped}.");
        }

        [MenuItem(MenuRoot + "Validate Current Scene")]
        private static void ValidateScene()
        {
            int issues = 0;

            var managers = Object.FindObjectsByType<WorldTrailManager>(FindObjectsSortMode.None);
            if (managers.Length == 0)
            {
                Debug.LogWarning("[Trail Validate] No WorldTrailManager in scene — trail system inactive.");
                issues++;
            }
            else if (managers.Length > 1)
            {
                Debug.LogError($"[Trail Validate] {managers.Length} WorldTrailManager components in scene. Only one allowed.", managers[0]);
                issues++;
            }

            var localShader = Shader.Find(LocalShaderName);
            var worldShader = Shader.Find(WorldShaderName);
            var stockShader = Shader.Find(StockShaderName);

            var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            var seenIds = new Dictionary<string, LocalTrailReceiver>();

            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                var mat = r.sharedMaterial;
                if (mat == null || mat.shader == null) continue;

                bool isLocal = mat.shader == localShader;
                var receiver = r.GetComponent<LocalTrailReceiver>();
                if (receiver == null) receiver = r.GetComponentInParent<LocalTrailReceiver>();
                if (receiver == null) receiver = r.GetComponentInChildren<LocalTrailReceiver>();

                if (isLocal && receiver == null)
                {
                    Debug.LogWarning($"[Trail Validate] Renderer '{r.name}' uses local-variant shader but no LocalTrailReceiver in hierarchy — will show no trail.", r);
                    issues++;
                }
            }

            var receivers = Object.FindObjectsByType<LocalTrailReceiver>(FindObjectsSortMode.None);
            for (int i = 0; i < receivers.Length; i++)
            {
                var rc = receivers[i];
                var r = rc.GetComponent<Renderer>();
                if (r == null || r.sharedMaterial == null) continue;
                if (r.sharedMaterial.shader != localShader)
                {
                    Debug.LogWarning($"[Trail Validate] LocalTrailReceiver on '{rc.name}' but its renderer material '{r.sharedMaterial.name}' is not on the local variant shader.", rc);
                    issues++;
                }

                if (!string.IsNullOrEmpty(rc.OverrideId))
                {
                    if (seenIds.TryGetValue(rc.OverrideId, out var other))
                    {
                        Debug.LogError($"[Trail Validate] Duplicate overrideId '{rc.OverrideId}' on '{rc.name}' and '{other.name}'. Their saves will clobber each other.", rc);
                        issues++;
                    }
                    else seenIds[rc.OverrideId] = rc;
                }

                if (rc.ContactCollider == null)
                {
                    Debug.LogWarning($"[Trail Validate] LocalTrailReceiver on '{rc.name}' has no contactCollider — stamps cannot route to this platform.", rc);
                    issues++;
                }
            }

            if (stockShader != null && managers.Length > 0)
            {
                var mgr = managers[0];
                if (mgr.GroundLayers.value != 0)
                {
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        var r = renderers[i];
                        if (((1 << r.gameObject.layer) & mgr.GroundLayers.value) == 0) continue;
                        if (r.sharedMaterial != null && r.sharedMaterial.shader == stockShader)
                        {
                            Debug.LogWarning($"[Trail Validate] Renderer '{r.name}' on ground layer still uses stock StylizedLit.", r);
                            issues++;
                        }
                    }
                }
            }

            if (issues == 0) Debug.Log("[Trail Validate] Scene OK.");
            else Debug.Log($"[Trail Validate] {issues} issue(s) found — see messages above.");
        }

        [MenuItem(MenuRoot + "Open Save Folder")]
        private static void OpenSaveFolder()
        {
            string path = Path.Combine(Application.persistentDataPath, "trail");
            Directory.CreateDirectory(path);
            EditorUtility.RevealInFinder(path);
        }

        [MenuItem(MenuRoot + "Clear Saves for Active Scene")]
        private static void ClearSavesActiveScene()
        {
            string root = Path.Combine(Application.persistentDataPath, "trail");
            if (!Directory.Exists(root)) { Debug.Log("[Trail] No save folder yet."); return; }

            int sceneIdx = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
            string worldPrefix = "world_" + sceneIdx;
            string localPrefix = "local_" + sceneIdx + "_";

            if (!EditorUtility.DisplayDialog(
                "Clear Trail Saves?",
                $"Delete all trail save files for scene build index {sceneIdx}?\n\nFolder: {root}",
                "Delete", "Cancel")) return;

            int deleted = 0;
            foreach (var file in Directory.GetFiles(root))
            {
                string name = Path.GetFileName(file);
                if (name.StartsWith(worldPrefix) || name.StartsWith(localPrefix))
                {
                    File.Delete(file);
                    deleted++;
                }
            }
            Debug.Log($"[Trail] Deleted {deleted} save file(s) for scene {sceneIdx}.");
        }
    }

    // ----------------------------------------------------------------- inspectors

    [CustomEditor(typeof(WorldTrailManager))]
    public class WorldTrailManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var mgr = (WorldTrailManager)target;

            // Memory label — live.
            var res = mgr.PlannedResolution;
            long bytes = mgr.PlannedMemoryBytes;
            string mem = FormatBytes(bytes);
            EditorGUILayout.HelpBox($"RT: {res.x} × {res.y}  ·  {mem}  (R8)", MessageType.None);

            if (res.x >= WorldTrailManager.MaxResolutionPerSide || res.y >= WorldTrailManager.MaxResolutionPerSide)
            {
                EditorGUILayout.HelpBox("RT size clamped — reduce worldSize or pixelsPerUnit.", MessageType.Warning);
            }

            // Save-mismatch warning.
            if (Application.isPlaying == false) DrawSaveMismatchWarning(mgr);

            if (GUILayout.Button("Fit Bounds to Scene Ground"))
            {
                Undo.RecordObject(mgr, "Trail: fit bounds");
                FitBoundsToSceneGround(mgr);
                EditorUtility.SetDirty(mgr);
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                {
                    if (GUILayout.Button("Clear World Trail (Play)"))
                    {
                        if (mgr.Rt != null) Graphics.Blit(Texture2D.blackTexture, mgr.Rt);
                        mgr.MarkDirty();
                    }
                    if (GUILayout.Button("Clear All (World + Locals)"))
                    {
                        if (mgr.Rt != null) Graphics.Blit(Texture2D.blackTexture, mgr.Rt);
                        mgr.MarkDirty();
                        foreach (var rc in Object.FindObjectsByType<LocalTrailReceiver>(FindObjectsSortMode.None))
                            rc.ClearTrail();
                    }
                }
                if (GUILayout.Button("Delete Saved File"))
                {
                    LocalTrailReceiverEditor.DeleteSaveFile(mgr.SaveKey);
                }
            }

            EditorGUILayout.Space();
            DrawDefaultInspector();
        }

        private static void DrawSaveMismatchWarning(WorldTrailManager mgr)
        {
            string basePath = Path.Combine(Application.persistentDataPath, "trail", Sanitize(mgr.SaveKey));
            if (!File.Exists(basePath + ".meta")) return;

            try
            {
                var saved = JsonUtility.FromJson<TrailTargetMeta>(File.ReadAllText(basePath + ".meta"));
                var expected = mgr.BuildMeta();
                if (MetaMatches(saved, expected)) return;

                EditorGUILayout.HelpBox(
                    "An existing save for this scene will be ignored on next Play Mode due to bounds/resolution mismatch.\n" +
                    $"Saved:    {saved.resolutionX}x{saved.resolutionY} @ ({saved.originX},{saved.originY}) size ({saved.sizeX},{saved.sizeY})\n" +
                    $"Current:  {expected.resolutionX}x{expected.resolutionY} @ ({expected.originX},{expected.originY}) size ({expected.sizeX},{expected.sizeY})",
                    MessageType.Warning);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Clear Save"))
                    {
                        if (File.Exists(basePath + ".dat")) File.Delete(basePath + ".dat");
                        if (File.Exists(basePath + ".meta")) File.Delete(basePath + ".meta");
                    }
                    if (GUILayout.Button("Revert Bounds to Saved"))
                    {
                        var so = new SerializedObject(mgr);
                        so.FindProperty("worldMin").vector2Value = new Vector2(saved.originX, saved.originY);
                        so.FindProperty("worldSize").vector2Value = new Vector2(saved.sizeX, saved.sizeY);
                        so.ApplyModifiedProperties();
                    }
                }
            }
            catch { /* unreadable meta — ignore, next Play rewrites it */ }
        }

        private static void FitBoundsToSceneGround(WorldTrailManager mgr)
        {
            if (mgr.GroundLayers.value == 0)
            {
                Debug.LogWarning("[Trail] Set 'Ground Layers' on the manager before running Fit Bounds.");
                return;
            }

            Bounds? combined = null;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (((1 << r.gameObject.layer) & mgr.GroundLayers.value) == 0) continue;
                if (combined.HasValue) { var b = combined.Value; b.Encapsulate(r.bounds); combined = b; }
                else combined = r.bounds;
            }
            if (!combined.HasValue)
            {
                Debug.LogWarning("[Trail] No renderers found on configured ground layers.");
                return;
            }

            var b2 = combined.Value;
            const float pad = 1f;
            Vector2 newMin  = new Vector2(b2.min.x - pad, b2.min.y - pad);
            Vector2 newSize = new Vector2(b2.size.x + pad * 2f, b2.size.y + pad * 2f);

            var so = new SerializedObject(mgr);
            so.FindProperty("worldMin").vector2Value = newMin;
            so.FindProperty("worldSize").vector2Value = newSize;
            so.ApplyModifiedProperties();
        }

        private static string FormatBytes(long b)
        {
            if (b < 1024) return b + " B";
            if (b < 1024 * 1024) return (b / 1024f).ToString("0.0") + " KB";
            return (b / 1024f / 1024f).ToString("0.0") + " MB";
        }

        private static bool MetaMatches(TrailTargetMeta a, TrailTargetMeta b)
        {
            return a.version == b.version
                && a.resolutionX == b.resolutionX
                && a.resolutionY == b.resolutionY
                && Mathf.Approximately(a.originX, b.originX)
                && Mathf.Approximately(a.originY, b.originY)
                && Mathf.Approximately(a.sizeX, b.sizeX)
                && Mathf.Approximately(a.sizeY, b.sizeY)
                && a.format == b.format;
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unnamed";
            var buf = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.') buf.Append(c);
                else buf.Append('_');
            }
            if (buf.Length > 120) buf.Length = 120;
            return buf.ToString();
        }
    }

    [CustomEditor(typeof(LocalTrailReceiver))]
    public class LocalTrailReceiverEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var rc = (LocalTrailReceiver)target;

            var res = rc.PlannedResolution;
            long bytes = (long)res.x * res.y;
            string mem = bytes < 1024 ? bytes + " B" : (bytes / 1024f).ToString("0.0") + " KB";
            EditorGUILayout.HelpBox($"Local RT: {res.x} × {res.y}  ·  {mem}  (R8)", MessageType.None);

            // Material-variant check.
            var rend = rc.GetComponent<Renderer>();
            if (rend != null && rend.sharedMaterial != null && rend.sharedMaterial.shader != null)
            {
                if (rend.sharedMaterial.shader.name != "Quibli/Stylized Lit (Trail Receiver Local)")
                {
                    EditorGUILayout.HelpBox(
                        "This renderer's material is NOT on the local-variant shader.\n" +
                        "Trail will not appear on this platform. Migrate via Tools > Trail.",
                        MessageType.Warning);
                }
            }

            if (rc.ContactCollider == null)
            {
                EditorGUILayout.HelpBox(
                    "contactCollider is not assigned. Stamps cannot route to this platform.\n" +
                    "Drag the platform's physics collider into the field below, or the component will try to auto-resolve via GetComponentInParent.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                {
                    if (GUILayout.Button("Clear Trail (Play)"))
                    {
                        rc.ClearTrail();
                    }
                }
                if (GUILayout.Button("Delete Saved File"))
                {
                    DeleteSaveFile(rc.SaveKey);
                }
            }

            DrawDefaultInspector();
        }

        public static void DeleteSaveFile(string saveKey)
        {
            string basePath = System.IO.Path.Combine(Application.persistentDataPath, "trail", Sanitize(saveKey));
            int deleted = 0;
            foreach (var suffix in new[] { ".dat", ".meta" })
            {
                var p = basePath + suffix;
                if (System.IO.File.Exists(p)) { System.IO.File.Delete(p); deleted++; }
            }
            Debug.Log($"[Trail] Deleted {deleted} file(s) for '{saveKey}'. (Trail in memory is unaffected — next Play starts fresh.)");
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unnamed";
            var buf = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.') buf.Append(c);
                else buf.Append('_');
            }
            if (buf.Length > 120) buf.Length = 120;
            return buf.ToString();
        }
    }
}
#endif
