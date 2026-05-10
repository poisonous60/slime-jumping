// Retroactively add MeshCollider2DWrapper marker to GameObjects that look like
// they were created by MeshToCollider2DGenerator but predate the wrapper component.
// Detection: name contains "_Collider2D" (handles "name_Collider2D" and Unity
// duplicate suffixes like "name_Collider2D (3)") AND has a child MeshFilter.
// Skips anything already wrapped, anything with blocked components, anything
// with isTrigger=true.

var blocked = new System.Collections.Generic.HashSet<string> {
    "TautLine", "DeformablePlatform", "SnakeSpine", "MegaDeform2DCollider", "WaterCurrentZone", "SnowField"
};

// Match either "...{name}_Collider2D" or "...{name}_Collider2D (n)" (Unity dup suffix).
var nameRegex = new System.Text.RegularExpressions.Regex(@"_Collider2D(\s*\(\d+\))?$");

var allPolys = UnityEngine.Object.FindObjectsByType<PolygonCollider2D>(FindObjectsSortMode.None);

int candidates = 0, alreadyWrapped = 0, addedWrapper = 0;
int skippedNameMismatch = 0, skippedNoChildMesh = 0;
int skippedBlocked = 0, skippedTrigger = 0;
int newlyEligibleVerts = 0;

var sb = new System.Text.StringBuilder();
sb.AppendLine("Per-object decisions (top 20 by point count):");

int undoGroup = Undo.GetCurrentGroup();
Undo.SetCurrentGroupName("Add MeshCollider2DWrapper to generator-created colliders");

foreach (var poly in allPolys.OrderByDescending(p => p.GetTotalPointCount())) {
    var go = poly.gameObject;

    // Already wrapped?
    if (go.GetComponent<SlimeJumping.MeshTools.MeshCollider2DWrapper>() != null) {
        alreadyWrapped++;
        continue;
    }
    candidates++;

    bool nameOk = nameRegex.IsMatch(go.name);
    bool hasChildMesh = false;
    foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true)) {
        if (mf.gameObject != go) { hasChildMesh = true; break; }
    }
    bool hasBlocked = false;
    foreach (var c in go.GetComponents<Component>()) {
        if (c == null) continue;
        if (blocked.Contains(c.GetType().Name)) { hasBlocked = true; break; }
    }
    bool isTrigger = poly.isTrigger;

    string decision;
    if (!nameOk) { skippedNameMismatch++; decision = "SKIP (name)"; }
    else if (!hasChildMesh) { skippedNoChildMesh++; decision = "SKIP (no childMesh)"; }
    else if (hasBlocked) { skippedBlocked++; decision = "SKIP (blocked component)"; }
    else if (isTrigger) { skippedTrigger++; decision = "SKIP (trigger)"; }
    else {
        Undo.AddComponent<SlimeJumping.MeshTools.MeshCollider2DWrapper>(go);
        EditorUtility.SetDirty(go);
        if (!Application.isPlaying)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        addedWrapper++;
        newlyEligibleVerts += poly.GetTotalPointCount();
        decision = "ADD WRAPPER";
    }

    if (sb.Length < 4000) {
        sb.AppendLine($"  {decision,-26} [{poly.GetTotalPointCount(),5}pts] {go.name}");
    }
}

Undo.CollapseUndoOperations(undoGroup);

sb.AppendLine();
sb.AppendLine($"Total polys: {allPolys.Length}");
sb.AppendLine($"  already wrapped: {alreadyWrapped}");
sb.AppendLine($"  candidates checked: {candidates}");
sb.AppendLine($"  + wrapper added: {addedWrapper}  ({newlyEligibleVerts} verts now eligible)");
sb.AppendLine($"  skipped (name mismatch): {skippedNameMismatch}");
sb.AppendLine($"  skipped (no child mesh): {skippedNoChildMesh}");
sb.AppendLine($"  skipped (blocked component): {skippedBlocked}");
sb.AppendLine($"  skipped (isTrigger): {skippedTrigger}");

return sb.ToString();
