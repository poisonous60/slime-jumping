#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SlimeJumping.MeshTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SlimeJumping.EditorTools
{
    // Collinear / curve simplification of terrain PolygonCollider2D paths via
    // Ramer-Douglas-Peucker. Targets only mesh-derived terrain colliders
    // (MeshCollider2DWrapper marker), excludes triggers and any object whose
    // shape is rewritten at runtime (TautLine, DeformablePlatform, SnakeSpine,
    // MegaDeform2DCollider, WaterCurrentZone). Provides a SceneView preview
    // (green = original, red = proposed) before commit so the user can verify
    // each collider visually.
    public class TerrainColliderOptimizer : EditorWindow
    {
        // Components whose presence on a wrapper makes the collider unsafe to
        // pre-bake — they rewrite SetPath at runtime, or read GetPath shape and
        // would visually desync if curve simplification changed the outline.
        // Matched by simple type-name string (no assembly dependency).
        static readonly HashSet<string> BlockedComponentNames = new HashSet<string>
        {
            "TautLine",
            "DeformablePlatform",
            "SnakeSpine",
            "MegaDeform2DCollider",
            "WaterCurrentZone",
            // SnowField uses its host polygon for placement masks; curve simplification
            // would let snow drift outside the simplified outline. Collinear-only would
            // be safe, but a single conservative blocklist is simpler than per-mode rules.
            "SnowField",
        };

        // Path with <4 vertices is too small to simplify; <3 after simplify is degenerate.
        const int MinPathVertexCount = 4;
        const int MinPostSimplifyVertexCount = 3;

        enum Mode { Collinear, Curve }
        Mode mode = Mode.Collinear;

        float collinearEpsilon = 0.001f;   // chord error in meters; 0.001 ~= "purely collinear"
        float curveEpsilon = 0.03f;        // 3 cm chord error; ~3° on a 2 m radius arc

        // Preview cache keyed by GameObject InstanceID so the SceneView callback
        // can render without holding component refs across domain reloads.
        static Dictionary<int, PreviewEntry> previewCache = new Dictionary<int, PreviewEntry>();

        struct PreviewEntry
        {
            public int instanceId;
            public Transform transform;
            public Vector2[][] originalPaths;   // local-space
            public Vector2[][] simplifiedPaths; // local-space
        }

        Vector2 scroll;

        [MenuItem("SlimeJumping/Terrain Collider Optimizer", false, 210)]
        static void Open()
        {
            var w = GetWindow<TerrainColliderOptimizer>(false, "Terrain Collider Optimizer", true);
            w.minSize = new Vector2(420f, 360f);
        }

        void OnEnable() { SceneView.duringSceneGui += OnSceneGUI; }
        void OnDisable() { SceneView.duringSceneGui -= OnSceneGUI; }

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "RDP-based polygon simplification for mesh-derived terrain colliders.\n" +
                "ONLY processes objects with MeshCollider2DWrapper. Skips triggers and runtime-shape platforms.\n" +
                "Use 'Preview' to inspect (green=orig, red=simplified) in SceneView before 'Commit'.",
                MessageType.Info);

            EditorGUILayout.Space();

            mode = (Mode)EditorGUILayout.EnumPopup(new GUIContent("Mode",
                "Collinear: remove redundant points on straight segments (eps≈0.001m, near-zero risk).\n" +
                "Curve: simplify curves by chord error (eps≈0.03m, requires per-collider visual review)."), mode);

            if (mode == Mode.Collinear)
            {
                collinearEpsilon = EditorGUILayout.Slider(new GUIContent("Epsilon (m)",
                    "Max perpendicular chord error in meters. Keep ≤ 0.005 for collinear-only safety."),
                    collinearEpsilon, 0.0001f, 0.01f);
            }
            else
            {
                curveEpsilon = EditorGUILayout.Slider(new GUIContent("Epsilon (m)",
                    "Max perpendicular chord error in meters. 0.03 ≈ 3° on 2m radius. Verify each collider in SceneView."),
                    curveEpsilon, 0.005f, 0.1f);
            }

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Find All Eligible In Scene", GUILayout.Height(28f)))
                {
                    SelectAllEligible();
                }
                if (GUILayout.Button("Clear Preview", GUILayout.Height(28f)))
                {
                    ClearPreview();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Preview Selected", GUILayout.Height(32f)))
                {
                    BuildPreview(GetEligibleFromSelection(), CurrentEpsilon());
                }
                GUI.backgroundColor = previewCache.Count > 0 ? new Color(1f, 0.7f, 0.5f) : Color.white;
                if (GUILayout.Button("Commit Preview", GUILayout.Height(32f)))
                {
                    CommitPreview();
                }
                GUI.backgroundColor = Color.white;
            }

            EditorGUILayout.Space();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var sel = GetEligibleFromSelection();
                int totalSelectedPolys = Selection.gameObjects.Count(g => g != null && g.GetComponent<PolygonCollider2D>() != null);
                int blocked = totalSelectedPolys - sel.Count;

                EditorGUILayout.LabelField($"Selected GameObjects: {Selection.gameObjects.Length}");
                EditorGUILayout.LabelField($"  with PolygonCollider2D: {totalSelectedPolys}");
                EditorGUILayout.LabelField($"  eligible (wrapper, non-trigger, no runtime SetPath): {sel.Count}");
                EditorGUILayout.LabelField($"  blocked: {blocked}");

                if (sel.Count > 0)
                {
                    int origVerts = sel.Sum(p => p.GetTotalPointCount());
                    EditorGUILayout.LabelField($"  total vertices in eligible: {origVerts}");
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"Preview cache: {previewCache.Count} colliders");
                if (previewCache.Count > 0)
                {
                    int previewOrig = previewCache.Values.Sum(e => e.originalPaths.Sum(p => p.Length));
                    int previewSimp = previewCache.Values.Sum(e => e.simplifiedPaths.Sum(p => p.Length));
                    int delta = previewOrig - previewSimp;
                    int pct = previewOrig > 0 ? (int)(100f * delta / previewOrig) : 0;
                    EditorGUILayout.LabelField($"  vertices: {previewOrig} → {previewSimp}  (−{delta}, −{pct}%)");
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Eligibility filter", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("• must have MeshCollider2DWrapper component");
            EditorGUILayout.LabelField("• PolygonCollider2D.isTrigger == false");
            EditorGUILayout.LabelField("• no runtime SetPath component:");
            using (new EditorGUI.IndentLevelScope())
            {
                foreach (var n in BlockedComponentNames)
                    EditorGUILayout.LabelField("    " + n);
            }
        }

        float CurrentEpsilon() => mode == Mode.Collinear ? collinearEpsilon : curveEpsilon;

        // === Eligibility ===

        static bool IsEligible(GameObject go)
        {
            if (go == null) return false;
            if (go.GetComponent<MeshCollider2DWrapper>() == null) return false;
            var poly = go.GetComponent<PolygonCollider2D>();
            if (poly == null) return false;
            if (poly.isTrigger) return false;
            // String-name check avoids hard assembly references.
            var components = go.GetComponents<Component>();
            foreach (var c in components)
            {
                if (c == null) continue;
                if (BlockedComponentNames.Contains(c.GetType().Name)) return false;
            }
            return true;
        }

        List<PolygonCollider2D> GetEligibleFromSelection()
        {
            var result = new List<PolygonCollider2D>();
            foreach (var go in Selection.gameObjects)
            {
                if (!IsEligible(go)) continue;
                result.Add(go.GetComponent<PolygonCollider2D>());
            }
            return result;
        }

        void SelectAllEligible()
        {
            var all = FindObjectsByType<MeshCollider2DWrapper>(FindObjectsSortMode.None);
            var eligible = all.Where(w => IsEligible(w.gameObject)).Select(w => w.gameObject).ToArray();
            Selection.objects = eligible;
            ShowNotification(new GUIContent($"Selected {eligible.Length} eligible terrain colliders"));
        }

        // === Preview ===

        void BuildPreview(List<PolygonCollider2D> targets, float eps)
        {
            previewCache.Clear();
            int origTotal = 0, simpTotal = 0;
            foreach (var poly in targets)
            {
                int n = poly.pathCount;
                var orig = new Vector2[n][];
                var simp = new Vector2[n][];
                for (int i = 0; i < n; i++)
                {
                    orig[i] = poly.GetPath(i);
                    simp[i] = SimplifyClosedPath(orig[i], eps);
                    origTotal += orig[i].Length;
                    simpTotal += simp[i].Length;
                }
                previewCache[poly.gameObject.GetInstanceID()] = new PreviewEntry
                {
                    instanceId = poly.gameObject.GetInstanceID(),
                    transform = poly.transform,
                    originalPaths = orig,
                    simplifiedPaths = simp,
                };
            }
            SceneView.RepaintAll();
            int delta = origTotal - simpTotal;
            int pct = origTotal > 0 ? (int)(100f * delta / origTotal) : 0;
            ShowNotification(new GUIContent($"Preview: {targets.Count} colliders, {origTotal} → {simpTotal} verts (−{pct}%)"));
        }

        void ClearPreview()
        {
            previewCache.Clear();
            SceneView.RepaintAll();
        }

        // === Commit ===

        void CommitPreview()
        {
            if (previewCache.Count == 0)
            {
                ShowNotification(new GUIContent("No preview to commit. Click Preview first."));
                return;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Terrain Collider Optimize");

            int committed = 0, skippedDegenerate = 0;
            int origVerts = 0, newVerts = 0;
            foreach (var entry in previewCache.Values)
            {
                var go = EditorUtility.InstanceIDToObject(entry.instanceId) as GameObject;
                if (go == null) continue;
                var poly = go.GetComponent<PolygonCollider2D>();
                if (poly == null) continue;
                // Re-verify eligibility right before commit (selection or scene may have changed).
                if (!IsEligible(go)) continue;

                Undo.RecordObject(poly, "Optimize terrain collider");
                bool anyApplied = false;
                int n = Mathf.Min(poly.pathCount, entry.simplifiedPaths.Length);
                for (int i = 0; i < n; i++)
                {
                    var simp = entry.simplifiedPaths[i];
                    var orig = entry.originalPaths[i];
                    origVerts += orig.Length;
                    if (simp.Length < MinPostSimplifyVertexCount)
                    {
                        // Degenerate result — keep original path untouched.
                        newVerts += orig.Length;
                        skippedDegenerate++;
                        continue;
                    }
                    poly.SetPath(i, simp);
                    newVerts += simp.Length;
                    anyApplied = true;
                }
                if (anyApplied)
                {
                    EditorUtility.SetDirty(poly);
                    if (!Application.isPlaying)
                        EditorSceneManager.MarkSceneDirty(go.scene);
                    committed++;
                }
            }

            Undo.CollapseUndoOperations(undoGroup);

            int delta = origVerts - newVerts;
            int pct = origVerts > 0 ? (int)(100f * delta / origVerts) : 0;
            ShowNotification(new GUIContent($"Committed {committed} colliders, {origVerts} → {newVerts} verts (−{pct}%). Skipped degenerate paths: {skippedDegenerate}"));
            previewCache.Clear();
            SceneView.RepaintAll();
        }

        // === SceneView Gizmo ===

        static void OnSceneGUI(SceneView sv)
        {
            if (previewCache.Count == 0) return;
            foreach (var entry in previewCache.Values)
            {
                if (entry.transform == null) continue;
                var t = entry.transform;
                // Original = green, Simplified = red. Drawn closed.
                Handles.color = new Color(0.2f, 1f, 0.2f, 0.9f);
                foreach (var path in entry.originalPaths) DrawPolylineLocal(t, path, true);
                Handles.color = new Color(1f, 0.2f, 0.2f, 0.95f);
                foreach (var path in entry.simplifiedPaths) DrawPolylineLocal(t, path, true);
            }
        }

        static void DrawPolylineLocal(Transform t, Vector2[] localPath, bool closed)
        {
            if (localPath == null || localPath.Length < 2) return;
            int n = localPath.Length;
            var pts = new Vector3[closed ? n + 1 : n];
            for (int i = 0; i < n; i++)
            {
                Vector3 wp = t.TransformPoint(localPath[i]);
                pts[i] = wp;
            }
            if (closed) pts[n] = pts[0];
            Handles.DrawAAPolyLine(2.5f, pts);
        }

        // === RDP ===

        // Simplify a closed polygon path via RDP. Uses two diametrically-opposite
        // anchor points (the pair with maximum pairwise distance) to split the
        // loop into two open polylines — avoids seam artifacts at index 0.
        public static Vector2[] SimplifyClosedPath(Vector2[] path, float epsilon)
        {
            if (path == null) return path;
            int n = path.Length;
            if (n < MinPathVertexCount) return path;

            // Find anchor pair (O(n²) — fine up to ~few thousand vertices).
            int anchorA = 0, anchorB = 0;
            float maxSq = -1f;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    float d = (path[i] - path[j]).sqrMagnitude;
                    if (d > maxSq) { maxSq = d; anchorA = i; anchorB = j; }
                }
            }
            if (anchorA == anchorB) return path;

            // Build two halves: anchorA..anchorB (forward) and anchorB..anchorA (forward, wrapping).
            var half1 = new List<Vector2>();
            for (int idx = anchorA; ; idx = (idx + 1) % n)
            {
                half1.Add(path[idx]);
                if (idx == anchorB) break;
            }
            var half2 = new List<Vector2>();
            for (int idx = anchorB; ; idx = (idx + 1) % n)
            {
                half2.Add(path[idx]);
                if (idx == anchorA) break;
            }

            var simp1 = RDP(half1.ToArray(), epsilon);
            var simp2 = RDP(half2.ToArray(), epsilon);

            // Merge: simp1 goes A→B, simp2 goes B→A. Drop duplicates at boundaries.
            var result = new List<Vector2>(simp1.Length + simp2.Length);
            result.AddRange(simp1);
            for (int i = 1; i < simp2.Length - 1; i++) result.Add(simp2[i]);

            // Guard: never return below 3 vertices for a polygon path.
            if (result.Count < MinPostSimplifyVertexCount) return path;
            return result.ToArray();
        }

        // Iterative RDP on an open polyline; keeps endpoints, drops interior
        // vertices whose perpendicular distance from the chord is < epsilon.
        static Vector2[] RDP(Vector2[] points, float epsilon)
        {
            int n = points.Length;
            if (n < 3) return points;

            var keep = new bool[n];
            keep[0] = true;
            keep[n - 1] = true;

            var stack = new Stack<(int start, int end)>();
            stack.Push((0, n - 1));

            while (stack.Count > 0)
            {
                var (start, end) = stack.Pop();
                if (end - start < 2) continue;

                float maxDist = 0f;
                int maxIdx = -1;
                for (int i = start + 1; i < end; i++)
                {
                    float d = PerpDistanceToLine(points[i], points[start], points[end]);
                    if (d > maxDist) { maxDist = d; maxIdx = i; }
                }
                if (maxIdx >= 0 && maxDist > epsilon)
                {
                    keep[maxIdx] = true;
                    stack.Push((start, maxIdx));
                    stack.Push((maxIdx, end));
                }
            }

            var result = new List<Vector2>(n);
            for (int i = 0; i < n; i++) if (keep[i]) result.Add(points[i]);
            return result.ToArray();
        }

        // Perpendicular distance from point p to the infinite line through a and b.
        // |cross(b-a, p-a)| / |b-a|. Falls back to point-to-point distance for degenerate ab.
        static float PerpDistanceToLine(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 1e-12f) return (p - a).magnitude;
            float cross = ab.x * (p.y - a.y) - ab.y * (p.x - a.x);
            return Mathf.Abs(cross) / Mathf.Sqrt(lenSq);
        }
    }
}
#endif
