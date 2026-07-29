using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    /// <summary>Designer-authored nav graph the GraniteBlast emitter A*s over per fire. Nodes are transform-local, edges undirected; sized for &lt; 100 nodes.</summary>
    public class GraniteBlastNavGraph : MonoBehaviour
    {
        [Tooltip("Local-space node positions. Z is honored — set non-zero Z to make A* paths swerve in depth.")]
        [SerializeField] private Vector3[] nodes = new Vector3[0];
        [Tooltip("Edges as (a, b) index pairs into nodes[]. Undirected — each pair is traversable both ways. Self-loops and out-of-range indices are ignored.")]
        [SerializeField] private Vector2Int[] edges = new Vector2Int[0];

        [Header("Gizmos")]
        [SerializeField] private Color nodeColor = new Color(0.4f, 0.85f, 1f, 0.9f);
        [SerializeField] private Color edgeColor = new Color(0.4f, 0.85f, 1f, 0.5f);
        [Tooltip("Color used for edges that fail edit-time LOS validation (Linecast hits a collider on edgeValidationMask). Red by default — designer's signal that the edge passes through a wall and should be re-routed.")]
        [SerializeField] private Color blockedEdgeColor = new Color(1f, 0.25f, 0.2f, 0.9f);
        [SerializeField] private float nodeGizmoRadius = 0.18f;

        [Header("Edit-time Edge Validation")]
        [Tooltip("Layers used to flag edges that pass through walls (red gizmo). Default = Environment (layer 7). Set to 0 to disable edit-time validation. This is independent from the emitter's runtime navWallMask — it's purely an authoring aid.")]
        [SerializeField] private LayerMask edgeValidationMask = (1 << 7);

        // --- adjacency cache ---
        private List<int>[] adjacency;
        private bool adjacencyDirty = true;

        public int NodeCount => nodes != null ? nodes.Length : 0;

        public Vector3 GetWorldNode(int i) => transform.TransformPoint(nodes[i]);

        private void RebuildAdjacency()
        {
            int n = NodeCount;
            adjacency = new List<int>[n];
            for (int i = 0; i < n; i++) adjacency[i] = new List<int>(4);
            if (edges != null)
            {
                for (int i = 0; i < edges.Length; i++)
                {
                    int a = edges[i].x;
                    int b = edges[i].y;
                    if (a < 0 || b < 0 || a >= n || b >= n || a == b) continue;
                    if (!adjacency[a].Contains(b)) adjacency[a].Add(b);
                    if (!adjacency[b].Contains(a)) adjacency[b].Add(a);
                }
            }
            adjacencyDirty = false;
        }

        /// <summary>Closest node to a world point (full Vector3 distance), or -1 if empty. No LOS check — see ClosestVisibleNode.</summary>
        public int ClosestNodeIndex(Vector3 world)
        {
            int n = NodeCount;
            if (n == 0) return -1;
            int best = 0;
            float bestSqr = (GetWorldNode(0) - world).sqrMagnitude;
            for (int i = 1; i < n; i++)
            {
                float d = (GetWorldNode(i) - world).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = i; }
            }
            return best;
        }

        /// <summary>Closest node with clear LOS on wallMask, or -1 if none (caller falls back to direct fire). wallMask=0 degrades to ClosestNodeIndex.</summary>
        public int ClosestVisibleNode(Vector3 world, LayerMask wallMask)
        {
            int n = NodeCount;
            if (n == 0) return -1;
            if (wallMask == 0) return ClosestNodeIndex(world);

            int best = -1;
            float bestSqr = float.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                Vector3 wp = GetWorldNode(i);
                float d = (wp - world).sqrMagnitude;
                if (d >= bestSqr) continue; // can't improve
                // Caller contract: wallMask must be wall layers only, never the slime layer.
                var hit = Physics2D.Linecast(world, wp, wallMask);
                if (hit.collider != null) continue;
                bestSqr = d;
                best = i;
            }
            return best;
        }

        /// <summary>Random LOS-visible node within (1 + radiusTolerance) × nearestDist; radiusTolerance=0 returns ClosestVisibleNode.</summary>
        public int RandomVisibleNode(Vector3 world, LayerMask wallMask, float radiusTolerance)
        {
            int n = NodeCount;
            if (n == 0) return -1;
            if (radiusTolerance <= 0f) return ClosestVisibleNode(world, wallMask);

            EnsureNodeScratch(n);
            float bestSqr = float.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                Vector3 wp = GetWorldNode(i);
                float d = (wp - world).sqrMagnitude;
                bool visible = wallMask == 0
                    || Physics2D.Linecast(world, wp, wallMask).collider == null;
                nodeVisible[i] = visible;
                nodeDistSqr[i] = d;
                if (visible && d < bestSqr) bestSqr = d;
            }
            if (float.IsPositiveInfinity(bestSqr)) return -1;

            float scale = 1f + radiusTolerance;
            float threshold = bestSqr * scale * scale;

            // Reservoir sampling — pick uniformly among eligible candidates.
            int picked = -1;
            int count = 0;
            for (int i = 0; i < n; i++)
            {
                if (!nodeVisible[i]) continue;
                if (nodeDistSqr[i] > threshold) continue;
                count++;
                if (Random.Range(0, count) == 0) picked = i;
            }
            return picked;
        }

        private bool[] nodeVisible;
        private float[] nodeDistSqr;
        private void EnsureNodeScratch(int n)
        {
            if (nodeVisible == null || nodeVisible.Length < n)
            {
                nodeVisible = new bool[n];
                nodeDistSqr = new float[n];
            }
        }

        /// <summary>True if a straight a→b Linecast on wallMask is unobstructed (caller may skip graph routing). wallMask=0 always returns false.</summary>
        public bool IsDirectPathClear(Vector3 a, Vector3 b, LayerMask wallMask)
        {
            if (wallMask == 0) return false;
            return Physics2D.Linecast(a, b, wallMask).collider == null;
        }

        /// <summary>A* between the LOS-clear nodes nearest worldStart/worldEnd; appends worldStart, nav nodes, worldEnd. False (outPath untouched) if empty, no LOS node, or disconnected.</summary>
        public bool FindPath(Vector3 worldStart, Vector3 worldEnd, List<Vector3> outPath, LayerMask wallMask)
            => FindPath(worldStart, worldEnd, outPath, wallMask, 0f, 0f);

        /// <summary>Stochastic FindPath: costJitter (0..1) randomizes per-edge cost, entryRadiusJitter (0..1) randomizes the entry/exit node. Both 0 = deterministic.</summary>
        public bool FindPath(Vector3 worldStart, Vector3 worldEnd, List<Vector3> outPath,
                             LayerMask wallMask, float costJitter, float entryRadiusJitter)
        {
            if (NodeCount == 0 || outPath == null) return false;
            if (adjacencyDirty || adjacency == null || adjacency.Length != NodeCount)
                RebuildAdjacency();

            int startIdx, endIdx;
            if (entryRadiusJitter > 0f && wallMask != 0)
            {
                startIdx = RandomVisibleNode(worldStart, wallMask, entryRadiusJitter);
                endIdx = RandomVisibleNode(worldEnd, wallMask, entryRadiusJitter);
            }
            else
            {
                startIdx = wallMask == 0 ? ClosestNodeIndex(worldStart) : ClosestVisibleNode(worldStart, wallMask);
                endIdx = wallMask == 0 ? ClosestNodeIndex(worldEnd) : ClosestVisibleNode(worldEnd, wallMask);
            }
            if (startIdx < 0 || endIdx < 0) return false;

            int n = NodeCount;
            // Scratch reused per call — allocation dominates A* on small graphs.
            EnsureScratch(n);

            for (int i = 0; i < n; i++)
            {
                gScore[i] = float.PositiveInfinity;
                cameFrom[i] = -1;
                inOpen[i] = false;
                closed[i] = false;
            }

            Vector3 endWorld = GetWorldNode(endIdx);
            gScore[startIdx] = 0f;
            float h0 = (GetWorldNode(startIdx) - endWorld).magnitude;
            openHeapCount = 0;
            HeapPush(startIdx, h0);
            inOpen[startIdx] = true;

            bool found = false;
            while (openHeapCount > 0)
            {
                int current = HeapPop();
                inOpen[current] = false;
                if (current == endIdx) { found = true; break; }
                closed[current] = true;

                Vector3 cwp = GetWorldNode(current);
                var nbrs = adjacency[current];
                for (int k = 0; k < nbrs.Count; k++)
                {
                    int nb = nbrs[k];
                    if (closed[nb]) continue;
                    float edgeCost = (GetWorldNode(nb) - cwp).magnitude;
                    if (costJitter > 0f)
                        edgeCost *= 1f + Random.Range(-costJitter, costJitter);
                    float tentative = gScore[current] + edgeCost;
                    if (tentative < gScore[nb])
                    {
                        cameFrom[nb] = current;
                        gScore[nb] = tentative;
                        float h = (GetWorldNode(nb) - endWorld).magnitude;
                        HeapPush(nb, tentative + h);
                        inOpen[nb] = true;
                    }
                }
            }

            if (!found) return false;

            scratchPath.Clear();
            int cur = endIdx;
            while (cur != -1)
            {
                scratchPath.Add(GetWorldNode(cur));
                cur = cameFrom[cur];
            }

            outPath.Add(worldStart);
            for (int i = scratchPath.Count - 1; i >= 0; i--)
            {
                outPath.Add(scratchPath[i]);
            }
            outPath.Add(worldEnd);
            return true;
        }

        // --- A* scratch ---
        private float[] gScore;
        private int[] cameFrom;
        private bool[] inOpen;
        private bool[] closed;
        private readonly List<Vector3> scratchPath = new List<Vector3>(32);

        // Binary min-heap; duplicate pushes are tolerated — the closed[] guard rejects stale entries.
        private int[] heapIdx;
        private float[] heapKey;
        private int openHeapCount;

        private void EnsureScratch(int n)
        {
            if (gScore == null || gScore.Length < n)
            {
                gScore = new float[n];
                cameFrom = new int[n];
                inOpen = new bool[n];
                closed = new bool[n];
                heapIdx = new int[Mathf.Max(8, n * 4)];
                heapKey = new float[Mathf.Max(8, n * 4)];
            }
        }

        private void HeapPush(int idx, float key)
        {
            if (openHeapCount >= heapIdx.Length)
            {
                int newSize = heapIdx.Length * 2;
                System.Array.Resize(ref heapIdx, newSize);
                System.Array.Resize(ref heapKey, newSize);
            }
            int i = openHeapCount++;
            heapIdx[i] = idx;
            heapKey[i] = key;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (heapKey[parent] <= heapKey[i]) break;
                Swap(parent, i);
                i = parent;
            }
        }

        private int HeapPop()
        {
            int top = heapIdx[0];
            openHeapCount--;
            if (openHeapCount > 0)
            {
                heapIdx[0] = heapIdx[openHeapCount];
                heapKey[0] = heapKey[openHeapCount];
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1;
                    int r = l + 1;
                    int smallest = i;
                    if (l < openHeapCount && heapKey[l] < heapKey[smallest]) smallest = l;
                    if (r < openHeapCount && heapKey[r] < heapKey[smallest]) smallest = r;
                    if (smallest == i) break;
                    Swap(smallest, i);
                    i = smallest;
                }
            }
            return top;
        }

        private void Swap(int a, int b)
        {
            int ti = heapIdx[a]; heapIdx[a] = heapIdx[b]; heapIdx[b] = ti;
            float tk = heapKey[a]; heapKey[a] = heapKey[b]; heapKey[b] = tk;
        }

        private void OnValidate()
        {
            adjacencyDirty = true;
            edgeValidationDirty = true;
        }

        // --- Edit-time edge validation cache ---
        // Authoring aid only, recomputed lazily in OnDrawGizmos. Runtime A* ignores it and
        // does its own per-call LOS gating against the emitter's navWallMask.
        [System.NonSerialized] private bool[] edgeBlocked;
        [System.NonSerialized] private bool edgeValidationDirty = true;

        private void RevalidateEdges()
        {
            int e = edges != null ? edges.Length : 0;
            if (edgeBlocked == null || edgeBlocked.Length != e) edgeBlocked = new bool[e];
            if (edgeValidationMask == 0)
            {
                for (int i = 0; i < e; i++) edgeBlocked[i] = false;
                edgeValidationDirty = false;
                return;
            }
            int n = NodeCount;
            for (int i = 0; i < e; i++)
            {
                var pair = edges[i];
                if (pair.x < 0 || pair.y < 0 || pair.x >= n || pair.y >= n || pair.x == pair.y)
                {
                    edgeBlocked[i] = false;
                    continue;
                }
                Vector3 a = GetWorldNode(pair.x);
                Vector3 b = GetWorldNode(pair.y);
                edgeBlocked[i] = Physics2D.Linecast(a, b, edgeValidationMask).collider != null;
            }
            edgeValidationDirty = false;
        }

        private void OnDrawGizmos()
        {
            if (nodes == null) return;
            if (edgeValidationDirty) RevalidateEdges();

            if (edges != null)
            {
                for (int i = 0; i < edges.Length; i++)
                {
                    int a = edges[i].x;
                    int b = edges[i].y;
                    if (a < 0 || b < 0 || a >= nodes.Length || b >= nodes.Length) continue;
                    bool blocked = edgeBlocked != null && i < edgeBlocked.Length && edgeBlocked[i];
                    Gizmos.color = blocked ? blockedEdgeColor : edgeColor;
                    Gizmos.DrawLine(transform.TransformPoint(nodes[a]),
                                    transform.TransformPoint(nodes[b]));
                }
            }
            Gizmos.color = nodeColor;
            for (int i = 0; i < nodes.Length; i++)
            {
                Gizmos.DrawWireSphere(transform.TransformPoint(nodes[i]), nodeGizmoRadius);
            }
        }
    }
}
