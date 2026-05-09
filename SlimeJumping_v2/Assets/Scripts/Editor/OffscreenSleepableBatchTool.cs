using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SlimeJumping.Optimization;

namespace SlimeJumping.EditorTools.Optimization
{
    /// <summary>
    /// Bulk-attaches <see cref="OffscreenSleepable"/> markers to every GameObject in the
    /// project (prefabs + scenes) that hosts one of the candidate component types.
    ///
    /// Operates in two modes:
    ///   - Dry Run: Reports which prefabs/scenes WOULD be modified, without writing.
    ///     Use first to confirm the impact size before committing to the real run.
    ///   - Attach All: Saves any unsaved scenes (prompting the user), then iterates prefabs
    ///     and scenes, adding markers, saving, and finally restoring the originally-active
    ///     scene. Idempotent — re-running adds 0.
    ///
    /// Recommendations:
    ///   - Run after a clean git commit. The diff includes prefab + scene meta files for
    ///     every modified asset, which is large and not trivial to revert mid-stream.
    ///   - Inspect the Dry Run output first.
    /// </summary>
    public static class OffscreenSleepableBatchTool
    {
        const string kAttachMenu = "Tools/Optimization/Attach OffscreenSleepable to all candidates";
        const string kDryRunMenu = "Tools/Optimization/Dry Run — Report candidates without modifying";

        // Skip third-party / plugin paths to avoid LoadAssetAtPath triggering the OnValidate
        // hook on imported assets — some have null-defererencing OnValidate (e.g. DBK
        // Building Pack's DBK_ColorThemeSelect). Also speeds up the scan: the 13 candidate
        // types are all in SlimeJumping.* namespaces and never appear in third-party prefabs.
        static readonly string[] SkipPathPrefixes = new string[]
        {
            "Assets/ThirdParty/",
            "Assets/Plugins/",
        };

        static bool ShouldSkipAssetPath(string path)
        {
            for (int i = 0; i < SkipPathPrefixes.Length; i++)
            {
                if (path.StartsWith(SkipPathPrefixes[i], System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        [MenuItem(kAttachMenu)]
        static void AttachAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            int wouldAddCount = ScanCandidateCount();
            if (wouldAddCount == 0)
            {
                EditorUtility.DisplayDialog(
                    "OffscreenSleepable Batch",
                    "이미 모든 후보 GameObject에 OffscreenSleepable이 부착되어 있습니다.\n변경 없음.",
                    "OK");
                return;
            }

            bool proceed = EditorUtility.DisplayDialog(
                "OffscreenSleepable 일괄 부착",
                $"이 작업은 모든 prefab/scene을 순회하며 13종 후보를 가진 GameObject에\n" +
                $"OffscreenSleepable 마커를 추가합니다.\n\n" +
                $"예상 추가 컴포넌트 수 (prefab 기준): {wouldAddCount}\n" +
                $"(scene 자산은 실행 중에 추가 스캔 — 실제 합계는 더 클 수 있음)\n\n" +
                $"prefab/scene meta 대량 변경 — git 커밋 후 실행 권장.\n\n" +
                $"계속하시겠습니까?",
                "진행", "취소");
            if (!proceed) return;

            string originalScenePath = SceneManager.GetActiveScene().path;
            int prefabsModified = 0;
            int scenesModified = 0;
            int componentsAdded = 0;

            try
            {
                // -- 1) Prefab assets --
                AssetDatabase.StartAssetEditing();
                try
                {
                    var prefabGuids = AssetDatabase.FindAssets("t:Prefab");
                    for (int i = 0; i < prefabGuids.Length; i++)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                        if (ShouldSkipAssetPath(path)) continue;
                        if (EditorUtility.DisplayCancelableProgressBar(
                            "Attach OffscreenSleepable — Prefabs",
                            path, (float)i / prefabGuids.Length))
                        {
                            return;
                        }

                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (prefab == null) continue;

                        int addedHere = AttachToHierarchy(prefab);
                        if (addedHere > 0)
                        {
                            EditorUtility.SetDirty(prefab);
                            PrefabUtility.SavePrefabAsset(prefab);
                            prefabsModified++;
                            componentsAdded += addedHere;
                        }
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }

                // -- 2) Scene assets --
                var sceneGuids = AssetDatabase.FindAssets("t:Scene");
                for (int i = 0; i < sceneGuids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);
                    // Skip Library/, Packages/ (FindAssets only returns Assets/ but defensive)
                    if (!path.StartsWith("Assets/")) continue;
                    if (ShouldSkipAssetPath(path)) continue;

                    if (EditorUtility.DisplayCancelableProgressBar(
                        "Attach OffscreenSleepable — Scenes",
                        path, (float)i / sceneGuids.Length))
                    {
                        return;
                    }

                    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    int addedHere = 0;
                    foreach (var root in scene.GetRootGameObjects())
                        addedHere += AttachToHierarchy(root);

                    if (addedHere > 0)
                    {
                        EditorSceneManager.SaveScene(scene);
                        scenesModified++;
                        componentsAdded += addedHere;
                    }
                }

            }
            finally
            {
                EditorUtility.ClearProgressBar();
                // Restore original active scene regardless of cancel/exception path —
                // cancel during the scene loop would otherwise leave the editor on whatever
                // scene the tool last opened.
                if (!string.IsNullOrEmpty(originalScenePath) &&
                    SceneManager.GetActiveScene().path != originalScenePath)
                {
                    EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);
                }
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[OffscreenSleepableBatchTool] Prefabs modified: {prefabsModified}, " +
                      $"Scenes modified: {scenesModified}, Components added: +{componentsAdded}");
            EditorUtility.DisplayDialog(
                "OffscreenSleepable 일괄 부착 완료",
                $"Prefabs: {prefabsModified}\nScenes: {scenesModified}\nComponents added: +{componentsAdded}",
                "OK");
        }

        [MenuItem(kDryRunMenu)]
        static void DryRun()
        {
            // Scene scan opens each scene single-mode, which would discard unsaved edits.
            // Prompt user to save first.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            int totalPrefabCandidates = 0, prefabAlreadyAttached = 0;
            int totalSceneCandidates = 0, sceneAlreadyAttached = 0;
            var perTypeCounts = new Dictionary<System.Type, int>();
            var perTypeMissing = new Dictionary<System.Type, int>();
            var prefabsToModify = new List<string>();
            var scenesToModify = new List<string>();

            string originalScenePath = SceneManager.GetActiveScene().path;

            try
            {
                // -- 1) Prefabs --
                var prefabGuids = AssetDatabase.FindAssets("t:Prefab");
                for (int i = 0; i < prefabGuids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                    if (ShouldSkipAssetPath(path)) continue;
                    if (EditorUtility.DisplayCancelableProgressBar(
                        "Dry Run — Prefabs", path, (float)i / prefabGuids.Length))
                    {
                        return;
                    }

                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) continue;

                    int prefabMissing = CountCandidates(prefab,
                        perTypeCounts, perTypeMissing,
                        ref totalPrefabCandidates, ref prefabAlreadyAttached);
                    if (prefabMissing > 0) prefabsToModify.Add($"  {path} (+{prefabMissing})");
                }

                // -- 2) Scenes --
                var sceneGuids = AssetDatabase.FindAssets("t:Scene");
                for (int i = 0; i < sceneGuids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);
                    if (!path.StartsWith("Assets/")) continue;
                    if (ShouldSkipAssetPath(path)) continue;
                    if (EditorUtility.DisplayCancelableProgressBar(
                        "Dry Run — Scenes", path, (float)i / sceneGuids.Length))
                    {
                        return;
                    }

                    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    int sceneMissing = 0;
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        sceneMissing += CountCandidates(root,
                            perTypeCounts, perTypeMissing,
                            ref totalSceneCandidates, ref sceneAlreadyAttached);
                    }
                    if (sceneMissing > 0) scenesToModify.Add($"  {path} (+{sceneMissing})");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (!string.IsNullOrEmpty(originalScenePath) &&
                    SceneManager.GetActiveScene().path != originalScenePath)
                {
                    EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);
                }
            }

            int totalCandidates = totalPrefabCandidates + totalSceneCandidates;
            int alreadyAttached = prefabAlreadyAttached + sceneAlreadyAttached;
            int wouldAdd = totalCandidates - alreadyAttached;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== OffscreenSleepable Dry Run (Prefabs + Scenes) ===");
            sb.AppendLine($"Prefab candidates: {totalPrefabCandidates,4} (attached={prefabAlreadyAttached}, missing={totalPrefabCandidates - prefabAlreadyAttached})");
            sb.AppendLine($"Scene  candidates: {totalSceneCandidates,4} (attached={sceneAlreadyAttached}, missing={totalSceneCandidates - sceneAlreadyAttached})");
            sb.AppendLine($"Would add total:   {wouldAdd}");
            sb.AppendLine();
            sb.AppendLine("Per-type breakdown (total / missing):");
            foreach (var type in OffscreenSleeperCandidates.CandidateTypes)
            {
                perTypeCounts.TryGetValue(type, out int total);
                perTypeMissing.TryGetValue(type, out int missing);
                sb.AppendLine($"  {type.Name,-30} total={total,4}, missing={missing,4}");
            }
            if (prefabsToModify.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Prefabs to be modified ({prefabsToModify.Count}):");
                foreach (var p in prefabsToModify) sb.AppendLine(p);
            }
            if (scenesToModify.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Scenes to be modified ({scenesToModify.Count}):");
                foreach (var p in scenesToModify) sb.AppendLine(p);
            }
            Debug.Log(sb.ToString());
            EditorUtility.DisplayDialog(
                "OffscreenSleepable Dry Run",
                "Dry run 완료. Console 로그를 확인하세요.\n\n" +
                $"Would add: {wouldAdd} component(s)\n" +
                $"Prefabs: {prefabsToModify.Count}, Scenes: {scenesToModify.Count}",
                "OK");
        }

        /// <summary>
        /// Walks <paramref name="root"/> for each candidate type, accumulating into the
        /// per-type counts and the totals. Returns the count of MISSING markers in this
        /// hierarchy (not yet attached). Shared by prefab and scene scan paths.
        /// </summary>
        static int CountCandidates(GameObject root,
            Dictionary<System.Type, int> perTypeCounts,
            Dictionary<System.Type, int> perTypeMissing,
            ref int totalAccum, ref int attachedAccum)
        {
            int missing = 0;
            foreach (var type in OffscreenSleeperCandidates.CandidateTypes)
            {
                var comps = root.GetComponentsInChildren(type, true);
                perTypeCounts.TryGetValue(type, out int existing);
                perTypeCounts[type] = existing + comps.Length;
                totalAccum += comps.Length;

                foreach (var c in comps)
                {
                    var go = ((Component)c).gameObject;
                    if (go.GetComponent<OffscreenSleepable>() != null) { attachedAccum++; continue; }
                    missing++;
                    perTypeMissing.TryGetValue(type, out int m);
                    perTypeMissing[type] = m + 1;
                }
            }
            return missing;
        }

        /// <summary>
        /// Adds OffscreenSleepable to every GameObject under <paramref name="root"/> that
        /// hosts any candidate type and doesn't already have a marker. Returns count added.
        /// </summary>
        static int AttachToHierarchy(GameObject root)
        {
            int added = 0;
            // Use a HashSet so a GameObject hosting two candidate types doesn't get two markers.
            var seen = new HashSet<GameObject>();

            foreach (var type in OffscreenSleeperCandidates.CandidateTypes)
            {
                var comps = root.GetComponentsInChildren(type, true);
                for (int i = 0; i < comps.Length; i++)
                {
                    var go = ((Component)comps[i]).gameObject;
                    if (!seen.Add(go)) continue;
                    if (go.GetComponent<OffscreenSleepable>() != null) continue;
                    Undo.AddComponent<OffscreenSleepable>(go);
                    added++;
                }
            }
            return added;
        }

        /// <summary>
        /// Quick prefab scan to estimate work before showing the confirmation dialog. Doesn't
        /// open scenes (would be slow) — actual attach pass handles scenes too.
        /// </summary>
        static int ScanCandidateCount()
        {
            int wouldAdd = 0;
            var prefabGuids = AssetDatabase.FindAssets("t:Prefab");
            foreach (var guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (ShouldSkipAssetPath(path)) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                var seen = new HashSet<GameObject>();
                foreach (var type in OffscreenSleeperCandidates.CandidateTypes)
                {
                    foreach (var c in prefab.GetComponentsInChildren(type, true))
                    {
                        var go = ((Component)c).gameObject;
                        if (!seen.Add(go)) continue;
                        if (go.GetComponent<OffscreenSleepable>() == null) wouldAdd++;
                    }
                }
            }
            return wouldAdd;
        }
    }
}
