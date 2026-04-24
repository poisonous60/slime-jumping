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
