using System.Text;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Debugging
{
    // Read-only diagnostic for the sandwich-PINCH (squeeze between two WaypointPlatformV2 plates).
    // Pulls everything from SlimeController public getters — writes nothing back (CLAUDE.md rule:
    // debug scripts pull from the controller, never push). Attach to the Slime and toggle `logging`
    // in the inspector at runtime. Logs one tagged "[Pinch]" line per FixedUpdate while a pinch is
    // active (plus an optional tail after release to see the exit).
    //
    // Runs at order 3002 so it samples AFTER the controller + UpdatePinch + platform writes — the
    // slime's final resting state each FU.
    //
    // Tuning signals:
    //   comp    = PinchCompression01 (squash depth 0..1) — is it actually squashing?
    //   velTan  = slime velocity along the squeeze-out tangent — is the lateral spring pushing?
    //   moveTan = realized lateral displacement / dt — is it ACTUALLY sliding out, or stuck?
    //   velAxis = velocity along the squeeze axis (gravity / press direction).
    //   gap/pen = derived from comp (gap = 2R(1−comp)) — how deep the plates closed.
    [DefaultExecutionOrder(3002)]
    [DisallowMultipleComponent]
    [AddComponentMenu("SlimeJumping/Debug/Pinch Debugger")]
    public class PinchDebugger : MonoBehaviour
    {
        [Tooltip("Master on/off — toggle in the inspector (also works at runtime in Play mode).")]
        [SerializeField] bool logging = true;
        [Tooltip("Slime to read. Auto-found from this GameObject / scene if left empty.")]
        [SerializeField] SlimeController slime;
        [Tooltip("Keep logging this many FixedUpdates after the pinch releases, to see the exit.")]
        [SerializeField, Min(0)] int tailFrames = 6;

        Vector2 lastPos;
        bool hasLast;
        int tail;

        void Awake()
        {
            if (slime == null) slime = GetComponent<SlimeController>();
            if (slime == null) slime = FindFirstObjectByType<SlimeController>();
        }

        void FixedUpdate()
        {
            if (!logging || slime == null) return;

            float dt = Time.fixedDeltaTime;
            Vector2 pos = slime.Position;
            Vector2 dPos = hasLast ? pos - lastPos : Vector2.zero;
            lastPos = pos;
            hasLast = true;

            bool active = slime.PinchActive;
            if (!active)
            {
                if (tail > 0) tail--;
                else return;
            }
            else
            {
                tail = tailFrames;
            }

            Vector2 axis = slime.PinchAxis;
            if (axis.sqrMagnitude < 1e-6f) axis = Vector2.up;
            axis = axis.normalized;
            Vector2 tan = new Vector2(-axis.y, axis.x);   // squeeze-out direction

            float R = slime.SlimeRadius;
            float comp = slime.PinchCompression01;
            float pen = 2f * R * comp;
            float gap = 2f * R - pen;

            Vector2 vel = slime.Velocity;
            float velTan = Vector2.Dot(vel, tan);
            float velAxis = Vector2.Dot(vel, axis);
            float moveTan = dt > 0f ? Vector2.Dot(dPos, tan) / dt : 0f;

            StringBuilder sb = new StringBuilder(256);
            sb.Append("[Pinch] #").Append(Time.frameCount)
              .Append(" st=").Append(slime.CurrentState)
              .Append(" active=").Append(active)
              .Append(" comp=").Append(comp.ToString("F2"))
              .Append(" scale=").Append(slime.BodyScaleRatio.ToString("F2"))
              .Append(" gap=").Append(gap.ToString("F3"))
              .Append(" pen=").Append(pen.ToString("F3"))
              .Append(" velTan=").Append(velTan.ToString("F3"))
              .Append(" moveTan=").Append(moveTan.ToString("F3"))
              .Append(" velAxis=").Append(velAxis.ToString("F3"))
              .Append(" contacts=").Append(slime.PinchContactCount);
            for (int i = 0; i < slime.PinchContactCount; i++)
                sb.Append(" n").Append(i).Append('=').Append(V(slime.GetPinchContactNormal(i)));
            Debug.Log(sb.ToString(), this);
        }

        static string V(Vector2 v) => "(" + v.x.ToString("F2") + "," + v.y.ToString("F2") + ")";
    }
}
