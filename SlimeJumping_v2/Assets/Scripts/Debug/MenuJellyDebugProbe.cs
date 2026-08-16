using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.UI;

namespace SlimeJumping.Debugging
{
    /// 메뉴 젤리 접촉 진단 프로브 — 슬라임에 붙여 Play 한다. 젤리 표면과 닿아 있는 동안
    /// 매 틱 슬라임 상태/위치 델타/속도/법선/최근접 접촉점을 찍어 법선 플립과 침투 해소
    /// 킥을 구분한다(에피소드당 로그 상한으로 스팸 방지). 표면 전환 시엔 요약 1줄.
    /// 대상은 슬라임의 SurfaceCollider 를 매 틱 폴링해 해석 — 배선 불요.
    public sealed class MenuJellyDebugProbe : MonoBehaviour
    {
        const int MaxTickLogsPerEpisode = 120;

        SlimeController slime;
        Collider2D lastSurface;
        Vector2 lastPos;
        int tickLogs;
        readonly System.Collections.Generic.Dictionary<Collider2D, float> firstTop =
            new System.Collections.Generic.Dictionary<Collider2D, float>();

        void Start()
        {
            slime = GetComponent<SlimeController>();
            if (slime == null)
            {
                Debug.LogError("[JellyProbe] SlimeController 없음 — 슬라임에 붙여야 한다.", this);
                enabled = false;
            }
        }

        void FixedUpdate()
        {
            var surf = slime.IsTouchingSurface ? slime.SurfaceCollider : null;
            var jelly = surf != null ? surf.GetComponent<MenuJellyBase>() : null;
            if (surf != lastSurface)
            {
                Debug.Log($"[JellyProbe] surface {(lastSurface != null ? lastSurface.name : "none")} → " +
                          $"{(surf != null ? surf.name : "none")} jelly={jelly != null} " +
                          $"state={slime.CurrentState} slimeY={slime.Position.y:F3} vel={slime.Velocity} " +
                          $"n={slime.SurfaceNormal} impact={slime.ImpactNormalSpeed:F2}");
                lastSurface = surf;
                lastPos = slime.Position;
                tickLogs = 0;
            }
            if (jelly == null) return;
            if (!firstTop.ContainsKey(surf)) firstTop[surf] = surf.bounds.max.y;

            Vector2 pos = slime.Position;
            Vector2 dp = pos - lastPos;
            lastPos = pos;
            if (tickLogs >= MaxTickLogsPerEpisode) return;
            tickLogs++;
            Vector2 cp = surf.ClosestPoint(pos);
            float top = surf.bounds.max.y;
            Debug.Log($"[JellyProbe:T] {surf.name} st={slime.CurrentState} p=({pos.x:F3},{pos.y:F3}) " +
                      $"dp=({dp.x:+0.0000;-0.0000},{dp.y:+0.0000;-0.0000}) v=({slime.Velocity.x:F2},{slime.Velocity.y:F2}) " +
                      $"n=({slime.SurfaceNormal.x:F2},{slime.SurfaceNormal.y:F2}) cp=({cp.x:F3},{cp.y:F3}) " +
                      $"topΔ={top - firstTop[surf]:+0.000;-0.000} wave={jelly.DebugWavePeak:F2} body={jelly.DebugBodyComp:F2}");
        }
    }
}
