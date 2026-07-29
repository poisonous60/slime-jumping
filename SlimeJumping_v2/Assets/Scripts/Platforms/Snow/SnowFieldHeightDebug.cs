using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlimeJumping.Platforms.Snow
{
    /// Diagnoses zero-height SnowField output. Mirrors RebuildNow's pipeline 1:1 (no mesh
    /// emit) and reports which stage killed the height:
    ///   0 = face-slope filter, 1 = minPatchTris, 2 = overlap mask,
    ///   3 = slopeToHeightScale curve, 4 = BFS edge taper, -1 = kept.
    /// Must stay in sync with SnowField.RebuildNow or the attribution lies.
    [ExecuteAlways]
    [RequireComponent(typeof(SnowField))]
    [AddComponentMenu("SlimeJumping/Platforms/Snow/Snow Field Height Debug")]
    public sealed class SnowFieldHeightDebug : MonoBehaviour
    {
        [Header("Output")]
        [Tooltip("Print pipeline stage counts + angle / height histograms to the Console.")]
        public bool logToConsole = true;

        [Tooltip("Also dump the worst-offender raw values (e.g. example dropped tris, example zero-scale verts) to help spot patterns.")]
        public bool logExamples = true;

        [Range(0, 20)] public int examplesPerCategory = 3;

        [Header("Gizmos")]
        [Tooltip("Draw mesh tris color-coded by which pipeline stage killed (or kept) them.")]
        public bool drawTris = true;

        [Tooltip("Draw a small sphere at each kept-patch vertex, colored from red (height 0) to green (full restThickness).")]
        public bool drawVertHeights = true;

        [Range(0.005f, 0.2f)] public float vertSphereRadius = 0.04f;

        [Range(0f, 1f)] public float keptAlpha = 0.18f;
        [Range(0f, 1f)] public float droppedAlpha = 0.55f;

        struct TriRec
        {
            public Vector3 v0, v1, v2;     // world-space triangle corners
            public float faceAngleDeg;     // face normal angle from +Y in mesh-local
            public int stage;              // 0 = face filter, 1 = minPatchTris, 2 = mask, -1 = ok
        }

        struct VertRec
        {
            public Vector3 worldPos;
            public float vertAngleDeg;     // averaged normal angle from +Y (mesh-local)
            public float slopeScale;       // slopeToHeightScale.Evaluate(vertAngleDeg)
            public float taperHeight;      // restThickness * smoothstep(hop / taperHops)
            public float finalHeight;      // taperHeight * slopeScale
            public int stage;              // 3 = slope-curve zero, 4 = taper zero, -1 = ok
        }

        [System.NonSerialized] List<TriRec> lastTris;
        [System.NonSerialized] List<VertRec> lastVerts;
        [System.NonSerialized] float lastRestThickness;

        [ContextMenu("Diagnose Height")]
        public void Diagnose()
        {
            var field = GetComponent<SnowField>();
            if (field == null) { Debug.LogWarning("[SnowFieldHeightDebug] No SnowField on this GO."); return; }
            if (field.sourceMeshFilter == null || field.sourceMeshFilter.sharedMesh == null)
            {
                Debug.LogWarning("[SnowFieldHeightDebug] SnowField has no source mesh.");
                return;
            }

            var mesh = field.sourceMeshFilter.sharedMesh;
            var meshT = field.sourceMeshFilter.transform;

            float maxSlopeDeg = field.MaxSnowSlopeDeg;
            float cosMax = Mathf.Cos(Mathf.Clamp(maxSlopeDeg, 0f, 89.99f) * Mathf.Deg2Rad);
            int minPatchTris = field.MinPatchTris;
            int subdivLevels = field.SubdivLevels;
            int taperHops = field.EdgeTaperHops;
            float restThick = field.RestThickness;
            lastRestThickness = restThick;

            var slopeCurve = field.settings != null ? field.settings.slopeToHeightScale : null;

            // ---------------- Stage 0: face-slope filter on raw mesh tris ---------
            var meshVerts = mesh.vertices;
            var meshTris = mesh.triangles;
            int totalMeshTris = meshTris.Length / 3;

            var triRecs = new List<TriRec>(totalMeshTris);
            int stage0Dropped = 0, stage0Degenerate = 0;
            // Face angle histogram (10° bins, 0..90).
            var faceAngleHist = new int[10];

            for (int t = 0; t < meshTris.Length; t += 3)
            {
                Vector3 a = meshVerts[meshTris[t]];
                Vector3 b = meshVerts[meshTris[t + 1]];
                Vector3 c = meshVerts[meshTris[t + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-12f)
                {
                    stage0Degenerate++;
                    continue;
                }
                n.Normalize();
                float angleDeg = Mathf.Acos(Mathf.Clamp(n.y, -1f, 1f)) * Mathf.Rad2Deg;
                int bin = Mathf.Clamp((int)(angleDeg / 10f), 0, 9);
                faceAngleHist[bin]++;

                bool dropped = n.y < cosMax;
                triRecs.Add(new TriRec
                {
                    v0 = meshT.TransformPoint(a),
                    v1 = meshT.TransformPoint(b),
                    v2 = meshT.TransformPoint(c),
                    faceAngleDeg = angleDeg,
                    stage = dropped ? 0 : -1,
                });
                if (dropped) stage0Dropped++;
            }

            // ---------------- Stage 1: minPatchTris on extracted patches ---------
            var patches = SnowTopSurfaceExtractor.Extract(mesh, maxSlopeDeg);

            int stage1DroppedPatches = 0, stage1KeptPatches = 0;
            int stage1DroppedTris = 0, stage1KeptTris = 0;
            int patchIdx = 0;
            var patchSizes = new List<int>(patches.Count);

            // Stages 2-4 must run on the post-subdivide patch (what RebuildNow sees), and
            // MaskOutOverlap is inlined here so each dropped tri can be attributed.
            var filter = ContactFilter2D.noFilter;
            filter.SetLayerMask(field.overlapLayerMask);
            filter.useTriggers = false;
            var hits = new Collider2D[16];
            int myId = field.hostCollider != null
                ? field.hostCollider.GetInstanceID()
                : field.GetInstanceID();
            bool maskActive = field.excludeOverlapWithOtherPlatforms && field.hostCollider != null;

            int stage2Dropped = 0, stage2Kept = 0;
            int stage3VertsZeroed = 0, stage4VertsZeroed = 0, vertsOk = 0;

            // Vertex-stage histograms.
            var vertAngleHist = new int[10]; // 0..90 deg, 10° bins
            var finalHeightHist = new int[6]; // 0, 0..0.2, 0.2..0.4, ..., >1.0 of restThick

            var vertRecs = new List<VertRec>();

            for (int s = 0; s < patches.Count; s++)
            {
                var patch = patches[s];
                if (patch.verts == null || patch.verts.Length < 3) continue;

                int triCount0 = patch.tris != null ? patch.tris.Length / 3 : 0;
                patchSizes.Add(triCount0);
                if (triCount0 < minPatchTris)
                {
                    stage1DroppedPatches++;
                    stage1DroppedTris += triCount0;
                    // Stage-1 drops get no gizmo color: patch verts are welded, so they
                    // can't be mapped back to the original mesh tris.
                    continue;
                }
                stage1KeptPatches++;
                stage1KeptTris += triCount0;
                patchIdx++;

                if (subdivLevels > 0)
                    patch = SnowPatchSubdivider.Subdivide(patch, subdivLevels);

                // ------------ Stage 2: overlap mask (per-tri, per-sample) -------
                var keepTri = new bool[patch.tris.Length / 3];
                int keptInPatch = 0;
                if (maskActive)
                {
                    int triCount = patch.tris.Length / 3;
                    for (int t = 0; t < triCount; t++)
                    {
                        int ia = patch.tris[t * 3];
                        int ib = patch.tris[t * 3 + 1];
                        int ic = patch.tris[t * 3 + 2];
                        Vector3 wa = meshT.TransformPoint(patch.verts[ia]);
                        Vector3 wb = meshT.TransformPoint(patch.verts[ib]);
                        Vector3 wc = meshT.TransformPoint(patch.verts[ic]);
                        Vector2 p0 = new Vector2(wa.x, wa.y);
                        Vector2 p1 = new Vector2(wb.x, wb.y);
                        Vector2 p2 = new Vector2(wc.x, wc.y);
                        Vector2 pc = new Vector2((wa.x + wb.x + wc.x) / 3f, (wa.y + wb.y + wc.y) / 3f);

                        bool dropTri = SamplePointHitsOther(p0, field, filter, hits, myId)
                                    || SamplePointHitsOther(p1, field, filter, hits, myId)
                                    || SamplePointHitsOther(p2, field, filter, hits, myId)
                                    || SamplePointHitsOther(pc, field, filter, hits, myId);
                        keepTri[t] = !dropTri;
                        if (dropTri) stage2Dropped++;
                        else { stage2Kept++; keptInPatch++; }
                    }
                }
                else
                {
                    for (int t = 0; t < keepTri.Length; t++) keepTri[t] = true;
                    keptInPatch = keepTri.Length;
                    stage2Kept += keepTri.Length;
                }

                if (keptInPatch == 0) continue;

                // ------------ Stage 4: BFS edge taper (matches RebuildNow) ------
                int V = patch.verts.Length;
                var taperHeight = new float[V];

                // Only verts touched by surviving tris count — else mask-dropped verts
                // report phantom heights.
                var alive = new bool[V];
                for (int t = 0; t < keepTri.Length; t++)
                {
                    if (!keepTri[t]) continue;
                    alive[patch.tris[t * 3]] = true;
                    alive[patch.tris[t * 3 + 1]] = true;
                    alive[patch.tris[t * 3 + 2]] = true;
                }

                if (taperHops > 0 && patch.boundaryEdges != null && patch.boundaryEdges.Length >= 2)
                {
                    var hop = new int[V];
                    for (int i = 0; i < V; i++) hop[i] = -1;
                    var adj = new HashSet<int>[V];
                    for (int i = 0; i < V; i++) adj[i] = new HashSet<int>();
                    // Adjacency from kept tris only — matches the post-mask taper RebuildNow runs.
                    for (int t = 0; t < keepTri.Length; t++)
                    {
                        if (!keepTri[t]) continue;
                        int a = patch.tris[t * 3];
                        int b = patch.tris[t * 3 + 1];
                        int c = patch.tris[t * 3 + 2];
                        adj[a].Add(b); adj[a].Add(c);
                        adj[b].Add(a); adj[b].Add(c);
                        adj[c].Add(a); adj[c].Add(b);
                    }
                    var queue = new Queue<int>();
                    for (int e = 0; e < patch.boundaryEdges.Length; e++)
                    {
                        int v = patch.boundaryEdges[e];
                        if (!alive[v]) continue;
                        if (hop[v] != 0) { hop[v] = 0; queue.Enqueue(v); }
                    }
                    while (queue.Count > 0)
                    {
                        int u = queue.Dequeue();
                        foreach (int n in adj[u])
                        {
                            if (hop[n] == -1)
                            {
                                hop[n] = hop[u] + 1;
                                queue.Enqueue(n);
                            }
                        }
                    }
                    for (int v = 0; v < V; v++)
                    {
                        if (!alive[v]) { taperHeight[v] = 0f; continue; }
                        int h = hop[v] < 0 ? taperHops : hop[v];
                        float ramp = Mathf.Clamp01((float)h / taperHops);
                        taperHeight[v] = restThick * Mathf.SmoothStep(0f, 1f, ramp);
                    }
                }
                else
                {
                    for (int i = 0; i < V; i++) taperHeight[i] = alive[i] ? restThick : 0f;
                }

                // ------------ Stage 3: slopeToHeightScale curve ----------------
                for (int i = 0; i < V; i++)
                {
                    if (!alive[i]) continue;
                    float ny = Mathf.Clamp(patch.vertNormals[i].y, -1f, 1f);
                    float angleDeg = Mathf.Acos(ny) * Mathf.Rad2Deg;
                    int bin = Mathf.Clamp((int)(angleDeg / 10f), 0, 9);
                    vertAngleHist[bin]++;

                    float scale = 1f;
                    if (slopeCurve != null && slopeCurve.length > 0)
                    {
                        scale = slopeCurve.Evaluate(angleDeg);
                        if (scale < 0f) scale = 0f;
                    }
                    float final = taperHeight[i] * scale;

                    int stage = -1;
                    if (final < 1e-5f)
                    {
                        // Taper wins the tie: when both zero, the hop=0 boundary is the
                        // more useful thing to highlight.
                        if (taperHeight[i] < 1e-5f) { stage4VertsZeroed++; stage = 4; }
                        else                         { stage3VertsZeroed++; stage = 3; }
                    }
                    else
                    {
                        vertsOk++;
                    }

                    if (final < 1e-5f) finalHeightHist[0]++;
                    else
                    {
                        float r = Mathf.Clamp01(final / Mathf.Max(restThick, 1e-5f));
                        int hbin = Mathf.Clamp(1 + (int)(r * 5f), 1, 5);
                        finalHeightHist[hbin]++;
                    }

                    vertRecs.Add(new VertRec
                    {
                        worldPos = meshT.TransformPoint(patch.verts[i]),
                        vertAngleDeg = angleDeg,
                        slopeScale = scale,
                        taperHeight = taperHeight[i],
                        finalHeight = final,
                        stage = stage,
                    });
                }
            }

            lastTris = triRecs;
            lastVerts = vertRecs;

            if (logToConsole)
            {
                LogSummary(field, mesh, totalMeshTris, stage0Dropped, stage0Degenerate,
                    faceAngleHist, patchSizes, stage1DroppedPatches, stage1KeptPatches,
                    stage1DroppedTris, stage1KeptTris, maskActive, stage2Dropped, stage2Kept,
                    stage3VertsZeroed, stage4VertsZeroed, vertsOk, vertAngleHist,
                    finalHeightHist, restThick, slopeCurve);

                if (logExamples) LogExamples(field);
            }

#if UNITY_EDITOR
            UnityEditor.SceneView.RepaintAll();
#endif
        }

        [ContextMenu("Clear Gizmos")]
        public void ClearGizmos()
        {
            lastTris = null;
            lastVerts = null;
#if UNITY_EDITOR
            UnityEditor.SceneView.RepaintAll();
#endif
        }

        // ---------------- Logging helpers ---------------------------------------

        void LogSummary(SnowField field, Mesh mesh, int totalMeshTris,
            int stage0Dropped, int stage0Degenerate, int[] faceAngleHist,
            List<int> patchSizes, int stage1DroppedPatches, int stage1KeptPatches,
            int stage1DroppedTris, int stage1KeptTris, bool maskActive,
            int stage2Dropped, int stage2Kept,
            int stage3VertsZeroed, int stage4VertsZeroed, int vertsOk,
            int[] vertAngleHist, int[] finalHeightHist,
            float restThick, AnimationCurve slopeCurve)
        {
            int kept = totalMeshTris - stage0Dropped - stage0Degenerate;
            float keptPct = totalMeshTris > 0 ? 100f * kept / totalMeshTris : 0f;
            Debug.Log($"[SnowFieldHeightDebug] '{field.name}'  mesh='{mesh.name}'\n" +
                      $"  Settings: maxSlope={field.MaxSnowSlopeDeg:F1}°, restThick={restThick:F3}, " +
                      $"taperHops={field.EdgeTaperHops}, subdiv={field.SubdivLevels}, " +
                      $"minPatch={field.MinPatchTris}, mask={(maskActive ? "on" : "off")}\n" +
                      $"  Stage 0 (face filter): meshTris={totalMeshTris}, " +
                      $"degenerate={stage0Degenerate}, dropped={stage0Dropped}, kept={kept} ({keptPct:F1}%)",
                      field);

            string faceHist = HistString(faceAngleHist, "°");
            Debug.Log($"  Face-normal angle histogram (mesh-local +Y):  {faceHist}", field);

            patchSizes.Sort((a, b) => b.CompareTo(a));
            string sizeStr = string.Join(", ", patchSizes.Take(8).Select(s => s.ToString()));
            if (patchSizes.Count > 8) sizeStr += $", +{patchSizes.Count - 8} more";
            Debug.Log($"  Stage 1 (minPatchTris={field.MinPatchTris}): patches={patchSizes.Count} " +
                      $"[{sizeStr}], kept patches={stage1KeptPatches} ({stage1KeptTris} tris), " +
                      $"dropped patches={stage1DroppedPatches} ({stage1DroppedTris} tris)", field);

            if (maskActive)
                Debug.Log($"  Stage 2 (overlap mask): post-subdiv tris kept={stage2Kept}, dropped={stage2Dropped}", field);
            else
                Debug.Log($"  Stage 2 (overlap mask): SKIPPED (mask off or hostCollider null)", field);

            int totalVerts = stage3VertsZeroed + stage4VertsZeroed + vertsOk;
            float vertsOkPct = totalVerts > 0 ? 100f * vertsOk / totalVerts : 0f;
            Debug.Log($"  Stage 3/4 (per-vertex height): " +
                      $"slope-curve zeroed={stage3VertsZeroed}, taper zeroed={stage4VertsZeroed}, " +
                      $"non-zero={vertsOk} ({vertsOkPct:F1}% of {totalVerts})", field);

            string vertHist = HistString(vertAngleHist, "°");
            Debug.Log($"  Vertex-normal angle histogram (mesh-local +Y):  {vertHist}", field);

            if (slopeCurve != null && slopeCurve.length > 0)
            {
                var sb = new System.Text.StringBuilder("  Slope curve at bin centers:  ");
                for (int i = 0; i < 10; i++)
                {
                    float ang = i * 10f + 5f;
                    float v = slopeCurve.Evaluate(ang);
                    sb.Append($"{(int)ang}°={v:F2}");
                    if (i < 9) sb.Append(", ");
                }
                Debug.Log(sb.ToString(), field);
            }
            else
            {
                Debug.Log("  Slope curve: null or empty (every vert keeps full taper height)", field);
            }

            string heightHist = HeightHistString(finalHeightHist, restThick);
            Debug.Log($"  Final heightRest distribution (×restThick):  {heightHist}", field);

            int worstStage = -1;
            int worstCount = 0;
            void Consider(int s, int c) { if (c > worstCount) { worstCount = c; worstStage = s; } }
            Consider(0, stage0Dropped);
            Consider(1, stage1DroppedTris);
            Consider(2, stage2Dropped);
            Consider(3, stage3VertsZeroed);
            Consider(4, stage4VertsZeroed);
            string diag = worstStage switch
            {
                0 => $"face filter dropped {worstCount} mesh tris — mesh-local +Y likely doesn't match world +Y, or maxSnowSlopeDeg too tight",
                1 => $"minPatchTris dropped {worstCount} tris worth of small components — bump minPatchTris down or check connectivity",
                2 => $"overlap mask dropped {worstCount} tris — run SnowMaskDebug for the offending collider",
                3 => $"slopeToHeightScale curve zeroed {worstCount} verts — soften / extend the curve, or flatten the mesh",
                4 => $"BFS taper zeroed {worstCount} boundary verts — expected at edges; only worrying if the patch interior is also empty",
                _ => "no obvious dominant kill stage",
            };
            Debug.Log($"  → Most damage at stage {worstStage}: {diag}", field);
        }

        void LogExamples(SnowField field)
        {
            if (lastTris == null || lastVerts == null) return;

            // Stage-0 angles are the fastest way to tell if mesh-local up is wrong.
            int shown = 0;
            for (int i = 0; i < lastTris.Count && shown < examplesPerCategory; i++)
            {
                var r = lastTris[i];
                if (r.stage != 0) continue;
                Vector3 c = (r.v0 + r.v1 + r.v2) / 3f;
                Debug.Log($"    stage0 example: tri@({c.x:F2},{c.y:F2},{c.z:F2}) faceAngle={r.faceAngleDeg:F1}°", field);
                shown++;
            }

            shown = 0;
            for (int i = 0; i < lastVerts.Count && shown < examplesPerCategory; i++)
            {
                var v = lastVerts[i];
                if (v.stage != 3) continue;
                Debug.Log($"    stage3 example: vert@({v.worldPos.x:F2},{v.worldPos.y:F2},{v.worldPos.z:F2}) " +
                          $"vertAngle={v.vertAngleDeg:F1}° → slopeScale={v.slopeScale:F3}, taper={v.taperHeight:F3}", field);
                shown++;
            }
        }

        static string HistString(int[] bins, string unit)
        {
            // 10 bins × 10° each. Print non-zero bins so it's readable.
            int total = 0;
            for (int i = 0; i < bins.Length; i++) total += bins[i];
            if (total == 0) return "(empty)";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < bins.Length; i++)
            {
                if (bins[i] == 0) continue;
                int lo = i * 10, hi = (i + 1) * 10;
                float pct = 100f * bins[i] / total;
                sb.Append($"[{lo}-{hi}{unit}] {bins[i]} ({pct:F0}%)  ");
            }
            return sb.ToString().TrimEnd();
        }

        static string HeightHistString(int[] bins, float restThick)
        {
            int total = 0;
            for (int i = 0; i < bins.Length; i++) total += bins[i];
            if (total == 0) return "(empty)";
            var sb = new System.Text.StringBuilder();
            string[] labels = { "0", "0–0.2", "0.2–0.4", "0.4–0.6", "0.6–0.8", "0.8–1.0+" };
            for (int i = 0; i < bins.Length; i++)
            {
                if (bins[i] == 0) continue;
                float pct = 100f * bins[i] / total;
                sb.Append($"[{labels[i]}] {bins[i]} ({pct:F0}%)  ");
            }
            return sb.ToString().TrimEnd();
        }

        // Mirrors SnowField.SamplePointHitsOther exactly.
        static bool SamplePointHitsOther(Vector2 worldXY, SnowField field,
            ContactFilter2D filter, Collider2D[] hits, int myId)
        {
            int n = Physics2D.OverlapPoint(worldXY, filter, hits);
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (h == null) continue;
                if (h == field.hostCollider) continue;
                if (SnowFieldRegistry.TryGet(h, out var otherField) && otherField != null && otherField != field)
                {
                    if (otherField.overlapPriority > field.overlapPriority) return true;
                    if (otherField.overlapPriority < field.overlapPriority) continue;
                    if (h.GetInstanceID() > myId) return true;
                    continue;
                }
                return true;
            }
            return false;
        }

        // ---------------- Gizmos ------------------------------------------------

        static readonly Color ColFaceFilter = new Color(1f, 0.25f, 0.25f, 1f);   // stage 0 (red)
        static readonly Color ColMinPatch   = new Color(1f, 0.55f, 0.1f, 1f);    // stage 1 (orange)
        static readonly Color ColMask       = new Color(1f, 0.95f, 0.1f, 1f);    // stage 2 (yellow)
        static readonly Color ColKept       = new Color(0.2f, 1f, 0.3f, 1f);     // kept (green)

        void OnDrawGizmosSelected()
        {
            if (drawTris && lastTris != null)
            {
                for (int i = 0; i < lastTris.Count; i++)
                {
                    var r = lastTris[i];
                    Color c = r.stage == 0 ? ColFaceFilter
                            : r.stage == 1 ? ColMinPatch
                            : r.stage == 2 ? ColMask
                            : ColKept;
                    c.a = r.stage == -1 ? keptAlpha : droppedAlpha;
                    Gizmos.color = c;
                    Gizmos.DrawLine(r.v0, r.v1);
                    Gizmos.DrawLine(r.v1, r.v2);
                    Gizmos.DrawLine(r.v2, r.v0);
                }
            }

            if (drawVertHeights && lastVerts != null)
            {
                float rest = Mathf.Max(lastRestThickness, 1e-5f);
                for (int i = 0; i < lastVerts.Count; i++)
                {
                    var v = lastVerts[i];
                    float r = Mathf.Clamp01(v.finalHeight / rest);
                    // red (zero) → green (full).
                    Gizmos.color = new Color(1f - r, r, 0f, 0.95f);
                    Gizmos.DrawSphere(v.worldPos, vertSphereRadius);
                }
            }
        }
    }
}
