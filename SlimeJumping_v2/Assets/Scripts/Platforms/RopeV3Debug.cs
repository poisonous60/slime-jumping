using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using SlimeJumping.Controller;

namespace SlimeJumping.Platforms
{
    // Investigates V3 rope-rider coupling bugs (spurious detach, opposite-
    // direction kick on landing, rope drift while charging). Pulls per-FU
    // snapshots from RopeV3 + SlimeController into a ring buffer; on each
    // configured trigger (detach / attach / high speed / manual mark) dumps
    // a window of frames around the event to the console with a cooldown so
    // multiple events per session are captured.
    //
    // Reads only — never writes to controller or rope. Per memory rule:
    // debug pulls from controller/rope, never pushes; no debug fields in
    // SlimeController.
    [RequireComponent(typeof(RopeV3))]
    [DefaultExecutionOrder(100)]
    public class RopeV3Debug : MonoBehaviour
    {
        [Header("Bindings")]
        [SerializeField] RopeV3 rope;
        [SerializeField] SlimeController slime;

        [Header("Recording")]
        [SerializeField, Range(16, 512)] int historyFrames = 120;
        [Tooltip("How many post-event FUs to also capture before dumping. " +
                 "0 = dump immediately on event with only the lead-up frames.")]
        [SerializeField, Range(0, 60)] int postEventFrames = 6;
        [Tooltip("Skip dumps that fire within this many FUs of the previous " +
                 "dump, to keep console readable when an event chain fires.")]
        [SerializeField, Min(0)] int eventCooldownFus = 30;

        [Header("Triggers")]
        [SerializeField] bool triggerOnDetach = true;
        [Tooltip("Airborne → Attached/Charging on this rope. Use to inspect " +
                 "landing impulse direction and impact-velocity handling.")]
        [SerializeField] bool triggerOnAttach = true;
        [Tooltip("Slime velocity magnitude exceeds threshold while attached " +
                 "to this rope (catches rope-kicks-slime-wrong-direction).")]
        [SerializeField] bool triggerOnHighSpeed = true;
        [SerializeField, Min(0f)] float highSpeedThreshold = 6f;
        [Tooltip("Slime velocity ⋅ rope contact velocity < threshold (negative " +
                 "= opposite direction). Catches rope flinging slime against " +
                 "its own motion. -1 disables.")]
        [SerializeField] float oppositeKickDotThreshold = -2f;

        [Header("Manual mark")]
        [SerializeField] Key manualMarkKey = Key.M;

        [Header("HUD")]
        [SerializeField] bool drawHud = true;
        [SerializeField] Vector2 hudAnchor = new Vector2(10f, 220f);
        [SerializeField] Vector2 hudSize = new Vector2(380f, 320f);

        [Header("Gizmos")]
        [SerializeField] bool drawGizmos = true;

        struct Frame
        {
            public int fu;          // monotonic FU counter
            public float t;
            public float dt;        // FU dt actually used

            // Rope-side
            public bool ropeRiderActive;
            public bool ropeAttachValid;
            public int seg;
            public float segT;
            public Vector2 contactPos;
            public Vector2 contactVel;
            public float impactDepth;
            public Vector2 pendingCarry;
            public Vector2 lastCarryRaw;
            public Vector2 lastCarryReturned;
            public bool lastCarryClamped;
            public float maxTensionRatio;

            // Slime-side
            public SlimeController.SlimeState slimeState;
            public bool slimeTouching;
            public Vector2 slimePos;
            public Vector2 slimeVel;
            public bool surfaceIsThisRope;
            public float distSlimeToContact;
            public float slimeAboveContactY;
            public float slimeDeltaY;       // signed Δ since previous frame
            public float contactDeltaY;     // signed Δ since previous frame
            public float velDotContactVel;  // < 0 means opposite direction
            public Vector2 surfaceNormal;
            public float surfaceAngleDeg;   // degrees from world-up
        }

        Frame[] ring;
        int head;
        int count;
        int fuCounter;
        int lastDumpFu = int.MinValue;

        // Event countdown — when set > 0, every FU we decrement; on 0, dump.
        int pendingDumpAt;
        bool hasPendingDump;
        string pendingReason;
        int pendingTriggerFu;

        // For state-edge detection
        bool prevSurfaceWasThisRope;
        SlimeController.SlimeState prevSlimeState;
        bool prevValid;
        Vector2 prevSlimePos;
        Vector2 prevContactPos;

        // Quick lookup of recent events for HUD
        readonly Queue<string> recentEventLog = new Queue<string>();
        const int MaxRecentEvents = 5;

        void Reset() { rope = GetComponent<RopeV3>(); }

        void Awake()
        {
            if (rope == null) rope = GetComponent<RopeV3>();
            if (slime == null) slime = FindFirstObjectByType<SlimeController>();
            EnsureRing();
        }

        void EnsureRing()
        {
            int n = Mathf.Max(16, historyFrames);
            if (ring == null || ring.Length != n)
            {
                ring = new Frame[n];
                head = 0;
                count = 0;
            }
        }

        void FixedUpdate()
        {
            if (rope == null || slime == null) return;
            EnsureRing();
            fuCounter++;

            var snap = rope.GetDebugSnapshot();

            bool surfMine = false;
            var surf = slime.SurfaceCollider;
            if (surf != null && rope.Edge != null && surf == rope.Edge) surfMine = true;

            Vector2 slimePos = slime.Position;
            Vector2 slimeVel = slime.Velocity;
            Vector2 contactVel = rope.SampleVelocityAt(snap.contactPos);
            float dist = (slimePos - snap.contactPos).magnitude;
            float aboveY = slimePos.y - snap.contactPos.y;

            float slimeDy = prevValid ? (slimePos.y - prevSlimePos.y) : 0f;
            float contactDy = prevValid ? (snap.contactPos.y - prevContactPos.y) : 0f;
            float vDot = Vector2.Dot(slimeVel, contactVel);
            Vector2 surfaceNormal = slime.SurfaceNormal;
            float surfaceAngleDeg = Mathf.Atan2(surfaceNormal.x, surfaceNormal.y) * Mathf.Rad2Deg;

            var f = new Frame
            {
                fu = fuCounter,
                t = Time.time,
                dt = Time.fixedDeltaTime,
                ropeRiderActive = snap.riderActive,
                ropeAttachValid = snap.attachSnapshotValid,
                seg = snap.riderSegment,
                segT = snap.riderSegmentT,
                contactPos = snap.contactPos,
                contactVel = contactVel,
                impactDepth = snap.activeImpactDepth,
                pendingCarry = snap.pendingCarry,
                lastCarryRaw = snap.lastCarryRaw,
                lastCarryReturned = snap.lastCarryReturned,
                lastCarryClamped = snap.lastCarryClamped,
                maxTensionRatio = snap.maxTensionRatio,
                slimeState = slime.CurrentState,
                slimeTouching = slime.IsTouchingSurface,
                slimePos = slimePos,
                slimeVel = slimeVel,
                surfaceIsThisRope = surfMine,
                distSlimeToContact = dist,
                slimeAboveContactY = aboveY,
                slimeDeltaY = slimeDy,
                contactDeltaY = contactDy,
                velDotContactVel = vDot,
                surfaceNormal = surfaceNormal,
                surfaceAngleDeg = surfaceAngleDeg,
            };

            ring[head] = f;
            head = (head + 1) % ring.Length;
            if (count < ring.Length) count++;

            // Trigger detection — only fire when nothing is already pending.
            if (!hasPendingDump && (fuCounter - lastDumpFu) >= eventCooldownFus)
            {
                string reason = DetectTrigger(f);
                if (reason != null)
                {
                    hasPendingDump = true;
                    pendingReason = reason;
                    pendingTriggerFu = fuCounter;
                    pendingDumpAt = fuCounter + Mathf.Max(0, postEventFrames);
                }
            }

            if (hasPendingDump && fuCounter >= pendingDumpAt)
            {
                DumpEvent(pendingReason, pendingTriggerFu);
                lastDumpFu = fuCounter;
                hasPendingDump = false;
                pendingReason = null;
            }

            // Manual mark via key — captured outside FixedUpdate normally, but
            // checking here is fine since input system polls at fixed cadence.
            var kb = Keyboard.current;
            if (kb != null && kb[manualMarkKey].wasPressedThisFrame)
                MarkManualEvent();

            prevSurfaceWasThisRope = f.surfaceIsThisRope;
            prevSlimeState = f.slimeState;
            prevSlimePos = slimePos;
            prevContactPos = snap.contactPos;
            prevValid = true;
        }

        string DetectTrigger(in Frame f)
        {
            if (!prevValid) return null;

            bool prevWasRiding = prevSurfaceWasThisRope
                && (prevSlimeState == SlimeController.SlimeState.Attached
                 || prevSlimeState == SlimeController.SlimeState.Charging);
            bool prevWasAirborne = prevSlimeState == SlimeController.SlimeState.Airborne;
            bool nowRiding = f.surfaceIsThisRope
                && (f.slimeState == SlimeController.SlimeState.Attached
                 || f.slimeState == SlimeController.SlimeState.Charging);
            bool nowAirborne = f.slimeState == SlimeController.SlimeState.Airborne;

            if (triggerOnDetach && prevWasRiding && nowAirborne)
                return "Detach";

            if (triggerOnAttach && prevWasAirborne && nowRiding)
                return "Attach";

            if (triggerOnHighSpeed && nowRiding && f.slimeVel.sqrMagnitude > highSpeedThreshold * highSpeedThreshold)
                return $"HighSpeed(|v|={f.slimeVel.magnitude:F1})";

            if (oppositeKickDotThreshold >= -100f && nowRiding
                && f.velDotContactVel < oppositeKickDotThreshold)
                return $"OppositeKick(dot={f.velDotContactVel:F2})";

            return null;
        }

        public void MarkManualEvent()
        {
            if (hasPendingDump) return;
            hasPendingDump = true;
            pendingReason = "Manual";
            pendingTriggerFu = fuCounter;
            pendingDumpAt = fuCounter + Mathf.Max(0, postEventFrames);
        }

        [ContextMenu("Mark Now")]
        public void MarkNowMenu() => MarkManualEvent();

        [ContextMenu("Clear Recording")]
        public void ClearRecording()
        {
            count = 0;
            head = 0;
            hasPendingDump = false;
            recentEventLog.Clear();
        }

        [ContextMenu("Dump Now")]
        public void DumpNow() => DumpEvent("Forced", fuCounter);

        void DumpEvent(string reason, int triggerFu)
        {
            if (count == 0)
            {
                Debug.Log($"[RopeV3Debug:{name}] {reason} — no frames recorded", this);
                return;
            }

            var sb = new StringBuilder(8192);
            sb.Append("[RopeV3Debug:").Append(name).Append("] EVENT=").Append(reason)
              .Append(" triggerFU=").Append(triggerFu)
              .Append(" frames=").Append(count).AppendLine();
            sb.AppendLine(
                "FU       state    touch surfMine rA aV seg(t)     " +
                "contY    slimeY   dist   dY(s)    dY(c)   surfAng vSlime           vRope            v.v   " +
                "pendC          consC          clamp impact tens");
            int start = (head - count + ring.Length) % ring.Length;
            for (int i = 0; i < count; i++)
            {
                var fr = ring[(start + i) % ring.Length];
                bool isTrigger = fr.fu == triggerFu;
                if (isTrigger) sb.Append(">>>");
                else           sb.Append("   ");
                sb.Append(fr.fu.ToString("D6")).Append(' ')
                  .Append(fr.slimeState.ToString().PadRight(8)).Append(' ')
                  .Append(fr.slimeTouching ? "T " : "F ").Append(' ')
                  .Append(fr.surfaceIsThisRope ? "Y  " : "n  ").Append(' ')
                  .Append(fr.ropeRiderActive ? "1 " : "0 ")
                  .Append(fr.ropeAttachValid ? "1 " : "0 ")
                  .Append(fr.seg.ToString("D2")).Append('(').Append(fr.segT.ToString("F2")).Append(") ")
                  .Append(fr.contactPos.y.ToString("F3").PadLeft(8)).Append(' ')
                  .Append(fr.slimePos.y.ToString("F3").PadLeft(8)).Append(' ')
                  .Append(fr.distSlimeToContact.ToString("F3").PadLeft(6)).Append(' ')
                  .Append(fr.slimeDeltaY.ToString("+0.000;-0.000").PadLeft(8)).Append(' ')
                  .Append(fr.contactDeltaY.ToString("+0.000;-0.000").PadLeft(8)).Append(' ')
                  .Append(fr.surfaceAngleDeg.ToString("+0.0;-0.0").PadLeft(6)).Append("° ")
                  .Append('(').Append(fr.slimeVel.x.ToString("+0.00;-0.00")).Append(',')
                              .Append(fr.slimeVel.y.ToString("+0.00;-0.00")).Append(") ")
                  .Append('(').Append(fr.contactVel.x.ToString("+0.00;-0.00")).Append(',')
                              .Append(fr.contactVel.y.ToString("+0.00;-0.00")).Append(") ")
                  .Append(fr.velDotContactVel.ToString("+0.00;-0.00").PadLeft(6)).Append(' ')
                  .Append('(').Append(fr.pendingCarry.x.ToString("F3")).Append(',')
                              .Append(fr.pendingCarry.y.ToString("F3")).Append(") ")
                  .Append('(').Append(fr.lastCarryReturned.x.ToString("F3")).Append(',')
                              .Append(fr.lastCarryReturned.y.ToString("F3")).Append(") ")
                  .Append(fr.lastCarryClamped ? "CLAMP " : "      ")
                  .Append(fr.impactDepth.ToString("F3")).Append(' ')
                  .Append(fr.maxTensionRatio.ToString("F2"))
                  .AppendLine();
            }
            Debug.Log(sb.ToString(), this);

            recentEventLog.Enqueue($"FU{triggerFu} {reason}");
            while (recentEventLog.Count > MaxRecentEvents) recentEventLog.Dequeue();
        }

        Frame? Latest()
        {
            if (count == 0) return null;
            int idx = (head - 1 + ring.Length) % ring.Length;
            return ring[idx];
        }

        void OnGUI()
        {
            if (!drawHud) return;
            if (!Application.isPlaying) return;
            if (count == 0) return;

            var rect = new Rect(hudAnchor.x, hudAnchor.y, hudSize.x, hudSize.y);
            GUI.Box(rect, hasPendingDump
                ? $"RopeV3 Debug — {pendingReason} (dump in {pendingDumpAt - fuCounter} FU)"
                : $"RopeV3 Debug — recording  FU={fuCounter}");

            var f = Latest().Value;
            var inner = new Rect(rect.x + 8f, rect.y + 22f, rect.width - 16f, rect.height - 30f);
            GUILayout.BeginArea(inner);
            GUILayout.Label($"slime: {f.slimeState}  touch={f.slimeTouching}  surfMine={f.surfaceIsThisRope}");
            GUILayout.Label($"rope:  riderAct={f.ropeRiderActive}  attachValid={f.ropeAttachValid}");
            GUILayout.Label($"seg:   {f.seg}(t={f.segT:F2})  dist={f.distSlimeToContact:F3}  aboveY={f.slimeAboveContactY:+0.000;-0.000}");
            GUILayout.Label($"vSlime=({f.slimeVel.x:+0.00;-0.00},{f.slimeVel.y:+0.00;-0.00})  |v|={f.slimeVel.magnitude:F2}");
            GUILayout.Label($"vRope =({f.contactVel.x:+0.00;-0.00},{f.contactVel.y:+0.00;-0.00})  v⋅v={f.velDotContactVel:+0.00;-0.00}");
            GUILayout.Label($"surfAng={f.surfaceAngleDeg:+0.0;-0.0}°  norm=({f.surfaceNormal.x:+0.00;-0.00},{f.surfaceNormal.y:+0.00;-0.00})");
            GUILayout.Label($"pendCarry|={f.pendingCarry.magnitude:F3}|  cons|={f.lastCarryReturned.magnitude:F3}|  clamp={f.lastCarryClamped}");
            GUILayout.Label($"impactDepth={f.impactDepth:F3}  maxTens={f.maxTensionRatio:F2}");
            GUILayout.Label($"frames={count}/{ring.Length}  cooldown={Mathf.Max(0, eventCooldownFus - (fuCounter - lastDumpFu))}");
            GUILayout.Space(2);
            GUILayout.Label($"Mark key: [{manualMarkKey}]");
            if (recentEventLog.Count > 0)
            {
                GUILayout.Label("Recent events:");
                foreach (var s in recentEventLog) GUILayout.Label("  " + s);
            }
            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Mark")) MarkManualEvent();
            if (GUILayout.Button("Dump")) DumpNow();
            if (GUILayout.Button("Clear")) ClearRecording();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void OnDrawGizmos()
        {
            if (!drawGizmos) return;
            if (rope == null || slime == null) return;
            if (!Application.isPlaying) return;
            var lf = Latest();
            if (!lf.HasValue) return;
            var f = lf.Value;

            Gizmos.color = f.surfaceIsThisRope ? Color.magenta : Color.cyan;
            Gizmos.DrawLine(f.slimePos, f.contactPos);
            Gizmos.DrawWireSphere(f.contactPos, 0.08f);

            if (f.contactVel.sqrMagnitude > 1e-4f)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(f.contactPos, f.contactPos + f.contactVel * 0.2f);
            }
            if (f.slimeVel.sqrMagnitude > 1e-4f)
            {
                Gizmos.color = f.velDotContactVel < 0f ? Color.red : Color.white;
                Gizmos.DrawLine(f.slimePos, f.slimePos + f.slimeVel * 0.2f);
            }
            if (f.lastCarryReturned.sqrMagnitude > 1e-6f)
            {
                Gizmos.color = f.lastCarryClamped ? Color.red : Color.yellow;
                Gizmos.DrawLine(f.contactPos, f.contactPos + f.lastCarryReturned * 5f);
            }
        }
    }
}
