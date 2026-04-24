# Shift 대시 (Celeste-style)

**Shift** 누르면 8방향 대시. Attached / Airborne 모두에서 발동 (Attached 에선 표면을 박차고 이륙). 1충전제이며 어떤 표면이든 착지하면 재충전. 기존 `SlimeController` 와는 얇은 API 로만 연결된 **독립 컴포넌트** — 컴포넌트 비활성화로 기능 off.

## 구성

- [SlimeAirDash.cs](../../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeAirDash.cs) — 입력, 상태, 쿨다운.
- [SlimeAirDashVisual.cs](../../../SlimeJumping_v2/Assets/Scripts/Visual/SlimeAirDashVisual.cs) — 파티클 / 트레일 / 색 플래시 / SFX.
- `SlimeController.ApplyAirDash(Vector2 velocity, float duration)` — 속도 직접 주입 + `airDashActiveTimer` 설정.
- `SlimeController.OnAirDashStarted` / `SlimeAirDash.OnDash` — 비주얼/오디오 훅.

## 동작

1. `SlimeAirDash.Update` 에서 `Keyboard.current[dashKey].wasPressedThisFrame` 감지.
2. 조건: `CurrentState ∈ {Attached, Airborne}`, `!HasOverride`, `dashAvailable`, `cooldownTimer <= 0`.
3. 방향 해석 (Celeste 8-way):
   - WASD 입력 (`SlimeInputReader.LastInputMove`) 의 부호로 `(h, v)` 결정.
   - 양쪽 0 이면 마지막 수평 입력 부호(최초 프레임엔 velocity.x 부호, 없으면 +1).
   - 대각선은 `.normalized` 로 길이 1.
4. `SlimeController.ApplyAirDash(dir * dashSpeed, dashActiveDuration)` 호출.
   - Attached → `ForceAirborneWithVelocity(vel)` 으로 carrier detach + stick/crawl/rope 정리 + Airborne 전환.
   - Airborne → velocity 덮어쓰고 `ResetLaunchReference()`.
5. `dashAvailable = false`, `cooldownTimer = dashCooldown`.
6. `OnAirDashStarted`, `OnDash` 발화.

## 중력 / 에어 컨트롤 정지

`FixedUpdateAirborne` 상단에서 `airDashActiveTimer > 0` 이면 해당 프레임의 gravity 블록과 air-control 블록을 skip. 착지 판정은 그대로 실행되므로 대시 중 표면 접촉 시 정상 착지 → `ChangeState` 가 타이머를 0 으로 클리어.

## 지면 리셋

`SlimeController.OnLanded` 구독 → `dashAvailable = true`. 바닥/벽/천장 구분 없이 리셋 (슬라임은 전 표면 부착 가능).

## 다음 점프 계승 (자동)

`ApplyAirDash` 는 속도를 덮고 `ResetLaunchReference()` 로 에너지 변환 기준점을 갱신. 결과:
- 대시 탄젠트 속도 → 착지 시 `crawlVelocity` 에 계승 ([airborne.md](airborne.md) 의 ComputeLandingCrawlVelocity).
- 차징 진입 시 `chargeEntryCrawl = crawlVelocity`.
- Launch 에서 `tangentInheritanceFactor` 곱해져 점프에 반영 ([hyper-dash.md](hyper-dash.md)).

**결론**: 별도 코드 없이 기존 하이퍼 대시 경로로 "대시 → 착지 → 점프" 에 대시 속도가 녹아든다.

## 상태 테이블

| 상태 | Shift |
|------|-------|
| Attached (dashAvailable) | 표면 박차고 대시 (ForceAirborneWithVelocity) |
| Airborne (dashAvailable) | 속도 덮어쓰기 대시 |
| Attached / Airborne (소진) | 무시 |
| Charging | 무시 (차징은 반드시 Launch 로 종료) |
| Stunned | 무시 |
| HasOverride | 무시 |

## 비주얼

`SlimeAirDashVisual` 이 `SlimeAirDash.OnDash` 구독. 구성 가능 요소:
- **파티클 버스트**: `ParticleSystem.Emit(burstCount)` — 방향으로 회전시켜 발사.
- **트레일**: 자식 `TrailRenderer.emitting = true` → `dashActiveDuration + trailTailExtension` 후 off.
- **색 플래시**: `flashTargets` 의 각 Renderer 에 `MaterialPropertyBlock` 으로 `_BaseColor` 를 `flashColor` 로 보간, `flashCurve` 로 감쇠.
- **SFX**: `SoundService.PlayAt(clip, pos, SFX_Slime)`.

콜라이더/Rigidbody 에는 영향 없음 (CLAUDE.md 룰: divide visual and collider).

## 파라미터 (`SlimeAirDash`)

| 필드 | 기본값 | 설명 |
|------|--------|------|
| dashSpeed | 22 | 월드 유닛/초 |
| dashActiveDuration | 0.15 | 중력/에어컨트롤 정지 구간 |
| dashCooldown | 0.2 | 착지 리셋과 별개 안전장치 |
| dashKey | Key.LeftShift | New Input System Key enum |

## 관련

- [ground-dash.md](ground-dash.md) — 별개 시스템 (Attached S 임펄스), 유지.
- [hyper-dash.md](hyper-dash.md) — 대시 속도가 다음 점프에 실리는 원리.
- [airborne.md](airborne.md) — gravity / air control 분기 위치.
