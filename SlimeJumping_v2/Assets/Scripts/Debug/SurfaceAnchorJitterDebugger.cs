using System.Text;
using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Physics;

namespace SlimeJumping.Debugging
{
    [DefaultExecutionOrder(3001)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SlimeController))]
    [RequireComponent(typeof(Rigidbody2D))]
    [AddComponentMenu("SlimeJumping/Debug/Surface Anchor Jitter Debugger")]
    public class SurfaceAnchorJitterDebugger : MonoBehaviour
    {
        [SerializeField] SlimeController slime;
        [SerializeField] Rigidbody2D rb;
        [SerializeField] KeyCode dumpKey = KeyCode.F7;

        [Header("Triggers")]
        [SerializeField] bool autoDump = true;
        [SerializeField, Min(0f)] float autoDumpCooldown = 0.75f;
        [SerializeField, Min(0f)] float deltaDeltaTrigger = 0.045f;
        [SerializeField, Min(0f)] float normalAngleTriggerDeg = 7f;
        [SerializeField, Min(0f)] float anchorDeltaTrigger = 0.035f;
        [SerializeField] bool onlyWhenProviderActive = true;

        [Header("Display")]
        [SerializeField] bool showGui = true;
        [SerializeField] Vector2 guiPos = new Vector2(10f, 720f);
        [SerializeField, Range(20, 160)] int samplesToDump = 50;

        const int RingSize = 180;

        struct Snap
        {
            public int frame;
            public float time;
            public SlimeController.SlimeState state;
            public Vector2 rbPos;
            public Vector2 dPos;
            public Vector2 dDelta;
            public Vector2 surfaceNormal;
            public float normalDeltaDeg;
            public int surfaceId;
            public int prevSurfaceId;
            public string surfaceName;
            public string providerName;
            public bool providerActive;
            public bool consumesCarry;
            public Vector2 anchorPos;
            public Vector2 anchorDelta;
            public string reason;
        }

        readonly Snap[] ring = new Snap[RingSize];
        int ringHead;
        int ringCount;
        bool initialized;
        Snap previous;
        Snap latest;
        float lastAutoDumpTime = -999f;
        GUIStyle guiStyle;
        GUIStyle hotStyle;

        void Awake()
        {
            if (slime == null) slime = GetComponent<SlimeController>();
            if (rb == null) rb = GetComponent<Rigidbody2D>();
        }

        void Update()
        {
            if (Input.GetKeyDown(dumpKey))
                DumpRing("[SurfaceAnchorJitter] manual dump");
        }

        void FixedUpdate()
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

            bool providerAllowed = !onlyWhenProviderActive || s.providerActive;
            if (autoDump && providerAllowed && HasReason(s) && Time.time - lastAutoDumpTime >= autoDumpCooldown)
            {
                lastAutoDumpTime = Time.time;
                DumpRing("[SurfaceAnchorJitter] auto dump: " + s.reason);
            }

            previous = s;
        }

        Snap CaptureSnap()
        {
            Snap s = new Snap();
            s.frame = Time.frameCount;
            s.time = Time.time;
            s.state = slime.CurrentState;
            s.rbPos = rb.position;
            s.surfaceNormal = slime.IsTouchingSurface ? slime.SurfaceNormal : slime.GravityUp;
            s.surfaceName = NameOf(slime.SurfaceCollider);
            s.surfaceId = slime.SurfaceCollider != null ? slime.SurfaceCollider.GetInstanceID() : 0;
            s.providerName = "<none>";

            SurfaceAnchorInfo info = slime.LastSurfaceAnchorInfo;
            if (!string.IsNullOrEmpty(info.providerName))
            {
                s.providerActive = true;
                s.providerName = info.providerName;
                s.consumesCarry = info.consumesPlatformCarry;
                s.anchorPos = info.anchorWorldPos;
                s.anchorDelta = s.rbPos - info.anchorWorldPos;
            }

            return s;
        }

        void FillDeltas(ref Snap s, Snap prev)
        {
            s.dPos = s.rbPos - prev.rbPos;
            s.dDelta = s.dPos - prev.dPos;
            if (prev.surfaceNormal.sqrMagnitude > 1e-6f && s.surfaceNormal.sqrMagnitude > 1e-6f)
                s.normalDeltaDeg = Vector2.Angle(prev.surfaceNormal, s.surfaceNormal);
            s.prevSurfaceId = prev.surfaceId;
        }

        void Classify(ref Snap s)
        {
            StringBuilder sb = new StringBuilder(96);
            if (s.surfaceId != s.prevSurfaceId && s.prevSurfaceId != 0 && s.surfaceId != 0)
                AppendReason(sb, "surface switch");
            if (s.dDelta.magnitude >= deltaDeltaTrigger)
                AppendReason(sb, "dDelta spike");
            if (s.normalDeltaDeg >= normalAngleTriggerDeg)
                AppendReason(sb, "normal jump");
            if (s.providerActive && s.anchorDelta.magnitude >= anchorDeltaTrigger)
                AppendReason(sb, "anchor residual");
            s.reason = sb.ToString();
        }

        static bool HasReason(Snap s)
        {
            return !string.IsNullOrEmpty(s.reason);
        }

        static void AppendReason(StringBuilder sb, string reason)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(reason);
        }

        void Push(Snap s)
        {
            ring[ringHead] = s;
            ringHead = (ringHead + 1) % RingSize;
            if (ringCount < RingSize) ringCount++;
        }

        void DumpRing(string header)
        {
            StringBuilder sb = new StringBuilder(4096);
            sb.AppendLine(header);
            int count = Mathf.Min(samplesToDump, ringCount);
            int start = (ringHead - count + RingSize) % RingSize;
            for (int i = 0; i < count; i++)
            {
                Snap s = ring[(start + i) % RingSize];
                sb.Append('#').Append(s.frame)
                    .Append(" t=").Append(s.time.ToString("F2"))
                    .Append(" state=").Append(s.state)
                    .Append(" pos=").Append(V(s.rbPos))
                    .Append(" d=").Append(V(s.dPos))
                    .Append(" dd=").Append(V(s.dDelta))
                    .Append(" normalJump=").Append(s.normalDeltaDeg.ToString("F1"))
                    .Append(" surface=").Append(s.surfaceName)
                    .Append(" provider=").Append(s.providerName)
                    .Append(" carryConsumed=").Append(s.consumesCarry)
                    .Append(" anchorDelta=").Append(V(s.anchorDelta));
                if (!string.IsNullOrEmpty(s.reason))
                    sb.Append(" reason=").Append(s.reason);
                sb.AppendLine();
            }
            Debug.Log(sb.ToString(), this);
        }

        void OnGUI()
        {
            if (!showGui) return;
            if (guiStyle == null)
            {
                guiStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    fontSize = 12
                };
                guiStyle.normal.textColor = Color.white;
                hotStyle = new GUIStyle(guiStyle);
                hotStyle.normal.textColor = new Color(1f, 0.85f, 0.35f);
            }

            GUIStyle style = HasReason(latest) ? hotStyle : guiStyle;
            string text =
                "Surface Anchor Debug\n" +
                "provider: " + latest.providerName + " carryConsumed=" + latest.consumesCarry + "\n" +
                "surface: " + latest.surfaceName + "\n" +
                "anchorDelta: " + latest.anchorDelta.magnitude.ToString("F4") + "\n" +
                "reason: " + (string.IsNullOrEmpty(latest.reason) ? "-" : latest.reason);
            GUI.Box(new Rect(guiPos.x, guiPos.y, 330f, 104f), text, style);
        }

        static string NameOf(Object obj)
        {
            return obj != null ? obj.name : "<none>";
        }

        static string V(Vector2 v)
        {
            return "(" + v.x.ToString("F4") + "," + v.y.ToString("F4") + ")";
        }
    }
}
