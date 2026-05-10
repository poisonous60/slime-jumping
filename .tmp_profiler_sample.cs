int first = ProfilerDriver.firstFrameIndex;
int last = ProfilerDriver.lastFrameIndex;
int count = last - first + 1;
if (count <= 0) return "no frames";

int sampleN = Math.Min(30, count);
int startFrame = last - sampleN + 1;

// path -> (sumTotalMs, sumCalls, framesSeen)
var agg = new Dictionary<string, (double total, double self, int calls, int frames)>();

int actualSampled = 0;
double frameTotalMsSum = 0;

for (int f = startFrame; f <= last; f++) {
    using (var view = ProfilerDriver.GetHierarchyFrameDataView(
        f, 0,
        HierarchyFrameDataView.ViewModes.Default,
        HierarchyFrameDataView.columnTotalTime,
        true)) {
        if (view == null || !view.valid) continue;
        actualSampled++;

        int root = view.GetRootItemID();
        var stack = new Stack<(int id, string path, int depth)>();

        var rootChildren = new List<int>();
        view.GetItemChildren(root, rootChildren);
        double frameTotalMs = 0;
        foreach (var c in rootChildren) {
            stack.Push((c, view.GetItemName(c), 0));
            frameTotalMs += view.GetItemColumnDataAsFloat(c, HierarchyFrameDataView.columnTotalTime);
        }
        frameTotalMsSum += frameTotalMs;

        var seenInFrame = new HashSet<string>();

        while (stack.Count > 0) {
            var (id, path, depth) = stack.Pop();
            if (depth > 6) continue;

            float total = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnTotalTime);
            float self = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime);
            float calls = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnCalls);

            if (!agg.TryGetValue(path, out var v)) v = (0, 0, 0, 0);
            v.total += total;
            v.self += self;
            v.calls += (int)calls;
            if (seenInFrame.Add(path)) v.frames += 1;
            agg[path] = v;

            // recurse only if total >= 0.05 ms to limit explosion
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

if (actualSampled == 0) return "no valid frames sampled";

// Build report sorted by avg self time descending
var rows = agg
    .Select(kv => new {
        path = kv.Key,
        avgTotal = kv.Value.total / actualSampled,
        avgSelf = kv.Value.self / actualSampled,
        avgCalls = (double)kv.Value.calls / actualSampled,
        frames = kv.Value.frames
    })
    .Where(r => r.avgSelf >= 0.02 || r.avgTotal >= 0.10)
    .OrderByDescending(r => r.avgSelf)
    .Take(40)
    .ToList();

var sb = new System.Text.StringBuilder();
sb.AppendLine($"sampled {actualSampled} frames (range {startFrame}..{last}), avg frameTotal={frameTotalMsSum/actualSampled:F2}ms");
sb.AppendLine($"top by avgSelfMs (path | avgSelf | avgTotal | avgCalls | seenFrames):");
foreach (var r in rows) {
    sb.AppendLine($"  {r.avgSelf,7:F3} | {r.avgTotal,7:F3} | {r.avgCalls,6:F1} | {r.frames,3}/{actualSampled} | {r.path}");
}
return sb.ToString();
