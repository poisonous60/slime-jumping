# 하이퍼 대시 (Dash → Charge → Launch 콤보)

그라운드 대시 후 즉시 차징하면 대시 운동량이 발사에 합산. Hop 경로도 지원.

## 캡처 시점
- Attached → Charging 진입 시 `chargeEntryCrawl = crawlVelocity` 저장 (HandleJumpPressed).
- Hop 시에도 캡처 — 홉 후 에너지 전환 착지에서 crawl boost 사용.
- 차징 중 `crawlDeceleration`이 시각적 감속을 처리하지만, 캡처 값은 보존.

## 발사 시 합산
```
excess = Max(0, |chargeEntryCrawl| - hyperDashDeduction)
velocity += lastSurfaceTangent * sign(chargeEntryCrawl) * excess * tangentInheritanceFactor
```
- `hyperDashDeduction`(5): 이 속도 이하 crawl은 무시. 0으로 설정하면 전체 반영.
- 일반 crawl → charge = boost 0 (crawlSpeed 전체가 deduction 처리).
- dash → charge = dash 초과분만 boost.

## 에너지 전환 감쇠
에너지 전환 크기에 비례하여 crawl boost 감쇠:
```
conversionRatio = activeChargeRadius / chargeRadius
```
- `conversionRatio ≤ hyperDashConversionDampingMin`(1.2): 100% 유지.
- `conversionRatio ≥ hyperDashConversionDampingMax`(2.0): 0%.
- 사이 선형 보간.
- 약한 전환(홉 등)은 full boost, 강한 전환은 판정원 확장으로 이미 충분하므로 boost 제거.

## 코요테 탄젠트 캐싱
`lastSurfaceTangent`를 `lastSurfaceNormal`과 함께 캐싱. 코요테 중 표면 이탈 시에도 Launch가 올바른 접선 방향 사용 (기본값 (0,1) 대신).

## 파라미터
| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| tangentInheritanceFactor | 0.7 | 캡처 crawl의 발사 합산 비율 |
| hyperDashDeduction | 5 | 이 이하 crawl은 발사 합산 제외 (0=전체 반영) |
| hyperDashConversionDampingMin | 1.2 | 이 전환 비율 이하 100% 유지 |
| hyperDashConversionDampingMax | 2.0 | 이 전환 비율 이상 0% |

## 관련
- [ground-dash.md](ground-dash.md)
- [charge-launch.md](charge-launch.md)
- [energy-conversion.md](energy-conversion.md)
