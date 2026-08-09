using System.Text;
using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Debugging
{
    // Read-only diagnostic for "multi-point overlap feels wrong" in GravityFieldV2.
    // Order 3002 → samples after the controller so gravityUp/velocity are the final FU state.
    // Fingerprints promoted to warnings:
    //   <<< V2-DEADZONE : |field| below deadzoneRatio × |settings.gravity| (cancellation pocket)
    //   <<< V2-TILT     : attached but gravityUp deviates from surface normal beyond tiltWarnDeg
    //   <<< V2-LAG      : gravityUp far from raw field up (rate-limit saturation or low-g latch)
    [DefaultExecutionOrder(3002)]
    [DisallowMultipleComponent]
    [AddComponentMenu("SlimeJumping/Debug/Gravity Field V2 Overlap Debugger")]
    public class GravityFieldV2OverlapDebugger : MonoBehaviour
    {
        [SerializeField] SlimeController slime;

        [Header("Trigger")]
        [Tooltip("점 정규화 거리 q 가 이 값 미만인 점이 하나라도 있으면 로깅.")]
        [SerializeField] float logWithinQ = 1.15f;
        [Tooltip("Log one line every N qualifying FixedUpdates.")]
        [SerializeField, Min(1)] int logEveryNFrames = 1;

        [Header("Fingerprints")]
        [Tooltip("|field| 가 기준 중력의 이 비율 미만이면 데드존 경고.")]
        [SerializeField] float deadzoneRatio = 0.1f;
        [Tooltip("부착 중 gravityUp 과 표면 노멀의 각도차가 이 값(도) 초과면 기울어짐 경고.")]
        [SerializeField] float tiltWarnDeg = 25f;
        [Tooltip("gravityUp 과 raw 장 방향의 각도차가 이 값(도) 초과면 래그/래치 경고.")]
        [SerializeField] float lagWarnDeg = 30f;

        GravityPoint2D[] points;
        int fuCounter;
        readonly StringBuilder sb = new StringBuilder(256);

        void Awake()
        {
            if (slime == null) slime = GetComponent<SlimeController>();
            if (slime == null) slime = FindFirstObjectByType<SlimeController>();
            points = FindObjectsByType<GravityPoint2D>(FindObjectsSortMode.InstanceID);
        }

        void FixedUpdate()
        {
            if (slime == null || points == null || points.Length == 0) return;

            Vector2 pos = slime.Position;

            bool nearAny = false;
            for (int i = 0; i < points.Length; i++)
            {
                if (points[i] != null && points[i].isActiveAndEnabled
                    && points[i].NormalizedDistance(pos) < logWithinQ) { nearAny = true; break; }
            }
            if (!nearAny) return;

            fuCounter++;
            if (fuCounter % logEveryNFrames != 0) return;

            float baseGravity = Mathf.Abs(slime.Gravity);
            Vector2 sum = Vector2.zero;

            sb.Length = 0;
            sb.Append("[GravV2] #").Append(fuCounter);
            sb.Append(" state=").Append(slime.CurrentState);
            sb.Append(" pos=(").Append(pos.x.ToString("F2")).Append(',').Append(pos.y.ToString("F2")).Append(')');

            for (int i = 0; i < points.Length; i++)
            {
                var p = points[i];
                if (p == null || !p.isActiveAndEnabled) continue;
                float q = p.NormalizedDistance(pos);
                if (q >= 1f)
                {
                    sb.Append(" p").Append(i).Append("(q=").Append(q.ToString("F2")).Append(" out)");
                    continue;
                }
                Vector2 c = p.Contribution(pos);
                sum += c;
                sb.Append(" p").Append(i).Append("(q=").Append(q.ToString("F2"))
                  .Append(" |g|=").Append(c.magnitude.ToString("F2")).Append(')');
            }

            Vector2 gravityUp = slime.GravityUp;
            float fieldMag = sum.magnitude;
            float lagDeg = fieldMag > 0.001f
                ? Vector2.Angle(gravityUp, -sum / fieldMag)
                : 0f;

            sb.Append(" sum=(").Append(sum.x.ToString("F2")).Append(',').Append(sum.y.ToString("F2"))
              .Append(") |sum|=").Append(fieldMag.ToString("F2"));
            sb.Append(" gMag=").Append(slime.GravityMagnitude.ToString("F2"));
            sb.Append(" gUp=(").Append(gravityUp.x.ToString("F2")).Append(',').Append(gravityUp.y.ToString("F2")).Append(')');
            sb.Append(" lagDeg=").Append(lagDeg.ToString("F1"));
            sb.Append(" |vel|=").Append(slime.Velocity.magnitude.ToString("F2"));

            bool attached = slime.CurrentState == SlimeController.SlimeState.Attached
                         || slime.CurrentState == SlimeController.SlimeState.Charging;
            float tiltDeg = 0f;
            if (attached)
            {
                tiltDeg = Vector2.Angle(gravityUp, slime.SurfaceNormal);
                sb.Append(" tiltDeg=").Append(tiltDeg.ToString("F1"));
            }

            bool deadzone = fieldMag < deadzoneRatio * baseGravity;
            bool tilt = attached && tiltDeg > tiltWarnDeg;
            bool lag = fieldMag > 0.001f && lagDeg > lagWarnDeg;

            if (deadzone) sb.Append(" <<< V2-DEADZONE");
            if (tilt) sb.Append(" <<< V2-TILT");
            if (lag) sb.Append(" <<< V2-LAG");

            if (deadzone || tilt || lag) Debug.LogWarning(sb.ToString());
            else Debug.Log(sb.ToString());
        }
    }
}
