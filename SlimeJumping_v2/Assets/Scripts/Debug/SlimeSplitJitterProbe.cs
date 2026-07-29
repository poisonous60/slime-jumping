using System.Text;
using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Physics;
using SlimeJumping.Effects;

namespace SlimeJumping.Debugging
{
    // Read-only diagnostic for the "camera shakes + slime floats while CUT (split)" bugs.
    // Pulls from public getters only, writes nothing back (CLAUDE.md: debug pulls, never pushes).
    // Order 3002 so it samples AFTER the Attached handler + SnapToSurface + platform writes,
    // i.e. the slime's final RESTING state each FixedUpdate.
    [DefaultExecutionOrder(3002)]
    [DisallowMultipleComponent]
    [AddComponentMenu("SlimeJumping/Debug/Slime Split Jitter Probe")]
    public class SlimeSplitJitterProbe : MonoBehaviour
    {
        [SerializeField] SlimeController slime;
        [SerializeField] SlimeVisual visual;
        [SerializeField] SlimeSplitSequence split;

        [Header("Trigger")]
        [Tooltip("Only log while CUT (SlimeSplitSequence.IsActive). Off = log whenever grounded.")]
        [SerializeField] bool onlyWhenSplit = true;
        [Tooltip("Log one line every N qualifying FixedUpdates.")]
        [SerializeField, Min(1)] int logEveryNFrames = 1;

        [Header("Jitter detection")]
        [Tooltip("Per-FU center motion along the surface normal above this (abs) counts as a jitter sample.")]
        [SerializeField] float heightJitterMin = 0.0015f;
        [Tooltip("Per-FU tangential motion below this counts as 'stationary' (so jitter isn't just sliding).")]
        [SerializeField] float stationaryTanMax = 0.01f;

        int fuCounter;
        Vector2 lastPos;
        Vector2 lastMeshPos;
        Vector2 lastCamPos;
        float lastHeightDelta;
        float lastReach;
        bool hasLast;

        // meshT world origin is what the VISUAL cut is centered on.
        Transform meshT;
        Transform camFollow;

        void Awake()
        {
            if (slime == null) slime = GetComponent<SlimeController>();
            if (slime == null) slime = FindFirstObjectByType<SlimeController>();
            if (visual == null) visual = FindFirstObjectByType<SlimeVisual>();
            if (split == null) split = FindFirstObjectByType<SlimeSplitSequence>();

            var mf = slime != null ? slime.GetComponentInChildren<MeshFilter>(true) : null;
            if (mf != null) meshT = mf.transform;

            var proxy = FindFirstObjectByType<SlimeJumping.Camera.SlimeCameraTargetProxy>();
            if (proxy != null) camFollow = proxy.transform;
        }

        void FixedUpdate()
        {
            if (slime == null) return;

            float dt = Time.fixedDeltaTime;
            var state = slime.CurrentState;
            bool grounded = state == SlimeController.SlimeState.Attached
                         || state == SlimeController.SlimeState.Charging;

            Vector2 pos = slime.Position;
            Vector2 nrm = slime.SurfaceNormal;
            Vector2 tan = new Vector2(-nrm.y, nrm.x);

            Vector2 dPos = hasLast ? pos - lastPos : Vector2.zero;
            float dHeight = Vector2.Dot(dPos, nrm);   // center motion ALONG normal (the shake axis)
            float dTan = Vector2.Dot(dPos, tan);       // tangential motion (sliding, not shake)
            lastPos = pos;

            // meshDY nonzero = visual cut plane sits at a different height than the collision one
            // ("floating cloak"); dMesh = does the rendered body jitter even when rb doesn't.
            Vector2 meshPos = meshT != null ? (Vector2)meshT.position : pos;
            float dMesh = hasLast ? Vector2.Dot(meshPos - lastMeshPos, nrm) : 0f;
            float meshDY = meshT != null ? (meshPos.y - pos.y) : 0f;
            lastMeshPos = meshPos;
            // Camera target motion along the normal — jitter here with rb steady = visual follow chain.
            float dCam = 0f;
            if (camFollow != null && hasLast)
                dCam = Vector2.Dot((Vector2)camFollow.position - lastCamPos, nrm);
            if (camFollow != null) lastCamPos = (Vector2)camFollow.position;

            bool splitActive = split != null && split.IsActive;

            if (!grounded || (onlyWhenSplit && !splitActive))
            {
                hasLast = true;
                lastHeightDelta = dHeight;
                return;
            }

            fuCounter++;
            if (fuCounter % logEveryNFrames != 0) { hasLast = true; lastHeightDelta = dHeight; return; }

            var probe = slime.Probe;
            int sel = probe != null ? probe.SelectedIndex : -1;
            int active = probe != null ? probe.ActiveContactCount : 0;

            // Resting reach: center → selected surface after SnapToSurface. Cut → cut offset, uncut → R.
            float reach = (probe != null && sel >= 0) ? probe.GetContactDistance(sel) : float.NaN;
            float R = slime.SlimeRadius;
            float reachFrac = R > 1e-5f ? reach / R : float.NaN;
            float dReach = hasLast && !float.IsNaN(reach) && !float.IsNaN(lastReach) ? reach - lastReach : 0f;
            lastReach = reach;

            Vector2 selN = (probe != null && sel >= 0) ? probe.GetContactNormal(sel) : nrm;

            // body.up vs true normal: nonzero = cut plane tilted off the surface frame → reach error.
            float bodyUpDev = float.NaN;
            if (visual != null && visual.Body3d != null)
            {
                Vector2 bUp = (Vector2)visual.Body3d.up;
                if (bUp.sqrMagnitude > 1e-6f)
                    bodyUpDev = Vector2.SignedAngle(nrm, bUp);
            }

            bool stationary = Mathf.Abs(dTan) < stationaryTanMax;
            // Fingerprint: cut + stationary + normal-axis motion whose sign flips (oscillation, not settle).
            bool jitter = splitActive && stationary
                       && Mathf.Abs(dHeight) > heightJitterMin
                       && hasLast && (dHeight * lastHeightDelta) < 0f;
            lastHeightDelta = dHeight;
            hasLast = true;

            StringBuilder sb = new StringBuilder(320);
            sb.Append("[SplitJitter] #").Append(Time.frameCount)
              .Append(" st=").Append(state)
              .Append(" split=").Append(splitActive)
              .Append(" active=").Append(active).Append("/sel=").Append(sel)
              .Append(" reach=").Append(reach.ToString("F4"))
              .Append(" reachFrac=").Append(reachFrac.ToString("F3"))
              .Append(" dReach=").Append(dReach.ToString("F4"))
              .Append(" dHeight=").Append(dHeight.ToString("F4"))
              .Append(" dTan=").Append(dTan.ToString("F4"))
              .Append(" dMesh=").Append(dMesh.ToString("F4"))
              .Append(" meshDY=").Append(meshDY.ToString("F4"))
              .Append(" dCam=").Append(dCam.ToString("F4"))
              .Append(" bodyUpDev=").Append(bodyUpDev.ToString("F2"))
              .Append(" nrm=").Append(V(nrm))
              .Append(" selN=").Append(V(selN));

            if (jitter)
                Debug.LogWarning(sb.Append("  <<< SPLIT-JITTER").ToString(), this);
            else
                Debug.Log(sb.ToString(), this);
        }

        // Render-frame sample of the VISUAL offset: bodyLocalY oscillating while rb is stone-still
        // means the shake is the SlimeVisual deform pipeline, not the cut geometry.
        float lastVisLocalY;
        bool hasVisLast;
        void LateUpdate()
        {
            if (slime == null || visual == null || visual.Body3d == null) return;
            var state = slime.CurrentState;
            bool grounded = state == SlimeController.SlimeState.Attached
                         || state == SlimeController.SlimeState.Charging;
            if (!grounded) return;
            bool splitActive = split != null && split.IsActive;
            if (onlyWhenSplit && !splitActive) return;

            float localY = visual.Body3d.localPosition.y;
            float dLocalY = hasVisLast ? localY - lastVisLocalY : 0f;
            lastVisLocalY = localY;
            hasVisLast = true;
            float meshWY = meshT != null ? meshT.position.y : visual.Body3d.position.y;
            // Discriminators: rootTY oscillating with rbY constant = Rigidbody2D interpolation;
            // scaleY/eulerZ oscillating = SlimeVisual deform sweeping the off-pivot mesh.
            float rootTY = slime.transform.position.y;
            float scaleY = visual.Body3d.localScale.y;
            float eulerZ = visual.Body3d.localEulerAngles.z;

            Debug.Log($"[SplitVis] #{Time.frameCount} split={splitActive}"
                + $" meshWY={meshWY:F4} rbY={slime.Position.y:F4} rootTY={rootTY:F4}"
                + $" bodyLocalY={localY:F4} scaleY={scaleY:F4} eulerZ={eulerZ:F2}", this);
        }

        static string V(Vector2 v)
        {
            return "(" + v.x.ToString("F3") + "," + v.y.ToString("F3") + ")";
        }
    }
}
