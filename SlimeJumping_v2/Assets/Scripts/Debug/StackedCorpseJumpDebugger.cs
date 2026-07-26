using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Platforms;

namespace SlimeJumping.Debugging
{
    /// Read-only probe for the "can't jump (charge-launch) off a fallen bamboo
    /// corpse that is stacked on a live stalk" bug. Attach to the Slime.
    ///
    /// Logs one [StackedJump] line per FixedUpdate while any corpse exists:
    /// slime state / attach target / probe contacts / charge & lockout timers,
    /// plus the corpse rigidbody state and the nearest live stalk's spring state.
    /// State transitions and Launch events are promoted to LogWarning markers.
    /// Pull-only: reads public getters, writes nothing back (CLAUDE.md rule).
    [DefaultExecutionOrder(3002)]
    public class StackedCorpseJumpDebugger : MonoBehaviour
    {
        SlimeController slime;
        SlimeController.SlimeState prevState;
        int frame;
        readonly ContactPoint2D[] contactBuffer = new ContactPoint2D[8];

        void Awake()
        {
            slime = GetComponent<SlimeController>();
            if (slime == null)
            {
                Debug.LogError("[StackedJump] No SlimeController on this GameObject — probe disabled.", this);
                enabled = false;
                return;
            }
            slime.OnLaunch += HandleLaunch;
            prevState = slime.CurrentState;
        }

        void OnDestroy()
        {
            if (slime != null) slime.OnLaunch -= HandleLaunch;
        }

        void HandleLaunch(Vector2 pos, Vector2 vel, bool wasHop)
        {
            Debug.LogWarning(
                $"[StackedJump] <<< LAUNCH #{frame} pos={pos} vel={vel} |v|={vel.magnitude:F2} hop={wasHop}");
        }

        void FixedUpdate()
        {
            frame++;
            var corpses = Object.FindObjectsByType<FallingBambooCorpseMotor>(FindObjectsSortMode.None);
            if (corpses.Length == 0) return;

            var st = slime.CurrentState;
            if (st != prevState)
                Debug.LogWarning($"[StackedJump] <<< STATE {prevState} -> {st} #{frame} " +
                                 $"postLaunch={slime.PostLaunchTimer:F2} coyote={slime.CoyoteTimer:F2}");
            prevState = st;

            var surf = slime.SurfaceCollider;
            string surfName = surf != null
                ? surf.transform.root.name + "/" + surf.name
                : "none";

            var corpse = corpses[0];
            var crb = corpse.GetComponent<Rigidbody2D>();
            string corpseInfo = "no-rb";
            if (crb != null)
            {
                int n = crb.GetContacts(contactBuffer);
                string touches = "";
                for (int i = 0; i < n && i < 4; i++)
                {
                    var oc = contactBuffer[i].collider;
                    if (oc != null) touches += (i > 0 ? "," : "") + oc.transform.root.name + "/" + oc.name;
                }
                corpseInfo =
                    $"{crb.bodyType} sleep={crb.IsSleeping()} v={crb.linearVelocity} w={crb.angularVelocity:F1} " +
                    $"pos={crb.position} rot={crb.rotation:F1} touches[{n}]={touches}";
            }

            var slimeCarrier = GetComponent<SlimeJumping.Platforms.PlatformCarrier>();
            string carrierInfo = slimeCarrier != null && slimeCarrier.IsOnPlatform
                ? $"{slimeCarrier.CurrentPlatform.name} launchV={slimeCarrier.GetLaunchVelocity()}"
                : "off";

            // Nearest live stalk (skip the corpse clone — it has no BambooPlatformV3).
            BambooPlatformV3 stalk = null;
            float best = float.PositiveInfinity;
            foreach (var b in Object.FindObjectsByType<BambooPlatformV3>(FindObjectsSortMode.None))
            {
                float d = ((Vector2)b.transform.position - slime.Position).sqrMagnitude;
                if (d < best) { best = d; stalk = b; }
            }
            string stalkInfo = stalk != null
                ? $"{stalk.name} rider={stalk.RiderAttached} bend={stalk.CurrentDeflectionDeg:F1}"
                : "none";

            Debug.Log(
                $"[StackedJump] #{frame} st={st} pos={slime.Position} |v|={slime.Velocity.magnitude:F2} " +
                $"touch={slime.IsTouchingSurface} surf={surfName} contacts={slime.Probe.ActiveContactCount} " +
                $"aim={slime.AimOffset.magnitude:F2}/{slime.ActiveChargeRadius:F2} " +
                $"postLaunch={slime.PostLaunchTimer:F2} coyote={slime.CoyoteTimer:F2} stick={slime.StickTimer:F2} " +
                $"lockout={slime.AttachLockoutTimer:F2} toggles={slime.AttachToggleCount} " +
                $"| carrier {carrierInfo} | corpse {corpseInfo} | stalk {stalkInfo}");
        }
    }
}
