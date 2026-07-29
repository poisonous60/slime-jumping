using System.Text;
using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Platforms;

namespace SlimeJumping.Debugging
{
    /// <summary>
    /// Attach to the SLIME to trace the Bamboo V3 chain: which stalk is engaged, where the
    /// contact -> probe -> attach -> carrier -> impulse chain breaks, and every rider impulse
    /// injected into the stalk. Read-only — never writes slime or stalk state.
    /// </summary>
    [DefaultExecutionOrder(3000)]
    [DisallowMultipleComponent]
    [AddComponentMenu("SlimeJumping/Debug/Bamboo V3 Chain Debug")]
    public class BambooV3ChainDebug : MonoBehaviour
    {
        [Header("Display")]
        [SerializeField] bool showGui = true;
        [SerializeField] Vector2 guiPos = new Vector2(10f, 10f);
        [SerializeField] KeyCode dumpKey = KeyCode.F7;
        [SerializeField, Range(20, 240)] int samplesToDump = 90;

        [Header("Logging")]
        [Tooltip("Log one line whenever the chain stage changes (contact gained, probe picked it up, attached, impulse reached, thrown).")]
        [SerializeField] bool logStageChanges = true;
        [Tooltip("Log one line whenever the slime's state changes while on/near a stalk (Airborne/Attached/Charging), with the stalk's angVel and bend — this is where a jump shows up.")]
        [SerializeField] bool logSlimeStateChanges = true;
        [Tooltip("Log one line whenever a COUPLED (per-frame) rider impulse of at least this magnitude hits the stalk. Coupled impulses during Charging are the 'pressing jump kicks the stalk' symptom. 0 disables.")]
        [SerializeField, Min(0f)] float logCoupledImpulseAbove = 2f;
        [Tooltip("Log a line whenever the stalk throws the slime, with the peak bend and exit speed.")]
        [SerializeField] bool logThrows = true;

        [Header("Thresholds")]
        [Tooltip("Gap (world units) from the slime circle to the nearest segment collider below which we call it contact.")]
        [SerializeField, Min(0f)] float contactGapEpsilon = 0.02f;

        const int RingSize = 240;

        enum Stage
        {
            NoStalk = 0,            // no BambooPlatformV3 in the scene
            NoContact = 1,          // slime is nowhere near the resolved stalk
            ContactNotProbed = 2,   // touching a segment, but SurfaceProbe did not select it
            ProbedNotAttached = 3,  // it IS the surface, but the slime is not Attached/Charging
            AttachedNoCarrier = 4,  // Attached, but the carrier is on a different platform (or none)
            CarrierNoImpulse = 5,   // carrier says this stalk, but ApplyRiderInfluence never ran
            Live = 6,               // impulse path is alive; bend/peak/throw numbers are meaningful
        }

        struct Snap
        {
            public int frame;
            public float time;
            public Stage stage;
            public string stalkName;
            public SlimeController.SlimeState slimeState;
            public float slimeSpeed;
            public float approachSpeed;      // last airborne speed before contact = impact speed
            public bool touching;
            public string surfaceName;
            public bool surfaceIsOurs;
            public bool carrierIsUs;
            public bool riderAttached;
            public float nearestGap;
            public string nearestSegment;
            public float bendDeg;
            public float angVelDeg;
            public float peakDeg;
            public bool returning;
            public float armLen;
            public float tipSpeed;
            public int attachToggles;
            public float attachLockout;
            // Impulse instrumentation (from the stalk's diagnostic getters).
            public float impulseDeg;         // omega injected this step by rider influence
            public int impulseKind;          // 0 none, 1 landing, 2 coupled
            public bool riderCharging;       // isCharging flag the carrier passed
            public float weightTorque;
        }

        SlimeController slime;
        Collider2D slimeCollider;

        BambooPlatformV3[] allStalks;
        BambooPlatformV3 stalk;          // resolved this step
        Collider2D[] segments;           // child colliders of the resolved stalk

        readonly Snap[] ring = new Snap[RingSize];
        int ringHead;
        int ringCount;

        Stage lastStage = Stage.NoStalk;
        SlimeController.SlimeState lastSlimeState = SlimeController.SlimeState.Airborne;
        float lastAirborneSpeed;
        bool prevRiderAttached;
        float peakSeenThisRide;
        float impactSpeedThisRide;

        void Awake()
        {
            slime = GetComponent<SlimeController>();
            if (slime == null) slime = FindFirstObjectByType<SlimeController>();
            if (slime != null) slimeCollider = slime.GetComponent<Collider2D>();
            RefreshStalkList();
        }

        void RefreshStalkList()
        {
            allStalks = FindObjectsByType<BambooPlatformV3>(FindObjectsSortMode.None);
        }

        void RefreshSegments(BambooPlatformV3 forStalk)
        {
            if (forStalk == null) { segments = null; return; }
            var found = forStalk.GetComponentsInChildren<Collider2D>(true);
            int n = 0;
            for (int i = 0; i < found.Length; i++)
                if (found[i].transform != forStalk.transform) n++;

            segments = new Collider2D[n];
            int k = 0;
            for (int i = 0; i < found.Length; i++)
                if (found[i].transform != forStalk.transform) segments[k++] = found[i];
        }

        // Carrier's platform if it is a V3, else the nearest V3 by pivot distance.
        BambooPlatformV3 ResolveStalk()
        {
            if (slime == null) return null;
            if (allStalks == null || allStalks.Length == 0) RefreshStalkList();
            if (allStalks == null || allStalks.Length == 0) return null;

            var carrier = slime.Carrier;
            if (carrier != null)
            {
                var onIt = carrier.CurrentPlatform as BambooPlatformV3;
                if (onIt != null) return onIt;
            }

            Vector2 p = slime.transform.position;
            BambooPlatformV3 best = null;
            float bestSqr = float.PositiveInfinity;
            for (int i = 0; i < allStalks.Length; i++)
            {
                var st = allStalks[i];
                if (st == null) continue;
                float d = ((Vector2)st.PivotWorldPos - p).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = st; }
            }
            return best;
        }

        void FixedUpdate()
        {
            if (slime == null)
            {
                slime = GetComponent<SlimeController>();
                if (slime == null) slime = FindFirstObjectByType<SlimeController>();
                if (slime != null) slimeCollider = slime.GetComponent<Collider2D>();
                if (slime == null) return;
            }

            var resolved = ResolveStalk();
            if (resolved != stalk)
            {
                stalk = resolved;
                RefreshSegments(stalk);
            }

            var s = new Snap
            {
                frame = Time.frameCount,
                time = Time.time,
                nearestGap = float.PositiveInfinity,
                nearestSegment = "-",
                surfaceName = "-",
                stalkName = stalk != null ? stalk.name : "-",
            };

            if (stalk == null)
            {
                s.stage = Stage.NoStalk;
                s.slimeState = slime.CurrentState;
                Push(s);
                LogStateChange(s);
                return;
            }

            s.bendDeg = stalk.CurrentDeflectionDeg;
            s.angVelDeg = stalk.AngularVelocity;
            s.peakDeg = stalk.PeakBendSinceAttach;
            s.returning = stalk.ReturningFromPeak;
            s.riderAttached = stalk.RiderAttached;
            s.tipSpeed = stalk.TipSpeed;
            s.impulseDeg = stalk.LastRiderImpulseDeg;
            s.impulseKind = stalk.LastRiderImpulseKind;
            s.riderCharging = stalk.LastRiderCharging;
            s.weightTorque = stalk.LastWeightTorque;

            s.slimeState = slime.CurrentState;
            s.slimeSpeed = slime.Velocity.magnitude;
            s.attachToggles = slime.AttachToggleCount;
            s.attachLockout = slime.AttachLockoutTimer;

            if (slime.CurrentState == SlimeController.SlimeState.Airborne)
                lastAirborneSpeed = s.slimeSpeed;
            s.approachSpeed = lastAirborneSpeed;

            if (slimeCollider != null && segments != null)
            {
                for (int i = 0; i < segments.Length; i++)
                {
                    var col = segments[i];
                    if (col == null || !col.enabled || !col.gameObject.activeInHierarchy) continue;
                    var d = col.Distance(slimeCollider);
                    if (!d.isValid) continue;
                    if (d.distance < s.nearestGap)
                    {
                        s.nearestGap = d.distance;
                        s.nearestSegment = col.name;
                    }
                }
            }

            var surfaceCol = slime.SurfaceCollider;
            s.touching = slime.IsTouchingSurface;
            if (surfaceCol != null)
            {
                s.surfaceName = surfaceCol.name;
                s.surfaceIsOurs = surfaceCol.GetComponentInParent<BambooPlatformV3>() == stalk;
            }

            var carrier2 = slime.Carrier;
            s.carrierIsUs = carrier2 != null && carrier2.CurrentPlatform == (PlatformMotor)stalk;

            Vector2 arm = stalk.LastRiderWorldPos - stalk.PivotWorldPos;
            s.armLen = arm.magnitude;

            s.stage = Classify(s);
            Push(s);

            // Ride bookkeeping for the throw log.
            if (s.riderAttached)
            {
                if (!prevRiderAttached)
                {
                    peakSeenThisRide = 0f;
                    impactSpeedThisRide = s.approachSpeed;
                }
                if (s.peakDeg > peakSeenThisRide) peakSeenThisRide = s.peakDeg;
            }
            else if (prevRiderAttached && logThrows)
            {
                Debug.Log(
                    $"[BambooV3 {s.stalkName}] rider left — impact {impactSpeedThisRide:F1} m/s, " +
                    $"peak bend {peakSeenThisRide:F1}° (gate {stalk.EjectBendDeg:F1}°), " +
                    $"tip speed now {s.tipSpeed:F1} m/s, slime state {s.slimeState}, slime speed {s.slimeSpeed:F1}", this);
            }
            prevRiderAttached = s.riderAttached;

            // Coupled per-frame impulse: during Charging this is "pressing jump kicks the stalk".
            if (logCoupledImpulseAbove > 0f && s.impulseKind == 2 && Mathf.Abs(s.impulseDeg) >= logCoupledImpulseAbove)
            {
                Debug.Log(
                    $"[BambooV3 {s.stalkName}] COUPLED impulse {s.impulseDeg:F1}°/s  slime={s.slimeState} " +
                    $"charging={(s.riderCharging ? "Y" : "n")}  angVel {s.angVelDeg:F1}°/s  bend {s.bendDeg:F1}°  " +
                    $"slimeSpd {s.slimeSpeed:F1}  arm {s.armLen:F2}", this);
            }

            if (logStageChanges && s.stage != lastStage)
            {
                Debug.Log($"[BambooV3 {s.stalkName}] stage {lastStage} -> {s.stage} : {StageText(s)}", this);
                lastStage = s.stage;
            }
            LogStateChange(s);
        }

        void LogStateChange(in Snap s)
        {
            if (logSlimeStateChanges && s.slimeState != lastSlimeState)
            {
                Debug.Log(
                    $"[BambooV3 {s.stalkName}] slime {lastSlimeState} -> {s.slimeState}  " +
                    $"stalk angVel {s.angVelDeg:F1}°/s  bend {s.bendDeg:F1}°  peak {s.peakDeg:F1}°  " +
                    $"lastImpulse {s.impulseDeg:F1}°/s (kind {s.impulseKind})", this);
                lastSlimeState = s.slimeState;
            }
        }

        Stage Classify(in Snap s)
        {
            bool inContact = s.nearestGap <= contactGapEpsilon;
            if (!inContact && !s.surfaceIsOurs) return Stage.NoContact;
            if (!s.surfaceIsOurs) return Stage.ContactNotProbed;
            if (s.slimeState != SlimeController.SlimeState.Attached
                && s.slimeState != SlimeController.SlimeState.Charging) return Stage.ProbedNotAttached;
            if (!s.carrierIsUs) return Stage.AttachedNoCarrier;
            if (!s.riderAttached) return Stage.CarrierNoImpulse;
            return Stage.Live;
        }

        string StageText(in Snap s)
        {
            switch (s.stage)
            {
                case Stage.NoStalk:
                    return "no BambooPlatformV3 in the scene";
                case Stage.NoContact:
                    return $"slime not on stalk {s.stalkName} (nearest {s.nearestSegment} gap {s.nearestGap:F3})";
                case Stage.ContactNotProbed:
                    return $"TOUCHING {s.nearestSegment} (gap {s.nearestGap:F3}) but SurfaceProbe picked '{s.surfaceName}' — " +
                           "this stalk is not being treated as a surface. Check the segment LAYER against SlimeSettings.groundLayer, " +
                           "and the attach-angle ceiling for this contact normal.";
                case Stage.ProbedNotAttached:
                    return $"stalk {s.stalkName} IS the probed surface but slime is {s.slimeState} " +
                           $"(toggles {s.attachToggles}, lockout {s.attachLockout:F2}) — landing is being rejected or debounced.";
                case Stage.AttachedNoCarrier:
                    return "slime is Attached to this stalk but PlatformCarrier is NOT on it — " +
                           "carrier.Attach never resolved this stalk's PlatformMotor.";
                case Stage.CarrierNoImpulse:
                    return "carrier is on this stalk but ApplyRiderInfluence has not run — no impulse can reach the spring.";
                default:
                    return $"impulse path alive: impact {s.approachSpeed:F1} m/s -> bend {s.bendDeg:F1}°, " +
                           $"peak {s.peakDeg:F1}° / gate {stalk.EjectBendDeg:F1}°, " +
                           $"{(s.returning ? "springing back" : "still bending out")}, arm {s.armLen:F2}";
            }
        }

        void Push(in Snap s)
        {
            ring[ringHead] = s;
            ringHead = (ringHead + 1) % RingSize;
            if (ringCount < RingSize) ringCount++;
        }

        void Update()
        {
            if (Input.GetKeyDown(dumpKey)) Dump();
        }

        void Dump()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== BambooV3ChainDebug (on slime) — last {Mathf.Min(samplesToDump, ringCount)} FixedUpdates ===");
            if (stalk != null)
                sb.AppendLine($"resolved stalk: {stalk.name}, settings {(stalk.HasSettingsAsset ? "assigned" : "MISSING")}, " +
                              $"length {stalk.BambooLength:F2}, stalk inertia {stalk.StalkInertiaDebug:F2}, " +
                              $"freq {stalk.NaturalFreqHz:F2} Hz, zeta {stalk.DampingRatio:F2}, gate {stalk.EjectBendDeg:F1}°");
            sb.AppendLine("frame | stage | slime | spd | gap(seg) | ours car rider | bend | angVel | peak ret | arm | imp(kind chg) | wTorque");

            int n = Mathf.Min(samplesToDump, ringCount);
            int start = (ringHead - n + RingSize) % RingSize;
            for (int i = 0; i < n; i++)
            {
                var s = ring[(start + i) % RingSize];
                sb.AppendLine(
                    $"{s.frame} | {s.stage} | {s.slimeState} | {s.slimeSpeed:F1} | " +
                    $"{s.nearestGap:F3}({s.nearestSegment}) | " +
                    $"{(s.surfaceIsOurs ? 'Y' : 'n')} {(s.carrierIsUs ? 'Y' : 'n')} {(s.riderAttached ? 'Y' : 'n')} | " +
                    $"{s.bendDeg:F2} | {s.angVelDeg:F1} | {s.peakDeg:F2} {(s.returning ? 'R' : '-')} | {s.armLen:F2} | " +
                    $"{s.impulseDeg:F1}(k{s.impulseKind} {(s.riderCharging ? 'c' : '-')}) | {s.weightTorque:F1}");
            }
            Debug.Log(sb.ToString(), this);
        }

        void OnGUI()
        {
            if (!showGui || ringCount == 0) return;

            var s = ring[(ringHead - 1 + RingSize) % RingSize];
            var sb = new StringBuilder();
            sb.AppendLine($"<b>BambooV3ChainDebug (on slime)</b>   stalk <b>{s.stalkName}</b>   [{dumpKey}] dump");
            sb.AppendLine($"stage <b>{s.stage}</b>");
            sb.AppendLine(StageText(s));
            sb.AppendLine();
            sb.AppendLine($"contact  nearest {s.nearestSegment} gap {s.nearestGap:F3}   probed '{s.surfaceName}' ours={(s.surfaceIsOurs ? "Y" : "n")}");
            sb.AppendLine($"slime    {s.slimeState}  spd {s.slimeSpeed:F1}  approach {s.approachSpeed:F1}  toggles {s.attachToggles}  lockout {s.attachLockout:F2}");
            sb.AppendLine($"carrier  onThisStalk={(s.carrierIsUs ? "Y" : "n")}   riderAttached={(s.riderAttached ? "Y" : "n")}   arm {s.armLen:F2}");
            sb.AppendLine($"spring   bend {s.bendDeg:F1}°  angVel {s.angVelDeg:F0}°/s  tip {s.tipSpeed:F1} m/s");
            sb.AppendLine($"impulse  <b>{s.impulseDeg:F1}°/s</b>  kind {s.impulseKind} ({KindName(s.impulseKind)})  charging={(s.riderCharging ? "Y" : "n")}  weightTorque {s.weightTorque:F1}");
            sb.AppendLine($"throw    peak {s.peakDeg:F1}° / gate {(stalk != null ? stalk.EjectBendDeg : 0f):F1}°  {(s.returning ? "SPRINGING BACK" : "bending out")}  lockout {(stalk != null ? stalk.EjectLockoutTimer : 0f):F2}");
            if (stalk != null && !stalk.HasSettingsAsset)
                sb.AppendLine("<b>WARNING: no BambooV3Settings asset assigned — running on class defaults.</b>");

            var style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 12,
                richText = true,
                padding = new RectOffset(8, 8, 6, 6),
            };
            GUI.Box(new Rect(guiPos.x, guiPos.y, 660f, 190f), sb.ToString(), style);
        }

        static string KindName(int kind) => kind == 1 ? "landing" : kind == 2 ? "coupled" : "none";

        void OnDrawGizmosSelected()
        {
            if (stalk == null) return;
            Vector3 pivot = stalk.PivotWorldPos;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(pivot, 0.15f);
            if (Application.isPlaying && stalk.RiderAttached)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(pivot, stalk.LastRiderWorldPos);
                Gizmos.DrawWireSphere(stalk.LastRiderWorldPos, 0.12f);
            }
        }
    }
}
