# 표면 속성 시스템 (SurfaceMaterial)

표면마다 물리 배율을 커스터마이징. `SurfaceMaterial` (ScriptableObject) + `SurfaceTag` (MonoBehaviour) + `SurfaceModifiers` (struct) 패턴.

## 구조
- `SurfaceMaterial`: 에셋. "빙판", "진흙" 등을 만들어 공유.
- `SurfaceTag`: 기본값과 다른 표면 오브젝트에만 부착하는 마커.
- `SurfaceModifiers`: SlimeController가 읽는 구조체. SlimeController는 `SurfaceMaterial`을 직접 참조하지 않음 (간접 읽기).
- SurfaceTag 없는 표면 = 모든 배율 1.0 (기본 동작).

## AngleScaledValue
모든 파라미터는 `AngleScaledValue` 타입. 표면 각도에 따라 값을 보간.
```
struct AngleScaledValue {
    baseValue   // angleMin 이하에서의 값
    fullValue   // angleMax 이상에서의 값
    angleMin    // 보간 시작 각도 (0° = 바닥)
    angleMax    // 보간 끝 각도 (90° = 벽)
}
// Evaluate(surfaceAngle) = Lerp(baseValue, fullValue, InverseLerp(angleMin, angleMax, surfaceAngle))
// Constant(v) = 모든 각도 동일 값 (하위 호환)
```

## 조회 타이밍
- **착지 시** (carrier.Attach 시점): `GetComponentInParent<SurfaceTag>()` 한 번 조회 → 캐싱.
- **Airborne 전환 시**: `SurfaceModifiers.Default`로 리셋.
- **표면 전환**: `FixedUpdate`에서 `surfaceCollider != prevSurfaceCollider`일 때 재호출.

## 파라미터
| 파라미터 | 기본 | 설명 |
|---------|------|------|
| carrierFriction | 1 | 플랫폼 속도 전달 (0=얼음, 1=완전) |
| crawlSpeedMultiplier | 1 | 크롤 속도 |
| crawlAccelerationMultiplier | 1 | 크롤 가속 |
| crawlDecelerationMultiplier | 1 | 크롤 감속 (낮=관성 큼) |
| crawlOverspeedDecelerationMultiplier | 1 | 초과속 감속 |
| slideResistanceMultiplier | 1 | 슬라이드 저항 (0=없음, 3=진흙) |
| slideGravityMultiplier | 1 | 슬라이드 중력 |
| slideRampUpTime | 0 | 슬라이드 램프업 (0=즉시, SlimeSettings 기본 fallback) |
| landingVelocityRetention | 0 | 착지 접선 보존 (0=정지, 0.8=빙판) |
| stickDurationMultiplier | 1 | 스틱 배율 (0=빙판) |
| impactRedirectRatio | 0 | 법선 충돌 → 접선 전환 (벽에서만 자동 활성) |
| wallAttachSpeedDampen | 1 | 착지 접선 속도 감쇠 |
| groundDashScale | 1 | 대시 impulse 배율 (0=대시 불가) |
| hopVelocityRetention | 1 | 홉 시 접선 보존 (1=ice, 0=mud) |
| hopInstantFlip | true | 홉 반대 방향 즉시 반전 (true=mud/grass, false=ice 관성) |

## 표면별 상호작용
- **바닥** (surfaceUp > slopeThreshold): 슬라이드 없음, 스틱 없음. 안정적.
- **벽** (surfaceUp ≈ 0): 중력으로 아래 슬라이드. 크롤로 위아래 이동 (중력보다 느린 속도). 스틱 타이머 적용.
- **급경사** (slopeThreshold ~ ceilingThreshold): 기울기 비례 슬라이드. 차징 중에도 미끄러짐. 스틱 적용.
- **천장** (surfaceUp < ceilingThreshold): 부착 불가. 법선 속도만 상쇄, Airborne 유지.

## 예시 조합
| 표면 | carrierFriction | slideResistance | crawlSpeed | retention | stickDuration | impactRedirect | 느낌 |
|------|----|----|----|----|----|----|------|
| 기본 | 1.0 | 1.0 | 1.0 | 0 | 1.0 | 0 | 기본 |
| 빙판 | 0.2 | 0.1 | 1.0 | 0.8 | 0 | 0.5 | 미끄러움, 벽 타기 |
| 진흙 | 1.0 | 3.0 | 0.5 | 0 | 1.0 | 0 | 느리고 안 미끄러움 |

## 값 검증
- `SurfaceMaterial.OnValidate()`: 음수 방지, friction/retention은 0~1로 클램핑.
- `SlimeSettings`: `chargeRadius`, `baseLaunchPower`에 `[Min(0.01f)]`, `initialConversionRate`에 `[Min(0f)]`.

## 관련 파일
- [SurfaceMaterial.cs](../../../SlimeJumping_v2/Assets/Scripts/Physics/SurfaceMaterial.cs)
- [SurfaceTag.cs](../../../SlimeJumping_v2/Assets/Scripts/Physics/SurfaceTag.cs)
- [SurfaceModifiers.cs](../../../SlimeJumping_v2/Assets/Scripts/Physics/SurfaceModifiers.cs)
