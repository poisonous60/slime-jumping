// Survey scene's 2D colliders: counts by type, parent grouping, and largest groups.
// Helps identify if the "curve" is one large collider or many small pieces.

var allColliders = UnityEngine.Object.FindObjectsByType<Collider2D>(
    FindObjectsInactive.Exclude, FindObjectsSortMode.None);

int total = allColliders.Length;
var byType = allColliders.GroupBy(c => c.GetType().Name)
    .Select(g => (type: g.Key, count: g.Count()))
    .OrderByDescending(x => x.count)
    .ToList();

// Group by top-level scene parent (the scene root child name) to find clusters
var byRoot = allColliders.GroupBy(c => {
    var t = c.transform;
    while (t.parent != null) t = t.parent;
    return t.name;
}).Select(g => (root: g.Key, count: g.Count(),
                polyCount: g.Count(c => c is PolygonCollider2D),
                edgeCount: g.Count(c => c is EdgeCollider2D),
                avgPolyVerts: g.OfType<PolygonCollider2D>().Any()
                    ? g.OfType<PolygonCollider2D>().Average(p => p.GetTotalPointCount())
                    : 0.0))
.OrderByDescending(g => g.count)
.Take(20)
.ToList();

// Find the largest single PolygonCollider2D by vertex count, and biggest "cluster"
// (any GameObject hierarchy with many polygon children)
var polyByImmediateParent = allColliders.OfType<PolygonCollider2D>()
    .GroupBy(p => p.transform.parent != null ? p.transform.parent.name : "(root)")
    .Select(g => (parent: g.Key, count: g.Count(),
                  totalVerts: g.Sum(p => p.GetTotalPointCount()),
                  paths: g.Sum(p => p.pathCount)))
    .OrderByDescending(x => x.count)
    .Take(15)
    .ToList();

var sb = new System.Text.StringBuilder();
sb.AppendLine($"=== Scene Collider2D survey ===");
sb.AppendLine($"Total Collider2D: {total}");
sb.AppendLine();
sb.AppendLine("By type:");
foreach (var t in byType) sb.AppendLine($"  {t.count,5} {t.type}");
sb.AppendLine();
sb.AppendLine("By scene-root parent (top 20):");
sb.AppendLine($"  {"count",5} {"poly",5} {"edge",5} {"avgVerts",10}  rootName");
foreach (var r in byRoot)
    sb.AppendLine($"  {r.count,5} {r.polyCount,5} {r.edgeCount,5} {r.avgPolyVerts,10:F1}  {r.root}");
sb.AppendLine();
sb.AppendLine("PolygonCollider2D by immediate parent (top 15):");
sb.AppendLine($"  {"count",5} {"verts",6} {"paths",5}  parentName");
foreach (var p in polyByImmediateParent)
    sb.AppendLine($"  {p.count,5} {p.totalVerts,6} {p.paths,5}  {p.parent}");

return sb.ToString();
