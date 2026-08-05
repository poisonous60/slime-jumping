using System.Text;
using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Mechanisms.Afterimage;

namespace SlimeJumping.Debugging
{
    // Read-only probe: samples the AfterimageStamp registry after all sim writes.
    // Fingerprint: a ghost old enough to harden, not overlapping the slime, and
    // still without a collider -> "<<< STUCK-GHOST" warning.
    [DefaultExecutionOrder(3002)]
    public class AfterimageStampDebugger : MonoBehaviour
    {
        SlimeController slime;
        float nextLogTime;
        static readonly StringBuilder sb = new StringBuilder(256);

        void Awake()
        {
            slime = GetComponent<SlimeController>();
        }

        void FixedUpdate()
        {
            var list = AfterimageStamp.Active;
            if (list.Count == 0) return;

            int hard = 0, ghost = 0;
            AfterimageStamp oldestGhost = null;
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s.ColliderOn) hard++; else ghost++;
                if (!s.Hardened && (oldestGhost == null || s.Age > oldestGhost.Age))
                    oldestGhost = s;
            }

            bool stuck = oldestGhost != null && oldestGhost.Age > 1f && !oldestGhost.OverlapsSlimeNow;
            if (!stuck && Time.time < nextLogTime) return;
            nextLogTime = Time.time + 0.25f;

            sb.Length = 0;
            sb.Append("[AfterimageDbg] #").Append(Time.frameCount)
              .Append(" stamps=").Append(list.Count)
              .Append(" hard=").Append(hard)
              .Append(" ghost=").Append(ghost);
            if (slime != null)
                sb.Append(" slimePos=").Append(slime.Position.ToString("F2"))
                  .Append(" state=").Append(slime.CurrentState);
            if (oldestGhost != null)
            {
                float dist = slime != null
                    ? Vector2.Distance(slime.Position, oldestGhost.transform.position) : -1f;
                sb.Append(" | oldestGhost age=").Append(oldestGhost.Age.ToString("F2"))
                  .Append(" dist=").Append(dist.ToString("F2"))
                  .Append(" colOn=").Append(oldestGhost.ColliderOn)
                  .Append(" pos=").Append(((Vector2)oldestGhost.transform.position).ToString("F2"))
                  .Append(" inBlockBox=").Append(oldestGhost.OverlapsSlimeNow);
            }

            if (stuck)
            {
                sb.Append(" <<< STUCK-GHOST");
                Debug.LogWarning(sb.ToString());
            }
            else
            {
                Debug.Log(sb.ToString());
            }
        }
    }
}
