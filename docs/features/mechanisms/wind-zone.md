# WindZone2D (바람 구간)

Trigger volume에 들어오면 슬라임에 지속적 가속을 적용하는 환경 기믹. Pogostuck / Egging On 스타일.

## 콜라이더 지원
`BoxCollider2D` (직사각) 또는 `PolygonCollider2D` (임의 모양). 파티클 경계는 바람 방향 기준 min/max 투영으로 자동 계산.

## 핵심 필드
| 필드 | 타입 | 설명 |
|------|------|------|
| `windAcceleration` | Vector2 | 바람 방향 + 세기 (units/s²). 월드 기준 |
| `terminalSpeed` | float | 바람 방향 최대 속도. 도달 시 가속 감쇠. 0=무제한 |
| `airborneMultiplier` | float | Airborne 상태 배율 (기본 1.0) |
| `attachedMultiplier` | float | Attached 상태 배율 (기본 0.7) |
| `chargingMultiplier` | float | Charging 상태 배율 (기본 0.2) |

## 동작
```
windDir = normalize(windAcceleration)
falloff = clamp01(1 - dot(slime.Velocity, windDir) / terminalSpeed)
deltaV  = windDir * |windAcceleration| * stateMultiplier * falloff * dt
slime.AddExternalVelocity(deltaV)
```

## 상태별 처리
- **Airborne**: deltaV를 velocity에 직접 가산.
- **Attached/Charging**: deltaV를 `surfaceTangent`에 투영 후 가산 (법선 성분 제거로 표면 스냅 지터 방지).

외부 힘 파이프라인 상세는 [../infra/external-velocity-two-channel.md](../infra/external-velocity-two-channel.md).

## 비주얼
자식 ParticleSystem. Stretched Billboard + Velocity over Lifetime으로 바람 방향 시각화. Noise 모듈로 물결 효과 가능. `SyncParticles`가 바람 방향 투영으로 파티클 emit strip / lifetime / rate 자동 계산.

## 관련 파일
- [WindZone2D.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/WindZone2D.cs)

## v2 — Snow ParticleSystem 연동 (2026-05-08)

v1 기능 전부 보존 + 자식 `ParticleSystemForceField` 자동 싱크로 글로벌 Snow 파티클이 zone 안에서 바람 방향으로 휨. v1 은 그대로 두고 별도 컴포넌트 [WindZone2D_v2.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/WindZone2D_v2.cs).

### 추가 필드
| 필드 | 타입 | 설명 |
|------|------|------|
| `snowForceField` | ParticleSystemForceField | 메인 자식 ForceField 참조. OnValidate 에서 BoxCollider2D 기준 size/위치/direction 자동 싱크 |
| `snowDrag` | float | ForceField drag. 바람 영역 안에서 입자 terminal velocity ≈ `|windAcceleration| / snowDrag`. 0 = 무한 가속 |
| `sideForceFieldLeft` / `sideForceFieldRight` | ParticleSystemForceField | optional 좌/우 보조 필드. main collider 양 옆에 자동 배치되어 wind 경계 transition 부드럽게 만듦 (안: 대각선 / 바로 옆: 수직낙하 인 hard edge 완화). 강도/너비는 main 비율로. 둘 중 하나만 두거나 비활성도 가능 (null 이면 sync skip) |
| `sideFieldWidthRatio` | float (0.1~2) | side 필드 너비 = main collider 너비 × ratio. 기본 0.5 |
| `sideFieldStrength` | float (0~1) | side 필드 direction 세기 = main windAcceleration × multiplier. 기본 0.3 (메인 30%) |

### 동작
1. **Box-only**: ForceField shape = Box, transform.localScale = `BoxCollider2D.size`. PolygonCollider2D 모양은 v2 비주얼 연동에서 지원 안 함 (물리 push 는 v1 처럼 polygon 도 동작).
2. **Direction**: `directionX/Y = windAcceleration` (월드 기준 가속도, units/s²). 슬라임 물리와 동일 source-of-truth.
3. **Drag → terminal velocity**: 바람 영역 안에서 눈 속도가 `|windAcceleration| / snowDrag` 로 빠르게 수렴 → 균일한 흐름 비주얼.

### Snow 측 셋업
[Snow.prefab](../../../SlimeJumping_v2/Assets/Prebs/VFX/Snow.prefab) 의 ParticleSystem 에 박힌 설정:
- `ExternalForcesModule.enabled = 1`, `influenceFilter = LayerMask`, `influenceMask = WindForceField` (layer 13) — 같은 mask 의 ForceField 만 영향. v2 가 자식 ForceField GameObject 를 자동으로 `WindForceField` 레이어로 보냄 → 별도 wiring 불필요.
- `LimitVelocityOverLifetime.enabled = 1`, `separateAxes = 1`, `limitX = 0`, `limitY = limitZ = 1000`, `dampen = 0.05` — zone 밖에서 X 속도 점진적 감쇠 ("서서히 가속이 줄어드는" 비주얼). Y/Z 는 limit 가 매우 커서 영향 없음. dampen 낮추면 더 천천히 감쇠 (0.01 ≈ ~30%/s).

### ForceField 박스 차원 (주의)
`ParticleSystemForceField` 의 Box shape 은 **half-extents = `endRange × transform.lossyScale`** (실험 검증). v2 SyncForceField 가 다음과 같이 자동 셋업:
- `endRange = 0.5` (고정)
- `localScale = (BoxCollider2D.size.x, BoxCollider2D.size.y, 100f)` — XY 는 collider 와 일치, Z 는 100 으로 사실상 무한
- 결과: XY half-extents = 0.5 × box.size = box.size/2 → BoxCollider2D 와 정확히 일치, Z half-extents = 50 → noise 모듈로 Z 드리프트 한 파티클도 모두 안에 머무름

Z 를 1 로 두면 Snow PS 의 noise (3D, strength 0.5) 로 일부 파티클이 Z=±0.5 밖으로 새어나가 force 적용이 일관되지 않음 — 그래서 Z=100 강제.

### Drag 모델
ForceField 의 `drag` 필드는 linear drag (multiplyDragByParticleVelocity = false) 로 강제 — terminal velocity 가 `|windAcceleration| / snowDrag` 로 직관적. 기본 multiplyDragByParticleVelocity=true 면 quadratic drag 가 되어 같은 drag 값에 terminal velocity 가 √(accel/drag) 로 훨씬 낮아짐. v2 의 SyncForceField 가 매 OnValidate 마다 false 로 강제 보정.

LimitVelocityOverLifetime.dampen 도 함께 작동하므로 inside 의 effective terminal = `accel / (FFdrag + dampen_rate)`. 두 노브를 분리해서 inside terminal 과 outside 감쇠를 독립 튜닝 가능.

### 레이어
[TagManager.asset](../../../SlimeJumping_v2/ProjectSettings/TagManager.asset) layer 13 = `WindForceField` (전용 레이어). `GravityField` 와 동일 패턴. 다른 미래 ForceField 가 의도치 않게 Snow 에 영향 주는 것 차단.

### 테스트 셋업
`SlimeJumping/Mechanism/Setup Wind Zone V2 Test` 메뉴 ([WindZoneV2TestSetup.cs](../../../SlimeJumping_v2/Assets/Scripts/Editor/WindZoneV2TestSetup.cs)) — v1 셋업 (Floor / Walls / Launcher / Ceiling / Landing) + WindZone2D_v2 + 자식 SnowForceField + Snow.prefab 인스턴스. 슬라임 발사로 물리 push 확인 + zone 영역 안 눈이 바람 방향으로 흐르는 비주얼 확인 동시 가능.
