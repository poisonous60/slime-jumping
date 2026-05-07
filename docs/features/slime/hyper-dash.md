# 하이퍼 대시 (Dash → Charge → Launch 콤보)

그라운드 대시 후 즉시 차징하면 대시 운동량이 발사에 합산. Hop 경로도 지원.

## 캡처 시점
- Attached → Charging 진입 시 `chargeEntryCrawl = crawlVelocity + slideVelocity` 저장 (HandleJumpPressed).
- Hop 시에도 캡처 — 홉 후 에너지 전환 착지에서 crawl boost 사용.
- 차징 중 `crawlDeceleration`이 시각적 감속을 처리하지만, 캡처 값은 보존.

## 관성 소스 옵션 (`hyperDashInertiaSource`)
| 값 | 동작 |
|----|------|
| `ChargeStart` (기본, 레거시) | 위 캡처 값(`chargeEntryCrawl`)을 그대로 사용. 차징 중 감속이 발사 부스트에 반영되지 않음. |
| `LaunchMoment` | 발사 프레임의 라이브 `crawlVelocity + slideVelocity`를 사용. 차징 중 감속/슬라이드 변화가 그대로 발사 관성에 반영. |

`LaunchMoment`에서는 에너지 전환 코요테 경로의 `chargeEntryCrawl = 0` 가드가 무력화되므로, 코요테 전환 + 라이브 관성 조합 시 부스트가 강해질 수 있음.

## 발사 시 합산
```
preLaunchCrawl = (hyperDashInertiaSource == LaunchMoment)
                  ? (crawlVelocity + slideVelocity)   // 발사 프레임 값
                  : chargeEntryCrawl                  // 차징 시작 시 캡처
excess = Max(0, |preLaunchCrawl| - hyperDashDeduction)
inheritedVec = lastSurfaceTangent * sign(preLaunchCrawl) * excess * tangentInheritanceFactor
velocity = BlendLaunchInertia(launchVec, inheritedVec, surfaceMod)   // surface별 blend 모드
```
- `hyperDashDeduction`(5): 이 속도 이하 crawl은 무시. 0으로 설정하면 전체 반영.
- 일반 crawl → charge = boost 0 (crawlSpeed 전체가 deduction 처리).
- dash → charge = dash 초과분만 boost.

### Launch Inertia Blend (per-surface)
관성 벡터(`I`)와 조준 발사 벡터(`L`)를 합치는 방식은 `SurfaceMaterial.launchInertiaBlendMode`로 결정. 방향은 항상 `(L+I).normalized`, 크기만 모드에 따라 달라짐.
| 모드 | 크기 공식 | 특징 |
|------|-----------|------|
| `Sum` (기본, 레거시) | `\|L+I\|` | 순수 벡터합. ice + inertial slide에서 슬라이드 속도 누적이 가장 큼. |
| `LerpMaxToSum` | `Lerp(max(\|L\|,\|I\|), \|L+I\|, launchInertiaLerpFactor)` | 0=max, 1=Sum. ice에서 누적 억제용 0.3~0.5 권장. |
| `PNorm` | `(\|L\|^p + \|I\|^p)^(1/p)` | p=1=Sum, p=2=피타고라스(에너지 합산), p≫1≈max. 일반적으로 1.5~3. |

기본값은 `Sum` — 미설정 surface는 레거시 동작과 동일하다.

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
| hyperDashInertiaSource | ChargeStart | 관성 소스 — `ChargeStart`(차징 시작 캡처) / `LaunchMoment`(발사 프레임 라이브) |
| tangentInheritanceFactor | 0.7 | 선택된 crawl 값의 발사 합산 비율 |
| hyperDashDeduction | 5 | 이 이하 crawl은 발사 합산 제외 (0=전체 반영) |
| hyperDashConversionDampingMin | 1.2 | 이 전환 비율 이하 100% 유지 |
| hyperDashConversionDampingMax | 2.0 | 이 전환 비율 이상 0% |
| launchInertiaBlendMode (per-surface) | Sum | 발사 관성 합산 방식 (Sum / LerpMaxToSum / PNorm) |
| launchInertiaLerpFactor (per-surface) | 0.5 | LerpMaxToSum 모드: 0=max, 1=Sum |
| launchInertiaPNorm (per-surface) | 2.0 | PNorm 모드: 1=Sum, 2=에너지, ≫1≈max |

## 관련
- [ground-dash.md](ground-dash.md)
- [charge-launch.md](charge-launch.md)
- [energy-conversion.md](energy-conversion.md)
