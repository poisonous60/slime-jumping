# 곡선 슬라이드 stutter 튜닝 가이드

## 목적과 배경

슬라임이 **곡면 (halfpipe / 둥근 ice / U-bottom)** 을 sliding 으로 통과할 때 polygon vertex 마다 한 프레임씩 위치가 saw-tooth 형태로 hitching 되는 현상을 줄이기 위한 튜닝 가이드.

이전 history: [docs/faceted-terrain-jitter-attempts.md](faceted-terrain-jitter-attempts.md) 가 2026-04-28 에 동일 문제를 edge-enumeration + soft-band EffectiveNormal + teleport-then-snap 으로 "허용 범위" 까지 줄인 기록을 가지고 있음. 본 가이드는 그 잔여 saw-tooth 까지 더 부드럽게 만드는 후속 튜닝.

본 가이드의 5 개 토글 (A, B, C, D, E) 은 모두 [SlimeSettings.asset](../SlimeJumping_v2/Assets/Settings/SlimeSettings.asset) 인스펙터의 두 헤더에 노출됨:
- `Surface Probe` 헤더: C (`surfaceProbeDedupeDot`), D (`effectiveNormalSoftBandMultiplier`)
- `Surface Probe — Curve Slide Anti-Stutter` 헤더: A, B, E

## 진단 원리 요약

토글의 의미를 이해하려면 한 FixedUpdate 의 흐름을 알아야 함:

```
1) pos += velocity × dt           ← 깨끗한 tangent 진행
2) Phase A 디페너트레이션 (push)   ← multi-contact 시 tangent 성분 새어들어옴 → [A] 가 자름
3) Phase B re-anchor              ← ClosestPoint 가 vertex 에 잡히면 backward kick → [B] skip, [E] raycast 로 대체
```

추가로:
- [C] `surfaceProbeDedupeDot` 를 낮추면 인접 facet contact 가 머지되어 `aC` (active contact count) churn 빈도 자체가 감소 — 2) 와 3) 의 발동 빈도를 줄임
- [D] `effectiveNormalSoftBandMultiplier` 를 낮추면 active band 자체가 좁아져 churn 감소

A/B/E 는 churn 이 일어나도 **위치에 영향이 가지 않게** 만들고, C/D 는 churn **빈도** 자체를 줄임.

## 사전 준비

1. **CurveSlideJitterProbe 부착 확인**
   - Slime 프리팹 또는 씬의 Slime 인스턴스에 [CurveSlideJitterProbe](../SlimeJumping_v2/Assets/Scripts/Debug/CurveSlideJitterProbe.cs) 컴포넌트가 붙어있는지 확인. 없으면 추가.
   - `Auto Dump On Jitter`: ON (기본). dPos reversal 발생 시 자동 dump.
   - `Dump Key`: F2 (기본). 수동 dump 단축키.

2. **테스트 위치**
   - MainScene 의 곡선 ice 구간 (TerrainShape_TerrainMake), 또는 stage2 의 halfpipe 형 곡면
   - 슬라임이 곡면에 약 10–20 m/s 의 tangent 속도로 진입하도록 발사 위치 설정 (충분히 빠르면 saw-tooth 가 시각적으로 두드러짐)

3. **OnGUI 확인**
   - Probe 가 화면 좌측 상단(기본 좌표 10, 160)에 1초 윈도우 통계 표시:
     - `dPos reversals (last 1s)` — 위치 역방향 변동 횟수
     - `primary flips (last 1s)` — primary collider 변경 횟수
     - `|tf - rb| max (1s)` — visual / physics gap (= 0 이면 visual 부분 영향 없음)
     - `surfN Δ max (1s)` — 한 프레임 normal 회전 최대각

## Baseline 측정 (모든 토글 OFF)

기본 SlimeSettings.asset 값으로 한 차례 곡면 슬라이드. F2 dump.

기록 항목:

| 지표 | 값 | 비고 |
|---|---|---|
| dPos reversals/sec | | |
| primary flips/sec | | |
| surfN Δ max (deg/frame) | | |
| **`aC=2 \|dPos\|` 평균** | | dump 의 aC=2 행만 평균 |
| **`aC=1 \|dPos\|` 평균** | | dump 의 aC=1 행만 평균 |
| **비율** = (aC=2)/(aC=1) | | 본 작업의 핵심 비교 지표 |
| 시각 평가 (눈으로 stutter 보임?) | yes/no | |

`(aC=2)/(aC=1)` 비율이 1.0 에 가까울수록 부드러움. 사용자 dump 의 baseline 은 약 0.27–0.35 (= 70% 이상 dPos 손실).

## 단독 토글 테스트

각 테스트마다 **이전 변경을 모두 되돌리고 해당 토글 하나만** 켠 뒤 측정. 동일 위치에서 동일 launch 조건으로 비교.

### A. `phaseAStripTangentComponent`

- ON, `phaseAStripStrength` = 1.0
- 측정: 위 표 다시 작성
- **확인사항**:
  - aC=2 dPos / aC=1 dPos 비율 개선?
  - V-notch wedge (sharp inside corner) 에 일부러 박아 본 뒤, useFrictionBasedDetachment 가 정상 detach 시키는가?
  - 평면 ice 위 정지 시 슬라임 위치 안정한가?

선택 튜닝: `phaseAStripStrength` = 0.5 → 절반만 제거. 절충안.

### B. `phaseBSkipAtVertexClamp`

- A 끄고 B 만 ON, `phaseBVertexClampDotThreshold` = 0.9
- 측정: 위 표
- **확인사항**:
  - Re-anchor skip 빈도가 너무 잦으면 슬라임이 곡면에서 살짝 떠오르는가? (눈으로)
  - 코너 통과 시 slime 이 코너 위로 잠깐 부유?

선택 튜닝: threshold 를 0.95 (보수적, skip 적음) ↔ 0.8 (관대, skip 많음) 비교.

### C. `surfaceProbeDedupeDot` (기존 필드)

- A, B 모두 끄고 C 단독 변경: 0.97 → 0.93 → 0.88 단계
- 측정: 위 표 + `aC` 분포 (얼마나 자주 1, 2, 3 인가)
- **확인사항**:
  - `aC ≥ 2` 빈도가 낮아지는가?
  - 90° corner 에서 dedupe 가 너무 강하면 corner 의 두 face 가 한 contact 로 merge 되어 corner 회피가 부정확해질 수 있음 — corner 통과 시 슬라임이 corner 안쪽으로 박히지 않는지 확인
  - history 문서에서 0.93 권장. 0.88 은 그보다 더 적극적.

### D. `effectiveNormalSoftBandMultiplier` (기존 필드)

- 다른 토글 모두 OFF, D 만 변경: 5 (asset 기본) → 3 → 2
- 측정: 위 표
- **확인사항**:
  - 빠른 곡선 진입 (= 빠른 tangent 속도) 에서 active set 이 너무 좁아 normal 재구성이 lag 하지 않는가?
  - 이 효과는 SurfaceProbe.cs:367-400 의 EffectiveNormal 가중합에 영향. ProjectVelocityAgainstContacts 도 같은 band 사용.
  - 너무 낮추면 (1) 챔 fast 슬라이드에서 normal 재구성 늦어짐, (2) 코너 hand-off 시 한 frame 정도 normal 이 stale 할 수 있음.

### E. `phaseBUseRaycastAnchor`

- A, B 모두 OFF, E 만 ON. `phaseBRaycastDistanceMultiplier` = 2.0
- 측정: 위 표
- **확인사항**:
  - aC=2 dPos / aC=1 dPos 비율 — **이론상 거의 1.0 에 근접해야 함**
  - Raycast 가 surfaceCollider 를 못 맞히는 케이스 (Console 경고 / fallback 빈도): smoothed surfaceNormal 이 실제 face 와 너무 어긋나면 raycast 빗나감 → legacy ClosestPoint Phase B 로 fallback. 이게 자주 발생하면 매끈함 효과가 떨어짐.
  - 시각 평가 — 가장 부드러워야 함 (이론상)

선택 튜닝: ray multiplier 1.5 (보수적) ↔ 3.0 (멀리까지 hit 검색). 보통 2.0 으로 충분.

## 조합 테스트

단독 결과 본 뒤 조합:

| 조합 | 의도 | 비고 |
|---|---|---|
| A + B | Phase A/B 양쪽 fix, 가장 안전 | E 가 fallback 안 갈 때 |
| A + E | Phase A 정리 + Phase B raycast 대체 | E 가 fallback 갈 때 A 가 받쳐줌 |
| C(0.92) + D(3) | churn 빈도 자체 줄이기 | A/B/E 손 안 대고 |
| A + C(0.92) | tangent 정리 + churn 빈도 줄이기 | 추천 보수적 조합 |
| **A + E** | **추천 1순위** | 이론상 최강. fallback 안전망 포함 |
| **A + C(0.92) + D(3)** | **추천 2순위** | 토글 모두 끄고 기존 두 필드만 — 가장 보수적 |

## 회귀 (regression) 체크

각 조합에서 다음을 모두 확인:

1. **90° sharp corner 통과**
   - 위치: stage2 의 sharp inner corner 또는 임시 placement
   - useFrictionBasedDetachment 가 ON 인 상태에서 일부 corner 는 detach (centrifugal), 일부는 wrap-over 되어야 함
   - heatmap (SlimeSettings 인스펙터 하단) 으로 frictionDetachFormula 의 detach 영역 시각화 가능

2. **V-notch wedge 탈출**
   - 두 평면이 V 형으로 만나는 위치에 슬라임이 박혀들어가게 발사
   - useFrictionBasedDetachment 의 horizon detach 가 정상 작동해 빠져나오는지 확인
   - A 토글이 ON 일 때 wedge 탈출이 약해지면 A 의 strength 를 낮추는 방향

3. **SpringyBranch / 회전 thin collider 통과 안 됨**
   - SpringyBranch 가 있는 씬 (있다면) 에서 슬라임이 통과하지 않는지
   - 본 토글들은 Multi probe 모드의 Phase A/B 만 건드림 — Single 모드 (SpringyBranch 가 사용) 는 영향 받지 않아야 함

4. **평면 ice 위 정지**
   - 평평한 ice 위에 슬라임이 정지해 있을 때 contact 1 개, 0° 표면 normal 만 잡히는지 (history 문서의 acceptance criterion)
   - aC churn 발생 안 함 → A/B/E 토글 효과 없음 (정상)

5. **느린 곡선 슬라이드**
   - 빠른 슬라이드는 stutter 가 두드러지지만 느린 슬라이드도 정상 동작해야 함
   - tangent 속도가 cornerDetachMinSpeed (= 1.5 m/s) 이하일 때 friction-detach 가 transient frame 으로 처리되는지

## Pass criterion

다음 모두 만족:

- 곡선 슬라이드: `(aC=2)/(aC=1) |dPos|` 비율이 baseline 대비 개선 — 목표 0.7 이상
- 시각적으로 stutter / hitching 보이지 않음 (사용자 평가)
- 모든 회귀 체크 통과
- Console 에 NaN / null reference / unexpected raycast warning 없음

## 결과 기록 양식

테스트마다 같은 launch 조건으로 측정 후 표 작성:

| 토글 조합 | aC=1 평균 dPos | aC=2 평균 dPos | 비율 | reversals/s | flips/s | surfN Δ max | 시각 평가 | 회귀 결과 | 비고 |
|---|---|---|---|---|---|---|---|---|---|
| Baseline (전부 OFF) | | | | | | | | | |
| A only | | | | | | | | | |
| B only | | | | | | | | | |
| C(0.93) only | | | | | | | | | |
| D(3) only | | | | | | | | | |
| E only | | | | | | | | | |
| A + B | | | | | | | | | |
| A + E | | | | | | | | | |
| A + C(0.92) + D(3) | | | | | | | | | |

가장 좋은 조합을 결정한 뒤, 그것을 SlimeSettings.asset 의 default 로 승격할지 별도 결정.

## 참고

- 토글 구현: [SlimeSettings.cs](../SlimeJumping_v2/Assets/Scripts/Settings/SlimeSettings.cs), [SurfaceProbe.cs](../SlimeJumping_v2/Assets/Scripts/Physics/SurfaceProbe.cs), [MultiContactSurfaceProbe.cs](../SlimeJumping_v2/Assets/Scripts/Physics/MultiContactSurfaceProbe.cs)
- Probe: [CurveSlideJitterProbe.cs](../SlimeJumping_v2/Assets/Scripts/Debug/CurveSlideJitterProbe.cs)
- 이전 history: [faceted-terrain-jitter-attempts.md](faceted-terrain-jitter-attempts.md)
- SingleContactSurfaceProbe 는 이번 토글 영향권 밖 (회전 thin collider / SpringyBranch 안전성 우선)
