// Load capture and produce a comprehensive report
string capturePath = @"e:\260301\project\SlimeJumping_v2_project\SlimeJumping_v2\ProfilerCaptures\SlimeJumping_v2_2026-05-10_17-43-58.data";

ProfilerDriver.ClearAllFrames();
bool ok = ProfilerDriver.LoadProfile(capturePath, false);
if (!ok) return "LoadProfile failed";

int first = ProfilerDriver.firstFrameIndex;
int last  = ProfilerDriver.lastFrameIndex;
int count = last - first + 1;
if (count <= 0) return "no frames";

// Per-frame total ms (CPU main thread)
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
if (frameMs.Count == 0) return "no valid frames";

var sb = new System.Text.StringBuilder();
var sortedMs = frameMs.Values.OrderBy(x => x).ToList();
double median = sortedMs[sortedMs.Count / 2];
double mean = sortedMs.Average();
double p95 = sortedMs[(int)(sortedMs.Count * 0.95)];
double p99 = sortedMs[(int)(sortedMs.Count * 0.99)];
double maxMs = sortedMs.Last();
double minMs = sortedMs.First();
sb.AppendLine($"=== FRAME TIME DISTRIBUTION ({frameMs.Count} valid frames, range {first}..{last}) ===");
sb.AppendLine($"min={minMs:F2}ms  median={median:F2}ms  mean={mean:F2}ms  p95={p95:F2}ms  p99={p99:F2}ms  max={maxMs:F2}ms");
sb.AppendLine($"@60fps target=16.67ms   @30fps=33.3ms");
sb.AppendLine();

// Aggregate hierarchy across all frames
var agg = new Dictionary<string, (double total, double self, int calls, int frames)>();
int actualSampled = 0;
foreach (int f in frameMs.Keys) {
    using (var view = ProfilerDriver.GetHierarchyFrameDataView(
        f, 0, HierarchyFrameDataView.ViewModes.Default,
        HierarchyFrameDataView.columnTotalTime, true)) {
        if (view == null || !view.valid) continue;
        actualSampled++;
        int root = view.GetRootItemID();
        var stack = new Stack<(int id, string path, int depth)>();
        var rootChildren = new List<int>();
        view.GetItemChildren(root, rootChildren);
        foreach (var c in rootChildren) stack.Push((c, view.GetItemName(c), 0));
        var seenInFrame = new HashSet<string>();
        while (stack.Count > 0) {
            var (id, path, depth) = stack.Pop();
            if (depth > 8) continue;
            float total = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnTotalTime);
            float self  = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime);
            float calls = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnCalls);
            if (!agg.TryGetValue(path, out var v)) v = (0, 0, 0, 0);
            v.total += total; v.self += self; v.calls += (int)calls;
            if (seenInFrame.Add(path)) v.frames += 1;
            agg[path] = v;
            if (total < 0.05f && depth > 1) continue;
            var kids = new List<int>();
            view.GetItemChildren(id, kids);
            foreach (var k in kids) {
                string kname = view.GetItemName(k);
                stack.Push((k, path + "/" + kname, depth + 1));
            }
        }
    }
}

sb.AppendLine($"=== TOP COST (all {actualSampled} frames, sorted by avgTotalMs) ===");
sb.AppendLine($"{"avgTotal",8} {"avgSelf",8} {"avgCalls",8} {"frames",6}  path");
foreach (var r in agg
    .Select(kv => new {
        path = kv.Key,
        avgTotal = kv.Value.total / actualSampled,
        avgSelf = kv.Value.self / actualSampled,
        avgCalls = (double)kv.Value.calls / actualSampled,
        frames = kv.Value.frames
    })
    .Where(r => r.avgTotal >= 0.10)
    .OrderByDescending(r => r.avgTotal)
    .Take(40))
    sb.AppendLine($"{r.avgTotal,8:F3} {r.avgSelf,8:F3} {r.avgCalls,8:F1} {r.frames,6}  {r.path}");

sb.AppendLine();
sb.AppendLine("=== TOP SELF (sorted by avgSelfMs — method-local cost) ===");
sb.AppendLine($"{"avgSelf",8} {"avgTotal",8} {"avgCalls",8}  path");
foreach (var r in agg
    .Select(kv => new {
        path = kv.Key,
        avgTotal = kv.Value.total / actualSampled,
        avgSelf = kv.Value.self / actualSampled,
        avgCalls = (double)kv.Value.calls / actualSampled
    })
    .Where(r => r.avgSelf >= 0.05)
    .OrderByDescending(r => r.avgSelf)
    .Take(30))
    sb.AppendLine($"{r.avgSelf,8:F3} {r.avgTotal,8:F3} {r.avgCalls,8:F1}  {r.path}");

// Slow vs fast split (by p75 vs p25 to see the heavy frames more clearly)
double q25 = sortedMs[sortedMs.Count / 4];
double q75 = sortedMs[(sortedMs.Count * 3) / 4];
var slowFrames = frameMs.Where(kv => kv.Value >= q75).Select(kv => kv.Key).ToList();
var fastFrames = frameMs.Where(kv => kv.Value <= q25).Select(kv => kv.Key).ToList();

Func<List<int>, Dictionary<string, (double total, double self, int frames)>> aggFn = frames => {
    var d = new Dictionary<string, (double total, double self, int frames)>();
    foreach (int f in frames) {
        using (var v = ProfilerDriver.GetHierarchyFrameDataView(
            f, 0, HierarchyFrameDataView.ViewModes.Default,
            HierarchyFrameDataView.columnTotalTime, true)) {
            if (v == null || !v.valid) continue;
            var stack = new Stack<(int id, string path, int depth)>();
            var seen = new HashSet<string>();
            var rootKids = new List<int>();
            v.GetItemChildren(v.GetRootItemID(), rootKids);
            foreach (var c in rootKids) stack.Push((c, v.GetItemName(c), 0));
            while (stack.Count > 0) {
                var (id, path, depth) = stack.Pop();
                if (depth > 8) continue;
                float total = v.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnTotalTime);
                float self  = v.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime);
                if (!d.TryGetValue(path, out var x)) x = (0, 0, 0);
                x.total += total; x.self += self;
                if (seen.Add(path)) x.frames += 1;
                d[path] = x;
                if (total < 0.05f && depth > 1) continue;
                var kids = new List<int>();
                v.GetItemChildren(id, kids);
                foreach (var k in kids) stack.Push((k, path + "/" + v.GetItemName(k), depth + 1));
            }
        }
    }
    return d;
};

var slowAgg = aggFn(slowFrames);
var fastAgg = aggFn(fastFrames);
int Ns = slowFrames.Count, Nf = fastFrames.Count;
double slowMean = slowFrames.Average(f => frameMs[f]);
double fastMean = fastFrames.Average(f => frameMs[f]);

sb.AppendLine();
sb.AppendLine($"=== SLOW (>=p75={q75:F2}ms, N={Ns}, avg={slowMean:F2}ms) vs FAST (<=p25={q25:F2}ms, N={Nf}, avg={fastMean:F2}ms) ===");
sb.AppendLine($"delta avg = {slowMean-fastMean:F2}ms");
sb.AppendLine();
sb.AppendLine("=== TOP DELTA (slowAvgMs - fastAvgMs) — what grows when slow ===");
sb.AppendLine($"{"deltaMs",8} {"slowMs",8} {"fastMs",8}  path");
var rows = new List<(string path, double slowAvg, double fastAvg, double delta)>();
foreach (var kv in slowAgg) {
    double slowAvg = kv.Value.total / Ns;
    fastAgg.TryGetValue(kv.Key, out var fv);
    double fastAvg = Nf > 0 ? fv.total / Nf : 0;
    rows.Add((kv.Key, slowAvg, fastAvg, slowAvg - fastAvg));
}
foreach (var r in rows.Where(r => r.delta >= 0.10).OrderByDescending(r => r.delta).Take(25))
    sb.AppendLine($"{r.delta,8:F3} {r.slowAvg,8:F3} {r.fastAvg,8:F3}  {r.path}");

return sb.ToString();
