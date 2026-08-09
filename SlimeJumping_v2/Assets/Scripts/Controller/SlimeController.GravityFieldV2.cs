using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // --- Gravity Field V2 (힘장) ---
        // 방향과 크기를 함께 공급하는 채널. V2 존이 하나라도 활성이면 방향은 아래 resolver 가,
        // 기본 크기는 샘플 크기가 맡는다. LowGravityZone 배율은 GravityMagnitude 에서 그 위에 곱해진다.
        // 물리장(gRaw)과 기준축(gravityUp)을 분리: 상쇄점에서 gRaw 는 0 이어도 축은 마지막 안정
        // 방향을 유지하고, 축 회전은 각속도 상한으로 프레임 간 급변을 차단한다.
        private readonly List<GravityFieldV2> gravityFieldV2Stack = new List<GravityFieldV2>();
        private readonly List<Collider2D> gravityFieldV2RescanBuffer = new List<Collider2D>(8);
        private int gravityFieldV2LayerMask;

        private Vector2 v2LastStableUp = Vector2.up;
        private bool v2LowGravityLatched;
        private float v2SampledMagnitude;

        // V2 활성 시 기본 중력 크기 = 샘플 크기. GravityMagnitude(LowGravity partial)가 경유한다.
        private float BaseGravityMagnitude => gravityFieldV2Stack.Count > 0
            ? v2SampledMagnitude
            : Mathf.Abs(settings.gravity);

        public void PushGravityFieldV2(GravityFieldV2 zone)
        {
            if (gravityFieldV2Stack.Contains(zone)) return;

            if (gravityFieldV2Stack.Count == 0)
            {
                v2LastStableUp = gravityUp;
                v2LowGravityLatched = false;
            }
            gravityFieldV2Stack.Add(zone);
        }

        public void PopGravityFieldV2(GravityFieldV2 zone) { gravityFieldV2Stack.Remove(zone); }

        // LIFO 최상단 존을 midpoint 에서 샘플. 래치 임계는 진입/이탈이 달라 경계에서 안 깜빡인다.
        private bool TryResolveGravityFieldV2(float dt)
        {
            if (gravityFieldV2Stack.Count == 0) return false;

            var zone = gravityFieldV2Stack[gravityFieldV2Stack.Count - 1];
            Vector2 samplePos = rb.position + velocity * (dt * 0.5f);
            float baseGravity = Mathf.Abs(settings.gravity);
            Vector2 g = zone.SampleAcceleration(samplePos, baseGravity);
            float mag = g.magnitude;
            v2SampledMagnitude = mag;

            if (v2LowGravityLatched)
            {
                if (mag > zone.LowGravityExitRatio * baseGravity) v2LowGravityLatched = false;
            }
            else if (mag < zone.LowGravityEnterRatio * baseGravity)
            {
                v2LowGravityLatched = true;
            }

            Vector2 targetUp = v2LowGravityLatched ? v2LastStableUp : -g / mag;
            if (!v2LowGravityLatched) v2LastStableUp = targetUp;

            float currentDeg = Mathf.Atan2(gravityUp.y, gravityUp.x) * Mathf.Rad2Deg;
            float targetDeg = Mathf.Atan2(targetUp.y, targetUp.x) * Mathf.Rad2Deg;
            float nextDeg = Mathf.MoveTowardsAngle(currentDeg, targetDeg, zone.RotationDegPerSec * dt);
            float nextRad = nextDeg * Mathf.Deg2Rad;
            gravityUp = new Vector2(Mathf.Cos(nextRad), Mathf.Sin(nextRad));
            gravityRight = new Vector2(gravityUp.y, -gravityUp.x);
            return true;
        }

        // 순간이동/override 해제 뒤엔 트리거 콜백이 안 온다. 양쪽 추적을 버리고 실제 겹침만 다시 push.
        internal void ResyncGravityFieldV2Zones()
        {
            for (int i = 0; i < gravityFieldV2Stack.Count; i++)
                gravityFieldV2Stack[i].ForceExitTracking();
            gravityFieldV2Stack.Clear();

            Physics2D.SyncTransforms();

            if (gravityFieldV2LayerMask == 0) gravityFieldV2LayerMask = LayerMask.GetMask("GravityField");

            var filter = new ContactFilter2D
            {
                useTriggers = true,
                useLayerMask = true,
                layerMask = gravityFieldV2LayerMask,
            };

            var cols = BodyColliders;
            if (cols == null)
            {
                ResyncGravityFieldV2With(circleCollider, filter);
                return;
            }
            for (int i = 0; i < cols.Length; i++)
                ResyncGravityFieldV2With(cols[i], filter);
        }

        private void ResyncGravityFieldV2With(CircleCollider2D col, ContactFilter2D filter)
        {
            if (col == null || !col.enabled) return;

            col.Overlap(filter, gravityFieldV2RescanBuffer);
            for (int i = 0; i < gravityFieldV2RescanBuffer.Count; i++)
            {
                var zone = gravityFieldV2RescanBuffer[i].GetComponentInParent<GravityFieldV2>();
                if (zone != null && zone.isActiveAndEnabled) zone.ForceEnterTracking(this);
            }
        }
    }
}
