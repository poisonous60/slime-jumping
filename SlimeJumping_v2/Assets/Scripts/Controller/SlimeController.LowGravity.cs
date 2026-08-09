using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // --- Low Gravity Zone ---
        // 중력 "크기" 채널. 방향은 gravityFieldStack 이 따로 들고 있어 둘은 독립 합성된다.
        // 크기를 읽는 코드는 전부 GravityMagnitude 를 경유한다.
        private readonly List<LowGravityZone> lowGravityStack = new List<LowGravityZone>();
        private readonly List<Collider2D> lowGravityRescanBuffer = new List<Collider2D>(8);
        // NameToLayer 는 필드 이니셜라이저에서 호출 금지 — 텔레포트 때만 쓰므로 그때 계산한다.
        private int lowGravityLayerMask;

        /// <summary>스택 최상위 배율 (LIFO). 비면 1.</summary>
        public float GravityScale => lowGravityStack.Count > 0
            ? Mathf.Clamp01(lowGravityStack[lowGravityStack.Count - 1].GravityScale)
            : 1f;

        /// <summary>유효 중력 가속도 크기. 기본 크기는 V2 힘장 활성 시 샘플 크기로 바뀐다.</summary>
        public float GravityMagnitude => BaseGravityMagnitude * GravityScale;

        public void PushLowGravityZone(LowGravityZone zone)
        {
            if (!lowGravityStack.Contains(zone)) lowGravityStack.Add(zone);
        }

        public void PopLowGravityZone(LowGravityZone zone) { lowGravityStack.Remove(zone); }

        // 순간이동/override 해제 뒤엔 Unity 가 트리거 enter/exit 를 안 쏜다. 양쪽 추적을 버리고
        // 새 위치가 실제로 겹치는 존만 다시 push 한다.
        internal void ResyncLowGravityZones()
        {
            for (int i = 0; i < lowGravityStack.Count; i++)
                lowGravityStack[i].ForceExitTracking();
            lowGravityStack.Clear();

            // autoSyncTransforms 가 꺼져 있어 방금 쓴 rb.position 이 아직 브로드페이즈에 없다.
            Physics2D.SyncTransforms();

            if (lowGravityLayerMask == 0) lowGravityLayerMask = LayerMask.GetMask("GravityField");

            var filter = new ContactFilter2D
            {
                useTriggers = true,
                useLayerMask = true,
                layerMask = lowGravityLayerMask,
            };

            // 콜라이더 쌍 단위로 세야 나중에 오는 exit 횟수와 카운트가 맞는다 (잘린 슬라임 = 로브 여러 개).
            var cols = BodyColliders;
            if (cols == null)
            {
                ResyncLowGravityWith(circleCollider, filter);
                return;
            }
            for (int i = 0; i < cols.Length; i++)
                ResyncLowGravityWith(cols[i], filter);
        }

        private void ResyncLowGravityWith(CircleCollider2D col, ContactFilter2D filter)
        {
            if (col == null || !col.enabled) return;

            col.Overlap(filter, lowGravityRescanBuffer);
            for (int i = 0; i < lowGravityRescanBuffer.Count; i++)
            {
                var zone = lowGravityRescanBuffer[i].GetComponentInParent<LowGravityZone>();
                if (zone != null && zone.isActiveAndEnabled) zone.ForceEnterTracking(this);
            }
        }
    }
}
