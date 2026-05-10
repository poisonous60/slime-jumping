// Run the TerrainColliderOptimizer's collinear pass (epsilon=0.001m) on every
// eligible MeshCollider2DWrapper in the active scene. Reflection into the
// internal API so we can run unattended without the EditorWindow open.

var sb = new System.Text.StringBuilder();

// Collect eligible wrappers using the same filter logic as the EditorWindow.
// Mirror IsEligible exactly: MeshCollider2DWrapper present, PolygonCollider2D
// present and non-trigger, no blocked component name on the GO.
var blocked = new System.Collections.Generic.HashSet<string> {
    "TautLine", "DeformablePlatform", "SnakeSpine", "MegaDeform2DCollider", "WaterCurrentZone"
};

System.Func<GameObject, bool> isEligible = go => {
    if (go == null) return false;
    if (go.GetComponent<SlimeJumping.MeshTools.MeshCollider2DWrapper>() == null) return false;
    var poly0 = go.GetComponent<PolygonCollider2D>();
    if (poly0 == null) return false;
    if (poly0.isTrigger) return false;
    foreach (var c in go.GetComponents<Component>()) {
        if (c == null) continue;
        if (blocked.Contains(c.GetType().Name)) return false;
    }
    return true;
};

var allWrappers = UnityEngine.Object.FindObjectsByType<SlimeJumping.MeshTools.MeshCollider2DWrapper>(FindObjectsSortMode.None);
var eligible = allWrappers.Where(w => isEligible(w.gameObject))
                          .Select(w => w.GetComponent<PolygonCollider2D>())
                          .ToList();

sb.AppendLine($"Eligible terrain wrappers: {eligible.Count} / {allWrappers.Length}");

// Stats baseline
int origTotalVerts = 0, newTotalVerts = 0;
int origPolys = eligible.Count;
int polysModified = 0, pathsModified = 0, pathsSkippedDegenerate = 0;
int pathsSkippedNoChange = 0;

const float epsilon = 0.001f;

// Reflect into SimplifyClosedPath (public static).
var simplifyMethod = typeof(SlimeJumping.EditorTools.TerrainColliderOptimizer).GetMethod("SimplifyClosedPath",
    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
if (simplifyMethod == null) return "ERROR: SimplifyClosedPath not found via reflection";

int undoGroup = Undo.GetCurrentGroup();
Undo.SetCurrentGroupName("Terrain Collider Collinear Pass");

foreach (var poly in eligible) {
    Undo.RecordObject(poly, "Optimize terrain collider (collinear)");
    bool anyApplied = false;
    int n = poly.pathCount;
    for (int i = 0; i < n; i++) {
        Vector2[] orig = poly.GetPath(i);
        origTotalVerts += orig.Length;
        Vector2[] simp = (Vector2[])simplifyMethod.Invoke(null, new object[] { orig, epsilon });
        if (simp.Length < 3) {
            // Degenerate — keep original.
            newTotalVerts += orig.Length;
            pathsSkippedDegenerate++;
            continue;
        }
        if (simp.Length == orig.Length) {
            // No-op for this path.
            newTotalVerts += orig.Length;
            pathsSkippedNoChange++;
            continue;
        }
        poly.SetPath(i, simp);
        newTotalVerts += simp.Length;
        anyApplied = true;
        pathsModified++;
    }
    if (anyApplied) {
        EditorUtility.SetDirty(poly);
        if (!Application.isPlaying)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(poly.gameObject.scene);
        polysModified++;
    }
}

Undo.CollapseUndoOperations(undoGroup);

int delta = origTotalVerts - newTotalVerts;
int pct = origTotalVerts > 0 ? (int)(100f * delta / origTotalVerts) : 0;

sb.AppendLine();
sb.AppendLine($"Verts: {origTotalVerts} → {newTotalVerts}  (−{delta}, −{pct}%)");
sb.AppendLine($"Polys modified: {polysModified} / {origPolys}");
sb.AppendLine($"Paths modified: {pathsModified}");
sb.AppendLine($"Paths skipped (no change): {pathsSkippedNoChange}");
sb.AppendLine($"Paths skipped (degenerate result): {pathsSkippedDegenerate}");

return sb.ToString();
