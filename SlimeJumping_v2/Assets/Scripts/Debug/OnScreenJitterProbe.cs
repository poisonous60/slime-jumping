using System.Text;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Debugging
{
    /// <summary>Per-render-frame slime-vs-camera jitter probe; dumps a ring buffer when on-screen relative acceleration spikes.</summary>
    [DefaultExecutionOrder(2100)]
    public class OnScreenJitterProbe : MonoBehaviour
    {
        [SerializeField] private SlimeController slime;
        [SerializeField] private UnityEngine.Camera cam;
        [SerializeField] private KeyCode dumpKey = KeyCode.F4;
        [Tooltip("Auto-dump when on-screen acceleration of slime-relative-to-camera exceeds this many pixels per frame²")]
        [SerializeField] private float relAccelTriggerPx = 1.5f;
        [SerializeField] private float autoDumpCooldown = 0.5f;
        [SerializeField] private bool autoDump = true;
        [SerializeField] private Vector2 guiPos = new Vector2(10f, 460f);

        private Rigidbody2D rb;

        private const int RING = 90; // ~1.5s at 60fps, ~0.6s at 144fps

        private struct Snap
        {
            public int frame;
            public float time;
            public float deltaTime;
            public Vector2 rbPos;
            public Vector2 tfPos;
            public Vector2 camPos;
            public Vector2 rbDelta;     // tf vs prev tf (NOT rb — this is what the eye sees)
            public Vector2 camDelta;
            public Vector2 relPos;
            public Vector2 relDelta;
            public float relAccelMagPx; // |relDelta - prev relDelta| in pixels — the jolt metric
            public bool isFixedTick;    // did a FixedUpdate run during this Update?
        }

        private readonly Snap[] ring = new Snap[RING];
        private int ringHead;
        private int ringCount;

        private bool initialized;
        private Vector2 prevTf;
        private Vector2 prevCam;
        private Vector2 prevRel;
        private Vector2 prevRelDelta;
        private int lastFixedFrame;
        private int currUpdateFixedTickFlag;

        private float lastAutoDumpTime = -999f;
        private GUIStyle labelStyle;

        // 1-second window stats
        private float windowStart;
        private float maxRelAccelPxWindow;
        private int updatesPerSecond;
        private int fixedTicksPerSecond;
        private float lastWindowMaxRelAccelPx;
        private int lastWindowUpdatesPerSec;
        private int lastWindowFixedTicksPerSec;

        private void Awake()
        {
            if (slime == null) slime = GetComponent<SlimeController>();
            if (slime == null) slime = FindAnyObjectByType<SlimeController>();
            if (slime != null) rb = slime.GetComponent<Rigidbody2D>();
            if (cam == null) cam = UnityEngine.Camera.main;
            windowStart = Time.unscaledTime;
        }

        private void FixedUpdate()
        {
            // Marks that a FixedUpdate ran since the last Update, to correlate jitter with physics steps.
            currUpdateFixedTickFlag = 1;
            fixedTicksPerSecond++;
        }

        private void Update()
        {
            if (slime == null || cam == null) return;

            updatesPerSecond++;

            Vector2 tf = slime.transform.position;
            Vector2 camP = cam.transform.position;
            Vector2 rbP = rb != null ? rb.position : tf;

            if (!initialized)
            {
                prevTf = tf;
                prevCam = camP;
                prevRel = tf - camP;
                prevRelDelta = Vector2.zero;
                initialized = true;
                if (Input.GetKeyDown(dumpKey)) DumpRing("[OnScreenJitterProbe] manual dump (init)");
                return;
            }

            Vector2 tfDelta = tf - prevTf;
            Vector2 camDelta = camP - prevCam;
            Vector2 rel = tf - camP;
            Vector2 relDelta = rel - prevRel;

            Vector2 relAccel = relDelta - prevRelDelta;
            float pxPerWorldUnit = (cam.pixelHeight * 0.5f) / Mathf.Max(0.0001f, cam.orthographicSize);
            float relAccelMagPx = relAccel.magnitude * pxPerWorldUnit;
            if (relAccelMagPx > maxRelAccelPxWindow) maxRelAccelPxWindow = relAccelMagPx;

            // Capture
            ref Snap s = ref ring[ringHead];
            s.frame = Time.frameCount;
            s.time = Time.unscaledTime;
            s.deltaTime = Time.unscaledDeltaTime;
            s.rbPos = rbP;
            s.tfPos = tf;
            s.camPos = camP;
            s.rbDelta = tfDelta;       // misnamed but kept; this is render-side delta of the slime
            s.camDelta = camDelta;
            s.relPos = rel;
            s.relDelta = relDelta;
            s.relAccelMagPx = relAccelMagPx;
            s.isFixedTick = currUpdateFixedTickFlag != 0;
            currUpdateFixedTickFlag = 0;

            ringHead = (ringHead + 1) % RING;
            if (ringCount < RING) ringCount++;

            if (Input.GetKeyDown(dumpKey))
                DumpRing("[OnScreenJitterProbe] manual dump");

            if (autoDump && relAccelMagPx > relAccelTriggerPx
                && Time.unscaledTime - lastAutoDumpTime > autoDumpCooldown)
            {
                lastAutoDumpTime = Time.unscaledTime;
                DumpRing($"[OnScreenJitterProbe] auto-dump on relAccel {relAccelMagPx:F2}px");
            }

            if (Time.unscaledTime - windowStart >= 1f)
            {
                lastWindowMaxRelAccelPx = maxRelAccelPxWindow;
                lastWindowUpdatesPerSec = updatesPerSecond;
                lastWindowFixedTicksPerSec = fixedTicksPerSecond;
                maxRelAccelPxWindow = 0f;
                updatesPerSecond = 0;
                fixedTicksPerSecond = 0;
                windowStart = Time.unscaledTime;
            }

            prevTf = tf;
            prevCam = camP;
            prevRel = rel;
            prevRelDelta = relDelta;
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
            if (lastWindowMaxRelAccelPx < 0.5f)
                verdict = "<color=#a0ffa0>screen-side: NO jolt detected (< 0.5 px/frame²)</color>";
            else if (lastWindowMaxRelAccelPx < 1.5f)
                verdict = "<color=#ffd080>screen-side: minor wobble (< 1.5 px/frame²)</color>";
            else
                verdict = "<color=#ff8080>screen-side: visible jolt (≥ 1.5 px/frame²)</color>";

            string text =
                "[On-Screen Jitter] " + verdict + "\n" +
                $"  max rel accel (1s): {lastWindowMaxRelAccelPx:F2} px/frame²\n" +
                $"  Update Hz: {lastWindowUpdatesPerSec}  FixedUpdate Hz: {lastWindowFixedTicksPerSec}\n" +
                $"  [{dumpKey}] dump last {ringCount} render frames";

            var rect = new Rect(guiPos.x, guiPos.y, 460f, 90f);
            GUI.Label(rect, text, labelStyle);
        }

        private void DumpRing(string header)
        {
            if (ringCount == 0) { Debug.Log($"{header} — ring empty"); return; }

            float pxPerWorld = cam != null ? (cam.pixelHeight * 0.5f) / Mathf.Max(0.0001f, cam.orthographicSize) : 1f;

            var sb = new StringBuilder(8192);
            sb.AppendLine(header);
            sb.AppendLine($"  pxPerWorldUnit={pxPerWorld:F1}  (deltas in world units; relAccelPx in pixels)");
            sb.AppendLine(
                "  idx frame   t      dt    fix  tfΔ                     |tfΔ|   camΔ                    |camΔ|   relΔ (slime-vs-cam)     |relΔ|   relAccelPx");

            int start = (ringHead - ringCount + RING) % RING;
            for (int i = 0; i < ringCount; i++)
            {
                var s = ring[(start + i) % RING];
                sb.AppendFormat(
                    "  {0,3} {1,6} {2,6:F2}  {3,5:F4}  {4}   ({5,7:F4},{6,7:F4})  {7,6:F4}   ({8,7:F4},{9,7:F4})  {10,6:F4}   ({11,7:F4},{12,7:F4})  {13,6:F4}   {14,6:F2}\n",
                    i, s.frame, s.time, s.deltaTime,
                    s.isFixedTick ? "F" : ".",
                    s.rbDelta.x, s.rbDelta.y, s.rbDelta.magnitude,
                    s.camDelta.x, s.camDelta.y, s.camDelta.magnitude,
                    s.relDelta.x, s.relDelta.y, s.relDelta.magnitude,
                    s.relAccelMagPx);
            }
            Debug.Log(sb.ToString());
        }
    }
}
