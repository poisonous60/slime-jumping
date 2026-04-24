# 상태 머신

4개 상태: **Attached**, **Charging**, **Airborne**, **Stunned**.

```
Attached ──Space──▶ Charging
    ▲                    │
    │                 릴리즈/풀차지/자동발사
    │                    ▼
    └──표면접촉── Airborne ──Space홀드+표면접촉──▶ Charging (에너지 전환)
                     ▲    │
                     │  위험접촉
                     │    ▼
                     └── Stunned (타이머 만료 시 Airborne 복귀)
```

## ChangeState 중앙화
- 모든 `currentState` 변경은 `ChangeState(newState, intentional)` 경유.
- `intentional=true`: Launch/Hop/Teleport 등 의도적 이탈. Attach 디바운스 카운터 리셋.
- 그 외 전환은 디바운스 토글로 카운트 (모서리 오실레이션 감지용).

## IExternalController Override
- `_override` 필드가 non-null이면 상태 머신 전체가 중단되고 외부 컨트롤러에 위임.
- `SetOverride(ctrl)` / `ReleaseOverride()` 로 진입/탈출.
- Override 전용 API: `OverrideVelocity(Vector2)` — HasOverride==true일 때만 유효 (그 외 LogError).
- 사용처: 뱀 삼킴, 벤치 앉기, 퍼즐 오버레이, 시네마틱.

## OnLanded 이벤트
- `event Action<Vector2, Vector2> OnLanded` — (position, surfaceNormal).
- Airborne→Attached 또는 Airborne→Charging(에너지 전환) 전환 시 발화.
- SnapToSurface 이후, CheckPlatformEffect 이전.
- 구독자: LandingDustSpawner, TrailStamper.

## Stunned 진입 시 정리
- 차징 비주얼 숨김, 캐리어 분리, 플랫폼 효과 해제, 표면 modifier 리셋.
- 모든 타이머/크롤/에임 초기화, `RegisterOverlapping` 호출.

## 관련
- [attach-debounce.md](attach-debounce.md)
- [stunned.md](stunned.md)
- [../infra/teleport.md](../infra/teleport.md)
