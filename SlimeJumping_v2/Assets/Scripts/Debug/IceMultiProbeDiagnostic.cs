using System.Text;
using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Physics;

namespace SlimeJumping.Debugging
{
    /// <summary>Per-FixedUpdate ring capture of MultiContactSurfaceProbe state (contacts, EffectiveNormal
    /// delta, active-set churn, recomputed Phase A push) to diagnose judder on Ice-tagged curves.</summary>
    [DefaultExecutionOrder(2000)]
    public class IceMultiProbeDiagnostic : MonoBehaviour
    {
        [SerializeField] private SlimeController slime;
        [SerializeField] private KeyCode dumpKey = KeyCode.F3;
        [SerializeField] private bool autoDumpOnJudder = true;
        [SerializeField] private float autoDumpCooldown = 0.5f;
        [Tooltip("Auto-dump if EffectiveNormal angle changes more than this in one fixed step.")]
        [SerializeField] private float normalJumpThresholdDeg = 5f;
        [Tooltip("Auto-dump if (samples added) + (samples lost active state) >= this in one frame.")]
        [SerializeField] private int activeChurnThreshold = 2;
        [Tooltip("Only auto-dump while CurrentState == Attached and surface tag name contains this substring (case-insensitive). Empty = any surface.")]
        [SerializeField] private string requiredTagSubstring = "Ice";
        [SerializeField] private Vector2 guiPos = new Vector2(10f, 320f);
        [Tooltip("Show per-contact details in the dump.")]
        [SerializeField] private bool dumpContactDetails = true;

        private Rigidbody2D rb;

        // Ring buffer
        private const int RING = 60;
        private const int MAX_CONTACTS = 16;
        private struct Snap
        {
            public int frame;
            public float time;
            public string state;
            public string materialName;
            public Vector2 rbPos;
            public Vector2 dPos;
            public Vector2 vel;
            public Vector2 surfN;       // SlimeController.surfaceNormal (filtered EffectiveNormal)
            public float surfNDeltaDeg; // angle change vs prev frame
            public Vector2 effN;        // probe.EffectiveNormal (raw, no smoothing)
            public Vector2 surfTangent;
            public int contactCount;
            public int activeCount;
            public int activeChurn;     // samples that flipped active state this frame
            public int phaseAIter;
            public float phaseARemPen;
            public float netPushMag;    // recomputed legacy-equivalent push magnitude (from contact buffer)
            public Vector2 netPushDir;
            public float appliedPushMag; // actual last-iter Phase A push magnitude (probe.LastDepenPushMag)
            public Vector2 appliedPushDir;
            public int selectedIdx;
            public string primaryName;
            // Per-contact (only first MAX_CONTACTS captured for the dump)
            public int captureCount;
            public ContactView[] contacts;
        }
        private struct ContactView
        {
            public string name;
            public Vector2 normal;
            public float dist;
            public float depth;     // radius - dist (negative if not penetrating)
            public bool active;
            public bool wasActive;  // was active in prev frame
        }

        private readonly Snap[] ring = new Snap[RING];
        private int ringHead;
        private int ringCount;

        // State for delta computation
        private Vector2 prevRbPos;
        private Vector2 prevDPos;
        private Vector2 prevSurfN;
        private bool prevHasFrame;
        // Active state of each contact in prev frame, keyed by collider InstanceID
        // (sample index isn't stable frame-to-frame in MultiProbe).
        private System.Collections.Generic.Dictionary<int, bool> prevActiveByCollider
            = new System.Collections.Generic.Dictionary<int, bool>(32);
        private System.Collections.Generic.Dictionary<int, bool> currActiveByCollider
            = new System.Collections.Generic.Dictionary<int, bool>(32);

        private float lastAutoDumpTime = -999f;
        private GUIStyle labelStyle;

        // OnGUI summary
        private string lastVerdict = "—";
        private int lastWindowJudderEvents;
        private int windowJudderEvents;
        private float windowStart;

        private void Awake()
        {
            if (slime == null) slime = GetComponent<SlimeController>();
            if (slime == null) slime = FindAnyObjectByType<SlimeController>();
            if (slime != null) rb = slime.GetComponent<Rigidbody2D>();
            for (int i = 0; i < RING; i++) ring[i].contacts = new ContactView[MAX_CONTACTS];
            windowStart = Time.time;
        }

        private void Update()
        {
            if (slime == null) return;
            if (Input.GetKeyDown(dumpKey)) DumpRing("[IceMultiProbeDiagnostic] manual dump");
        }

        private void FixedUpdate()
        {
            if (slime == null || rb == null) return;
            var probe = slime.Probe;
            if (probe == null) return;

            Vector2 rbPos = rb.position;
            Vector2 dPos = prevHasFrame ? rbPos - prevRbPos : Vector2.zero;
            Vector2 surfN = slime.SurfaceNormal;
            Vector2 effN = probe.EffectiveNormal;
            Vector2 surfTangent = slime.SurfaceTangent;

            float surfNDeltaDeg = 0f;
            if (prevHasFrame && prevSurfN.sqrMagnitude > 1e-6f && surfN.sqrMagnitude > 1e-6f)
                surfNDeltaDeg = Vector2.Angle(prevSurfN, surfN);

            // Per-contact capture + active churn
            int contactCount = probe.ContactCount;
            int activeCount = probe.ActiveContactCount;
            int captureCount = Mathf.Min(contactCount, MAX_CONTACTS);
            float radius = slime.SlimeRadius;

            // Multi-probe may emit several samples per collider, so a collider with any active
            // sample counts as "active" for churn detection.
            currActiveByCollider.Clear();
            for (int i = 0; i < contactCount; i++)
            {
                var c = probe.GetContactCollider(i);
                if (c == null) continue;
                int id = c.GetInstanceID();
                bool wasAny = currActiveByCollider.TryGetValue(id, out bool prevVal) && prevVal;
                currActiveByCollider[id] = wasAny || probe.IsContactActive(i);
            }

            int churn = 0;
            // additions: in curr active but not in prev active
            foreach (var kv in currActiveByCollider)
            {
                bool wasActive = prevActiveByCollider.TryGetValue(kv.Key, out bool v) && v;
                if (kv.Value && !wasActive) churn++;
            }
            // removals: in prev active but not in curr active
            foreach (var kv in prevActiveByCollider)
            {
                bool isActive = currActiveByCollider.TryGetValue(kv.Key, out bool v) && v;
                if (kv.Value && !isActive) churn++;
            }

            // Approximation, not the probe's own number: the real Phase A runs over
            // depenetrationBuffer's N samples; only the post-dedupe contacts are readable here.
            Vector2 netPush = Vector2.zero;
            for (int i = 0; i < contactCount; i++)
            {
                if (!probe.IsContactActive(i)) continue;
                float dist = probe.GetContactDistance(i);
                if (dist >= radius) continue;
                Vector2 n = probe.GetContactNormal(i);
                float depth = radius - dist;
                netPush += n * depth;
            }

            // Capture snapshot
            Snap s = ring[ringHead];
            s.frame = Time.frameCount;
            s.time = Time.time;
            s.state = slime.CurrentState.ToString();
            s.materialName = ResolveSurfaceMaterialName(slime.SurfaceCollider);
            s.rbPos = rbPos;
            s.dPos = dPos;
            s.vel = slime.Velocity;
            s.surfN = surfN;
            s.surfNDeltaDeg = surfNDeltaDeg;
            s.effN = effN;
            s.surfTangent = surfTangent;
            s.contactCount = contactCount;
            s.activeCount = activeCount;
            s.activeChurn = churn;
            s.phaseAIter = probe.PhaseAIterations;
            s.phaseARemPen = probe.PhaseARemainingPenetration;
            s.netPushMag = netPush.magnitude;
            s.netPushDir = netPush.sqrMagnitude > 1e-8f ? netPush.normalized : Vector2.zero;
            s.appliedPushMag = probe.LastDepenPushMag;
            s.appliedPushDir = probe.LastDepenPushDir;
            s.selectedIdx = probe.SelectedIndex;
            var primaryCol = slime.SurfaceCollider;
            s.primaryName = primaryCol != null ? primaryCol.name : "<none>";
            s.captureCount = captureCount;
            for (int i = 0; i < captureCount; i++)
            {
                var col = probe.GetContactCollider(i);
                int id = col != null ? col.GetInstanceID() : 0;
                bool wasActive = id != 0 && prevActiveByCollider.TryGetValue(id, out bool v) && v;
                s.contacts[i].name = col != null ? col.name : "<null>";
                s.contacts[i].normal = probe.GetContactNormal(i);
                s.contacts[i].dist = probe.GetContactDistance(i);
                s.contacts[i].depth = radius - s.contacts[i].dist;
                s.contacts[i].active = probe.IsContactActive(i);
                s.contacts[i].wasActive = wasActive;
            }
            ring[ringHead] = s;
            ringHead = (ringHead + 1) % RING;
            if (ringCount < RING) ringCount++;

            // Auto-dump triggers (only while attached, and tag matches)
            bool tagMatches = string.IsNullOrEmpty(requiredTagSubstring)
                || (s.materialName != null
                    && s.materialName.IndexOf(requiredTagSubstring, System.StringComparison.OrdinalIgnoreCase) >= 0);
            bool inAttached = slime.CurrentState == SlimeController.SlimeState.Attached;

            string trigger = null;
            if (autoDumpOnJudder && tagMatches && inAttached
                && Time.time - lastAutoDumpTime > autoDumpCooldown)
            {
                if (prevHasFrame
                    && dPos.sqrMagnitude > 1e-6f
                    && prevDPos.sqrMagnitude > 1e-6f
                    && Vector2.Dot(dPos, prevDPos) < 0f)
                {
                    trigger = $"dPos reversal ({Vector2.Angle(prevDPos, dPos):F0}°)";
                }
                else if (surfNDeltaDeg > normalJumpThresholdDeg)
                {
                    trigger = $"surfN jump {surfNDeltaDeg:F1}°";
                }
                else if (churn >= activeChurnThreshold)
                {
                    trigger = $"active churn {churn}";
                }
            }

            if (trigger != null)
            {
                lastAutoDumpTime = Time.time;
                windowJudderEvents++;
                lastVerdict = trigger;
                DumpRing($"[IceMultiProbeDiagnostic] auto-dump on {trigger}");
            }

            if (Time.time - windowStart >= 1f)
            {
                lastWindowJudderEvents = windowJudderEvents;
                windowJudderEvents = 0;
                windowStart = Time.time;
            }

            // Roll over state
            prevRbPos = rbPos;
            prevDPos = dPos;
            prevSurfN = surfN;
            prevHasFrame = true;
            // Swap instead of copy; the old prev is cleared at the top of the next FixedUpdate.
            var tmp = prevActiveByCollider;
            prevActiveByCollider = currActiveByCollider;
            currActiveByCollider = tmp;
        }

        private static string ResolveSurfaceMaterialName(Collider2D col)
        {
            if (col == null) return "<none>";
            var tag = col.GetComponentInParent<SurfaceTag>();
            if (tag == null) return "<no-tag>";
            if (tag.material == null) return "<no-material>";
            return tag.material.name;
        }

        private void OnGUI()
        {
            if (slime == null) return;
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    alignment = TextAnchor.UpperLeft,
                    richText = true,
                };
                labelStyle.normal.textColor = Color.white;
            }

            string mat = slime.SurfaceCollider != null
                ? ResolveSurfaceMaterialName(slime.SurfaceCollider) : "<none>";
            int aC = slime.Probe != null ? slime.Probe.ActiveContactCount : 0;
            int cC = slime.Probe != null ? slime.Probe.ContactCount : 0;

            string text =
                $"[Ice MultiProbe Diag]  mat=<color=#80c0ff>{mat}</color>  probe={slime.CurrentProbeMode}\n" +
                $"  contacts: total={cC}  active={aC}  selected={(slime.Probe != null ? slime.Probe.SelectedIndex : -1)}\n" +
                $"  judder events (last 1s): {lastWindowJudderEvents}\n" +
                $"  last trigger: {lastVerdict}\n" +
                $"  [{dumpKey}] dump last {ringCount} frames";

            var rect = new Rect(guiPos.x, guiPos.y, 460f, 110f);
            GUI.Label(rect, text, labelStyle);
        }

        private void DumpRing(string header)
        {
            if (ringCount == 0) { Debug.Log($"{header} — ring empty"); return; }

            var sb = new StringBuilder(8192);
            sb.AppendLine(header);
            sb.AppendLine(
                "  idx frame   t      state    mat            cC aC churn iter remPen  appliedMag appliedDir        netPushMag netPushDir       surfNΔ°  surfN              effN               tangent            dPos                 |dPos|  vel                primary");

            int start = (ringHead - ringCount + RING) % RING;
            for (int i = 0; i < ringCount; i++)
            {
                var s = ring[(start + i) % RING];
                sb.AppendFormat(
                    "  {0,3} {1,6} {2,5:F2}  {3,-8} {4,-13}  {5,2} {6,2}  {7,3}    {8,2}  {9,5:F4}  {10,8:F5}  ({11,5:F2},{12,5:F2})   {13,8:F5}  ({14,5:F2},{15,5:F2})  {16,5:F1}    ({17,5:F2},{18,5:F2})  ({19,5:F2},{20,5:F2})  ({21,5:F2},{22,5:F2})  ({23,7:F4},{24,7:F4})  {25,6:F4}  ({26,6:F2},{27,6:F2})  {28}\n",
                    i, s.frame, s.time, s.state, Trunc(s.materialName, 13),
                    s.contactCount, s.activeCount, s.activeChurn,
                    s.phaseAIter, s.phaseARemPen,
                    s.appliedPushMag, s.appliedPushDir.x, s.appliedPushDir.y,
                    s.netPushMag, s.netPushDir.x, s.netPushDir.y,
                    s.surfNDeltaDeg,
                    s.surfN.x, s.surfN.y,
                    s.effN.x, s.effN.y,
                    s.surfTangent.x, s.surfTangent.y,
                    s.dPos.x, s.dPos.y, s.dPos.magnitude,
                    s.vel.x, s.vel.y,
                    Trunc(s.primaryName, 22));
            }

            if (dumpContactDetails)
            {
                sb.AppendLine();
                sb.AppendLine("  Per-contact details (active=A, was-active-prev=W, '*' = primary):");
                for (int i = 0; i < ringCount; i++)
                {
                    var s = ring[(start + i) % RING];
                    if (s.captureCount == 0) continue;
                    sb.AppendFormat("  frame {0}  ", s.frame);
                    for (int k = 0; k < s.captureCount; k++)
                    {
                        var c = s.contacts[k];
                        bool isPrimary = (k == s.selectedIdx);
                        char a = c.active ? 'A' : '.';
                        char w = c.wasActive ? 'W' : '.';
                        char p = isPrimary ? '*' : ' ';
                        sb.AppendFormat("[{0}{1}{2}{3} n=({4,5:F2},{5,5:F2}) d={6:F4} pen={7:F4} {8}] ",
                            k, a, w, p, c.normal.x, c.normal.y, c.dist, c.depth, Trunc(c.name, 10));
                    }
                    sb.AppendLine();
                }
            }
            Debug.Log(sb.ToString());
        }

        private static string Trunc(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n);
        }
    }
}
