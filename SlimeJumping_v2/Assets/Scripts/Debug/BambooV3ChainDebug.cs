using System.Text;
using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Platforms;

namespace SlimeJumping.Debugging
{
    /// <summary>
    /// Attach to a BambooPlatformV3 GameObject to see which link of the springboard chain is
    /// broken. The stalk can only bend if an impulse reaches its spring, and the ONLY route
    /// for that is PlatformCarrier -> ApplyRiderInfluence, which requires the slime to
    /// actually attach to this stalk. When the stalk sits inert while the slime visibly
    /// bounces off it, the failure is somewhere in:
    ///
    ///   overlapping a segment collider
    ///     -> SurfaceProbe picks that collider as the surface
    ///       -> slime enters Attached / Charging
    ///         -> carrier.CurrentPlatform is THIS stalk
    ///           -> RiderAttached (ApplyRiderInfluence has run)
    ///             -> impulse -> bend -> peak -> throw
    ///
    /// This component names the first link that fails, live, on screen. It only reads public
    /// state; nothing here writes to the stalk or the slime.
    /// </summary>
    [DefaultExecutionOrder(3000)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BambooPlatformV3))]
    [AddComponentMenu("SlimeJumping/Debug/Bamboo V3 Chain Debug")]
    public class BambooV3ChainDebug : MonoBehaviour
    {
        [Header("Display")]
        [SerializeField] bool showGui = true;
        [SerializeField] Vector2 guiPos = new Vector2(10f, 10f);
        [SerializeField] KeyCode dumpKey = KeyCode.F7;
        [SerializeField, Range(20, 240)] int samplesToDump = 90;

        [Header("Logging")]
        [Tooltip("Log one line to the console every time the chain stage changes (contact gained, probe picked it up, attached, impulse reached, thrown). This is the cheapest way to see the sequence without reading the GUI mid-jump.")]
        [SerializeField] bool logStageChanges = true;
        [Tooltip("Log a line whenever the stalk throws the slime, with the peak bend and exit speed.")]
        [SerializeField] bool logThrows = true;

        [Header("Thresholds")]
        [Tooltip("Gap (world units) from the slime circle to the nearest segment collider below which we call it contact. Collider2D.Distance returns negative while overlapping.")]
        [SerializeField, Min(0f)] float contactGapEpsilon = 0.02f;

        const int RingSize = 240;

        enum Stage
        {
            NoSlime = 0,
            NoContact = 1,          // slime is nowhere near this stalk
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
        }

        BambooPlatformV3 stalk;
        SlimeController slime;
        Collider2D slimeCollider;
        Collider2D[] segments;

        readonly Snap[] ring = new Snap[RingSize];
        int ringHead;
        int ringCount;

        Stage lastStage = Stage.NoSlime;
        float lastAirborneSpeed;
        bool prevRiderAttached;
        float peakSeenThisRide;
        float impactSpeedThisRide;

        void Awake()
        {
            stalk = GetComponent<BambooPlatformV3>();
            RefreshSegments();
        }

        void RefreshSegments()
        {
            var found = GetComponentsInChildren<Collider2D>(true);
            int n = 0;
            for (int i = 0; i < found.Length; i++)
                if (found[i].transform != transform) n++;

            segments = new Collider2D[n];
            int k = 0;
            for (int i = 0; i < found.Length; i++)
                if (found[i].transform != transform) segments[k++] = found[i];
        }

        void ResolveSlime()
        {
            if (slime != null) return;
            slime = FindFirstObjectByType<SlimeController>();
            if (slime != null) slimeCollider = slime.GetComponent<Collider2D>();
        }

        void FixedUpdate()
        {
            ResolveSlime();
            if (segments == null || segments.Length == 0) RefreshSegments();

            var s = new Snap
            {
                frame = Time.frameCount,
                time = Time.time,
                bendDeg = stalk.CurrentDeflectionDeg,
                angVelDeg = stalk.AngularVelocity,
                peakDeg = stalk.PeakBendSinceAttach,
                returning = stalk.ReturningFromPeak,
                riderAttached = stalk.RiderAttached,
                tipSpeed = stalk.TipSpeed,
                nearestGap = float.PositiveInfinity,
                nearestSegment = "-",
                surfaceName = "-",
            };

            if (slime == null)
            {
                s.stage = Stage.NoSlime;
                Push(s);
                return;
            }

            s.slimeState = slime.CurrentState;
            s.slimeSpeed = slime.Velocity.magnitude;
            s.attachToggles = slime.AttachToggleCount;
            s.attachLockout = slime.AttachLockoutTimer;

            // Track the last airborne speed so the reported impact speed is the one the slime
            // actually arrived with, not the post-landing zeroed value.
            if (slime.CurrentState == SlimeController.SlimeState.Airborne)
                lastAirborneSpeed = s.slimeSpeed;
            s.approachSpeed = lastAirborneSpeed;

            // Closest segment collider, so "did it even touch us" is measured, not guessed.
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

            var carrier = slime.Carrier;
            s.carrierIsUs = carrier != null && carrier.CurrentPlatform == (PlatformMotor)stalk;

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
                    $"[BambooV3 {name}] rider left — impact {impactSpeedThisRide:F1} m/s, " +
                    $"peak bend {peakSeenThisRide:F1}° (gate {stalk.EjectBendDeg:F1}°), " +
                    $"tip speed now {s.tipSpeed:F1} m/s, slime state {s.slimeState}, slime speed {s.slimeSpeed:F1}", this);
            }
            prevRiderAttached = s.riderAttached;

            if (logStageChanges && s.stage != lastStage)
            {
                Debug.Log($"[BambooV3 {name}] stage {lastStage} -> {s.stage} : {StageText(s)}", this);
                lastStage = s.stage;
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
                case Stage.NoSlime:
                    return "no SlimeController in the scene";
                case Stage.NoContact:
                    return $"slime not on this stalk (nearest {s.nearestSegment} gap {s.nearestGap:F3})";
                case Stage.ContactNotProbed:
                    return $"TOUCHING {s.nearestSegment} (gap {s.nearestGap:F3}) but SurfaceProbe picked '{s.surfaceName}' — " +
                           "this stalk is not being treated as a surface. Check the segment LAYER against SlimeSettings.groundLayer, " +
                           "and the attach-angle ceiling for this contact normal.";
                case Stage.ProbedNotAttached:
                    return $"this stalk IS the probed surface but slime is {s.slimeState} " +
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
            sb.AppendLine($"=== BambooV3ChainDebug '{name}' — last {Mathf.Min(samplesToDump, ringCount)} FixedUpdates ===");
            sb.AppendLine($"settings asset: {(stalk.HasSettingsAsset ? "assigned" : "MISSING (class defaults in use)")}, " +
                          $"backgroundOnly {stalk.BackgroundOnly}, length {stalk.BambooLength:F2}, " +
                          $"stalk inertia {stalk.StalkInertiaDebug:F2}, freq {stalk.NaturalFreqHz:F2} Hz, " +
                          $"zeta {stalk.DampingRatio:F2}, gate {stalk.EjectBendDeg:F1}°, segments {(segments != null ? segments.Length : 0)}");
            sb.AppendLine("frame | stage | slime | spd | gap(seg) | surface | ours car rider | bend | angVel | peak ret | arm");

            int n = Mathf.Min(samplesToDump, ringCount);
            int start = (ringHead - n + RingSize) % RingSize;
            for (int i = 0; i < n; i++)
            {
                var s = ring[(start + i) % RingSize];
                sb.AppendLine(
                    $"{s.frame} | {s.stage} | {s.slimeState} | {s.slimeSpeed:F1} | " +
                    $"{s.nearestGap:F3}({s.nearestSegment}) | {s.surfaceName} | " +
                    $"{(s.surfaceIsOurs ? 'Y' : 'n')} {(s.carrierIsUs ? 'Y' : 'n')} {(s.riderAttached ? 'Y' : 'n')} | " +
                    $"{s.bendDeg:F2} | {s.angVelDeg:F1} | {s.peakDeg:F2} {(s.returning ? 'R' : '-')} | {s.armLen:F2}");
            }
            Debug.Log(sb.ToString(), this);
        }

        void OnGUI()
        {
            if (!showGui || ringCount == 0) return;

            var s = ring[(ringHead - 1 + RingSize) % RingSize];
            var sb = new StringBuilder();
            sb.AppendLine($"<b>BambooV3 '{name}'</b>   [{dumpKey}] dump");
            sb.AppendLine($"stage <b>{s.stage}</b>");
            sb.AppendLine(StageText(s));
            sb.AppendLine();
            sb.AppendLine($"contact  nearest {s.nearestSegment} gap {s.nearestGap:F3}   probed '{s.surfaceName}' ours={(s.surfaceIsOurs ? "Y" : "n")}");
            sb.AppendLine($"slime    {s.slimeState}  spd {s.slimeSpeed:F1}  approach {s.approachSpeed:F1}  toggles {s.attachToggles}  lockout {s.attachLockout:F2}");
            sb.AppendLine($"carrier  onThisStalk={(s.carrierIsUs ? "Y" : "n")}   riderAttached={(s.riderAttached ? "Y" : "n")}   arm {s.armLen:F2}");
            sb.AppendLine($"spring   bend {s.bendDeg:F1}°  angVel {s.angVelDeg:F0}°/s  tip {s.tipSpeed:F1} m/s");
            sb.AppendLine($"throw    peak {s.peakDeg:F1}° / gate {stalk.EjectBendDeg:F1}°  {(s.returning ? "SPRINGING BACK" : "bending out")}  lockout {stalk.EjectLockoutTimer:F2}");
            if (!stalk.HasSettingsAsset)
                sb.AppendLine("<b>WARNING: no BambooV3Settings asset assigned — running on class defaults.</b>");
            if (stalk.BackgroundOnly)
                sb.AppendLine("<b>WARNING: backgroundOnly is ON — no colliders, no rider physics.</b>");
            if (segments == null || segments.Length == 0)
                sb.AppendLine("<b>WARNING: no child colliders found — click 'Rebuild Segments'.</b>");

            var style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 12,
                richText = true,
                padding = new RectOffset(8, 8, 6, 6),
            };
            GUI.Box(new Rect(guiPos.x, guiPos.y, 640f, 170f), sb.ToString(), style);
        }

        void OnDrawGizmosSelected()
        {
            if (stalk == null) stalk = GetComponent<BambooPlatformV3>();
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
