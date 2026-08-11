using System.Reflection;
using System.Text;
using UnityEngine;
using SlimeJumping.Background;

namespace SlimeJumping.Debugging
{
    /// <summary>Read-only watch on StarFieldOverlay: pulls its private cam/quad/renderer/reveal
    /// by reflection and reports what the shader actually got, so a "stars show once then never
    /// again" report can be pinned on a specific link — camera height, reveal value, renderer
    /// enable, MPB upload, duplicate quads, or the background camera's culling mask.
    /// Logs every transition plus a heartbeat; attach to any object and delete to roll back.</summary>
    [DefaultExecutionOrder(2100)]
    public class StarFieldRevealProbe : MonoBehaviour
    {
        [SerializeField] private StarFieldOverlay overlay;
        [Tooltip("Seconds between heartbeat lines while above fadeStartY.")]
        [SerializeField] private float heartbeat = 0.5f;
        [Tooltip("Press the instant stars look wrong on screen — stamps a MARK line to line up with the values.")]
        [SerializeField] private KeyCode markKey = KeyCode.F8;
        [SerializeField] private bool showGui = true;
        [SerializeField] private Vector2 guiPos = new Vector2(10f, 10f);

        private const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;

        private FieldInfo fCam, fRend, fQuad, fReveal, fStartY, fEndY;
        private MaterialPropertyBlock read;
        private float nextBeat;
        private bool prevEnabled;
        private bool prevAbove;
        private string line = "";

        private void OnEnable()
        {
            if (overlay == null)
            {
                Debug.LogWarning("[StarProbe] overlay not assigned");
                enabled = false;
                return;
            }
            var t = typeof(StarFieldOverlay);
            fCam = t.GetField("cam", Priv);
            fRend = t.GetField("rend", Priv);
            fQuad = t.GetField("quad", Priv);
            fReveal = t.GetField("reveal", Priv);
            fStartY = t.GetField("fadeStartY", Priv);
            fEndY = t.GetField("fadeEndY", Priv);
            read = new MaterialPropertyBlock();
            Debug.Log("[StarProbe] armed. reveal field present=" + (fReveal != null));
        }

        private void LateUpdate()
        {
            var cam = fCam.GetValue(overlay) as UnityEngine.Camera;
            var rend = fRend.GetValue(overlay) as MeshRenderer;
            var quad = fQuad.GetValue(overlay) as Transform;
            float startY = (float)fStartY.GetValue(overlay);
            float endY = (float)fEndY.GetValue(overlay);

            float camY = cam == null ? float.NaN : cam.transform.position.y;
            bool above = camY > startY;
            bool rendOn = rend != null && rend.enabled;

            float mpbReveal = float.NaN;
            bool visible = false;
            if (rend != null)
            {
                rend.GetPropertyBlock(read);
                mpbReveal = read.isEmpty ? float.NaN : read.GetFloat("_Reveal");
                visible = rend.isVisible;
            }

            var sb = new StringBuilder();
            sb.Append("camY=").Append(camY.ToString("F1"))
              .Append(" win=").Append(startY).Append("~").Append(endY)
              .Append(" reveal=").Append(fReveal == null ? "n/a" : ((float)fReveal.GetValue(overlay)).ToString("F3"))
              .Append(" mpb_Reveal=").Append(mpbReveal.ToString("F3"))
              .Append(" rendOn=").Append(rendOn)
              .Append(" isVisible=").Append(visible)
              .Append(" quad=").Append(quad == null ? "NULL" : "ok")
              .Append(" dupQuads=").Append(CountQuads(cam))
              .Append(" bgMask=").Append(BgMaskState(cam))
              .Append(" dt=").Append(Time.deltaTime.ToString("F3"));
            line = sb.ToString();

            if (Input.GetKeyDown(markKey)) Debug.Log("[StarProbe] MARK " + line + Occluders(cam));

            bool transition = rendOn != prevEnabled || above != prevAbove;
            if (transition)
            {
                Debug.Log("[StarProbe] TRANSITION " + line);
                prevEnabled = rendOn;
                prevAbove = above;
                nextBeat = Time.time + heartbeat;
            }
            else if (above && Time.time >= nextBeat)
            {
                Debug.Log("[StarProbe] " + line);
                nextBeat = Time.time + heartbeat;
            }
        }

        /// <summary>One-shot on MARK only: every visible renderer sitting between the camera and
        /// the star quad, which is what could be painting over an additive layer that the values
        /// say is fully on. Scene-wide scan, so never call it per frame.</summary>
        private string Occluders(UnityEngine.Camera cam)
        {
            if (cam == null) return "";
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            var fwd = cam.transform.forward;
            var eye = cam.transform.position;
            var hits = new System.Collections.Generic.List<(float d, string s)>();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r == null || !r.enabled || r.forceRenderingOff || !r.isVisible) continue;
                if (r.gameObject.name == "StarFieldQuad") continue;
                float d = Vector3.Dot(r.bounds.center - eye, fwd);
                if (d <= 0f || d >= 280f) continue;
                if (!GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
                var mat = r.sharedMaterial;
                hits.Add((d, r.name + "(L" + r.gameObject.layer + " d" + d.ToString("F0")
                    + " q" + (mat == null ? "?" : mat.renderQueue.ToString())
                    + " sz" + r.bounds.size.magnitude.ToString("F0") + ")"));
            }
            hits.Sort((a, b) => b.d.CompareTo(a.d));
            var sb = new StringBuilder(" | inFront=").Append(hits.Count).Append(": ");
            for (int i = 0; i < hits.Count && i < 10; i++) sb.Append(hits[i].s).Append(' ');
            return sb.ToString();
        }

        private static int CountQuads(UnityEngine.Camera cam)
        {
            if (cam == null) return -1;
            int n = 0;
            foreach (Transform c in cam.transform)
                if (c.name == "StarFieldQuad") n++;
            return n;
        }

        private static string BgMaskState(UnityEngine.Camera cam)
        {
            if (cam == null) return "noCam";
            var bg = cam.transform.Find("BackgroundCamera");
            if (bg == null) return "noBgCam";
            var bgCam = bg.GetComponent<UnityEngine.Camera>();
            if (bgCam == null) return "noBgCamComp";
            int bit = 1 << LayerMask.NameToLayer("BackgroundBlur");
            return (bgCam.cullingMask & bit) != 0 ? "on" : "OFF";
        }

        private void OnGUI()
        {
            if (!showGui) return;
            GUI.Label(new Rect(guiPos.x, guiPos.y, 1400f, 22f), line);
        }
    }
}
