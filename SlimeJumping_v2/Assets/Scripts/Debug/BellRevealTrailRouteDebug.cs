using UnityEngine;
using SlimeJumping.Rendering;

namespace SlimeJumping.Debugging
{
    /// <summary>Read-only probe for the "no goo on hidden platform" report: on every contact with a
    /// TestSet platform it logs whether the stamp routing chain is intact — registry lookup, receiver
    /// identity, RT allocation. Delete when the question is answered.</summary>
    [DefaultExecutionOrder(3000)]
    public class BellRevealTrailRouteDebug : MonoBehaviour
    {
        void OnCollisionEnter2D(Collision2D c)
        {
            if (!enabled || !c.collider.name.Contains("TestSet")) return;
            bool found = TrailColliderRegistry.TryGet(c.collider, out var rc);
            Debug.Log($"[TrailRouteDebug] contact={c.collider.name} registryHit={found} " +
                      $"receiver={(rc != null ? rc.name : "null")} " +
                      $"rt={(rc != null && rc.Rt != null ? rc.Rt.width + "x" + rc.Rt.height : "NULL")} " +
                      $"mgr={(WorldTrailManager.Instance != null ? "present" : "NULL")}");
        }
    }
}
