# 무너지는 플랫폼 (Crumbling Platform)

슬라임이 부착하면 일정 시간 뒤 충돌 판정이 사라지는 플랫폼.

## 트리거 조건
- 슬라임이 **Attached 상태**로 이 플랫폼 표면에 붙은 순간 트리거.
- 한번 트리거되면 카운트다운은 **슬라임이 떨어져도 계속 진행** (hop으로 리셋 불가).
- 선택적 각도 필터 (`useAngleLimit`): 지정 각도 범위(0°=바닥, 90°=벽, 180°=천장) 내 부착에서만.
  - 각도 계산: `Acos(Dot(surfaceNormal, up))` — 0-180° 풀 레인지.

## 상태 머신
```
Idle → Triggered → Crumbling → Gone → (Respawn 대기 →) Idle
```

| 상태 | 동작 | 시각 피드백 |
|------|------|------------|
| Idle | 대기 | 없음 |
| Triggered | `triggerDelay` 카운트다운 | 색상 경고 (→빨강) + 약한 셰이크 |
| Crumbling | `crumbleDuration` 카운트다운 | 강한 셰이크 + 알파 감소 |
| Gone | 콜라이더 비활성, 렌더러 비활성 | 안 보임 |
| Respawn 대기 | `respawnTime` 후 겹침 체크 통과 시 복귀 | — |

## 구현 방식
- **폴링 방식**: 매 FixedUpdate에서 `slime.CurrentState`, `slime.SurfaceCollider` 확인. SlimeController 수정 없음.
- **콜라이더 비활성화**: SurfaceProbe가 다음 프레임에 표면 못 찾아 자동 Airborne 전환.
- **Respawn 겹침 체크**: `Collider2D.Overlap()` — 슬라임이 비켜야 복귀.

## 구조 (비주얼 분리)
```
CrumblingPlatform (부모)
├─ BoxCollider2D, CrumblingPlatform.cs, (선택) PlatformMotor
└─ Visual (자식) — MeshFilter + MeshRenderer
```
셰이크를 자식 `localPosition`에만 적용하여 PlatformMotor의 `MovePosition`과 충돌하지 않음.

## 주의 — 코너 배치
`useAngleLimit + min/maxSurfaceAngle` 사용 시, Primary+Constraints 모델에서 `SurfaceNormal`은 인접 벽/바닥의 집계 법선이라 코너에서 bisector 쪽으로 기울 수 있음. 디폴트 0°–90° 범위는 영향 없지만, 타이트한 범위로 튜닝 시 코너 인접 배치 지양.

## 파라미터
| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| triggerDelay | 0.5 | 트리거 후 무너지기 시작까지 (초) |
| crumbleDuration | 0.2 | 무너지는 연출 시간 (초) |
| respawnTime | 3.0 | 복구까지. 음수면 영구 소멸 |
| useAngleLimit | false | 각도 필터 활성 |
| minSurfaceAngle | 0 | 트리거 최소 각도 |
| maxSurfaceAngle | 180 | 트리거 최대 각도 |
