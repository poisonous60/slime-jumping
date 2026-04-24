using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Debugging
{
    /// <summary>
    /// Diagnoses whether "곡선으로 밀려가서 부딪칠 때 드득거림" is visual or physics.
    /// Attach to the Slime GameObject (or any object with a SlimeController reference).
    ///
    /// What it measures each FixedUpdate:
    ///   - rb.position Δ per frame + direction reversal (physics jitter signal)
    ///   - |transform.position - rb.position| gap (visual-vs-physics drift signal)
    ///   - Primary collider flip count over last 1s (rapid primary switching)
    ///   - SurfaceNormal rotation rate in deg/frame (EffectiveNormal wiggle)
    ///   - crawlVelocity, state, active contact count
    ///
    /// Triggers a ring-buffer dump to Console:
    ///   - Automatically on a detected direction reversal while Attached (cooldown 1s)
    ///   - Manually via dumpKey (F2 by default)
    ///
    /// How to read the OnGUI readout:
    ///   dPos reversals > 0 while Attached  → PHYSICS jitter (rb.position itself wiggles)
    ///   dPos reversals = 0 but jitter visible on screen → VISUAL (interpolation / child transform / camera)
    ///   |tf - rb| ≠ 0 → some script is writing transform directly (visual-physics decoupling)
    /// </summary>
    public class CurveSlideJitterProbe : MonoBehaviour
    {
        [SerializeField] private SlimeController slime;
        [SerializeField] private KeyCode dumpKey = KeyCode.F2;
        [SerializeField] private bool autoDumpOnJitter = true;
        [Tooltip("dPos magnitude squared above this is considered real motion (not snap noise).")]
        [SerializeField] private float motionThresholdSq = 1e-6f;
        [Tooltip("Seconds between auto-dumps to avoid spam.")]
        [SerializeField] private float autoDumpCooldown = 1.0f;
        [Tooltip("OnGUI readout position.")]
        [SerializeField] private Vector2 guiPos = new Vector2(10f, 160f);

        private Rigidbody2D rb;

        private const int RING = 60;
        private struct Snap
        {
            public int frame;
            public float time;
            public SlimeController.SlimeState state;
            public Vector2 rbPos;
            public Vector2 tfPos;
            public Vector2 rbVel;
            public Vector2 ctrlVel;
            public Vector2 dPos;
            public Vector2 surfN;
            public int primaryId;
            public string primaryName;
            public int activeContacts;
            public float crawl;
            public float stick;
        }
        private readonly Snap[] ring = new Snap[RING];
        private int ringHead;
        private int ringCount;

        private Vector2 prevRbPos;
        private Vector2 prevDPos;
        private Vector2 prevSurfN;
        private int prevPrimaryId = -1;
        private bool initialized;

        // 1-second sliding-window counters
        private float windowStart;
        private int windowReversals;
        private int windowPrimaryFlips;
        private float maxTfRbGap;
        private float maxNormalDegPerFrame;

        // Persistent "last tick" readouts for OnGUI
        private int lastWindowReversals;
        private int lastWindowPrimaryFlips;
        private float lastMaxTfRbGap;
        private float lastMaxNormalDeg;

        private float lastAutoDumpTime = -999f;

        private GUIStyle labelStyle;

        private void Awake()
        {
            if (slime == null) slime = GetComponent<SlimeController>();
            if (slime == null) slime = FindAnyObjectByType<SlimeController>();
            if (slime != null) rb = slime.GetComponent<Rigidbody2D>();
        }

        private void Update()
        {
            if (slime == null) return;
            if (Input.GetKeyDown(dumpKey)) DumpRing("[CurveSlideJitterProbe] manual dump");
        }

        private void FixedUpdate()
        {
            if (slime == null || rb == null) return;

            Vector2 rbPos = rb.position;
            Vector2 tfPos = slime.transform.position;
            Vector2 surfN = slime.IsTouchingSurface ? slime.SurfaceNormal : Vector2.up;
            var col = slime.SurfaceCollider;
            int primaryId = col != null ? col.GetInstanceID() : 0;

            if (!initialized)
            {
                prevRbPos = rbPos;
                prevSurfN = surfN;
                prevPrimaryId = primaryId;
                windowStart = Time.time;
                initialized = true;
                return;
            }

            Vector2 dPos = rbPos - prevRbPos;

            // Reversal detection: physics jitter signature
            bool significant = dPos.sqrMagnitude > motionThresholdSq
                            && prevDPos.sqrMagnitude > motionThresholdSq;
            bool reversal = significant && Vector2.Dot(dPos, prevDPos) < 0f;

            if (reversal) windowReversals++;
            if (primaryId != prevPrimaryId) windowPrimaryFlips++;

            float tfRbGap = (tfPos - rbPos).magnitude;
            if (tfRbGap > maxTfRbGap) maxTfRbGap = tfRbGap;

            float normalDeg = 0f;
            if (prevSurfN.sqrMagnitude > 1e-6f && surfN.sqrMagnitude > 1e-6f)
                normalDeg = Vector2.Angle(prevSurfN, surfN);
            if (normalDeg > maxNormalDegPerFrame) maxNormalDegPerFrame = normalDeg;

            // Record snapshot
            Snap s;
            s.frame = Time.frameCount;
            s.time = Time.time;
            s.state = slime.CurrentState;
            s.rbPos = rbPos;
            s.tfPos = tfPos;
            s.rbVel = rb.linearVelocity;
            s.ctrlVel = slime.Velocity;
            s.dPos = dPos;
            s.surfN = surfN;
            s.primaryId = primaryId;
            s.primaryName = col != null ? col.name : "<none>";
            s.activeContacts = slime.Probe != null ? slime.Probe.ActiveContactCount : 0;
            s.crawl = slime.CrawlVelocity;
            s.stick = slime.StickTimer;
            ring[ringHead] = s;
            ringHead = (ringHead + 1) % RING;
            if (ringCount < RING) ringCount++;

            // Auto-dump on reversal while Attached
            if (autoDumpOnJitter && reversal
                && slime.CurrentState == SlimeController.SlimeState.Attached
                && Time.time - lastAutoDumpTime > autoDumpCooldown)
            {
                lastAutoDumpTime = Time.time;
                DumpRing($"[CurveSlideJitterProbe] auto-dump on dPos reversal (angle {Vector2.Angle(prevDPos, dPos):F0}°)");
            }

            // Window rollover every 1 second
            if (Time.time - windowStart >= 1f)
            {
                lastWindowReversals = windowReversals;
                lastWindowPrimaryFlips = windowPrimaryFlips;
                lastMaxTfRbGap = maxTfRbGap;
                lastMaxNormalDeg = maxNormalDegPerFrame;
                windowReversals = 0;
                windowPrimaryFlips = 0;
                maxTfRbGap = 0f;
                maxNormalDegPerFrame = 0f;
                windowStart = Time.time;
            }

            prevRbPos = rbPos;
            prevDPos = dPos;
            prevSurfN = surfN;
            prevPrimaryId = primaryId;
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

            string verdict;
            if (lastWindowReversals > 0)
                verdict = "<color=#ff8080>PHYSICS jitter (dPos reversals)</color>";
            else if (lastMaxTfRbGap > 0.001f)
                verdict = "<color=#ffd080>VISUAL drift (tf ≠ rb)</color>";
            else
                verdict = "<color=#a0ffa0>no jitter signal</color>";

            string text =
                $"[Jitter Probe] {verdict}\n" +
                $"  dPos reversals (last 1s): {lastWindowReversals}\n" +
                $"  primary flips (last 1s): {lastWindowPrimaryFlips}\n" +
                $"  |tf - rb| max (1s): {lastMaxTfRbGap:F4}\n" +
                $"  surfN Δ max (1s): {lastMaxNormalDeg:F1}°/frame\n" +
                $"  state={slime.CurrentState} contacts={slime.Probe?.ActiveContactCount ?? 0}\n" +
                $"  [{dumpKey}] dump last {ringCount} frames";

            var rect = new Rect(guiPos.x, guiPos.y, 420f, 140f);
            GUI.Label(rect, text, labelStyle);
        }

        private void DumpRing(string header)
        {
            if (ringCount == 0)
            {
                Debug.Log($"{header} — ring empty");
                return;
            }

            var sb = new System.Text.StringBuilder(4096);
            sb.AppendLine(header);
            sb.AppendLine("  idx  frame    t       state      prim                       aC  dPos                  |dPos|   surfN            rbVel              ctrlVel            crawl  stick");

            int start = (ringHead - ringCount + RING) % RING;
            for (int i = 0; i < ringCount; i++)
            {
                var s = ring[(start + i) % RING];
                sb.AppendFormat(
                    "  {0,3}  {1,6}  {2,5:F2}  {3,-9}  {4,-24}  {5,2}  ({6,6:F3},{7,6:F3})  {8,6:F4}  ({9,5:F2},{10,5:F2})  ({11,6:F2},{12,6:F2})  ({13,6:F2},{14,6:F2})  {15,5:F2}  {16,5:F3}\n",
                    i,
                    s.frame,
                    s.time,
                    s.state.ToString(),
                    s.primaryName.Length > 24 ? s.primaryName.Substring(0, 24) : s.primaryName,
                    s.activeContacts,
                    s.dPos.x, s.dPos.y,
                    s.dPos.magnitude,
                    s.surfN.x, s.surfN.y,
                    s.rbVel.x, s.rbVel.y,
                    s.ctrlVel.x, s.ctrlVel.y,
                    s.crawl,
                    s.stick);
            }
            Debug.Log(sb.ToString());
        }
    }
}
