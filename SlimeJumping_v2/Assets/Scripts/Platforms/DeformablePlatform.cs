using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Platforms
{
    /// <summary>
    /// Clay/mud pillar: two vertical chains (left + right) of control points share a
    /// fixed Y ladder. Each level stores a leftX and rightX. Slime impacts push the
    /// struck side's X inward along the horizontal axis. Where the two sides cross
    /// (leftX ≥ rightX) the level has no material — the pillar splits at that Y and
    /// the slime passes through. Each connected vertical run of valid levels becomes
    /// a separate mesh island and a separate PolygonCollider2D path.
    ///
    /// Slime falls below the platform → all chains lerp back to rest.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(PolygonCollider2D))]
    public class DeformablePlatform : MonoBehaviour
    {
        [System.Serializable]
        public struct RestLevel
        {
            [Tooltip("Y position of this level (local).")]
            public float y;
            [Tooltip("Left boundary X at rest (local).")]
            public float leftX;
            [Tooltip("Right boundary X at rest (local).")]
            public float rightX;
        }

        [Header("Shape (authored, top → bottom)")]
        [Tooltip("Each entry is one Y level. First and last are treated as caps (do not deform). " +
                 "Use the component context menu (gear icon) to generate presets, or drag scene handles.")]
        [SerializeField] List<RestLevel> restLevels = new List<RestLevel>();

        [Header("Generator Defaults (used by context-menu presets only)")]
        [Min(0.1f)] [SerializeField] float genWidth = 1.5f;
        [Min(0.1f)] [SerializeField] float genHeight = 7f;
        [Min(3)]    [SerializeField] int   genSegmentCount = 22;

        [Header("Deformation")]
        [Tooltip("Meters-pushed per m/s of slime impact speed.")]
        [Min(0f)] [SerializeField] float impactToDisplacement = 0.04f;
        [Tooltip("Hard cap on how far a single hit can push any level (local units). " +
                 "Keep below the slime's surface-probe range or the slime will detach mid-dent.")]
        [Min(0.001f)] [SerializeField] float maxPerHitDisplacement = 0.12f;
        [Tooltip("Y distance (local units) over which neighboring levels also receive a weakened push.")]
        [Min(0.001f)] [SerializeField] float falloffYRange = 0.8f;
        [Tooltip("Additional push applied continuously while the slime is attached (per second).")]
        [Min(0f)] [SerializeField] float attachedCreepPerSec = 0f;

        [Header("3D Extrusion")]
        [Min(0.01f)] [SerializeField] float depth = 0.8f;

        [Header("Reset")]
        [Tooltip("Slime Y distance below the platform bounds that triggers reset.")]
        [Min(0f)] [SerializeField] float fallBelowMargin = 1.5f;
        [Tooltip("Seconds for all levels to lerp back to rest.")]
        [Min(0.01f)] [SerializeField] float resetDuration = 0.6f;

        [Header("Debug")]
        [SerializeField] bool drawGizmos = true;

        // --- Runtime state ---------------------------------------------------

        struct Level
        {
            public float y;
            public float leftRestX, rightRestX;
            public float leftCurX, rightCurX;
            public bool  isCap; // top & bottom — don't deform so corners stay anchored.
        }

        Level[] levels;
        bool dirty;

        Mesh mesh;
        MeshFilter meshFilter;
        PolygonCollider2D poly;

        bool      resetting;
        float     resetTimer;
        float[]   resetFromLeft;
        float[]   resetFromRight;

        SlimeController slime;
        bool wasAttachedToMe;

        // Scratch lists / buffers reused per rebuild (avoid per-frame GC).
        readonly List<Vector2>    scratchOutline    = new List<Vector2>();
        readonly List<int>        scratchEarIdx     = new List<int>();
        readonly List<int>        scratchEarTris    = new List<int>();
        readonly List<Vector3>    scratchVerts      = new List<Vector3>();
        readonly List<Vector3>    scratchNorms      = new List<Vector3>();
        readonly List<Vector2>    scratchUVs        = new List<Vector2>();
        readonly List<int>        scratchTris       = new List<int>();
        readonly List<Vector2[]>  scratchPiecePaths = new List<Vector2[]>();

        // --- Unity ----------------------------------------------------------

        void Reset()
        {
            if (levels == null || levels.Length < 3) BuildRuntime();
        }

        void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            poly = GetComponent<PolygonCollider2D>();
            BuildRuntime();
        }

        void FixedUpdate()
        {
            if (levels == null || levels.Length < 3) return;
            if (slime == null)
            {
                slime = FindAnyObjectByType<SlimeController>();
                if (slime == null) return;
            }

            bool attachedToMe = slime.CurrentState == SlimeController.SlimeState.Attached
                                && slime.SurfaceCollider == poly;

            if (attachedToMe && !wasAttachedToMe)
                ApplyImpactAt(slime.Position, slime.ImpactNormalSpeed);
            else if (attachedToMe && attachedCreepPerSec > 0f)
                ApplyImpactAt(slime.Position, attachedCreepPerSec * Time.fixedDeltaTime);

            wasAttachedToMe = attachedToMe;

            if (!resetting)
            {
                float localSlimeY = transform.InverseTransformPoint(slime.Position).y;
                // levels[] is sorted top→bottom, so last level is the lowest authored Y.
                float bottomY = levels[levels.Length - 1].y;
                if (localSlimeY < bottomY - fallBelowMargin)
                    BeginReset();
            }

            if (resetting) TickReset(Time.fixedDeltaTime);
        }

        void LateUpdate()
        {
            if (dirty) { Rebuild(); dirty = false; }
        }

        // --- Build ----------------------------------------------------------

        [ContextMenu("Rebuild Runtime")]
        public void BuildRuntime()
        {
            if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
            if (poly == null)       poly       = GetComponent<PolygonCollider2D>();

            if (restLevels == null || restLevels.Count < 3)
                GenerateRectangle(); // seed an initial shape so a brand-new component isn't empty

            // Author order may be messy; enforce top→bottom (Y descending).
            restLevels.Sort((a, b) => b.y.CompareTo(a.y));

            int n = restLevels.Count;
            levels = new Level[n];
            for (int i = 0; i < n; i++)
            {
                levels[i].y           = restLevels[i].y;
                levels[i].leftRestX   = restLevels[i].leftX;
                levels[i].rightRestX  = restLevels[i].rightX;
                levels[i].leftCurX    = restLevels[i].leftX;
                levels[i].rightCurX   = restLevels[i].rightX;
                levels[i].isCap       = (i == 0 || i == n - 1);
            }

            Rebuild();
        }

        // --- Shape generators (context menu) --------------------------------

        [ContextMenu("Generate/Rectangle")]
        public void GenerateRectangle()
        {
            if (restLevels == null) restLevels = new List<RestLevel>();
            restLevels.Clear();
            int n = Mathf.Max(3, genSegmentCount);
            float hw = genWidth * 0.5f, hh = genHeight * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float y = Mathf.Lerp(hh, -hh, t);
                restLevels.Add(new RestLevel { y = y, leftX = -hw, rightX = +hw });
            }
        }

        [ContextMenu("Generate/Taper (narrow top)")]
        public void GenerateTaper()
        {
            if (restLevels == null) restLevels = new List<RestLevel>();
            restLevels.Clear();
            int n = Mathf.Max(3, genSegmentCount);
            float hw = genWidth * 0.5f, hh = genHeight * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float y = Mathf.Lerp(hh, -hh, t);
                float w = Mathf.Lerp(hw * 0.05f, hw, t);
                restLevels.Add(new RestLevel { y = y, leftX = -w, rightX = +w });
            }
        }

        [ContextMenu("Generate/Hourglass")]
        public void GenerateHourglass()
        {
            if (restLevels == null) restLevels = new List<RestLevel>();
            restLevels.Clear();
            int n = Mathf.Max(3, genSegmentCount);
            float hw = genWidth * 0.5f, hh = genHeight * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float y = Mathf.Lerp(hh, -hh, t);
                float narrowness = Mathf.Abs(t - 0.5f) * 2f; // 0 at middle, 1 at ends
                float w = Mathf.Lerp(hw * 0.2f, hw, narrowness);
                restLevels.Add(new RestLevel { y = y, leftX = -w, rightX = +w });
            }
        }

        [ContextMenu("Generate/Dome Top")]
        public void GenerateDomeTop()
        {
            if (restLevels == null) restLevels = new List<RestLevel>();
            restLevels.Clear();
            int n = Mathf.Max(3, genSegmentCount);
            float hw = genWidth * 0.5f, hh = genHeight * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float y = Mathf.Lerp(hh, -hh, t);
                // top ~30% is a quarter-sine dome, rest is straight
                float w = t < 0.3f ? hw * Mathf.Sin(t / 0.3f * Mathf.PI * 0.5f) : hw;
                restLevels.Add(new RestLevel { y = y, leftX = -w, rightX = +w });
            }
        }

        [ContextMenu("Generate/Subdivide (double density)")]
        public void Subdivide()
        {
            if (restLevels == null || restLevels.Count < 2) return;
            restLevels.Sort((a, b) => b.y.CompareTo(a.y));
            var result = new List<RestLevel>(restLevels.Count * 2 - 1);
            for (int i = 0; i < restLevels.Count - 1; i++)
            {
                result.Add(restLevels[i]);
                var a = restLevels[i];
                var b = restLevels[i + 1];
                result.Add(new RestLevel
                {
                    y      = (a.y      + b.y)      * 0.5f,
                    leftX  = (a.leftX  + b.leftX)  * 0.5f,
                    rightX = (a.rightX + b.rightX) * 0.5f,
                });
            }
            result.Add(restLevels[restLevels.Count - 1]);
            restLevels = result;
        }

        public int RestLevelCount => restLevels == null ? 0 : restLevels.Count;
        public RestLevel GetRestLevel(int i) => restLevels[i];
        public void SetRestLevel(int i, RestLevel v) => restLevels[i] = v;

        // --- Deformation ----------------------------------------------------

        void ApplyImpactAt(Vector2 worldPos, float strength)
        {
            if (strength <= 0f || levels == null || levels.Length < 3) return;

            Vector2 local = transform.InverseTransformPoint(worldPos);

            int nearest = 0;
            float bestDy = float.MaxValue;
            for (int i = 0; i < levels.Length; i++)
            {
                float dy = Mathf.Abs(levels[i].y - local.y);
                if (dy < bestDy) { bestDy = dy; nearest = i; }
            }

            // Pick the side nearer to the slime's local X at the nearest level.
            float centerX = (levels[nearest].leftRestX + levels[nearest].rightRestX) * 0.5f;
            bool hitLeftSide = local.x < centerX;

            bool any = false;
            for (int i = 0; i < levels.Length; i++)
            {
                if (levels[i].isCap) continue;
                float yDist = Mathf.Abs(levels[i].y - levels[nearest].y);
                if (yDist > falloffYRange) continue;
                float w = 1f - yDist / falloffYRange;
                w = w * w * (3f - 2f * w); // smoothstep
                float push = impactToDisplacement * strength * w;
                if (push <= 0f) continue;
                push = Mathf.Min(push, maxPerHitDisplacement);

                if (hitLeftSide)
                {
                    // Can only increase (push further into material).
                    float newX = levels[i].leftCurX + push;
                    // Cap at the opposite REST wall so the dent can cross (pass-through)
                    // but the chain can't run off to infinity.
                    newX = Mathf.Min(newX, levels[i].rightRestX);
                    if (newX > levels[i].leftCurX) { levels[i].leftCurX = newX; any = true; }
                }
                else
                {
                    float newX = levels[i].rightCurX - push;
                    newX = Mathf.Max(newX, levels[i].leftRestX);
                    if (newX < levels[i].rightCurX) { levels[i].rightCurX = newX; any = true; }
                }
            }
            if (any) dirty = true;
        }

        // --- Reset ----------------------------------------------------------

        void BeginReset()
        {
            resetting = true;
            resetTimer = 0f;
            if (resetFromLeft == null || resetFromLeft.Length != levels.Length)
            {
                resetFromLeft  = new float[levels.Length];
                resetFromRight = new float[levels.Length];
            }
            for (int i = 0; i < levels.Length; i++)
            {
                resetFromLeft[i]  = levels[i].leftCurX;
                resetFromRight[i] = levels[i].rightCurX;
            }
        }

        void TickReset(float dt)
        {
            resetTimer += dt;
            float t = Mathf.Clamp01(resetTimer / resetDuration);
            float s = t * t * (3f - 2f * t);
            for (int i = 0; i < levels.Length; i++)
            {
                levels[i].leftCurX  = Mathf.Lerp(resetFromLeft[i],  levels[i].leftRestX,  s);
                levels[i].rightCurX = Mathf.Lerp(resetFromRight[i], levels[i].rightRestX, s);
            }
            dirty = true;
            if (t >= 1f) resetting = false;
        }

        // --- Rebuild (collider + mesh) --------------------------------------

        void Rebuild()
        {
            if (meshFilter == null || poly == null) return;
            if (levels == null || levels.Length < 3) return;

            // Walk level ladder, detect connected runs of valid (non-pinched) levels.
            // A level is "pinched" when leftCurX >= rightCurX.
            scratchPiecePaths.Clear();
            scratchVerts.Clear();
            scratchNorms.Clear();
            scratchUVs.Clear();
            scratchTris.Clear();

            int i = 0;
            while (i < levels.Length)
            {
                while (i < levels.Length && IsPinched(i)) i++;
                if (i >= levels.Length) break;
                int start = i;
                while (i < levels.Length && !IsPinched(i)) i++;
                int end = i - 1;
                // A piece needs at least 2 adjacent valid levels to form a quad.
                if (end > start) EmitPiece(start, end);
            }

            // Collider paths
            poly.pathCount = scratchPiecePaths.Count;
            for (int k = 0; k < scratchPiecePaths.Count; k++)
                poly.SetPath(k, scratchPiecePaths[k]);

            // Mesh
            if (mesh == null)
            {
                mesh = new Mesh { name = "DeformablePlatform_Mesh" };
                mesh.MarkDynamic();
                meshFilter.sharedMesh = mesh;
            }
            mesh.Clear();
            if (scratchVerts.Count > 0)
            {
                // Use 32-bit indices if vertex count is high (many pieces × many levels).
                mesh.indexFormat = scratchVerts.Count > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16;
                mesh.SetVertices(scratchVerts);
                mesh.SetNormals(scratchNorms);
                mesh.SetUVs(0, scratchUVs);
                mesh.SetTriangles(scratchTris, 0);
                mesh.RecalculateBounds();
            }
        }

        bool IsPinched(int i) => levels[i].leftCurX >= levels[i].rightCurX;

        // Emit a single piece spanning levels [start..end] (inclusive).
        // Appends to scratchPiecePaths (collider), scratchVerts/norms/uvs/tris (mesh).
        void EmitPiece(int start, int end)
        {
            // Build CCW outline: down the left chain, across bottom, up the right chain.
            // Level i=start is the TOP of this piece (levels generated top→bottom).
            scratchOutline.Clear();
            for (int i = start; i <= end; i++)
                scratchOutline.Add(new Vector2(levels[i].leftCurX, levels[i].y));
            for (int i = end; i >= start; i--)
                scratchOutline.Add(new Vector2(levels[i].rightCurX, levels[i].y));

            int n = scratchOutline.Count;

            // -- Collider path (just the 2D outline) --
            var pathArr = new Vector2[n];
            for (int k = 0; k < n; k++) pathArr[k] = scratchOutline[k];
            scratchPiecePaths.Add(pathArr);

            // -- Mesh: front ring + back ring + side strip (flat-normal per edge) --
            int vBase = scratchVerts.Count;
            float zF = -depth * 0.5f;
            float zB = +depth * 0.5f;

            // Front ring (normal -Z)
            for (int k = 0; k < n; k++)
            {
                scratchVerts.Add(new Vector3(scratchOutline[k].x, scratchOutline[k].y, zF));
                scratchNorms.Add(new Vector3(0f, 0f, -1f));
                scratchUVs.Add(scratchOutline[k]); // planar XY
            }
            // Back ring (normal +Z)
            for (int k = 0; k < n; k++)
            {
                scratchVerts.Add(new Vector3(scratchOutline[k].x, scratchOutline[k].y, zB));
                scratchNorms.Add(new Vector3(0f, 0f, 1f));
                scratchUVs.Add(scratchOutline[k]);
            }
            // Side strip: 4 verts per edge (flat face normal)
            int sideBase = vBase + 2 * n;
            float cursor = 0f;
            for (int k = 0; k < n; k++)
            {
                int kNext = (k + 1) % n;
                Vector2 a = scratchOutline[k];
                Vector2 b = scratchOutline[kNext];
                Vector2 edge = b - a;
                Vector2 outward = new Vector2(edge.y, -edge.x).normalized; // CCW outline → right-perp outward
                Vector3 nrm = new Vector3(outward.x, outward.y, 0f);

                scratchVerts.Add(new Vector3(a.x, a.y, zF)); scratchNorms.Add(nrm);
                scratchVerts.Add(new Vector3(b.x, b.y, zF)); scratchNorms.Add(nrm);
                scratchVerts.Add(new Vector3(a.x, a.y, zB)); scratchNorms.Add(nrm);
                scratchVerts.Add(new Vector3(b.x, b.y, zB)); scratchNorms.Add(nrm);

                float u0 = cursor;
                float u1 = cursor + edge.magnitude;
                scratchUVs.Add(new Vector2(u0, 0f));
                scratchUVs.Add(new Vector2(u1, 0f));
                scratchUVs.Add(new Vector2(u0, 1f));
                scratchUVs.Add(new Vector2(u1, 1f));
                cursor = u1;
            }

            // -- Triangles --
            // Front face: ear-clip the outline. Piece shape is a vertical-ish strip,
            // always simple (CCW, no self-intersection) because each level has
            // leftX < rightX and Y is monotonic.
            EarClip(n);

            // Front (indices into the front ring, which starts at vBase)
            for (int k = 0; k < scratchEarTris.Count; k++)
                scratchTris.Add(vBase + scratchEarTris[k]);
            // Back (reverse winding so it faces +Z; ring starts at vBase + n)
            for (int k = 0; k < scratchEarTris.Count; k += 3)
            {
                scratchTris.Add(vBase + n + scratchEarTris[k + 0]);
                scratchTris.Add(vBase + n + scratchEarTris[k + 2]);
                scratchTris.Add(vBase + n + scratchEarTris[k + 1]);
            }
            // Sides
            for (int k = 0; k < n; k++)
            {
                int b0 = sideBase + k * 4;
                scratchTris.Add(b0 + 0); scratchTris.Add(b0 + 1); scratchTris.Add(b0 + 2);
                scratchTris.Add(b0 + 1); scratchTris.Add(b0 + 3); scratchTris.Add(b0 + 2);
            }
        }

        // Ear clipping over scratchOutline (CCW). Writes triangle indices (into the outline)
        // to scratchEarTris.
        void EarClip(int n)
        {
            scratchEarTris.Clear();
            if (n < 3) return;

            scratchEarIdx.Clear();
            for (int k = 0; k < n; k++) scratchEarIdx.Add(k);

            int guard = n * n;
            while (scratchEarIdx.Count >= 3 && guard-- > 0)
            {
                int m = scratchEarIdx.Count;
                bool clipped = false;
                for (int k = 0; k < m; k++)
                {
                    int prev = scratchEarIdx[(k - 1 + m) % m];
                    int curr = scratchEarIdx[k];
                    int next = scratchEarIdx[(k + 1) % m];
                    if (IsEar(prev, curr, next))
                    {
                        scratchEarTris.Add(prev);
                        scratchEarTris.Add(curr);
                        scratchEarTris.Add(next);
                        scratchEarIdx.RemoveAt(k);
                        clipped = true;
                        break;
                    }
                }
                if (!clipped) break;
            }
        }

        bool IsEar(int prev, int curr, int next)
        {
            Vector2 a = scratchOutline[prev], b = scratchOutline[curr], c = scratchOutline[next];
            float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            if (cross <= 0f) return false;
            for (int k = 0; k < scratchEarIdx.Count; k++)
            {
                int idx = scratchEarIdx[k];
                if (idx == prev || idx == curr || idx == next) continue;
                if (PointInTriangle(scratchOutline[idx], a, b, c)) return false;
            }
            return true;
        }

        static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool hasNeg = (d1 < 0f) || (d2 < 0f) || (d3 < 0f);
            bool hasPos = (d1 > 0f) || (d2 > 0f) || (d3 > 0f);
            return !(hasNeg && hasPos);
        }

        // --- Gizmos ---------------------------------------------------------

        void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;

            // Authored rest outline (source of truth before runtime deform).
            if (restLevels != null && restLevels.Count >= 2)
            {
                Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.45f);
                for (int i = 0; i < restLevels.Count - 1; i++)
                {
                    var a = restLevels[i];
                    var b = restLevels[i + 1];
                    Vector3 aL = transform.TransformPoint(new Vector3(a.leftX,  a.y, 0f));
                    Vector3 bL = transform.TransformPoint(new Vector3(b.leftX,  b.y, 0f));
                    Vector3 aR = transform.TransformPoint(new Vector3(a.rightX, a.y, 0f));
                    Vector3 bR = transform.TransformPoint(new Vector3(b.rightX, b.y, 0f));
                    Gizmos.DrawLine(aL, bL);
                    Gizmos.DrawLine(aR, bR);
                }
                // Top and bottom caps
                var first = restLevels[0];
                var last  = restLevels[restLevels.Count - 1];
                Gizmos.DrawLine(
                    transform.TransformPoint(new Vector3(first.leftX,  first.y, 0f)),
                    transform.TransformPoint(new Vector3(first.rightX, first.y, 0f)));
                Gizmos.DrawLine(
                    transform.TransformPoint(new Vector3(last.leftX,   last.y, 0f)),
                    transform.TransformPoint(new Vector3(last.rightX,  last.y, 0f)));
            }

            if (levels == null) return;

            // Left chain (yellow) + right chain (cyan)
            Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.9f);
            for (int k = 0; k < levels.Length - 1; k++)
            {
                Vector3 a = transform.TransformPoint(new Vector3(levels[k].leftCurX,   levels[k].y,   0f));
                Vector3 b = transform.TransformPoint(new Vector3(levels[k+1].leftCurX, levels[k+1].y, 0f));
                Gizmos.DrawLine(a, b);
            }
            Gizmos.color = new Color(0.3f, 1f, 1f, 0.9f);
            for (int k = 0; k < levels.Length - 1; k++)
            {
                Vector3 a = transform.TransformPoint(new Vector3(levels[k].rightCurX,   levels[k].y,   0f));
                Vector3 b = transform.TransformPoint(new Vector3(levels[k+1].rightCurX, levels[k+1].y, 0f));
                Gizmos.DrawLine(a, b);
            }

            // Pinched levels: draw an X
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.8f);
            for (int k = 0; k < levels.Length; k++)
            {
                if (!IsPinched(k)) continue;
                Vector3 L = transform.TransformPoint(new Vector3(levels[k].leftCurX,  levels[k].y, 0f));
                Vector3 R = transform.TransformPoint(new Vector3(levels[k].rightCurX, levels[k].y, 0f));
                Gizmos.DrawLine(L, R);
            }
        }
    }
}
