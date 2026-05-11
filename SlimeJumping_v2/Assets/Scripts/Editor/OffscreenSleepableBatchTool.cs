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
        const string kCleanupMenu = "Tools/Optimization/Cleanup orphaned OffscreenSleepable markers";

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
            int activeSceneCount = ScanActiveSceneCandidateCount();
            if (wouldAddCount == 0 && activeSceneCount == 0)
            {
                EditorUtility.DisplayDialog(
                    "OffscreenSleepable Batch",
                    "이미 모든 후보 GameObject에 OffscreenSleepable이 부착되어 있습니다.\n변경 없음.",
                    "OK");
                return;
            }

            string activeScenePath = SceneManager.GetActiveScene().path;
            bool proceed = EditorUtility.DisplayDialog(
                "OffscreenSleepable 일괄 부착",
                $"이 작업은 모든 prefab + 현재 활성 씬을 순회하며 13종 후보를 가진 GameObject에\n" +
                $"OffscreenSleepable 마커를 추가합니다. (다른 씬은 건드리지 않음)\n\n" +
                $"활성 씬: {activeScenePath}\n" +
                $"예상 추가 (prefab): {wouldAddCount}\n" +
                $"예상 추가 (active scene): {activeSceneCount}\n\n" +
                $"prefab/scene meta 변경 — git 커밋 후 실행 권장.\n\n" +
                $"계속하시겠습니까?",
                "진행", "취소");
            if (!proceed) return;

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

                // -- 2) Active scene only (no other scenes opened) --
                var activeScene = SceneManager.GetActiveScene();
                int sceneAdded = 0;
                foreach (var root in activeScene.GetRootGameObjects())
                    sceneAdded += AttachToHierarchy(root);

                if (sceneAdded > 0)
                {
                    EditorSceneManager.MarkSceneDirty(activeScene);
                    EditorSceneManager.SaveScene(activeScene);
                    scenesModified = 1;
                    componentsAdded += sceneAdded;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
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
                    if (OffscreenSleeperCandidates.ShouldSkipAttach(go)) { attachedAccum++; continue; }
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
                    if (OffscreenSleeperCandidates.ShouldSkipAttach(go)) continue;
                    Undo.AddComponent<OffscreenSleepable>(go);
                    added++;
                }
            }
            return added;
        }

        /// <summary>
        /// Quick prefab scan to estimate work before showing the confirmation dialog. Doesn't
        /// open scenes (would be slow). Active-scene scan is in <see cref="ScanActiveSceneCandidateCount"/>.
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
                        if (go.GetComponent<OffscreenSleepable>() != null) continue;
                        if (OffscreenSleeperCandidates.ShouldSkipAttach(go)) continue;
                        wouldAdd++;
                    }
                }
            }
            return wouldAdd;
        }

        /// <summary>
        /// Counts MISSING markers in the currently active scene only. Cheap — the scene is
        /// already loaded, just walks root GameObjects.
        /// </summary>
        static int ScanActiveSceneCandidateCount()
        {
            int wouldAdd = 0;
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return 0;

            foreach (var root in scene.GetRootGameObjects())
            {
                var seen = new HashSet<GameObject>();
                foreach (var type in OffscreenSleeperCandidates.CandidateTypes)
                {
                    foreach (var c in root.GetComponentsInChildren(type, true))
                    {
                        var go = ((Component)c).gameObject;
                        if (!seen.Add(go)) continue;
                        if (go.GetComponent<OffscreenSleepable>() != null) continue;
                        if (OffscreenSleeperCandidates.ShouldSkipAttach(go)) continue;
                        wouldAdd++;
                    }
                }
            }
            return wouldAdd;
        }

        /// <summary>
        /// Removes OffscreenSleepable markers from prefabs + active scene where the host
        /// GameObject no longer hosts any candidate type (i.e. the type was removed from
        /// CandidateTypes). Useful after deciding a previously-attached type shouldn't be
        /// sleepable (e.g. SnowField — it's the ground, not a visual layer).
        /// </summary>
        [MenuItem(kCleanupMenu)]
        static void CleanupOrphanedMarkers()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            bool proceed = EditorUtility.DisplayDialog(
                "Cleanup orphaned markers",
                "현재 OffscreenSleeperCandidates.CandidateTypes에 없는 컴포넌트를 가진 GameObject에서\n" +
                "OffscreenSleepable 마커를 제거합니다.\n\n" +
                "예: SnowField가 후보 목록에서 빠졌으면 SnowField에 붙은 마커를 모두 제거.\n\n" +
                "prefab + 활성 씬을 처리. 다른 씬은 건드리지 않음.\n\n" +
                "git 커밋 후 실행 권장. 계속하시겠습니까?",
                "진행", "취소");
            if (!proceed) return;

            int prefabsModified = 0, sceneModified = 0, removed = 0;

            try
            {
                // Prefabs
                AssetDatabase.StartAssetEditing();
                try
                {
                    var prefabGuids = AssetDatabase.FindAssets("t:Prefab");
                    for (int i = 0; i < prefabGuids.Length; i++)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                        if (ShouldSkipAssetPath(path)) continue;
                        if (EditorUtility.DisplayCancelableProgressBar(
                            "Cleanup — Prefabs", path, (float)i / prefabGuids.Length)) return;

                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (prefab == null) continue;

                        int removedHere = RemoveOrphans(prefab);
                        if (removedHere > 0)
                        {
                            EditorUtility.SetDirty(prefab);
                            PrefabUtility.SavePrefabAsset(prefab);
                            prefabsModified++;
                            removed += removedHere;
                        }
                    }
                }
                finally { AssetDatabase.StopAssetEditing(); }

                // Active scene only
                var activeScene = SceneManager.GetActiveScene();
                int sceneRemoved = 0;
                foreach (var root in activeScene.GetRootGameObjects())
                    sceneRemoved += RemoveOrphans(root);
                if (sceneRemoved > 0)
                {
                    EditorSceneManager.MarkSceneDirty(activeScene);
                    EditorSceneManager.SaveScene(activeScene);
                    sceneModified = 1;
                    removed += sceneRemoved;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[OffscreenSleepableBatchTool] Cleanup — Prefabs: {prefabsModified}, Scene: {sceneModified}, Markers removed: {removed}");
            EditorUtility.DisplayDialog(
                "Cleanup 완료",
                $"Prefabs: {prefabsModified}\nActive scene: {sceneModified}\nMarkers removed: {removed}",
                "OK");
        }

        /// <summary>
        /// Find every OffscreenSleepable in <paramref name="root"/>'s hierarchy and remove
        /// the ones whose host GameObject no longer has any current candidate type.
        /// Returns count removed.
        /// </summary>
        static int RemoveOrphans(GameObject root)
        {
            int removed = 0;
            var markers = root.GetComponentsInChildren<OffscreenSleepable>(true);
            for (int i = 0; i < markers.Length; i++)
            {
                var marker = markers[i];
                if (marker == null) continue;

                bool stillCandidate = false;
                for (int t = 0; t < OffscreenSleeperCandidates.CandidateTypes.Length; t++)
                {
                    if (marker.GetComponent(OffscreenSleeperCandidates.CandidateTypes[t]) != null)
                    {
                        stillCandidate = true;
                        break;
                    }
                }
                // Two reasons to remove a marker:
                //   1) Host no longer has any candidate type (the type was removed from the
                //      list, e.g. SnowField after the wake-cost decision).
                //   2) Host is gated by ShouldSkipAttach (e.g. wrapper that hosts a SnowField
                //      in its hierarchy — sleeping the wrapper would cascade SetActive into
                //      the SnowField, breaking it).
                bool gated = stillCandidate && OffscreenSleeperCandidates.ShouldSkipAttach(marker.gameObject);
                if (!stillCandidate || gated)
                {
                    Undo.DestroyObjectImmediate(marker);
                    removed++;
                }
            }
            return removed;
        }
    }
}
