# Shift 대시 (Celeste-style)

**Shift** 누르면 8방향 대시. Attached / Airborne 모두에서 발동 (Attached 에선 표면을 박차고 이륙). 1충전제이며 어떤 표면이든 착지하면 재충전. 기존 `SlimeController` 와는 얇은 API 로만 연결된 **독립 컴포넌트** — 컴포넌트 비활성화로 기능 off.

## 구성

- [SlimeAirDash.cs](../../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeAirDash.cs) — 입력(Shift), 상태 가드, 쿨다운, 입력 버퍼, 공개 `RefillDash()` API.
- [SlimeAirDashVisual.cs](../../../SlimeJumping_v2/Assets/Scripts/Visual/SlimeAirDashVisual.cs) — VFX prefab 스폰 + HitStop freeze + ImpulseEmitter shake + `MaterialPropertyBlock` 색상 피드백 + SFX.
- `SlimeController.ApplyAirDash(Vector2 velocity, float duration)` — 속도 직접 주입 + `airDashActiveTimer` 설정.
- `SlimeController.OnAirDashStarted` / `SlimeAirDash.OnDash` — 비주얼/오디오 훅.

## 동작

1. `SlimeAirDash.Update` 에서 `Keyboard.current[dashKey].wasPressedThisFrame` 감지 → `pressBufferTimer = dashInputBuffer`.
2. 버퍼 살아 있는 동안 매 프레임 `TryDash()`. 조건:
   - `dashAvailable == true`
   - `cooldownTimer <= 0`
   - `!slimeController.HasOverride`
   - `slimeController.PostLaunchTimer <= 0` (발사 직후 대시 차단)
   - `CurrentState ∈ {Attached, Airborne}` (Charging/Stunned 차단)
3. 방향 해석 (Celeste 8-way):
   - WASD 입력 (`SlimeInputReader.LastInputMove`) 의 부호로 `(h, v)` 결정.
   - 양쪽 0 이면 마지막 수평 입력 부호(최초 프레임엔 velocity.x 부호, 없으면 +1).
   - 대각선은 `.normalized` 로 길이 1.
4. `SlimeController.ApplyAirDash(dir * dashSpeed, dashActiveDuration)` 호출.
   - Attached → `ForceAirborneWithVelocity(vel)` 으로 carrier detach + stick/crawl/rope 정리 + Airborne 전환.
   - Airborne → velocity 덮어쓰고 `ResetLaunchReference()`.
5. `dashAvailable = false`, `cooldownTimer = dashCooldown`, `pressBufferTimer = 0`.
6. `OnAirDashStarted`, `OnDash` 발화.

## 중력 / 에어 컨트롤 정지

`FixedUpdateAirborne` 상단에서 `airDashActiveTimer > 0` 이면 해당 프레임의 gravity 블록과 air-control 블록을 skip. 착지 판정은 그대로 실행되므로 대시 중 표면 접촉 시 정상 착지 → `ChangeState` 가 타이머를 0 으로 클리어.

## 지면 리셋 / 외부 리필

- `SlimeController.OnLanded` 구독 → `dashAvailable = true`, `cooldownTimer = 0`. 바닥/벽/천장 구분 없이 리셋.
- `SlimeAirDash.RefillDash()` 공개 메서드 — 크리스탈 하트 / 체크포인트 / 적 처치 리워드 등 외부 리필 소스용 진입점. `dashAvailable = true`, `cooldownTimer = 0` 세팅.

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
| Attached / Airborne (소진) | 무시 (단 `dashInputBuffer` 동안 조건 만족시까지 재시도) |
| Charging | 무시 (차징은 반드시 Launch 로 종료) |
| Stunned | 무시 |
| HasOverride | 무시 |
| PostLaunchTimer > 0 | 무시 (발사 직후 대시 차단) |

## 비주얼 / 피드백

`SlimeAirDashVisual` 이 `SlimeAirDash.OnDash` 구독 후 **독립 채널 4개**를 동시에 발동:

1. **VFX 스폰**: `dashVfxPrefab` 을 슬라임 `rb.position` 에 대시 방향 회전으로 인스턴스화, `vfxLifetime` 후 자동 파괴. `parentToSlime` 옵션으로 따라다니게 할 수도 있음 (기본 off — SlimeVisual 의 squash/stretch 가 VFX 왜곡을 일으킬 수 있음).
2. **HitStop + Camera Shake**: `HitStopService.Freeze(freezeDuration, cameraImpulse, impulseForce, freezeScale)` 한 번으로 시간 동결 + 카메라 impulse 동기 발동. `HitStopGuards` 가 차징/메뉴 중 자동 veto.
3. **색상 피드백**: `colorTargets` Renderer 에 `MaterialPropertyBlock` 으로 `_BaseColor` 를 세 가지 색 사이 보간 — `availableColor` (대시 가능), `exhaustedColor` (소진), `flashColor` (대시 순간 `flashDuration` 동안 페이드).
4. **SFX**: `SoundService.PlayAt(dashSfx, pos, SoundCategory.SFX_Slime)` + pitch jitter.

콜라이더/Rigidbody 에는 영향 없음 (CLAUDE.md 룰: divide visual and collider).

## 파라미터 (`SlimeAirDash`)

| 필드 | 기본값 | 설명 |
|------|--------|------|
| dashSpeed | 22 | 월드 유닛/초 |
| dashActiveDuration | 0.15 | 중력/에어컨트롤 정지 구간 |
| dashCooldown | 0.2 | 착지 리셋과 별개 안전장치 |
| dashInputBuffer | 0.12 | Shift 프레스 버퍼 (타이밍 관대) |
| dashKey | Key.LeftShift | New Input System Key enum |

## 파라미터 (`SlimeAirDashVisual`)

| 필드 | 기본값 | 설명 |
|------|--------|------|
| dashVfxPrefab | — | 대시 순간 스폰할 파티클 prefab |
| parentToSlime | false | true 면 슬라임 자식으로 붙어 따라감 |
| vfxLifetime | 0.6 | 스폰된 VFX 자동 파괴 시간 (0 = 절대 안 파괴) |
| freezeDuration | 0.05 | HitStop 지속 시간 (realtime sec) |
| freezeScale | 0 | 프리즈 중 Time.timeScale |
| cameraImpulse | — | 씬 카메라의 ImpulseEmitter (per-scene 수동 와이어) |
| impulseForce | 0.4 | 카메라 shake 세기 |
| colorTargets | [SlimeMesh] | 색상 피드백 대상 Renderer 배열 |
| colorProperty | _BaseColor | URP 기본 컬러 프로퍼티명 |
| availableColor / exhaustedColor | — | 대시 가능/소진 시 본체 색 |
| flashColor / flashDuration | white / 0.12 | 대시 순간 플래시 페이드 |

## 관련

- [ground-dash.md](ground-dash.md) — 별개 시스템 (Attached A/D 더블탭 임펄스), 유지.
- [hyper-dash.md](hyper-dash.md) — 대시 속도가 다음 점프에 실리는 원리.
- [airborne.md](airborne.md) — gravity / air control 분기 위치.
- [vfx-infra.md](../infra/vfx-infra.md) — HitStopService + ImpulseEmitter.
