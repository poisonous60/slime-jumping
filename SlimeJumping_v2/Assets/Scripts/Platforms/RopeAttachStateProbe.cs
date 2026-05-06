using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Platforms
{
    // State-diff probe for the rope re-attach bug. The bug repros only when the
    // slime had a non-rope contact between leaving the rope and re-touching it.
    // This component logs a single line at every "rope-touch-arrive" event,
    // tagged WORKING (no intermediate contact since last rope-leave) or
    // SUSPECT (had intermediate contact). Run both repros, diff the lines, and
    // the field that differs names the leaked state.
    //
    // Pull-only — never writes to SlimeController or RopeV3 (per project memory rule).
    [DefaultExecutionOrder(110)]
    public class RopeAttachStateProbe : MonoBehaviour
    {
        [SerializeField] RopeV3 rope;
        [SerializeField] SlimeController slime;

        [Tooltip("Also log the rope-leave event (when surfaceCollider was rope and now isn't).")]
        [SerializeField] bool logLeaves = true;

        [Tooltip("Also log every intermediate non-rope contact (helps confirm what surface caused the leak).")]
        [SerializeField] bool logIntermediates = true;

        bool prevSurfaceWasRope;
        bool hadIntermediateSinceLeave;
        Collider2D lastIntermediateSurface;
        int fu;

        void Reset()
        {
            rope = GetComponent<RopeV3>();
        }

        void Awake()
        {
            if (rope == null) rope = GetComponent<RopeV3>();
            if (slime == null) slime = FindFirstObjectByType<SlimeController>();
        }

        void FixedUpdate()
        {
            if (rope == null || slime == null || rope.Edge == null) return;
            fu++;

            Collider2D surf = slime.SurfaceCollider;
            bool surfIsRope = surf != null && surf == rope.Edge;
            bool surfIsOther = surf != null && !surfIsRope;

            // Rope-arrive edge: not rope last frame → rope this frame.
            if (surfIsRope && !prevSurfaceWasRope)
            {
                LogArrive();
                hadIntermediateSinceLeave = false;
                lastIntermediateSurface = null;
            }

            // Rope-leave edge: rope last frame → not rope this frame.
            if (!surfIsRope && prevSurfaceWasRope)
            {
                if (logLeaves) LogLeave();
                hadIntermediateSinceLeave = false;
                lastIntermediateSurface = null;
            }

            // Intermediate touch: any non-rope contact while we're tracking.
            if (surfIsOther && !hadIntermediateSinceLeave)
            {
                hadIntermediateSinceLeave = true;
                lastIntermediateSurface = surf;
                if (logIntermediates) LogIntermediate(surf);
            }

            prevSurfaceWasRope = surfIsRope;
        }

        void LogArrive()
        {
            string tag = hadIntermediateSinceLeave ? "SUSPECT" : "WORKING";
            string interSurf = lastIntermediateSurface != null ? lastIntermediateSurface.name : "<none>";
            string cached = slime.CachedSurfaceCollider != null ? slime.CachedSurfaceCollider.name : "<null>";
            string prev = slime.PrevSurfaceCollider != null ? slime.PrevSurfaceCollider.name : "<null>";
            string ropeRef = slime.CurrentRope != null
                ? (ReferenceEquals(slime.CurrentRope, rope) ? "this" : "OTHER")
                : "<null>";
            bool carrierOn = slime.Carrier != null && slime.Carrier.IsOnPlatform;

            Debug.Log(
                $"[RopeAttachProbe] {tag} ARRIVE fu={fu} t={Time.time:F3}\n" +
                $"  state={slime.CurrentState} touch={slime.IsTouchingSurface} pos=({slime.Position.x:F2},{slime.Position.y:F2}) vel=({slime.Velocity.x:F2},{slime.Velocity.y:F2})\n" +
                $"  currentRope={ropeRef} carrier.IsOnPlatform={carrierOn}\n" +
                $"  attachLockout={slime.AttachLockoutTimer:F3} blockAttach={slime.BlockAttach} postLaunch={slime.PostLaunchTimer:F3} coyote={slime.CoyoteTimer:F3} stick={slime.StickTimer:F3}\n" +
                $"  cachedSurface={cached} prevSurface={prev} intermediate={interSurf}",
                this);
        }

        void LogLeave()
        {
            Debug.Log(
                $"[RopeAttachProbe] LEAVE   fu={fu} t={Time.time:F3} " +
                $"state={slime.CurrentState} vel=({slime.Velocity.x:F2},{slime.Velocity.y:F2})",
                this);
        }

        void LogIntermediate(Collider2D surf)
        {
            Debug.Log(
                $"[RopeAttachProbe] INTER   fu={fu} t={Time.time:F3} surf={surf.name} " +
                $"state={slime.CurrentState}",
                this);
        }
    }
}
