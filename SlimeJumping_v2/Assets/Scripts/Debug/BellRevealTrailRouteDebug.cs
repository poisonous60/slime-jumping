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
            if (rc != null && rc.Rt != null)
            {
                DumpMpb(rc);
                StartCoroutine(DumpLater(rc));
            }
        }

        // The whole-platform-green symptom fits every fragment sampling ONE texel — which happens
        // when the renderer's MPB (mask + W2L matrix) is missing and the shader falls back to a
        // zero matrix. This reads what the renderer ACTUALLY has bound.
        static void DumpMpb(LocalTrailReceiver rc)
        {
            var r = rc.TargetRenderer;
            if (r == null) { Debug.Log("[TrailRouteDebug] MPB: targetRenderer NULL"); return; }
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            var tex = mpb.GetTexture("_LocalTrailMask");
            var m = mpb.GetMatrix("_LocalTrailW2L");
            Debug.Log($"[TrailRouteDebug] MPB on '{r.name}': empty={mpb.isEmpty} " +
                      $"maskTex={(tex == null ? "NULL" : tex.name + " " + tex.width + "x" + tex.height)} " +
                      $"w2l diag=({m.m00:F2},{m.m11:F2},{m.m22:F2},{m.m33:F2}) " +
                      $"min={mpb.GetVector("_LocalTrailMin")} size={mpb.GetVector("_LocalTrailSize")}");
        }

        System.Collections.IEnumerator DumpLater(LocalTrailReceiver rc)
        {
            yield return new WaitForSeconds(0.25f);
            if (rc != null && rc.Rt != null) DumpRtStats(rc);
        }

        // Answers "is the paint localized or flooding the whole RT" with actual pixel data.
        static void DumpRtStats(LocalTrailReceiver rc)
        {
            var rt = rc.Rt;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            var px = tex.GetPixels32();
            int painted = 0, strong = 0; byte max = 0;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int y = 0; y < rt.height; y++)
            for (int x = 0; x < rt.width; x++)
            {
                byte r = px[y * rt.width + x].r;
                if (r > max) max = r;
                if (r > 12) { painted++; if (x < minX) minX = x; if (y < minY) minY = y; if (x > maxX) maxX = x; if (y > maxY) maxY = y; }
                if (r > 128) strong++;
            }
            Destroy(tex);
            int total = rt.width * rt.height;
            Debug.Log($"[TrailRouteDebug] RT {rt.width}x{rt.height} max={max / 255f:F2} " +
                      $"painted(>5%)={painted * 100 / total}% strong(>50%)={strong * 100 / total}% " +
                      $"paintedBBox={(maxX < 0 ? "none" : $"x[{minX}..{maxX}] y[{minY}..{maxY}]")}");
        }
    }
}
