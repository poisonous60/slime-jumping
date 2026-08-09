using System.Text;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Debugging
{
    // Read-only diagnostic for "movement feels stuttery inside LowGravityZone".
    // Order 3002 → samples the final post-physics state each FU.
    // Discriminates three suspects: gravity-scale flapping (zone/upper-line flicker),
    // physics position hitches (dPos vs commanded vel), and render frame spikes.
    [DefaultExecutionOrder(3002)]
    [DisallowMultipleComponent]
    [AddComponentMenu("SlimeJumping/Debug/Low Gravity Stutter Debugger")]
    public class LowGravityStutterDebugger : MonoBehaviour
    {
        [SerializeField] SlimeController slime;

        [Header("Trigger")]
        [Tooltip("Keep logging this many FixedUpdates after leaving low gravity.")]
        [SerializeField, Min(0)] int exitTailFrames = 120;
        [Tooltip("Log one line every N qualifying FixedUpdates.")]
        [SerializeField, Min(1)] int logEveryNFrames = 25;

        [Header("Fingerprints")]
        [Tooltip("|dPos - vel*dt| above this flags a physics hitch.")]
        [SerializeField] float hitchDistMin = 0.02f;
        [Tooltip("Render frame dt above this multiple of the smoothed dt flags a spike.")]
        [SerializeField] float frameSpikeFactor = 2f;

        int fuCounter;
        int tailLeft;
        Vector2 lastPos;
        Vector2 lastVel;
        float lastScale = 1f;
        bool hasLast;
        float smoothFrameDt = 1f / 60f;

        void Awake()
        {
            if (slime == null) slime = GetComponent<SlimeController>();
            if (slime == null) slime = FindFirstObjectByType<SlimeController>();
        }

        void FixedUpdate()
        {
            if (slime == null) return;

            float dt = Time.fixedDeltaTime;
            Vector2 pos = slime.Position;
            Vector2 vel = slime.Velocity;
            float scale = slime.GravityScale;

            Vector2 dPos = hasLast ? pos - lastPos : Vector2.zero;
            Vector2 dVel = hasLast ? vel - lastVel : Vector2.zero;
            float hitch = hasLast ? (dPos - lastVel * dt).magnitude : 0f;
            bool scaleFlip = hasLast && !Mathf.Approximately(scale, lastScale);
            float prevScale = lastScale;

            lastPos = pos; lastVel = vel; lastScale = scale; hasLast = true;

            bool inZone = scale < 1f;
            if (inZone) tailLeft = exitTailFrames;
            else if (tailLeft > 0) tailLeft--;
            else return;

            fuCounter++;
            if (!scaleFlip && hitch < hitchDistMin && fuCounter % logEveryNFrames != 0) return;

            var sb = new StringBuilder(256);
            sb.Append("[LowGravStutter] #").Append(Time.frameCount)
              .Append(" st=").Append(slime.CurrentState)
              .Append(" y=").Append(pos.y.ToString("F3"))
              .Append(" gScale=").Append(scale.ToString("F3"))
              .Append(" gMag=").Append(slime.GravityMagnitude.ToString("F3"))
              .Append(" vel=").Append(V(vel))
              .Append(" dVelY/dt=").Append((dVel.y / dt).ToString("F2"))
              .Append(" actualSpd=").Append((dPos.magnitude / dt).ToString("F3"))
              .Append(" hitch=").Append(hitch.ToString("F4"));

            if (scaleFlip)
                Debug.LogWarning(sb.Append(" prevScale=").Append(prevScale.ToString("F3"))
                    .Append("  <<< GSCALE-FLIP").ToString(), this);
            else if (hitch >= hitchDistMin)
                Debug.LogWarning(sb.Append("  <<< POS-HITCH").ToString(), this);
            else
                Debug.Log(sb.ToString(), this);
        }

        int lastGC0;

        // Spikes are logged inside AND outside the zone so the zone can be compared to baseline.
        void Update()
        {
            float fdt = Time.unscaledDeltaTime;
            int gc0 = System.GC.CollectionCount(0);
            if (fdt > smoothFrameDt * frameSpikeFactor && slime != null)
            {
                bool inZone = slime.GravityScale < 1f;
                Debug.LogWarning($"[LowGravStutter] #<{Time.frameCount}> frameDt={fdt * 1000f:F1}ms"
                    + $" avg={smoothFrameDt * 1000f:F1}ms zone={inZone} gcDelta={gc0 - lastGC0}  <<< FRAME-SPIKE");
            }
            lastGC0 = gc0;
            smoothFrameDt = Mathf.Lerp(smoothFrameDt, fdt, 0.1f);
        }

        static string V(Vector2 v)
        {
            return "(" + v.x.ToString("F3") + "," + v.y.ToString("F3") + ")";
        }
    }
}
