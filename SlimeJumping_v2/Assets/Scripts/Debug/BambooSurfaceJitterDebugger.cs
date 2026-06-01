using System.Text;
using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Physics;
using SlimeJumping.Platforms;

namespace SlimeJumping.Debugging
{
    /// <summary>
    /// Attach to the Slime root while investigating bamboo descent/camera jitter.
    /// Samples after FixedUpdate and dumps a short ring buffer when the physics pose,
    /// surface identity, probe selection, or bamboo carry estimate jumps.
    /// </summary>
    [DefaultExecutionOrder(3000)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SlimeController))]
    [RequireComponent(typeof(Rigidbody2D))]
    [AddComponentMenu("SlimeJumping/Debug/Bamboo Surface Jitter Debugger")]
    public class BambooSurfaceJitterDebugger : MonoBehaviour
    {
        [SerializeField] private SlimeController slime;
        [SerializeField] private Rigidbody2D rb;
        [SerializeField] private CircleCollider2D slimeCollider;
        [SerializeField] private KeyCode dumpKey = KeyCode.F6;

        [Header("Triggers")]
        [SerializeField] private bool autoDump = true;
        [SerializeField, Min(0f)] private float autoDumpCooldown = 0.75f;
        [Tooltip("Frame-to-frame change in rb.position delta that counts as a position jolt.")]
        [SerializeField, Min(0f)] private float deltaDeltaTrigger = 0.045f;
        [Tooltip("Surface normal rotation in one FixedUpdate that counts as a normal jump.")]
        [SerializeField, Min(0f)] private float normalAngleTriggerDeg = 7f;
        [Tooltip("Change in Collider2D.Distance from slime circle to active surface that counts as a snap/depenetration jump.")]
        [SerializeField, Min(0f)] private float surfaceDistanceDeltaTrigger = 0.025f;
        [Tooltip("Observed movement minus controller velocity and platform carry. Large values point to snap/probe/solver correction.")]
        [SerializeField, Min(0f)] private float residualDeltaTrigger = 0.05f;
        [SerializeField] private bool dumpOnRawColliderSwitch = false;
        [Tooltip("ON: automatic dumps only fire while the current or previous sample is on a BambooPlatform. Manual F6 dumps still show the current ring buffer.")]
        [SerializeField] private bool autoDumpOnlyOnBamboo = true;
        [SerializeField] private bool onlyWhileOnBamboo = true;

        [Header("Display")]
        [SerializeField] private bool showGui = true;
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private Vector2 guiPos = new Vector2(10f, 600f);
        [SerializeField, Range(30, 240)] private int samplesToDump = 50;

        private const int RingSize = 240;

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
            public Vector2 dDelta;
            public Vector2 expectedByVelocity;
            public Vector2 carryDelta;
            public Vector2 residualDelta;
            public Vector2 surfaceNormal;
            public Vector2 surfaceTangent;
            public float normalDeltaDeg;
            public float surfaceDistance;
            public float surfaceDistanceDelta;
            public int surfaceId;
            public int prevSurfaceId;
            public int hostId;
            public int prevHostId;
            public bool rawColliderChanged;
            public bool hostChanged;
            public string surfaceName;
            public string prevSurfaceName;
            public string hostName;
            public string prevHostName;
            public bool onBamboo;
            public bool prevOnBamboo;
            public float bambooHeight01;
            public float bambooAngleDeg;
            public float bambooAngularVelDeg;
            public float bambooTipSpeed;
            public int probeContactCount;
            public int probeActiveCount;
            public int probeSelectedIndex;
            public int prevProbeSelectedIndex;
            public float depenPushMag;
            public string reason;
        }

        private readonly Snap[] ring = new Snap[RingSize];
        private int ringHead;
        private int ringCount;

        private bool initialized;
        private Snap previous;
        private Snap latest;
        private float lastAutoDumpTime = -999f;
        private string latestReason = "init";

        private GUIStyle guiStyle;
        private GUIStyle hotStyle;

        private void Awake()
        {
            if (slime == null) slime = GetComponent<SlimeController>();
            if (rb == null) rb = GetComponent<Rigidbody2D>();
            if (slimeCollider == null) slimeCollider = GetComponent<CircleCollider2D>();
        }

        private void Update()
        {
            if (Input.GetKeyDown(dumpKey))
                DumpRing("[BambooSurfaceJitter] manual dump");
        }

        private void FixedUpdate()
        {
            if (slime == null || rb == null) return;

            Snap s = CaptureSnap();

            if (!initialized)
            {
                initialized = true;
                previous = s;
                latest = s;
                Push(s);
                return;
            }

            FillDeltas(ref s, previous);
            Classify(ref s);
            latest = s;
            Push(s);

            bool bambooAutoAllowed = !autoDumpOnlyOnBamboo || s.onBamboo || s.prevOnBamboo;
            if (autoDump && bambooAutoAllowed && HasReason(s) && Time.time - lastAutoDumpTime >= autoDumpCooldown)
            {
                lastAutoDumpTime = Time.time;
                DumpRing("[BambooSurfaceJitter] auto dump: " + s.reason);
            }

            previous = s;
        }

        private Snap CaptureSnap()
        {
            Snap s = new Snap();
            s.frame = Time.frameCount;
            s.time = Time.time;
            s.state = slime.CurrentState;
            s.rbPos = rb.position;
            s.tfPos = transform.position;
            s.rbVel = rb.linearVelocity;
            s.ctrlVel = slime.Velocity;
            s.surfaceNormal = slime.IsTouchingSurface ? slime.SurfaceNormal : slime.GravityUp;
            s.surfaceTangent = slime.IsTouchingSurface ? slime.SurfaceTangent : slime.GravityRight;
            s.reason = "";

            Collider2D surface = slime.SurfaceCollider;
            s.surfaceId = surface != null ? surface.GetInstanceID() : 0;
            s.surfaceName = NameOf(surface);
            s.prevSurfaceName = "<none>";
            s.hostName = "<none>";
            s.prevHostName = "<none>";

            PlatformMotor host = surface != null ? surface.GetComponentInParent<PlatformMotor>() : null;
            s.hostId = host != null ? host.GetInstanceID() : 0;
            s.hostName = NameOf(host);

            BambooPlatform bamboo = host as BambooPlatform;
            s.onBamboo = bamboo != null;
            if (bamboo != null)
            {
                s.bambooHeight01 = bamboo.GetHeight01(s.rbPos);
                s.bambooAngleDeg = bamboo.CurrentDeflectionDeg;
                s.bambooAngularVelDeg = bamboo.AngularVelocity;
                s.bambooTipSpeed = bamboo.TipSwingSpeed;
            }

            SurfaceProbe probe = slime.Probe;
            if (probe != null)
            {
                s.probeContactCount = probe.ContactCount;
                s.probeActiveCount = probe.ActiveContactCount;
                s.probeSelectedIndex = probe.SelectedIndex;
                s.depenPushMag = probe.LastDepenPushMag;
            }
            else
            {
                s.probeSelectedIndex = -1;
            }

            s.surfaceDistance = ComputeSurfaceDistance(surface);
            return s;
        }

        private void FillDeltas(ref Snap s, Snap prev)
        {
            float dt = Mathf.Max(Time.fixedDeltaTime, 1e-5f);

            s.dPos = s.rbPos - prev.rbPos;
            s.dDelta = s.dPos - prev.dPos;
            s.expectedByVelocity = prev.ctrlVel * dt;
            s.carryDelta = ComputeCarryDelta(s.rbPos, prev.rbPos, dt);
            s.residualDelta = s.dPos - s.expectedByVelocity - s.carryDelta;

            if (prev.surfaceNormal.sqrMagnitude > 1e-6f && s.surfaceNormal.sqrMagnitude > 1e-6f)
                s.normalDeltaDeg = Vector2.Angle(prev.surfaceNormal, s.surfaceNormal);

            s.surfaceDistanceDelta = s.surfaceDistance - prev.surfaceDistance;
            s.rawColliderChanged = s.surfaceId != prev.surfaceId;
            s.hostChanged = s.hostId != prev.hostId;
            s.prevSurfaceId = prev.surfaceId;
            s.prevSurfaceName = prev.surfaceName;
            s.prevHostId = prev.hostId;
            s.prevHostName = prev.hostName;
            s.prevOnBamboo = prev.onBamboo;
            s.prevProbeSelectedIndex = prev.probeSelectedIndex;
        }

        private Vector2 ComputeCarryDelta(Vector2 currentPos, Vector2 previousPos, float dt)
        {
            Collider2D surface = slime.SurfaceCollider;
            if (surface == null) return Vector2.zero;

            PlatformMotor host = surface.GetComponentInParent<PlatformMotor>();
            if (host == null) return Vector2.zero;
            if (host is BambooPlatform) return Vector2.zero;

            return host.GetRiderCarryDelta(previousPos, dt);
        }

        private float ComputeSurfaceDistance(Collider2D surface)
        {
            if (surface == null || slimeCollider == null) return 0f;

            ColliderDistance2D dist = slimeCollider.Distance(surface);
            return dist.isValid ? dist.distance : 0f;
        }

        private void Classify(ref Snap s)
        {
            latestReason = "";
            if (onlyWhileOnBamboo && !s.onBamboo && !s.prevOnBamboo)
            {
                s.reason = "";
                return;
            }

            StringBuilder sb = new StringBuilder(128);

            bool surfaceLost = s.rawColliderChanged && s.prevSurfaceId != 0 && s.surfaceId == 0;
            bool surfaceGained = s.rawColliderChanged && s.prevSurfaceId == 0 && s.surfaceId != 0;
            bool bothSurfaces = s.prevSurfaceId != 0 && s.surfaceId != 0;
            bool oneHostExists = s.prevHostId != 0 || s.hostId != 0;

            if (surfaceLost)
                AppendReason(sb, "surface lost");
            else if (surfaceGained)
                AppendReason(sb, "surface gained");
            else if (s.rawColliderChanged && s.hostChanged && oneHostExists)
                AppendReason(sb, "host switch");
            else if (s.rawColliderChanged && bothSurfaces && s.prevHostId == s.hostId)
                AppendReason(sb, "segment switch");
            else if (s.rawColliderChanged)
                AppendReason(sb, "surface collider switch");

            if (s.probeSelectedIndex != s.prevProbeSelectedIndex)
                AppendReason(sb, "probe selected changed");
            if (s.normalDeltaDeg >= normalAngleTriggerDeg)
                AppendReason(sb, "normal jump");
            if (Mathf.Abs(s.surfaceDistanceDelta) >= surfaceDistanceDeltaTrigger)
                AppendReason(sb, "surface distance jump");
            if (s.dDelta.magnitude >= deltaDeltaTrigger)
                AppendReason(sb, "position delta jump");
            if (s.residualDelta.magnitude >= residualDeltaTrigger)
                AppendReason(sb, "snap/carry residual");
            if (s.depenPushMag >= surfaceDistanceDeltaTrigger)
                AppendReason(sb, "depen push");

            if (!dumpOnRawColliderSwitch && sb.ToString() == "segment switch")
                s.reason = "";
            else
                s.reason = sb.ToString();

            latestReason = HasReason(s) ? s.reason : "none";
        }

        private static void AppendReason(StringBuilder sb, string reason)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(reason);
        }

        private void Push(Snap s)
        {
            ring[ringHead] = s;
            ringHead = (ringHead + 1) % RingSize;
            if (ringCount < RingSize) ringCount++;
        }

        [ContextMenu("Dump Recent Bamboo Jitter Samples")]
        private void DumpRecentSamples()
        {
            DumpRing("[BambooSurfaceJitter] context dump");
        }

        private void DumpRing(string header)
        {
            if (ringCount == 0)
            {
                Debug.Log(header + " ring empty", this);
                return;
            }

            StringBuilder sb = new StringBuilder(16384);
            sb.AppendLine(header);
            AppendTriggerSummary(sb, latest);
            sb.AppendLine("Columns:");
            sb.AppendLine("  frame time state pos dPos dDelta residual carry surfDist dSurfDist normalDeg surface host probe contact bamboo reason");

            int count = Mathf.Min(samplesToDump, ringCount);
            int start = (ringHead - count + RingSize) % RingSize;
            for (int i = 0; i < count; i++)
            {
                Snap s = ring[(start + i) % RingSize];
                sb.AppendFormat(
                    "#{0} t={1:F3} {2} pos=({3:F3},{4:F3}) d=({5:F4},{6:F4}) dd=({7:F4},{8:F4}) residual=({9:F4},{10:F4}) carry=({11:F4},{12:F4}) ",
                    s.frame, s.time, s.state,
                    s.rbPos.x, s.rbPos.y,
                    s.dPos.x, s.dPos.y,
                    s.dDelta.x, s.dDelta.y,
                    s.residualDelta.x, s.residualDelta.y,
                    s.carryDelta.x, s.carryDelta.y);
                sb.AppendFormat(
                    "dist={0:F4} dDist={1:F4} nDelta={2:F1} surf='{3}' prevSurf='{4}' host='{5}' prevHost='{6}' rawSwitch={7} hostSwitch={8} ",
                    s.surfaceDistance, s.surfaceDistanceDelta, s.normalDeltaDeg,
                    s.surfaceName, s.prevSurfaceName, s.hostName, s.prevHostName,
                    s.rawColliderChanged, s.hostChanged);
                sb.AppendFormat(
                    "probe={0}/{1} selected={2}->{3} depen={4:F4} bamboo={5} h={6:F2} angle={7:F1} av={8:F1} tip={9:F2}",
                    s.probeActiveCount, s.probeContactCount,
                    s.prevProbeSelectedIndex, s.probeSelectedIndex,
                    s.depenPushMag,
                    s.onBamboo,
                    s.bambooHeight01,
                    s.bambooAngleDeg,
                    s.bambooAngularVelDeg,
                    s.bambooTipSpeed);
                if (HasReason(s))
                    sb.Append("  << ").Append(s.reason);
                sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("Reading hints:");
            sb.AppendLine("  segment switch + normal/distance jump => collider seam/contact selection is visible.");
            sb.AppendLine("  residual spike without segment switch => SnapToSurface/depenetration/solver correction is moving the slime.");
            sb.AppendLine("  carry spike/residual while bamboo angular velocity is high => bamboo carry model is mismatched.");
            sb.AppendLine("  clean physics but visible camera wobble => compare with OnScreenJitterProbe.");

            Debug.Log(sb.ToString(), this);
        }

        private void AppendTriggerSummary(StringBuilder sb, Snap s)
        {
            sb.Append("Trigger sample: ");
            sb.AppendFormat(
                "#{0} t={1:F3} {2} reason='{3}' bamboo={4} surf='{5}' prevSurf='{6}' host='{7}' prevHost='{8}' ",
                s.frame, s.time, s.state,
                HasReason(s) ? s.reason : "none",
                s.onBamboo,
                s.surfaceName,
                s.prevSurfaceName,
                s.hostName,
                s.prevHostName);
            sb.AppendFormat(
                "d=({0:F4},{1:F4}) dd=({2:F4},{3:F4}) residual=({4:F4},{5:F4}) nDelta={6:F1} dDist={7:F4} probe={8}/{9} selected={10}->{11}",
                s.dPos.x, s.dPos.y,
                s.dDelta.x, s.dDelta.y,
                s.residualDelta.x, s.residualDelta.y,
                s.normalDeltaDeg,
                s.surfaceDistanceDelta,
                s.probeActiveCount,
                s.probeContactCount,
                s.prevProbeSelectedIndex,
                s.probeSelectedIndex);
            sb.AppendLine();
        }

        private void OnGUI()
        {
            if (!showGui || !initialized) return;
            EnsureStyles();

            bool hotFrame = HasReason(latest);
            string hot = hotFrame ? latest.reason : "no trigger";
            GUIStyle style = hotFrame ? hotStyle : guiStyle;
            string text =
                "[Bamboo Surface Jitter]\n" +
                "  reason: " + hot + "\n" +
                "  state=" + latest.state +
                " bamboo=" + latest.onBamboo +
                " h=" + latest.bambooHeight01.ToString("F2") +
                " angle=" + latest.bambooAngleDeg.ToString("F1") +
                " av=" + latest.bambooAngularVelDeg.ToString("F1") + "\n" +
                "  surface=" + latest.surfaceName + " host=" + latest.hostName + "\n" +
                "  dPos=" + Vec(latest.dPos) +
                " dDelta=" + latest.dDelta.magnitude.ToString("F4") +
                " residual=" + latest.residualDelta.magnitude.ToString("F4") + "\n" +
                "  nDelta=" + latest.normalDeltaDeg.ToString("F1") +
                " dSurfDist=" + latest.surfaceDistanceDelta.ToString("F4") +
                " probe=" + latest.probeActiveCount + "/" + latest.probeContactCount +
                " selected=" + latest.prevProbeSelectedIndex + "->" + latest.probeSelectedIndex + "\n" +
                "  [" + dumpKey + "] dump last " + Mathf.Min(samplesToDump, ringCount) + " fixed samples";

            GUI.Label(new Rect(guiPos.x, guiPos.y, 620f, 130f), text, style);
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmos || !initialized || ringCount < 2) return;

            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.85f);
            int count = Mathf.Min(60, ringCount);
            int start = (ringHead - count + RingSize) % RingSize;
            Vector3 prev = ring[start].rbPos;
            for (int i = 1; i < count; i++)
            {
                Snap s = ring[(start + i) % RingSize];
                Vector3 curr = s.rbPos;
                Gizmos.DrawLine(prev, curr);
                prev = curr;
            }

            Gizmos.color = HasReason(latest) ? Color.red : Color.green;
            Gizmos.DrawWireSphere(latest.rbPos, 0.08f);

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(latest.rbPos, latest.rbPos + latest.surfaceNormal * 0.6f);

            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(latest.rbPos, latest.rbPos + latest.residualDelta * 10f);
        }

        private void EnsureStyles()
        {
            if (guiStyle == null)
            {
                guiStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    alignment = TextAnchor.UpperLeft,
                    richText = false
                };
                guiStyle.normal.textColor = Color.white;
            }
            if (hotStyle == null)
            {
                hotStyle = new GUIStyle(guiStyle);
                hotStyle.normal.textColor = new Color(1f, 0.55f, 0.45f, 1f);
                hotStyle.fontStyle = FontStyle.Bold;
            }
        }

        private static string NameOf(Object obj)
        {
            if (obj == null) return "<none>";
            return obj.name;
        }

        private static bool HasReason(Snap s)
        {
            return !string.IsNullOrEmpty(s.reason);
        }

        private static string Vec(Vector2 v)
        {
            return "(" + v.x.ToString("F4") + "," + v.y.ToString("F4") + ")";
        }
    }
}
