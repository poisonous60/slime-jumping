// Splits a loaded profiler capture's frames into "fast" vs "slow" buckets
// (median frameTotal ms split), aggregates hierarchy per bucket, and prints
// the methods whose avgTotal grew most in slow vs fast.
//
// Run AFTER ProfilerDriver.LoadProfile has populated frames.

int first = ProfilerDriver.firstFrameIndex;
int last  = ProfilerDriver.lastFrameIndex;
if (last < first) return "no frames loaded";

// 1) compute per-frame total ms
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

// 2) median split
var sorted = frameMs.Values.OrderBy(x => x).ToList();
double median = sorted[sorted.Count / 2];
var slowFrames = frameMs.Where(kv => kv.Value > median).Select(kv => kv.Key).ToList();
var fastFrames = frameMs.Where(kv => kv.Value <= median).Select(kv => kv.Key).ToList();

// 3) aggregator
Func<List<int>, Dictionary<string, (double total, double self, int frames)>> agg = frames => {
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
            foreach (var c in rootKids)
                stack.Push((c, v.GetItemName(c), 0));
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
                foreach (var k in kids)
                    stack.Push((k, path + "/" + v.GetItemName(k), depth + 1));
            }
        }
    }
    return d;
};

var slowAgg = agg(slowFrames);
var fastAgg = agg(fastFrames);
int Ns = slowFrames.Count, Nf = fastFrames.Count;
if (Ns == 0 || Nf == 0) return $"split failed: slow={Ns} fast={Nf} median={median:F2}ms";

// 4) compute delta for paths present in both
var rows = new List<(string path, double slowAvg, double fastAvg, double delta, double slowSelf, double fastSelf)>();
foreach (var kv in slowAgg) {
    double slowAvg = kv.Value.total / Ns;
    fastAgg.TryGetValue(kv.Key, out var fv);
    double fastAvg = Nf > 0 ? fv.total / Nf : 0;
    double slowSelf = kv.Value.self / Ns;
    double fastSelf = Nf > 0 ? fv.self / Nf : 0;
    rows.Add((kv.Key, slowAvg, fastAvg, slowAvg - fastAvg, slowSelf, fastSelf));
}

var sb = new System.Text.StringBuilder();
double slowMean = slowFrames.Average(f => frameMs[f]);
double fastMean = fastFrames.Average(f => frameMs[f]);
sb.AppendLine($"frames: {first}..{last} ({frameMs.Count} valid)");
sb.AppendLine($"split @ median={median:F2}ms  fast=N{Nf} avg{fastMean:F2}ms  slow=N{Ns} avg{slowMean:F2}ms  delta={slowMean-fastMean:F2}ms");
sb.AppendLine();
sb.AppendLine("=== TOP DELTA (slowAvgMs - fastAvgMs) — what grew when slow ===");
sb.AppendLine($"{"deltaMs",8} {"slowMs",8} {"fastMs",8} {"slowSelf",9} {"fastSelf",9}  path");
foreach (var r in rows
    .Where(r => r.delta >= 0.05)
    .OrderByDescending(r => r.delta)
    .Take(30))
    sb.AppendLine($"{r.delta,8:F3} {r.slowAvg,8:F3} {r.fastAvg,8:F3} {r.slowSelf,9:F3} {r.fastSelf,9:F3}  {r.path}");

sb.AppendLine();
sb.AppendLine("=== TOP DELTA by SELF time (method-local cost growth) ===");
sb.AppendLine($"{"deltaSlf",8} {"slowSlf",8} {"fastSlf",8}  path");
foreach (var r in rows
    .Select(r => (r.path, deltaSelf: r.slowSelf - r.fastSelf, r.slowSelf, r.fastSelf))
    .Where(r => r.deltaSelf >= 0.03)
    .OrderByDescending(r => r.deltaSelf)
    .Take(30))
    sb.AppendLine($"{r.deltaSelf,8:F3} {r.slowSelf,8:F3} {r.fastSelf,8:F3}  {r.path}");

return sb.ToString();
