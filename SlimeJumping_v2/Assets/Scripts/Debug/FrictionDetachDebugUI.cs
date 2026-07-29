using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Debugging
{
    // IMGUI readout of the friction detach predicate. Pulls SlimeController.FrictionDebug
    // (written once per FixedUpdateAttached); pure read, never writes back to the controller.
    // Row green = condition met, red = blocking detach.
    public class FrictionDetachDebugUI : MonoBehaviour
    {
        [SerializeField] private SlimeController slimeController;

        [Header("Layout")]
        [SerializeField] private Vector2 screenOffset = new Vector2(20f, 320f);
        [SerializeField] private float panelWidth = 320f;
        [SerializeField] private int fontSize = 12;

        [Header("Detach flash")]
        [SerializeField] private float detachFlashDuration = 0.4f;

        [Header("Logging")]
        [Tooltip("ON: every friction detach prints a one-line summary to the Unity Console.")]
        [SerializeField] private bool logDetachEvents = true;

        private GUIStyle labelStyle;
        private GUIStyle headerStyle;
        private GUIStyle rowStyle;
        private Texture2D bgTex;
        private Texture2D rowOkTex;
        private Texture2D rowWarnTex;
        private Texture2D rowFireTex;

        private float detachFlashTimer = -1f;

        private void Awake()
        {
            bgTex      = SolidTex(new Color(0f, 0f, 0f, 0.65f));
            rowOkTex   = SolidTex(new Color(0.10f, 0.45f, 0.10f, 0.8f));
            rowWarnTex = SolidTex(new Color(0.55f, 0.10f, 0.10f, 0.8f));
            rowFireTex = SolidTex(new Color(0.85f, 0.65f, 0.10f, 0.95f));
        }

        private void OnEnable()
        {
            if (slimeController != null)
                slimeController.OnFrictionDetach += HandleFrictionDetach;
        }

        private void OnDisable()
        {
            if (slimeController != null)
                slimeController.OnFrictionDetach -= HandleFrictionDetach;
        }

        private void OnDestroy()
        {
            if (bgTex != null) Destroy(bgTex);
            if (rowOkTex != null) Destroy(rowOkTex);
            if (rowWarnTex != null) Destroy(rowWarnTex);
            if (rowFireTex != null) Destroy(rowFireTex);
        }

        // Driven by the event, not polled: the controller writes the snapshot just before
        // invoking, so FrictionDebug read here is the exact detach frame.
        private void HandleFrictionDetach(Vector2 detachVel, Vector2 sNormal, float overshoot)
        {
            if (!logDetachEvents) return;
            var d = slimeController.FrictionDebug;
            Vector2 pos = slimeController.GetComponent<Rigidbody2D>().position;
            UnityEngine.Debug.Log(string.Format(
                "[FrictionDetach] t={0:F2}s pos=({1:F2},{2:F2}) vel=({3:F2},{4:F2}) " +
                "|v|={5:F2} ratio={6:F2} overshoot={7:F2} req={8:F2} avail={9:F2} " +
                "ω={10:F2}rad/s Δθ={11:F1}° tangentSpd={12:F2} sameSign={13} " +
                "convex={14} normal=({15:F2},{16:F2})",
                Time.time,
                pos.x, pos.y,
                detachVel.x, detachVel.y, detachVel.magnitude,
                d.rawRatio, overshoot,
                d.required, d.available,
                d.omega, d.dThetaDeg, d.tangentSignedSpeed, d.sameSignFrames,
                d.convex,
                sNormal.x, sNormal.y),
                this);
        }

        private void Update()
        {
            if (slimeController == null) return;
            if (slimeController.FrictionDebug.detachFiredThisFrame)
                detachFlashTimer = detachFlashDuration;
            else if (detachFlashTimer > 0f)
                detachFlashTimer -= Time.deltaTime;
        }

        private void OnGUI()
        {
            if (slimeController == null) return;
            EnsureStyles();

            var d = slimeController.FrictionDebug;

            float lineH = fontSize + 6f;
            float pad = 8f;
            int rowCount = 15;
            float panelH = pad * 2 + lineH * (rowCount + 1);

            Rect panelRect = new Rect(screenOffset.x, screenOffset.y, panelWidth, panelH);
            GUI.DrawTexture(panelRect, bgTex);

            float y = screenOffset.y + pad;
            float xLabel = screenOffset.x + pad;
            float xValue = screenOffset.x + pad + 140f;
            float labelW = 130f;
            float valueW = panelWidth - 140f - pad * 2;

            string banner = d.detachFiredThisFrame
                ? "★ FRICTION DETACH FIRED ★"
                : (detachFlashTimer > 0f ? "(detach " + detachFlashTimer.ToString("F2") + "s ago)" : "Friction Detach");
            Texture2D headerBg = (d.detachFiredThisFrame || detachFlashTimer > 0f) ? rowFireTex : null;
            if (headerBg != null) GUI.DrawTexture(new Rect(xLabel - 4f, y - 2f, panelWidth - pad * 2 + 8f, lineH), headerBg);
            GUI.Label(new Rect(xLabel, y, panelWidth - pad * 2, lineH), banner, headerStyle);
            y += lineH;

            DrawRow(ref y, xLabel, xValue, labelW, valueW, "toggle on", d.toggleOn ? "TRUE" : "FALSE", d.toggleOn);
            DrawRow(ref y, xLabel, xValue, labelW, valueW, "in attached", d.inAttached ? "TRUE" : "FALSE", d.inAttached);

            string transReason = "—";
            bool transientBlocking = d.transientFrame;
            if (d.transientFrame)
            {
                if (d.transientNoPrevTangent) transReason = "no prev tangent";
                else if (d.transientColliderChanged) transReason = "collider changed";
                else if (d.transientStickActive) transReason = "stick active";
                else if (d.transientBelowMinSpeed) transReason = "below minSpeed";
            }
            DrawRow(ref y, xLabel, xValue, labelW, valueW, "transient frame", transReason, !transientBlocking);

            DrawRow(ref y, xLabel, xValue, labelW, valueW, "tangentSpeed", d.tangentSignedSpeed.ToString("F3") + " m/s", null);
            DrawRow(ref y, xLabel, xValue, labelW, valueW, "Δθ", d.dThetaDeg.ToString("F2") + " deg", null);
            DrawRow(ref y, xLabel, xValue, labelW, valueW, "ω", d.omega.ToString("F2") + " rad/s", null);
            DrawRow(ref y, xLabel, xValue, labelW, valueW, "required (v·ω)", d.required.ToString("F2"), null);
            DrawRow(ref y, xLabel, xValue, labelW, valueW, "gravityHelp", d.gravityHelp.ToString("F2"), null);
            DrawRow(ref y, xLabel, xValue, labelW, valueW, "available", d.available.ToString("F2"), null);
            DrawRow(ref y, xLabel, xValue, labelW, valueW, "ratio", d.rawRatio.ToString("F3"), d.rawRatio > 1f);
            DrawRow(ref y, xLabel, xValue, labelW, valueW, "convex (info)", d.convex ? "TRUE" : "false", null);
            DrawRow(ref y, xLabel, xValue, labelW, valueW, "sameSignFrames", d.sameSignFrames.ToString(), null);

            // Pre-condition only; the geometric horizon test runs after this and isn't
            // snapshot-able. The banner above is ground truth for "actually detached".
            bool centrifugalAdmissible = d.toggleOn && !d.transientFrame && d.rawRatio > 1f;
            DrawRow(ref y, xLabel, xValue, labelW, valueW, "centrif. admissible", centrifugalAdmissible ? "YES" : "no", centrifugalAdmissible);

            // Public API mirror (smoothed gated value going to visual).
            DrawRow(ref y, xLabel, xValue, labelW, valueW, "CornerStressRatio", slimeController.CornerStressRatio.ToString("F3"), slimeController.CornerStressRatio > 0.01f);
        }

        private void DrawRow(ref float y, float xLabel, float xValue, float labelW, float valueW,
            string label, string value, bool? condition)
        {
            float lineH = fontSize + 6f;
            if (condition.HasValue)
            {
                Texture2D bg = condition.Value ? rowOkTex : rowWarnTex;
                GUI.DrawTexture(new Rect(xLabel - 4f, y - 2f, labelW + valueW + 8f, lineH), bg);
            }
            GUI.Label(new Rect(xLabel, y, labelW, lineH), label, labelStyle);
            GUI.Label(new Rect(xValue, y, valueW, lineH), value, rowStyle);
            y += lineH;
        }

        private void EnsureStyles()
        {
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize, alignment = TextAnchor.MiddleLeft };
                labelStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            }
            if (rowStyle == null)
            {
                rowStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize, alignment = TextAnchor.MiddleLeft };
                rowStyle.normal.textColor = Color.white;
                rowStyle.fontStyle = FontStyle.Bold;
            }
            if (headerStyle == null)
            {
                headerStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize + 1, alignment = TextAnchor.MiddleCenter };
                headerStyle.normal.textColor = Color.white;
                headerStyle.fontStyle = FontStyle.Bold;
            }
        }

        private static Texture2D SolidTex(Color c)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var pixels = new Color[4] { c, c, c, c };
            t.SetPixels(pixels);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }
    }
}
