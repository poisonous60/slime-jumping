using System.Text;
using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Platforms;

namespace SlimeJumping.Debugging
{
    // Read-only diagnostic for sideways drift while resting on a TautLine.
    // Order 3001 samples AFTER TautLine's order-200 position snap, i.e. the final state each FU.
    // dPos.x >> vel.x*dt means the rope's snap is the mover, not commanded velocity.
    [DefaultExecutionOrder(3001)]
    [DisallowMultipleComponent]
    [AddComponentMenu("SlimeJumping/Debug/TautLine Slide Debugger")]
    public class TautLineSlideDebugger : MonoBehaviour
    {
        [SerializeField] SlimeController slime;
        [Tooltip("Optional. Auto-resolved from the slime's current SurfaceCollider each FU if left null.")]
        [SerializeField] TautLine tautLine;

        [Header("Logging")]
        [Tooltip("Log one line every N FixedUpdates while the slime rests on the rope.")]
        [SerializeField, Min(1)] int logEveryNFrames = 1;
        [Tooltip("Also log when NOT on the rope (state transitions, airborne). Spammy.")]
        [SerializeField] bool logOffRope = false;

        int fuCounter;
        Vector2 lastPos;
        bool hasLast;

        void Awake()
        {
            if (slime == null) slime = FindFirstObjectByType<SlimeController>();
        }

        TautLine ResolveRope()
        {
            Collider2D surf = slime != null ? slime.SurfaceCollider : null;
            if (surf == null) return tautLine;
            TautLine onSurf = surf.GetComponentInParent<TautLine>();
            if (onSurf != null) { tautLine = onSurf; return onSurf; }
            return tautLine;
        }

        void FixedUpdate()
        {
            if (slime == null) return;

            float dt = Time.fixedDeltaTime;
            var state = slime.CurrentState;
            TautLine rope = ResolveRope();

            bool onRope = rope != null
                && rope.Polygon != null
                && slime.SurfaceCollider == rope.Polygon
                && (state == SlimeController.SlimeState.Attached
                    || state == SlimeController.SlimeState.Charging);

            Vector2 pos = slime.Position;
            Vector2 dPos = hasLast ? pos - lastPos : Vector2.zero;
            lastPos = pos;
            hasLast = true;

            if (!onRope && !logOffRope) return;

            fuCounter++;
            if (fuCounter % logEveryNFrames != 0) return;

            Vector2 vel = slime.Velocity;
            Vector2 tan = slime.SurfaceTangent;
            Vector2 nrm = slime.SurfaceNormal;
            Vector2 grav = slime.GravityVector;

            float velTan = Vector2.Dot(vel, tan);
            float gravTan = Vector2.Dot(grav, tan);              // gravity-along-tangent slide drive
            float slideV = slime.TangentSpeed - slime.CrawlVelocity; // inertial slideVelocity channel
            float crawlV = slime.CrawlVelocity;

            float expectedDx = vel.x * dt;
            float snapDx = dPos.x - expectedDx;                  // residual from TautLine snap / projection

            float riderT = onRope ? rope.DebugRiderT : -1f;
            float dipDepth = onRope ? rope.DebugDipDepth : 0f;
            float dipVel = onRope ? rope.DebugDipVelocity : 0f;

            StringBuilder sb = new StringBuilder(256);
            sb.Append("[TautSlide] #").Append(Time.frameCount)
              .Append(" st=").Append(state)
              .Append(" onRope=").Append(onRope)
              .Append(" riderT=").Append(riderT.ToString("F4"))
              .Append(" pos=").Append(V(pos))
              .Append(" dPos=").Append(V(dPos))
              .Append(" vel=").Append(V(vel))
              .Append(" velTan=").Append(velTan.ToString("F4"))
              .Append(" gravTan=").Append(gravTan.ToString("F4"))
              .Append(" slideV=").Append(slideV.ToString("F4"))
              .Append(" crawlV=").Append(crawlV.ToString("F4"))
              .Append(" expDx=").Append(expectedDx.ToString("F5"))
              .Append(" snapDx=").Append(snapDx.ToString("F5"))
              .Append(" tan=").Append(V(tan))
              .Append(" nrm=").Append(V(nrm))
              .Append(" dip=").Append(dipDepth.ToString("F3"))
              .Append(" dipV=").Append(dipVel.ToString("F3"));
            Debug.Log(sb.ToString(), this);
        }

        static string V(Vector2 v)
        {
            return "(" + v.x.ToString("F4") + "," + v.y.ToString("F4") + ")";
        }
    }
}
