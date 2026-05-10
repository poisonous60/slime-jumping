// Loads two profiler captures sequentially, computes per-frame and per-method
// aggregates for each, then prints a side-by-side diff (before vs after, with
// delta and % change). Use to verify the impact of an optimization landed
// between two captures.
//
// Edit the two paths below before running. Output goes to stdout.
//
//   cat tools/profiler/compare_captures.cs | \
//     unity-cli exec --usings UnityEditor.Profiling,UnityEditorInternal
//
// Output sections:
//   1. Frame-time distribution diff (min/median/mean/p95/p99/max).
//   2. Top improvements — methods whose avgTotal dropped most.
//   3. Top regressions — methods whose avgTotal rose most.
//   4. Top hotspots in B (current top-cost methods, with delta vs A).

string captureA = @"e:\260301\project\SlimeJumping_v2_project\SlimeJumping_v2\ProfilerCaptures\SlimeJumping_v2_2026-05-10_17-43-58.data";
string captureB = @"e:\260301\project\SlimeJumping_v2_project\SlimeJumping_v2\ProfilerCaptures\SlimeJumping_v2_2026-05-10_19-00-48.data";

string labelA = System.IO.Path.GetFileNameWithoutExtension(captureA);
string labelB = System.IO.Path.GetFileNameWithoutExtension(captureB);

// --- Per-capture analysis ----------------------------------------------------

System.Func<string, (double[] frameMs, Dictionary<string,(double total,double self,int frames)> agg, int sampledFrames)> analyze = path => {
    ProfilerDriver.ClearAllFrames();
    bool ok = ProfilerDriver.LoadProfile(path, false);
    if (!ok) return (new double[0], new Dictionary<string,(double,double,int)>(), 0);

    int first = ProfilerDriver.firstFrameIndex;
    int last  = ProfilerDriver.lastFrameIndex;
    if (last < first) return (new double[0], new Dictionary<string,(double,double,int)>(), 0);

    var msList = new List<double>();
    var agg = new Dictionary<string,(double total,double self,int frames)>();
    int sampled = 0;

    for (int f = first; f <= last; f++) {
        using (var view = ProfilerDriver.GetHierarchyFrameDataView(
            f, 0, HierarchyFrameDataView.ViewModes.Default,
            HierarchyFrameDataView.columnTotalTime, true)) {
            if (view == null || !view.valid) continue;
            sampled++;

            int root = view.GetRootItemID();
            var rootKids = new List<int>();
            view.GetItemChildren(root, rootKids);
            double frameTotal = 0;
            foreach (var c in rootKids)
                frameTotal += view.GetItemColumnDataAsFloat(c, HierarchyFrameDataView.columnTotalTime);
            msList.Add(frameTotal);

            // Walk hierarchy, accumulate by canonical path.
            var stack = new Stack<(int id, string name, int depth)>();
            foreach (var c in rootKids) stack.Push((c, view.GetItemName(c), 0));
            var seen = new HashSet<string>();
            while (stack.Count > 0) {
                var (id, p, depth) = stack.Pop();
                if (depth > 8) continue;
                float total = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnTotalTime);
                float self  = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime);
                if (!agg.TryGetValue(p, out var v)) v = (0, 0, 0);
                v.total += total; v.self += self;
                if (seen.Add(p)) v.frames += 1;
                agg[p] = v;
                if (total < 0.05f && depth > 1) continue;
                var kids = new List<int>();
                view.GetItemChildren(id, kids);
                foreach (var k in kids)
                    stack.Push((k, p + "/" + view.GetItemName(k), depth + 1));
            }
        }
    }
    return (msList.ToArray(), agg, sampled);
};

var a = analyze(captureA);
var b = analyze(captureB);

if (a.sampledFrames == 0 || b.sampledFrames == 0)
    return $"capture load failed: A frames={a.sampledFrames}  B frames={b.sampledFrames}";

System.Func<double[], (double min, double med, double mean, double p95, double p99, double max)> stats = arr => {
    var s = arr.OrderBy(x => x).ToArray();
    int n = s.Length;
    return (s[0], s[n/2], s.Average(), s[(int)(n*0.95)], s[(int)(n*0.99)], s[n-1]);
};

var sA = stats(a.frameMs);
var sB = stats(b.frameMs);

var sb = new System.Text.StringBuilder();
sb.AppendLine($"=== CAPTURE COMPARISON ===");
sb.AppendLine($"A: {labelA}  ({a.sampledFrames} frames)");
sb.AppendLine($"B: {labelB}  ({b.sampledFrames} frames)");
sb.AppendLine();

System.Func<double, double, string> deltaStr = (av, bv) => {
    double d = bv - av;
    double pct = av > 0.001 ? (d / av) * 100.0 : 0.0;
    string arrow = d < -0.005 ? "↓" : (d > 0.005 ? "↑" : "·");
    return $"{arrow}{d,+8:F2}ms ({pct,+6:F1}%)";
};

sb.AppendLine("=== FRAME-TIME DISTRIBUTION ===");
sb.AppendLine($"  {"metric",-8} {"A",10} {"B",10}  delta");
sb.AppendLine($"  {"min",-8} {sA.min,10:F2} {sB.min,10:F2}  {deltaStr(sA.min, sB.min)}");
sb.AppendLine($"  {"median",-8} {sA.med,10:F2} {sB.med,10:F2}  {deltaStr(sA.med, sB.med)}");
sb.AppendLine($"  {"mean",-8} {sA.mean,10:F2} {sB.mean,10:F2}  {deltaStr(sA.mean, sB.mean)}");
sb.AppendLine($"  {"p95",-8} {sA.p95,10:F2} {sB.p95,10:F2}  {deltaStr(sA.p95, sB.p95)}");
sb.AppendLine($"  {"p99",-8} {sA.p99,10:F2} {sB.p99,10:F2}  {deltaStr(sA.p99, sB.p99)}");
sb.AppendLine($"  {"max",-8} {sA.max,10:F2} {sB.max,10:F2}  {deltaStr(sA.max, sB.max)}");
sb.AppendLine();

// --- Per-method diff ---------------------------------------------------------

var allPaths = new HashSet<string>(a.agg.Keys);
allPaths.UnionWith(b.agg.Keys);

var rows = new List<(string path, double avgA, double avgB, double delta)>();
foreach (var p in allPaths) {
    double avgA = a.agg.TryGetValue(p, out var va) ? va.total / a.sampledFrames : 0;
    double avgB = b.agg.TryGetValue(p, out var vb) ? vb.total / b.sampledFrames : 0;
    rows.Add((p, avgA, avgB, avgB - avgA));
}

sb.AppendLine("=== TOP IMPROVEMENTS (B faster than A, sorted by delta) ===");
sb.AppendLine($"  {"deltaMs",10} {"A_ms",10} {"B_ms",10}  path");
foreach (var r in rows
    .Where(r => r.avgA >= 0.10 && r.delta < -0.05)
    .OrderBy(r => r.delta)
    .Take(20))
    sb.AppendLine($"  {r.delta,10:F3} {r.avgA,10:F3} {r.avgB,10:F3}  {r.path}");

sb.AppendLine();
sb.AppendLine("=== TOP REGRESSIONS (B slower than A) ===");
sb.AppendLine($"  {"deltaMs",10} {"A_ms",10} {"B_ms",10}  path");
foreach (var r in rows
    .Where(r => r.avgB >= 0.10 && r.delta > 0.05)
    .OrderByDescending(r => r.delta)
    .Take(15))
    sb.AppendLine($"  {r.delta,10:F3} {r.avgA,10:F3} {r.avgB,10:F3}  {r.path}");

sb.AppendLine();
sb.AppendLine("=== TOP HOTSPOTS IN B (where time is spent now) ===");
sb.AppendLine($"  {"B_ms",10} {"A_ms",10} {"delta",10}  path");
foreach (var r in rows
    .Where(r => r.avgB >= 0.20)
    .OrderByDescending(r => r.avgB)
    .Take(20))
    sb.AppendLine($"  {r.avgB,10:F3} {r.avgA,10:F3} {r.delta,10:F3}  {r.path}");

return sb.ToString();
