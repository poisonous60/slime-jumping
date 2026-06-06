using System.Text;
using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Physics;

namespace SlimeJumping.Debugging
{
    // Read-only diagnostic for the "slime wedges in a concave corner and keeps
    // accelerating instead of transitioning smoothly onto the wall face" bug.
    // Pulls everything from SlimeController + SurfaceProbe public getters — writes
    // nothing back (see CLAUDE.md: debug scripts pull from controller, never push).
    //
    // Runs at order 3002 so it samples AFTER the Attached handler + SnapToSurface +
    // any platform position writes, i.e. the slime's final resting state each FU.
    //
    // The suspected mechanism (to confirm with this log):
    //   FixedUpdateAttached → ComputeSlideSpeed (inertial branch) does
    //       slideVelocity += gravAlongTangent * ... * dt   EVERY frame, unconditionally.
    //   At a concave corner ProjectVelocityAgainstContacts clips the *commanded*
    //   velocity to ~0 (motion into both surfaces is cancelled), but the inertial
    //   slideVelocity STORE keeps integrating gravity → it grows without producing
    //   motion (wedge), then releases as a phantom launch when the wedge breaks.
    //   The existing drain only fires when velocity.sqrMagnitude < 0.01.
    //
    // Smoking gun to look for in the log:
    //   actualSpd ≈ 0 (motion clipped)  AND  |slideVel| rising frame-over-frame
    //   AND gravTan != 0  →  the WEDGE-ACCEL line fires. If drainArmed=False at the
    //   same time, the drain guard is too tight to catch this geometry.
    [DefaultExecutionOrder(3002)]
    [DisallowMultipleComponent]
    [AddComponentMenu("SlimeJumping/Debug/Corner Wedge Accel Debugger")]
    public class CornerWedgeAccelDebugger : MonoBehaviour
    {
        [SerializeField] SlimeController slime;

        [Header("Trigger")]
        [Tooltip("Min active contacts to count as a 'corner' frame and start logging.")]
        [SerializeField, Min(1)] int cornerContactThreshold = 2;
        [Tooltip("Log every FU while Attached/Charging, even with a single contact. Spammy.")]
        [SerializeField] bool logEveryFrame = false;
        [Tooltip("Log one line every N qualifying FixedUpdates.")]
        [SerializeField, Min(1)] int logEveryNFrames = 1;

        [Header("Wedge-accel detection")]
        [Tooltip("Actual speed (dPos/dt) below this counts as 'clipped/wedged'.")]
        [SerializeField] float wedgeActualSpeedMax = 0.05f;
        [Tooltip("|slideVel| frame-over-frame rise above this counts as 'still accelerating'.")]
        [SerializeField] float wedgeSlideRiseMin = 0.001f;

        int fuCounter;
        Vector2 lastPos;
        float lastSlideVel;
        bool hasLast;

        void Awake()
        {
            if (slime == null) slime = GetComponent<SlimeController>();
            if (slime == null) slime = FindFirstObjectByType<SlimeController>();
        }

        void FixedUpdate()
        {
            if (slime == null) return;

            float dt = Time.fixedDeltaTime;
            var state = slime.CurrentState;
            bool attached = state == SlimeController.SlimeState.Attached
                         || state == SlimeController.SlimeState.Charging;

            // Position + slideVel channel deltas (sampled every FU so deltas stay valid
            // across the trigger gate). slideVel = inertial store = TangentSpeed - CrawlVelocity.
            Vector2 pos = slime.Position;
            float slideVel = slime.TangentSpeed - slime.CrawlVelocity;
            Vector2 dPos = hasLast ? pos - lastPos : Vector2.zero;
            float dSlideVel = hasLast ? slideVel - lastSlideVel : 0f;
            lastPos = pos;
            lastSlideVel = slideVel;
            hasLast = true;

            var probe = slime.Probe;
            int active = probe != null ? probe.ActiveContactCount : 0;
            int total = probe != null ? probe.ContactCount : 0;

            bool cornerFrame = active >= cornerContactThreshold;
            if (!attached || (!cornerFrame && !logEveryFrame)) return;

            fuCounter++;
            if (fuCounter % logEveryNFrames != 0) return;

            Vector2 vel = slime.Velocity;
            Vector2 tan = slime.SurfaceTangent;
            Vector2 nrm = slime.SurfaceNormal;
            Vector2 grav = slime.GravityVector;

            float gravTan = Vector2.Dot(grav, tan);     // gravity-along-tangent slide drive
            float velCmdMag = vel.magnitude;            // commanded velocity after contact clip
            float velTan = Vector2.Dot(vel, tan);
            float actualSpd = dt > 0f ? dPos.magnitude / dt : 0f; // realized motion
            float clipLoss = velCmdMag - actualSpd;     // motion lost to contact cancellation
            bool drainArmed = vel.sqrMagnitude < 0.01f;  // would existing slideVelocity drain fire?

            float crawlVel = slime.CrawlVelocity;
            float tangentSpeed = slime.TangentSpeed;

            // Bug fingerprint: clipped to a standstill yet inertial store still climbing.
            bool wedgeAccel = actualSpd < wedgeActualSpeedMax
                           && dSlideVel > wedgeSlideRiseMin
                           && Mathf.Abs(gravTan) > 0.001f;

            StringBuilder sb = new StringBuilder(384);
            sb.Append("[CornerWedge] #").Append(Time.frameCount)
              .Append(" st=").Append(state)
              .Append(" contacts=").Append(active).Append('/').Append(total)
              .Append(" slideVel=").Append(slideVel.ToString("F4"))
              .Append(" dSlideVel=").Append(dSlideVel.ToString("F4"))
              .Append(" crawlVel=").Append(crawlVel.ToString("F4"))
              .Append(" tanSpd=").Append(tangentSpeed.ToString("F4"))
              .Append(" gravTan=").Append(gravTan.ToString("F4"))
              .Append(" velCmd=").Append(velCmdMag.ToString("F4"))
              .Append(" velTan=").Append(velTan.ToString("F4"))
              .Append(" actualSpd=").Append(actualSpd.ToString("F4"))
              .Append(" clipLoss=").Append(clipLoss.ToString("F4"))
              .Append(" drainArmed=").Append(drainArmed)
              .Append(" tan=").Append(V(tan))
              .Append(" nrm=").Append(V(nrm));

            if (wedgeAccel)
                Debug.LogWarning(sb.Append("  <<< WEDGE-ACCEL").ToString(), this);
            else
                Debug.Log(sb.ToString(), this);

            // Per-contact geometry: the two (or more) surfaces forming the corner.
            if (probe != null)
            {
                Vector2 gUp = slime.GravityUp;
                for (int i = 0; i < total; i++)
                {
                    var col = probe.GetContactCollider(i);
                    Vector2 n = probe.GetContactNormal(i);
                    float angle = SurfaceProbe.GetSurfaceAngle(n, gUp);
                    float surfUp = Vector2.Dot(n, gUp);
                    bool isActive = probe.IsContactActive(i);
                    bool isPrimary = i == probe.SelectedIndex;
                    float gravTanI = Vector2.Dot(grav, new Vector2(-n.y, n.x)); // grav along this contact's tangent
                    Debug.Log($"  [{i}] {(col != null ? col.name : "null")} angle={angle:F1}"
                        + $" surfUp={surfUp:F3} n={V(n)} gravTan={gravTanI:F3}"
                        + $" active={isActive}{(isPrimary ? " *PRIMARY" : "")}", this);
                }
            }
        }

        static string V(Vector2 v)
        {
            return "(" + v.x.ToString("F3") + "," + v.y.ToString("F3") + ")";
        }
    }
}
