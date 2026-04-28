using System.Text;
using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Physics;

namespace SlimeJumping.Debugging
{
    /// <summary>
    /// Diagnoses the "visual contracts then stretches in travel direction" symptom on
    /// curved TerrainMaker surfaces. Samples per render frame the body's deform state
    /// (body.localScale, body.up) and per fixed step the probe's normal/contact state,
    /// then auto-dumps when one of three thresholds trips:
    ///
    ///   visualScaleJump   — |Δbody.localScale| spike (the symptom itself)
    ///   bodyUpRotation    — body.up degrees rotated this frame (deform axis swing)
    ///   surfaceNormalJump — slimeController.SurfaceNormal degrees rotated this fixed step
    ///
    /// Also dumps the current SurfaceCollider's polygon shape (edge length distribution +
    /// inter-edge angle distribution) at startup and whenever the SurfaceCollider changes.
    /// That dump answers "how faceted is the collider the slime is currently on?" — the
    /// upstream cause of normal jumps. Compare the worst inter-edge angle to the
    /// surfaceNormalJump samples: if they match, the collider's discretization is the
    /// dominant source.
    ///
    /// Manual dump key: F5.
    /// </summary>
    [DefaultExecutionOrder(2200)]
    public class SlimeVisualDeformProbe : MonoBehaviour
    {
        [SerializeField] private SlimeController slime;
        [Tooltip("Inner deforming Transform — same one SlimeVisual writes localScale/up to (named 'body' in SlimeVisual).")]
        [SerializeField] private Transform body;

        [Header("Auto-dump thresholds")]
        [Tooltip("Auto-dump if |Δbody.localScale| (max of x/y delta vs prev render frame) exceeds this.")]
        [SerializeField] private float visualScaleJumpThreshold = 0.01f;
        [Tooltip("Auto-dump if body.up rotated more than this many degrees in one render frame.")]
        [SerializeField] private float bodyUpRotationThresholdDeg = 0.6f;
        [Tooltip("Auto-dump if SlimeController.SurfaceNormal (filtered) rotated more than this many degrees in one fixed step.")]
        [SerializeField] private float surfaceNormalJumpThresholdDeg = 0.6f;
        [Tooltip("Auto-dump if Probe.EffectiveNormal (raw, pre-filter) rotated more than this in one fixed step.")]
        [SerializeField] private float effectiveNormalJumpThresholdDeg = 1.5f;
        [Tooltip("Auto-dump if active-set churn (samples that flipped active state) is at least this in one fixed step.")]
        [SerializeField] private int activeChurnThreshold = 1;
        [SerializeField] private float autoDumpCooldown = 0.25f;
        [SerializeField] private bool autoDump = true;

        [Header("Manual")]
        [SerializeField] private KeyCode dumpKey = KeyCode.F5;
        [Tooltip("Press to re-dump the current SurfaceCollider's polygon shape stats.")]
        [SerializeField] private KeyCode dumpColliderKey = KeyCode.F6;

        [Header("HUD")]
        [SerializeField] private Vector2 guiPos = new Vector2(10f, 580f);

        private const int RING = 90;
        private struct Snap
        {
            public int frame;
            public float time;
            public bool isFixedTick;
            public string state;
            // Visual side
            public Vector3 localScale;
            public Vector2 scaleDelta;
            public float bodyUpDeg;
            public float bodyUpRotDeg;     // delta vs prev frame
            // Physics side (last-fixed-step snapshot)
            public Vector2 surfN;          // SlimeController.SurfaceNormal (filtered)
            public float surfNDeltaDeg;
            public Vector2 effN;           // Probe.EffectiveNormal (raw)
            public float effNDeltaDeg;
            public int contactCount;
            public int activeCount;
            public int activeChurn;
            public string colName;
            public float speed;
            public string trigger;          // why this frame is interesting
        }

        private readonly Snap[] ring = new Snap[RING];
        private int ringHead;
        private int ringCount;

        private bool initialized;
        private Vector3 prevScale;
        private Vector2 prevBodyUp;
        private Vector2 prevSurfN;
        private Vector2 prevEffN;
        private bool[] prevActiveSnapshot;
        private int prevActiveSnapshotCount;
        private Collider2D lastReportedCollider;

        // Set by FixedUpdate, consumed by Update so the ring snap carries correlated state.
        private Vector2 lastFixedSurfN;
        private float lastFixedSurfNDeltaDeg;
        private Vector2 lastFixedEffN;
        private float lastFixedEffNDeltaDeg;
        private int lastFixedContactCount;
        private int lastFixedActiveCount;
        private int lastFixedActiveChurn;
        private bool fixedTickThisUpdate;

        private float lastAutoDumpTime = -999f;
        private GUIStyle labelStyle;

        // 1-second window stats
        private float windowStart;
        private float maxScaleJumpWindow;
        private float maxBodyUpRotWindow;
        private float maxSurfNJumpWindow;
        private float maxEffNJumpWindow;
        private int maxChurnWindow;
        private float lastWindowMaxScaleJump;
        private float lastWindowMaxBodyUpRot;
        private float lastWindowMaxSurfNJump;
        private float lastWindowMaxEffNJump;
        private int lastWindowMaxChurn;
        private int autoDumpsThisRun;

        private void Awake()
        {
            if (slime == null) slime = FindAnyObjectByType<SlimeController>();
            if (body == null && slime != null)
            {
                var sv = slime.GetComponentInChildren<SlimeVisual>();
                if (sv != null)
                {
                    body = FindChildRecursive(sv.transform, "body");
                    if (body == null)
                    {
                        var sr = sv.GetComponentInChildren<SpriteRenderer>();
                        if (sr != null) body = sr.transform;
                    }
                }
            }
            if (body == null)
                Debug.LogWarning("[SlimeVisualDeformProbe] body Transform not wired and could not be auto-located. Visual deltas (scale / body.up) will not be sampled. Drag the SlimeVisual's 'body' child into the inspector.");
            prevActiveSnapshot = new bool[64];
            windowStart = Time.unscaledTime;
        }

        private static Transform FindChildRecursive(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var hit = FindChildRecursive(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        private void Start()
        {
            DumpCurrentColliderShape("startup");
        }

        private void FixedUpdate()
        {
            fixedTickThisUpdate = true;

            if (slime == null) return;

            Vector2 surfN = slime.SurfaceNormal;
            if (prevSurfN.sqrMagnitude > 1e-6f && surfN.sqrMagnitude > 1e-6f)
                lastFixedSurfNDeltaDeg = Vector2.Angle(prevSurfN, surfN);
            else
                lastFixedSurfNDeltaDeg = 0f;
            lastFixedSurfN = surfN;

            var probe = slime.Probe;
            if (probe != null)
            {
                Vector2 effN = probe.EffectiveNormal;
                if (prevEffN.sqrMagnitude > 1e-6f && effN.sqrMagnitude > 1e-6f)
                    lastFixedEffNDeltaDeg = Vector2.Angle(prevEffN, effN);
                else
                    lastFixedEffNDeltaDeg = 0f;
                lastFixedEffN = effN;
                prevEffN = effN;
                lastFixedContactCount = probe.ContactCount;
                lastFixedActiveCount = probe.ActiveContactCount;

                // Active churn vs prev fixed step. ContactCount can vary, so map by collider+normal
                // is overkill — count per-index changes against prev snapshot. With multi-probe the
                // active-set ordering is reasonably stable frame-to-frame.
                int churn = 0;
                int n = lastFixedContactCount;
                if (n > prevActiveSnapshot.Length) n = prevActiveSnapshot.Length;
                int compareN = Mathf.Min(n, prevActiveSnapshotCount);
                for (int i = 0; i < compareN; i++)
                {
                    bool nowActive = probe.IsContactActive(i);
                    if (nowActive != prevActiveSnapshot[i]) churn++;
                }
                churn += Mathf.Abs(n - prevActiveSnapshotCount);
                lastFixedActiveChurn = churn;

                for (int i = 0; i < n; i++) prevActiveSnapshot[i] = probe.IsContactActive(i);
                prevActiveSnapshotCount = n;
            }

            prevSurfN = surfN;

            if (lastFixedSurfNDeltaDeg > maxSurfNJumpWindow) maxSurfNJumpWindow = lastFixedSurfNDeltaDeg;
            if (lastFixedEffNDeltaDeg > maxEffNJumpWindow) maxEffNJumpWindow = lastFixedEffNDeltaDeg;
            if (lastFixedActiveChurn > maxChurnWindow) maxChurnWindow = lastFixedActiveChurn;

            // Re-dump collider shape if the slime moved to a different collider.
            if (slime.SurfaceCollider != null && slime.SurfaceCollider != lastReportedCollider)
            {
                lastReportedCollider = slime.SurfaceCollider;
                DumpColliderShape(lastReportedCollider, $"surface change → {lastReportedCollider.name}");
            }
        }

        private void Update()
        {
            if (slime == null) return;

            // body may be unwired; physics-side auto-dumps still fire below.
            Vector3 scale = body != null ? body.localScale : Vector3.one;
            Vector2 up = body != null ? (Vector2)body.up : Vector2.up;

            if (!initialized)
            {
                prevScale = scale;
                prevBodyUp = up;
                initialized = true;
                fixedTickThisUpdate = false;
                if (Input.GetKeyDown(dumpKey)) DumpRing("[SlimeVisualDeformProbe] manual dump (init)");
                if (Input.GetKeyDown(dumpColliderKey)) DumpCurrentColliderShape("manual");
                return;
            }

            float dx = Mathf.Abs(scale.x - prevScale.x);
            float dy = Mathf.Abs(scale.y - prevScale.y);
            float scaleJump = Mathf.Max(dx, dy);
            float bodyUpRot = (prevBodyUp.sqrMagnitude > 1e-6f && up.sqrMagnitude > 1e-6f)
                ? Vector2.Angle(prevBodyUp, up) : 0f;

            if (scaleJump > maxScaleJumpWindow) maxScaleJumpWindow = scaleJump;
            if (bodyUpRot > maxBodyUpRotWindow) maxBodyUpRotWindow = bodyUpRot;

            string trigger = "";
            bool shouldDump = false;
            if (autoDump)
            {
                if (body != null && scaleJump > visualScaleJumpThreshold) { trigger = $"scale Δ{scaleJump:F3}"; shouldDump = true; }
                else if (body != null && bodyUpRot > bodyUpRotationThresholdDeg) { trigger = $"body.up Δ{bodyUpRot:F2}°"; shouldDump = true; }
                else if (fixedTickThisUpdate && lastFixedSurfNDeltaDeg > surfaceNormalJumpThresholdDeg)
                { trigger = $"surfN Δ{lastFixedSurfNDeltaDeg:F2}°"; shouldDump = true; }
                else if (fixedTickThisUpdate && lastFixedEffNDeltaDeg > effectiveNormalJumpThresholdDeg)
                { trigger = $"effN Δ{lastFixedEffNDeltaDeg:F2}°"; shouldDump = true; }
                else if (fixedTickThisUpdate && lastFixedActiveChurn >= activeChurnThreshold)
                { trigger = $"churn {lastFixedActiveChurn}"; shouldDump = true; }
            }

            ref Snap s = ref ring[ringHead];
            s.frame = Time.frameCount;
            s.time = Time.unscaledTime;
            s.isFixedTick = fixedTickThisUpdate;
            s.state = slime.CurrentState.ToString();
            s.localScale = scale;
            s.scaleDelta = new Vector2(scale.x - prevScale.x, scale.y - prevScale.y);
            s.bodyUpDeg = Mathf.Atan2(up.y, up.x) * Mathf.Rad2Deg;
            s.bodyUpRotDeg = bodyUpRot;
            s.surfN = lastFixedSurfN;
            s.surfNDeltaDeg = lastFixedSurfNDeltaDeg;
            s.effN = lastFixedEffN;
            s.effNDeltaDeg = lastFixedEffNDeltaDeg;
            s.contactCount = lastFixedContactCount;
            s.activeCount = lastFixedActiveCount;
            s.activeChurn = lastFixedActiveChurn;
            s.colName = slime.SurfaceCollider != null ? slime.SurfaceCollider.name : "-";
            s.speed = slime.Velocity.magnitude;
            s.trigger = trigger;

            ringHead = (ringHead + 1) % RING;
            if (ringCount < RING) ringCount++;

            if (Input.GetKeyDown(dumpKey)) DumpRing("[SlimeVisualDeformProbe] manual dump");
            if (Input.GetKeyDown(dumpColliderKey)) DumpCurrentColliderShape("manual");

            if (shouldDump && Time.unscaledTime - lastAutoDumpTime > autoDumpCooldown)
            {
                lastAutoDumpTime = Time.unscaledTime;
                autoDumpsThisRun++;
                DumpRing($"[SlimeVisualDeformProbe] auto-dump ({trigger})");
            }

            if (Time.unscaledTime - windowStart >= 1f)
            {
                lastWindowMaxScaleJump = maxScaleJumpWindow;
                lastWindowMaxBodyUpRot = maxBodyUpRotWindow;
                lastWindowMaxSurfNJump = maxSurfNJumpWindow;
                lastWindowMaxEffNJump = maxEffNJumpWindow;
                lastWindowMaxChurn = maxChurnWindow;
                maxScaleJumpWindow = 0f;
                maxBodyUpRotWindow = 0f;
                maxSurfNJumpWindow = 0f;
                maxEffNJumpWindow = 0f;
                maxChurnWindow = 0;
                windowStart = Time.unscaledTime;
            }

            prevScale = scale;
            prevBodyUp = up;
            fixedTickThisUpdate = false;
        }

        private void OnGUI()
        {
            if (slime == null) return;
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true };
                labelStyle.normal.textColor = Color.white;
            }

            string scaleColor =
                lastWindowMaxScaleJump < visualScaleJumpThreshold * 0.5f ? "#a0ffa0" :
                lastWindowMaxScaleJump < visualScaleJumpThreshold       ? "#ffd080" : "#ff8080";
            string upColor =
                lastWindowMaxBodyUpRot < bodyUpRotationThresholdDeg * 0.5f ? "#a0ffa0" :
                lastWindowMaxBodyUpRot < bodyUpRotationThresholdDeg        ? "#ffd080" : "#ff8080";
            string nColor =
                lastWindowMaxSurfNJump < surfaceNormalJumpThresholdDeg * 0.5f ? "#a0ffa0" :
                lastWindowMaxSurfNJump < surfaceNormalJumpThresholdDeg        ? "#ffd080" : "#ff8080";
            string eColor =
                lastWindowMaxEffNJump < effectiveNormalJumpThresholdDeg * 0.5f ? "#a0ffa0" :
                lastWindowMaxEffNJump < effectiveNormalJumpThresholdDeg        ? "#ffd080" : "#ff8080";
            string churnColor =
                lastWindowMaxChurn < activeChurnThreshold ? "#a0ffa0" : "#ff8080";
            string bodyStatus = body != null
                ? $"<color=#a0ffa0>body wired ({body.name})</color>"
                : "<color=#ff8080>body NOT wired — visual deltas dark</color>";

            string text =
                "[Visual Deform Probe]\n" +
                $"  {bodyStatus}\n" +
                $"  scale Δ (1s):  <color={scaleColor}>{lastWindowMaxScaleJump:F3}</color>  thr {visualScaleJumpThreshold:F3}\n" +
                $"  body.up rot (1s):  <color={upColor}>{lastWindowMaxBodyUpRot:F2}°</color>  thr {bodyUpRotationThresholdDeg:F2}°\n" +
                $"  surfN rot (1s):    <color={nColor}>{lastWindowMaxSurfNJump:F2}°</color>  thr {surfaceNormalJumpThresholdDeg:F2}°\n" +
                $"  effN rot (1s):     <color={eColor}>{lastWindowMaxEffNJump:F2}°</color>  thr {effectiveNormalJumpThresholdDeg:F2}°\n" +
                $"  active churn (1s): <color={churnColor}>{lastWindowMaxChurn}</color>  thr {activeChurnThreshold}\n" +
                $"  auto-dumps: {autoDumpsThisRun}   [{dumpKey}] dump ring  [{dumpColliderKey}] dump collider";

            var rect = new Rect(guiPos.x, guiPos.y, 460f, 140f);
            GUI.Label(rect, text, labelStyle);
        }

        private void DumpCurrentColliderShape(string reason)
        {
            if (slime == null) return;
            var col = slime.SurfaceCollider;
            if (col == null) { Debug.Log($"[SlimeVisualDeformProbe] collider dump ({reason}) — no current SurfaceCollider"); return; }
            DumpColliderShape(col, reason);
        }

        // Reports edge length distribution and inter-edge turn angles for the polygon
        // path the slime is currently on. The worst inter-edge angle is the upper bound
        // on a per-vertex normal jump as the slime crawls across that vertex.
        private void DumpColliderShape(Collider2D col, string reason)
        {
            if (col is PolygonCollider2D poly)
            {
                var sb = new StringBuilder(2048);
                sb.AppendLine($"[SlimeVisualDeformProbe] PolygonCollider2D shape ({reason}): '{col.name}'");
                sb.AppendLine($"  paths: {poly.pathCount}");

                int totalVerts = 0;
                float worstTurn = 0f;
                int worstPath = -1, worstIdx = -1;
                float worstEdgeLen = 0f;
                int worstEdgeLenPath = -1, worstEdgeLenIdx = -1;

                for (int p = 0; p < poly.pathCount; p++)
                {
                    var path = poly.GetPath(p);
                    int n = path.Length;
                    totalVerts += n;
                    if (n < 3)
                    {
                        sb.AppendLine($"  path {p}: {n} verts (degenerate)");
                        continue;
                    }

                    // Convert to world space so reported lengths match the slime's world scale.
                    var wp = new Vector2[n];
                    for (int i = 0; i < n; i++) wp[i] = poly.transform.TransformPoint(path[i]);

                    float minLen = float.MaxValue, maxLen = 0f, sumLen = 0f;
                    float minTurn = float.MaxValue, maxTurn = 0f, sumTurn = 0f;
                    int turnSamples = 0;

                    for (int i = 0; i < n; i++)
                    {
                        Vector2 a = wp[i];
                        Vector2 b = wp[(i + 1) % n];
                        float len = Vector2.Distance(a, b);
                        if (len < minLen) minLen = len;
                        if (len > maxLen)
                        {
                            maxLen = len;
                            if (len > worstEdgeLen) { worstEdgeLen = len; worstEdgeLenPath = p; worstEdgeLenIdx = i; }
                        }
                        sumLen += len;

                        Vector2 prev = wp[(i - 1 + n) % n];
                        Vector2 e0 = (a - prev);
                        Vector2 e1 = (b - a);
                        if (e0.sqrMagnitude > 1e-10f && e1.sqrMagnitude > 1e-10f)
                        {
                            float turn = Vector2.Angle(e0, e1);
                            if (turn < minTurn) minTurn = turn;
                            if (turn > maxTurn) maxTurn = turn;
                            sumTurn += turn;
                            turnSamples++;
                            if (turn > worstTurn) { worstTurn = turn; worstPath = p; worstIdx = i; }
                        }
                    }

                    sb.AppendLine(
                        $"  path {p}: {n} verts | edge len min/avg/max = {minLen:F4} / {(sumLen / n):F4} / {maxLen:F4} | "
                        + $"turn deg min/avg/max = {minTurn:F2} / {(turnSamples > 0 ? sumTurn / turnSamples : 0f):F2} / {maxTurn:F2}");
                }
                sb.AppendLine($"  totals: {totalVerts} verts | worst turn = {worstTurn:F2}° at path {worstPath} idx {worstIdx} | worst edge len = {worstEdgeLen:F4} at path {worstEdgeLenPath} idx {worstEdgeLenIdx}");
                sb.AppendLine("  reading: worst-turn = upper bound on surface-normal jump as slime crosses that vertex.");
                Debug.Log(sb.ToString());
            }
            else if (col is EdgeCollider2D edge)
            {
                var pts = edge.points;
                int n = pts.Length;
                var sb = new StringBuilder(1024);
                sb.AppendLine($"[SlimeVisualDeformProbe] EdgeCollider2D ({reason}): '{col.name}' verts={n}");
                if (n < 2) { Debug.Log(sb.ToString()); return; }

                float minLen = float.MaxValue, maxLen = 0f, sumLen = 0f;
                float minTurn = float.MaxValue, maxTurn = 0f, sumTurn = 0f;
                int turnSamples = 0;
                var wp = new Vector2[n];
                for (int i = 0; i < n; i++) wp[i] = edge.transform.TransformPoint(pts[i]);
                for (int i = 0; i < n - 1; i++)
                {
                    float len = Vector2.Distance(wp[i], wp[i + 1]);
                    if (len < minLen) minLen = len;
                    if (len > maxLen) maxLen = len;
                    sumLen += len;
                }
                for (int i = 1; i < n - 1; i++)
                {
                    Vector2 e0 = wp[i] - wp[i - 1];
                    Vector2 e1 = wp[i + 1] - wp[i];
                    if (e0.sqrMagnitude > 1e-10f && e1.sqrMagnitude > 1e-10f)
                    {
                        float turn = Vector2.Angle(e0, e1);
                        if (turn < minTurn) minTurn = turn;
                        if (turn > maxTurn) maxTurn = turn;
                        sumTurn += turn;
                        turnSamples++;
                    }
                }
                sb.AppendLine($"  edge len min/avg/max = {minLen:F4} / {(sumLen / Mathf.Max(1, n - 1)):F4} / {maxLen:F4}");
                sb.AppendLine($"  turn deg min/avg/max = {minTurn:F2} / {(turnSamples > 0 ? sumTurn / turnSamples : 0f):F2} / {maxTurn:F2}");
                Debug.Log(sb.ToString());
            }
            else
            {
                Debug.Log($"[SlimeVisualDeformProbe] collider '{col.name}' is {col.GetType().Name} — shape stats not implemented for this type.");
            }
        }

        private void DumpRing(string header)
        {
            if (ringCount == 0) { Debug.Log($"{header} — ring empty"); return; }

            var sb = new StringBuilder(8192);
            sb.AppendLine(header);
            sb.AppendLine("  legend: F=fixed-step ran | scale=body.localScale | scaleΔ vs prev render frame | bodyUpΔ deg | surfNΔ filtered, effNΔ raw — both deg vs prev fixed | aC=active count | churn=active-set flips");
            sb.AppendLine(
                "  idx frame   t      F state      scale (x,y)      scaleΔ(x,y)         bodyUp°  bodyUpΔ°  surfN(x,y)          surfNΔ°  effN(x,y)         effNΔ°  cC aC churn  speed   col              trigger");

            int start = (ringHead - ringCount + RING) % RING;
            for (int i = 0; i < ringCount; i++)
            {
                var s = ring[(start + i) % RING];
                sb.AppendFormat(
                    "  {0,3} {1,6} {2,6:F2}  {3} {4,-9} ({5,5:F3},{6,5:F3})  ({7,6:F3},{8,6:F3})  {9,7:F2}  {10,7:F2}   ({11,5:F2},{12,5:F2})  {13,6:F2}   ({14,5:F2},{15,5:F2})  {16,6:F2}  {17,2} {18,2} {19,4}  {20,5:F2}  {21,-15}  {22}\n",
                    i, s.frame, s.time,
                    s.isFixedTick ? "F" : ".",
                    s.state,
                    s.localScale.x, s.localScale.y,
                    s.scaleDelta.x, s.scaleDelta.y,
                    s.bodyUpDeg, s.bodyUpRotDeg,
                    s.surfN.x, s.surfN.y, s.surfNDeltaDeg,
                    s.effN.x, s.effN.y, s.effNDeltaDeg,
                    s.contactCount, s.activeCount, s.activeChurn,
                    s.speed, s.colName, s.trigger);
            }
            Debug.Log(sb.ToString());
        }
    }
}
