# FallingTrainPlatform (낙하 기차)

슬라임이 탑승할 수 있는 정적 지형지물 → 트리거 시 1회 한정으로 **순차적 연결부 파괴 + 점진적 sag** 텔레그래프 → **silence beat** → 곡선 낙하 → **hit-stop** → **rebound bounce** → 디자이너 지정점에 안착하여 숏컷 플랫폼이 됨.

`PlatformMotor` 상속이라 슬라임은 모든 단계 (sag 회전 포함) 를 `PlatformCarrier` 로 자동 추종. 별도 페어런팅 없음.

## 설계 철학 (왜 sin/cos 럼블이 아닌가)

연속 진동은 *컨트롤러 진동* 처럼 보이고 긴장감을 만들지 못함. Uncharted 2 기차씬, HL2 발판, INSIDE 가 공통적으로 쓰는 패턴은:

1. **Discrete jolt + 정적 (rest beat)**: 흔들지 않고 *간헐적으로 친다*. 사이의 침묵이 dread 를 만듦.
2. **연결부 순차 파괴 + 자세 변화**: 진동 ❌ → 어느 쪽이 끊겼는지 *지오메트리가* 보여줌 (sag).
3. **부서진 모서리 기준 회전**: 무게중심이 아니라 *남은 연결부* 를 피벗으로 회전. 한 번에 가장 큰 "진짜처럼 보임" 업그레이드.
4. **Audio-leading silence**: 낙하 직전에 creak 사운드가 *멈춤* — 가장 강력한 단일 텔레그래프.
5. **시뮬레이션된 debris 동행**: 메인이 kinematic 이어도 옆에 진짜 물리 파편이 있으면 뇌가 메인도 물리로 인식.
6. **Hit-stop**: 충돌 80–120ms `Time.timeScale` dip. Smash, Hollow Knight 등의 표준.
7. **Secondary rebound**: 착지 후 작은 튐. 없으면 끈끈하게 박힌 느낌.
8. **3-layer impact SFX**: crack (sharp) + groan (sustained) + dust-settle (long tail).

## 상태 머신

| 상태 | 동작 |
|------|------|
| Idle | `riderSensor.RiderCount > 0` 인 순간 Telegraphing 진입. one-shot (`hasFiredOnce` 후 재진입 ❌). |
| Telegraphing | `connectors` 리스트를 순서대로 snap. 각 snap 마다: VFX 인스턴스화 + `snapSound` (`SoundService.PlayAt`) + `snapImpulse.Emit(joltForce)` 카메라 펄스 + **남은 연결부 centroid 를 피벗으로 sag 회전** (`sagDegrees` 만큼, `sagCurve` 보간). 부호는 자동 — 끊긴 쪽이 떨어지는 방향. 다음 snap 은 `이전 snap 의 sagDuration + 다음 connector.preDelay` 후. 모든 snap 완료 후 `creakSource.Stop()` → `silenceBeforeFall` 정적 → Falling. `fallOnRiderExit && RiderCount==0` 이면 즉시 Falling 으로 단축. |
| Falling | `currentSagPos → landTarget.position` 을 `fallDuration` 동안 보간. X = `fallCurveX` (linear 등속), Y = `fallCurveY` (ease-in 가속). 회전은 `fallTilt` 만큼 `fallTiltCurve` 로 누적. 진입 시 `riderSensor` 비활성, `SpawnDebris()` (`debrisCount` 개의 dynamic Rigidbody2D 파편을 connector 위치에 jitter 와 함께 spawn, 하방 240° fan 으로 random velocity), `onFallStart` UnityEvent. 슬라임은 PlatformCarrier 로 동행. |
| HitStop | 진입 시 3-layer impact SFX (`crackSound` 즉시 + `groanSound` 즉시 0.85x 볼륨 + `dustSettleSound` `dustSettleDelay` 후) + `impactImpulse.Emit(impactImpulseForce)` + `impactVFXPrefab` 인스턴스화 + `onImpact`. 코루틴이 `Time.timeScale = hitStopTimeScale` 로 `hitStopDuration` (real-time) 동안 freeze 후 복구. 위치는 `(fallEnd, fallEndRot)` 고정. |
| Rebound | `reboundDuration` 동안 damped sin: `pos.y += reboundHeight · sin(2π·f·t) · exp(-d·t)`, `rot += reboundRotation · (cos(2π·f·t)·exp(-d·t) - exp(-d·t))`. 회전은 cos-leading 으로 충돌 즉시 킥. 종료 시 Landed. |
| Landed | `(fallEnd, fallEndRot)` 정지. DeltaPosition = 0 → 슬라임은 정적 플랫폼 위에서 그대로 활동. |

## 주요 필드

### 라이더 감지
- `riderSensor` (`PlatformRiderSensor`).

### Telegraph (sequential connector failure)
- `connectors` (List<Connector>): 순서대로 snap.
  - `anchor` (Transform): VFX/사운드 위치, 피벗 계산용 local position 캐시 대상.
  - `snapVFXPrefab` (ParticleSystem): snap 시 인스턴스화 (`main.duration + startLifetime + 1s` 후 자동 파괴).
  - `snapSound` (AudioClip): sharp crack one-shot.
  - `preDelay` (s): 직전 snap 완료 후 (또는 Telegraph 진입 후 첫 connector) 대기 시간.
  - `sagDegrees`: 회전량 (절댓값). 부호는 끊긴 쪽이 아래로 가도록 자동 결정.
  - `sagDuration` (s): sag 보간 시간.
  - `joltForce`: `snapImpulse.Emit()` 의 force 인자.
- `sagCurve` (AnimationCurve): 0→1 보간. 기본 ease-out (snap fast, settle).
- `silenceBeforeFall` (s): 마지막 sag 완료 후 낙하까지 정적. **0.2s 권장** — 가장 강한 단일 텔레그래프.
- `fallOnRiderExit` (bool): 슬라임이 범위 밖이면 즉시 Falling 으로 단축 (남은 snap 스킵).
- `creakSource` (AudioSource): 텔레그래프 동안 루프. 마지막 sag 완료 시점에 자동 정지.
- `snapImpulse` (`ImpulseEmitter`): 모든 connector snap 이 공유. force 는 connector 별 `joltForce`.

### Fall Trajectory
- `landTarget` (Transform): **필수**. 정확한 착지 지점/회전.
- `fallDuration` (s).
- `fallCurveX`, `fallCurveY`: 0→1 보간. Y 의 ease-in 으로 중력감.
- `fallTilt` (deg), `fallTiltCurve`: 낙하 중 누적 회전.

### Debris (시뮬레이션 트레일)
- `debrisPrefab` (GameObject): Rigidbody2D 가 있어야 velocity/angularVelocity 가 적용됨. null 이면 스킵.
- `debrisCount` (int): connector 위치들에 round-robin 으로 분산 spawn.
- `debrisSpeedRange` (Vector2 min..max): 초기 속력 범위. 방향은 하방 -150°~-30° fan random.
- `debrisLifetime` (s): 자동 파괴까지.
- `debrisSpawnJitter` (units): connector 위치 주위 random 오프셋.

### Hit-Stop
- `hitStopDuration` (s, real-time): 80–120ms 권장. Coroutine 이 `WaitForSecondsRealtime` 로 측정.
- `hitStopTimeScale` (0.01~1): freeze 동안의 `Time.timeScale`. 0.05 ≈ 거의 정지.

### Rebound
- `reboundHeight` (units), `reboundRotation` (deg): 진폭.
- `reboundDuration` (s).
- `reboundFrequency` (cycles per duration), `reboundDamping` (decay rate). 기본 1.5, 5 — 빠르게 감쇠.

### Impact (3-layer SFX)
- `crackSound`: sharp 초기 충격 (impact 순간 즉시).
- `groanSound`: sustained 금속 신음 (impact 순간 즉시, 볼륨 0.85x).
- `dustSettleSound`: long-tail 먼지 가라앉음 (impact 후 `dustSettleDelay` 후, real-time).
- `impactVolume` (0~1): 셋 모두 공통.
- `impactImpulse` (`ImpulseEmitter`) + `impactImpulseForce`: 큰 카메라 셰이크.
- `impactVFXPrefab` (GameObject) + `impactVFXLifetime`: 먼지/파편 클라우드, `landTarget` 위치에 인스턴스화.

### Events
- `onFallStart` (UnityEvent): silence beat 직후 Falling 진입 시 1회. 옆 기차 wobble, 데칼, 게임플레이 플래그.
- `onImpact` (UnityEvent): HitStop 진입 시 1회 (hit-stop 시작 직전).

## 디자인 주의

- **One-shot**: `hasFiredOnce` true 후 재진입 ❌.
- **Hit-stop 은 글로벌**: `Time.timeScale` 을 건드리므로 씬 전체가 freeze. 의도된 효과 (충돌감 표준 기법). 80~100ms 가 적정.
- **회전 피벗 = 남은 connector centroid**: 무게중심 ❌. local position 은 Awake 때 캐시되어 안정.
- **Sag 부호 자동**: connector 가 left 면 CCW (왼쪽 떨어짐), right 면 CW. `sagDegrees` 는 절댓값으로만 입력.
- **Connector 순서 = snap 순서**: 보통 멀리 있는 것부터 (또는 더 약한 것부터) → 마지막은 메인 hinge.
- **Debris 는 메인을 *진짜처럼* 보이게 만드는 핵심**: `debrisPrefab` 은 꼭 채워두자. 작은 stylized 큐브여도 됨.
- **`creakSource.Stop()` 자동 호출 시점**: 마지막 sag 완료 = silence beat 시작. 이게 가장 강한 텔레그래프이므로 다른 사운드와 겹치지 않게.

## 에디터 셋업

`SlimeJumping > Mechanism > Setup Falling Train Test` → placeholder 큐브 + 라이더 센서 + 좌우 connector 앵커 + LandTarget + AudioSource + 두 개의 ImpulseEmitter (snap/impact 분리) + debris 템플릿 큐브가 자동 생성, 모두 wire-up 완료. 디폴트 스케줄: Connector_Left @ 0.6s (sag 5°, 0.45s) → Connector_Right @ 1.05s (sag 7°, 0.3s) → 0.2s silence → drop.

3D 모델 임포트 시 `Visual` 차일드만 교체, `BoxCollider2D.size` 를 모델에 맞게 조정.

## 구현 위치

- [`FallingTrainPlatform.cs`](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/FallingTrainPlatform.cs)
- [`FallingTrainTestSetup.cs`](../../../SlimeJumping_v2/Assets/Scripts/Editor/FallingTrainTestSetup.cs)
