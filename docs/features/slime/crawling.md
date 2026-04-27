# 크롤링 & 표면 미끄러짐

Attached 상태에서 A/D (바닥) 또는 W/S (벽) 로 표면 접선 방향 이동. 이동 속도는 느림 — 위치 미세 조정용. 주 이동은 차징 발사.

## 크롤링 (가속/감속 모델)
```
targetSpeed = input.x * crawlSpeed
overspeed = |crawlVelocity| > crawlSpeed
decel = overspeed ? crawlOverspeedDeceleration : crawlDeceleration

if (가속 또는 역방향)
    crawlVelocity = MoveTowards(crawlVelocity, targetSpeed, crawlAcceleration * dt)
else
    crawlVelocity = MoveTowards(crawlVelocity, targetSpeed, decel * dt)
```
- `crawlOverspeedDeceleration`: 착지 관성 등으로 crawlSpeed 초과 시 적용.
- 벽에서 위로 기면 중력에 의해 느려지고, 아래로 기면 빨라짐. 중력 > crawlSpeed이면 올라갈 수 없음.

## 표면 미끄러짐 (중력 분해)
중력을 접선/법선으로 분해. 접선 성분이 미끄러짐, 법선은 표면이 상쇄. SurfaceTag의 `useInertialSlide`로 두 모드 중 선택.

### 정상상태 모드 (기본 — SurfaceTag 없는 지면 / 일반 표면)
```
gravAlongTangent = dot(gravity, surfaceTangent)
if (abs(gravAlongTangent) < slideThreshold)  // 완만한 경사 보호
    gravAlongTangent = 0
slideSpeed = gravAlongTangent * slideGravityScale − slideResistance
```
- **속도 누적 없음** — 매 프레임 각도에서 직접 계산. A/D 놓으면 즉시 일정 속도 미끄러짐.
- 크롤링은 원래 중력 접선 전체와 싸움 (resistance/threshold 적용 전 값).
- `slideResistance`: 완만=많이 감속, 급경사=적게 감속.

### 관성 모드 (`useInertialSlide = true`, 빙판/하프파이프)
중력을 가속도로 적분 → KE↔높이 자연 변환.
```
slideAccel     = gravAlongTangent * slideGravityScale * slideGravityMultiplier  // [m/s²]
slopeGain      = (sign(slideAccel) == sign(slideVelocity)) ? slideDownhillGain
                                                            : slideUphillBrake   // 1=중립
slideAccel    *= slopeGain
slideVelocity += slideAccel * dt
slideVelocity *= max(0, 1 - slideLinearDrag * dt)                                // 속도 비례 드래그
frictionDecel  = slideResistance * slideResistanceMultiplier                     // [m/s²] 쿨롱 마찰
slideVelocity  = MoveTowards(slideVelocity, 0, frictionDecel * dt)
slideSpeed     = slideVelocity
```
- 데드존(slideThreshold) / 램프업 미적용.
- 착지 시 접선 속도는 `slideVelocity`로 들어가 마찰 외 감쇠 없음.
- `slideResistance` 의미가 바뀜: 빼는 속도 → 쿨롱 마찰 (m/s²). 속도 무관 일정 감속.
- `slideLinearDrag` (1/s): 속도 비례 감속. 빠를수록 더 깎임 → 하프파이프에서 올라가는 쪽이 내려간 쪽보다 짧아짐.
- `slideDownhillGain` / `slideUphillBrake`: 중력 접선 성분에 곱하는 게인. 내리막 가속과 오르막 감속을 비대칭으로 튜닝.
- 자세한 내용은 [surface-material.md](surface-material.md#관성-슬라이드-모드-useinertialslide).

## 최종 속도
`finalTangent = slideSpeed + crawlVelocity` → `velocity = surfaceTangent * finalTangent`. 관성 모드에서도 동일 — `slideSpeed`만 `slideVelocity`로 대체됨.

## 파라미터
| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| crawlSpeed | 5 | 최대 이동 속도 |
| crawlAcceleration | 50 | 입력 시 가속 |
| crawlDeceleration | 30 | 입력 해제 시 감속 (관성) |
| crawlOverspeedDeceleration | 15 | 최대 속도 초과 시 감속 |
| slideGravityScale | 1 | 중력 접선 → 미끄러짐 속도 배율 |
| slideThreshold | 1 | 이 이하 접선 중력은 무시 |
| slideResistance | 2 | slideSpeed에서 빼는 저항값 |

## 표면별 배율
`SurfaceMaterial`의 `crawlSpeedMultiplier`, `crawlAccelerationMultiplier`, `crawlDecelerationMultiplier`, `crawlOverspeedDecelerationMultiplier`, `slideResistanceMultiplier`, `slideGravityMultiplier`가 각각 곱해짐.

## 스틱 중 크롤
Stick 중에는 `stickCrawlMultiplier`(기본 0.2) 배율 적용. 0=완전 차단, 1=정상.

## 관련
- [surface-material.md](surface-material.md)
- [stick-timer.md](stick-timer.md)
