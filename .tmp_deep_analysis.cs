var sb = new System.Text.StringBuilder();

// === 1. Slime collider details ===
sb.AppendLine("=== SLIME COLLIDER DETAILS ===");
var slime = UnityEngine.Object.FindFirstObjectByType<SlimeJumping.Controller.SlimeController>();
if (slime != null) {
    var slimeColliders = slime.GetComponentsInChildren<Collider2D>(true);
    sb.AppendLine($"slime '{slime.name}' total child colliders: {slimeColliders.Length}");
    foreach (var c in slimeColliders) {
        string detail = c.GetType().Name;
        if (c is PolygonCollider2D p) detail += $" pts={p.GetTotalPointCount()}";
        if (c is EdgeCollider2D e) detail += $" pts={e.pointCount}";
        var rb = c.attachedRigidbody;
        string rbInfo = rb != null ? $"rb={rb.bodyType}" : "noRb";
        sb.AppendLine($"  {detail}  enabled={c.enabled}  trigger={c.isTrigger}  layer={LayerMask.LayerToName(c.gameObject.layer)}  {rbInfo}  on={c.gameObject.name}");
    }
}

// === 2. Heavy terrain rigidbody/layer info ===
sb.AppendLine();
sb.AppendLine("=== TOP HEAVY POLYGONS — rigidbody / layer ===");
var heavy = UnityEngine.Object.FindObjectsByType<PolygonCollider2D>(FindObjectsSortMode.None)
    .OrderByDescending(p => p.GetTotalPointCount()).Take(10).ToList();
foreach (var p in heavy) {
    var rb = p.attachedRigidbody;
    string rbInfo = rb != null ? $"rb={rb.bodyType}" : "<NO RB>";
    sb.AppendLine($"  pts={p.GetTotalPointCount()}  layer={LayerMask.LayerToName(p.gameObject.layer)}  {rbInfo}  trigger={p.isTrigger}  name={p.gameObject.name}");
}

// === 3. Layer collision matrix — does slime layer collide with terrain layer? ===
sb.AppendLine();
sb.AppendLine("=== LAYER COLLISION MATRIX (slime vs others) ===");
if (slime != null) {
    var slimeColls = slime.GetComponentsInChildren<Collider2D>(true);
    var slimeLayers = new HashSet<int>(slimeColls.Select(c => c.gameObject.layer));
    foreach (int sl in slimeLayers) {
        sb.AppendLine($"slime layer {sl} ({LayerMask.LayerToName(sl)}):");
        for (int other = 0; other < 32; other++) {
            if (Physics2D.GetIgnoreLayerCollision(sl, other)) continue;
            string lname = LayerMask.LayerToName(other);
            if (string.IsNullOrEmpty(lname)) continue;
            sb.AppendLine($"  collides with: {other} ({lname})");
        }
    }
}

// === 4. Physics2D settings ===
sb.AppendLine();
sb.AppendLine("=== Physics2D SETTINGS ===");
sb.AppendLine($"  fixedDeltaTime = {Time.fixedDeltaTime}  ({1f/Time.fixedDeltaTime:F1} Hz)");
sb.AppendLine($"  velocityIterations = {Physics2D.velocityIterations}");
sb.AppendLine($"  positionIterations = {Physics2D.positionIterations}");
sb.AppendLine($"  bounceThreshold = {Physics2D.bounceThreshold}");
sb.AppendLine($"  maxLinearCorrection = {Physics2D.maxLinearCorrection}");
sb.AppendLine($"  baumgarteScale = {Physics2D.baumgarteScale}");
sb.AppendLine($"  queriesHitTriggers = {Physics2D.queriesHitTriggers}");
sb.AppendLine($"  callbacksOnDisable = {Physics2D.callbacksOnDisable}");
sb.AppendLine($"  reuseCollisionCallbacks = {Physics2D.reuseCollisionCallbacks}");
sb.AppendLine($"  autoSyncTransforms = {Physics2D.autoSyncTransforms}");
sb.AppendLine($"  simulationMode = {Physics2D.simulationMode}");

// === 5. Detailed slow-frame breakdown — pick a frame near p95 ===
sb.AppendLine();
sb.AppendLine("=== INDIVIDUAL SLOW FRAME ANALYSIS (top 5 frames by total ms) ===");
int first = ProfilerDriver.firstFrameIndex;
int last  = ProfilerDriver.lastFrameIndex;
var frameMs = new Dictionary<int, double>();
for (int f = first; f <= last; f++) {
    using (var v = ProfilerDriver.GetHierarchyFrameDataView(
        f, 0, HierarchyFrameDataView.ViewModes.Default,
        HierarchyFrameDataView.columnTotalTime, true)) {
        if (v == null || !v.valid) continue;
        var kids = new List<int>();
        v.GetItemChildren(v.GetRootItemID(), kids);
        double sum = 0;
        foreach (var c in kids)
            sum += v.GetItemColumnDataAsFloat(c, HierarchyFrameDataView.columnTotalTime);
        frameMs[f] = sum;
    }
}
// Skip the absurd outlier (>500ms — first frame load) and take next 5
var topSlow = frameMs.Where(kv => kv.Value < 500.0).OrderByDescending(kv => kv.Value).Take(5).ToList();

foreach (var (frame, totalMs) in topSlow.Select(kv => (kv.Key, kv.Value))) {
    sb.AppendLine();
    sb.AppendLine($"--- FRAME {frame}  total={totalMs:F2}ms ---");
    using (var view = ProfilerDriver.GetHierarchyFrameDataView(
        frame, 0, HierarchyFrameDataView.ViewModes.Default,
        HierarchyFrameDataView.columnTotalTime, true)) {
        if (view == null || !view.valid) { sb.AppendLine("  (no data)"); continue; }
        // Walk top hierarchy with self time
        var stack = new Stack<(int id, int depth)>();
        var rootKids = new List<int>();
        view.GetItemChildren(view.GetRootItemID(), rootKids);
        foreach (var c in rootKids) stack.Push((c, 0));
        while (stack.Count > 0) {
            var (id, depth) = stack.Pop();
            if (depth > 6) continue;
            float total = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnTotalTime);
            float self  = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime);
            float calls = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnCalls);
            if (total < 0.5f) continue;
            string indent = new string(' ', depth * 2);
            sb.AppendLine($"  {indent}{total,7:F2}ms tot {self,7:F2} self {calls,4:F0}call  {view.GetItemName(id)}");
            var kids = new List<int>();
            view.GetItemChildren(id, kids);
            foreach (var k in kids) stack.Push((k, depth + 1));
        }
    }
}

// === 6. Profiler counters — GC alloc per frame, draw calls, etc. ===
sb.AppendLine();
sb.AppendLine("=== AVERAGE COUNTERS (across loaded capture) ===");
string[] counterNames = {
    "Render.OpaqueGeometry", "Triangles Count", "Vertices Count",
    "Draw Calls Count", "Batches Count",
    "GC Allocated In Frame", "GC Used Memory",
    "Total Reserved Memory", "Used Total Memory",
    "Set Pass Calls Count"
};
// (Counter API removed — frame markers below cover the same ground)
sb.AppendLine("  (counter API skipped)");

return sb.ToString();
