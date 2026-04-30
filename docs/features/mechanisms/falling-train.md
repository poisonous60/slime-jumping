# FallingTrainPlatform (낙하 기차)

슬라임이 탑승할 수 있는 정적 지형지물 → 트리거 시 1회 한정 **순차적 connector 파괴 + 점진적 sag** 텔레그래프 → **silence beat** → **Rigidbody2D Dynamic 물리 낙하** → 첫 ground 충돌 시 **hit-stop + 3-layer SFX** → **Settling** (자연 정착) → **Static 영구 숏컷 플랫폼**.

`PlatformMotor` 상속이라 슬라임은 모든 단계 (Telegraph + 물리 낙하 + Settling) 를 PlatformCarrier 로 자동 추종. 물리 단계에선 `Velocity` / `AngularVelocity` 프로퍼티가 `rb.linearVelocity` / `rb.angularVelocity` 를 forward 하므로 슬라임이 떨어지는 train 위에서 그대로 같이 떨어짐.

## 설계 철학

이전 버전은 `landTarget` 으로 디자이너가 끝점을 정하고 곡선으로 도달 — 부드럽게 도착해서 "도착한 다음 회전" 어색함이 있었음. 현재 버전은 **실제 Box2D 물리** 로 떨어지므로:

- **착지 위치는 디자이너가 *직접* 지정하지 않음**. 대신 train 의 시작 위치 + 물리 파라미터로 결정. 디자이너는 *시작 지점을 옮겨서* 원하는 착지 위치를 만듦.
- **충돌감이 진짜**: 무거운 mass + 중력 + 충돌 응답 + 약한 bounciness → 자연스러운 "쾅". Hit-stop + 3-layer SFX + impulse 가 충돌감을 강화.
- **각도도 물리가 결정**: release 시점 last-snapped connector 위치에 off-center 임펄스 → 자연 텀블. 결과 회전은 충돌 + 마찰로 정착.

## 상태 머신

| 상태 | bodyType | 동작 |
|------|----------|------|
| Idle | Kinematic | `riderSensor.RiderCount > 0` 인 순간 Telegraphing 진입. one-shot. |
| Telegraphing | Kinematic | 순차 connector snap + 점진적 sag. silence beat 후 Falling. (이전과 동일.) |
| Falling | **Dynamic** | bodyType 스왑 + mass/gravityScale/damping/material 적용 + last-snapped connector 위치에 `AddForceAtPosition(releaseImpulse, ForceMode2D.Impulse)` → 자연 텀블. `OnCollisionEnter2D` 가 첫 ground 충돌 감지. |
| HitStop | **Kinematic** (속도 stash) | timeScale dip 만으로는 freeze 가 약함 → bodyType=Kinematic + linearVelocity=0 으로 깔끔한 freeze. 80ms real-time 후 Dynamic 복귀 + stashed 속도 × retention 으로 재개. 진입 시 3-layer SFX + impulse + VFX + onImpact UnityEvent. |
| Settling | Dynamic | 매 FixedUpdate `linearVelocity.sqrMag < threshold²` && `|angularVelocity| < angThreshold` 체크. 조건 만족 시 `settledTimer += dt`; 누적 `settledHoldTime` 도달 시 또는 `rb.IsSleeping()` 시 Landed. `settleSafetyTimeout` 초과 시 강제 Landed (구덩이 fallback). |
| Landed | **Static** | `bodyType = Static`. 영구 고정 — 슬라임 충돌이나 다른 이벤트로 흔들리지 않음. `lastSettledPos/Rot` 기록, console log, `onLanded` UnityEvent. |

## 주요 필드

### 라이더 감지
- `riderSensor` (`PlatformRiderSensor`).

### Telegraph (이전과 동일)
- `connectors` (List<Connector>): 각 항목에 `anchor`, `snapVFXPrefab`, `snapSound`, `preDelay`, `sagDegrees`, `sagDuration`, `joltForce`.
- `sagCurve`, `silenceBeforeFall` (기본 0.2s — silence beat), `fallOnRiderExit`, `creakSource`, `snapImpulse`.

### Fall Physics (NEW)
- `fallMass` (기본 200): 무거워야 슬라임 충돌로 안 밀림.
- `fallGravityScale` (기본 2.0): 작은 카메라 프레임에서 1.0 은 floaty.
- `fallLinearDamping` (기본 0.05): 공기저항. 너무 높으면 floaty.
- `fallAngularDamping` (기본 0.6): **회전 멈추는 메인 노브**. 충돌 후 영원히 회전 방지.
- `useContinuousCollision` (기본 true): 무거운 body + 얇은 ground 의 tunneling 방지.
- `fallPhysicsMaterial` (PhysicsMaterial2D, 옵션): friction 0.7 / bounciness 0.05 권장. null 이면 Unity 기본값 (마찰 0 — 미끄러움).

### Release Impulse (자동 텀블 시드)
- `releaseImpulse` (Vector2, 기본 (0, -2)): **마지막으로 snap 된 connector 위치** 에 `AddForceAtPosition(impulse × mass, anchor, Impulse)` 적용. 무게중심 off-center 라 자유 각운동량 발생 → 자연스러운 텀블.
- `releaseAngularVelocity` (deg/s, 기본 0): 추가 각속도. 보통 0 (impulse 만으로 충분).

### Impact Detection
- `groundLayers` (LayerMask, 기본 ~0): 첫 ground 충돌 감지용 레이어 마스크. **슬라임 레이어 제외 필수** — 슬라임이 위에 앉아있으면 즉시 fire.

### Settlement
- `settledLinearThreshold` (units/s, 기본 0.1): 이 이하면 정적.
- `settledAngularThreshold` (deg/s, 기본 8).
- `settledHoldTime` (s, 기본 0.4): 임계값 이하로 이만큼 유지되면 Landed.
- `settleSafetyTimeout` (s, 기본 10): Falling+Settling 합산 이만큼 넘어가면 강제 Landed (구덩이 등).

### Hit-Stop
- `hitStopDuration` (s, real-time, 기본 0.08): timeScale dip 길이. 80–120ms 표준.
- `hitStopTimeScale` (기본 0.05).
- `postHitStopVelocityRetention` (0~1, 기본 0.4): hit-stop 끝나고 stashed velocity 의 이만큼 복원. 낮으면 train 이 박힌 채로 정착, 높으면 미끄러져서 정착.
- `postHitStopAngularRetention` (기본 0.6).

### Impact (3-layer SFX + VFX) — 이전과 동일
- `crackSound`, `groanSound`, `dustSettleSound`, `dustSettleDelay`.
- `impactImpulse` + `impactImpulseForce`.
- `impactVFXPrefab` + `impactVFXLifetime` (충돌 contact point 에 인스턴스화).

### Debris — 이전과 동일
- `debrisPrefab`, `debrisCount`, `debrisSpeedRange`, `debrisLifetime`, `debrisSpawnJitter`.

### Events
- `onFallStart`: Falling 진입 시.
- `onImpact`: 첫 ground 충돌 시.
- `onLanded`: Settling → Static 전환 시.

## 디자이너 이터레이션 워크플로

1. `SlimeJumping > Mechanism > Setup Falling Train Test` 메뉴로 placeholder 셋업 생성. PlaceholderGround 가 (0, -3.5) 에 같이 생김.
2. `Train_Carriage` 선택 → `Transform.position` 을 원하는 시작 지점으로 이동.
3. 인스펙터에서 `groundLayers` 를 슬라임 레이어 *제외* 한 마스크로 설정.
4. Play.
5. 슬라임이 train 위로 올라가서 sensor 트리거 (또는 FallingTrainPlatform 컴포넌트 우클릭 → **DEBUG: Trigger Fall Now (skip telegraph)** 로 즉시 낙하).
6. 물리 낙하 → 첫 충돌 시 hit-stop + SFX → settle (보통 0.5~3s).
7. 콘솔에 `[FallingTrainPlatform] Settled at (x.xx, y.yy) rot=N° after Ts` 출력. 씬뷰에서 train 선택 시 마지막 정착 지점에 초록 wire sphere 표시됨.
8. 만족스러운 위치면: 우클릭 → **DEBUG: Mark Last Settle Position In Scene** → 마커 게임오브젝트가 정착 위치에 생성됨 (트랙용).
9. 시작 위치를 다시 조정해서 반복.

## 결정성 (Determinism)

- 같은 머신 + 같은 Fixed Timestep + 같은 시작 포즈 → ±0.05 unit 이내 같은 정착 위치.
- 시도 사이엔 **씬을 reload** (Box2D 내부 상태 잔존 회피).
- 다른 머신/플랫폼 간 결정성은 보장 안 됨 (Box2D float 차이).

## 디자인 주의

- **One-shot**: `hasFiredOnce` true 후 재진입 ❌.
- **Hit-stop 은 글로벌**: `Time.timeScale` 을 건드리므로 씬 전체가 freeze. 80~100ms 가 적정.
- **bodyType 변경 시 contact 재평가**: Unity 매뉴얼대로 다음 FixedUpdate 에 contact 가 다시 평가됨 — 같은 프레임에 `IsTouching` 같은 거 읽으면 stale.
- **PlatformCarrier 호환성**: `Velocity` / `AngularVelocity` 프로퍼티가 Falling/Settling 동안 `rb.linearVelocity` / `rb.angularVelocity` 를 forward. 슬라임은 detach 안 됨 — 같이 떨어짐.
- **Static 전환 시점**: bodyType 을 Static 으로 바꾸기 *전에* `linearVelocity = 0` + `angularVelocity = 0` 으로 정리. Static body 는 velocity 무시하지만 contact 재평가 시 깔끔함.
- **groundLayers 누락**: 디폴트 `~0` (모든 레이어). **슬라임 레이어 반드시 제외** 안 하면 첫 FixedUpdate (slime 이 train 위에 앉아있는 상태) 에서 즉시 hit-stop fire. 인스펙터에서 명시 설정 필요.

## 셋업 메뉴가 만드는 것

`SlimeJumping > Mechanism > Setup Falling Train Test` 실행 시:

```
FallingTrainTest/
├── Train_Carriage          (Rigidbody2D Kinematic, BoxCollider2D 6×1.4, FallingTrainPlatform)
│   ├── Visual              (Cube primitive, 6×1.4×1.4 — collider 제거)
│   ├── RiderSensor         (BoxCollider2D trigger 6×2 + PlatformRiderSensor)
│   ├── Connector_Left      (anchor at -3, 0)
│   ├── Connector_Right     (anchor at +3, 0)
│   ├── SnapImpulse         (CinemachineImpulseSource + ImpulseEmitter)
│   ├── ImpactImpulse       (CinemachineImpulseSource + ImpulseEmitter)
│   └── (AudioSource on Train_Carriage 자체, loop 끼이익용)
├── PlaceholderGround       (BoxCollider2D 20×1, at (0, -3.5))
└── Debris_Template         (inactive Cube prefab template, Rigidbody2D dynamic)
```

기본 스케줄: Connector_Left @ 0.6s (sag 5°, 0.45s) → Connector_Right @ 1.05s (sag 7°, 0.30s) → 0.2s silence → 물리 낙하.

## 구현 위치

- [`FallingTrainPlatform.cs`](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/FallingTrainPlatform.cs)
- [`FallingTrainTestSetup.cs`](../../../SlimeJumping_v2/Assets/Scripts/Editor/FallingTrainTestSetup.cs)
