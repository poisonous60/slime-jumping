# 로프 (Rope — Verlet + 반동 Launch)

두 앵커 사이에 매달린 위치 기반 Verlet 로프. 라이더가 타면 자연스럽게 처지고, 차징 중에도 계속 처지며, Launch 시 누적된 sag displacement가 속도 보너스로 변환되어 고무줄 반동.

## 물리 모델
- **노드**: `segmentCount` 개 mass point. 양끝은 `anchorA`/`anchorB`에 pin. `pos[i]`, `prevPos[i]` Verlet pair.
- **FixedUpdate 파이프라인** (`StepSimulationOnce`):
  1. Verlet integration — `vel = (pos - prev) * damping`, + gravity step, + rider step.
  2. Tension-only elastic pull — `len > restLen`일 때만 `elasticFactor × excess`만큼 인접 노드끼리 당김.
  3. Jakobsen distance constraint — `constraintIterations`만큼 반복 projection, 매 iter 마지막에 pin 재확정.

## 콜라이더/비주얼 분리
- `colliderSubsample`로 EdgeCollider2D를 sim의 1/N 노드만 사용 (법선 지터 억제).
- `visualSubdivisions`로 LineRenderer만 Catmull-Rom 세분 (시뮬 비용 없음).

## 라이더 상호작용
- `ReportRiderContact(col, pos, mass, vel)`가 `SlimeController`에서 **Attached 또는 Charging** 상태마다 호출. Freeze 없음.
- 가까운 노드들에 `riderForceMul × mass × gravity × falloff`만큼 가속도 버퍼 주입. 영향 반경 = `segmentRestLen × riderInfluenceSpacing`.
- Charging 중에도 sag 유지 → 발사 직전의 처짐이 에너지 저장량.

## Launch 반동 (새 에너지 경로)
`SlimeController.Launch()`에서 기본 `dir × power` 계산 후:
```
sag = rope.GetMaxSagDistance()   // anchor-A → anchor-B 직선에서의 최대 수직 변위
velocity += launchDir × (sag × rope.ReboundSpeedPerMeter)
```
- `reboundSpeedPerMeter` (기본 4): 로프별 튜닝 파라미터. 0이면 반동 없음.
- DragZone/hyper-dash 보정은 그 다음 단계에서 적용되어 반동이 drag 배율의 영향을 받음.

## 이벤트
- `OnRopeEvent(FirstAttach, MaxTension, LaunchRebound)` — 히스테리시스(`maxTensionEnter/Exit` 비율)로 MaxTension 발동, Launch 시 자동 LaunchRebound.

## Scene 튜닝 도우미
- `[ContextMenu] Preview Max Sag` — `previewRiderMass` 크기의 가상 라이더를 중앙에 두고 `previewSteps` 스텝 headless sim 실행, 최대 처짐 모양을 **magenta 기즈모**로 표시. 현재 pos/prevPos는 실행 후 복원되어 플레이모드 영향 없음. 콘솔 로그에 `max sag = X m` 출력.
- `OnValidate` 가드: `colliderSpacing`이 `slimeRadius × [1.5, 3]` 범위 밖이면 경고(지터/꺾임). `elasticFactor > 0`과 `constraintIterations > 5` 동시일 때 이중 동작 경고.

## 주요 파라미터
| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| segmentCount | 15 | 시뮬 노드 수 |
| restLengthMul | 1.05 | 앵커 거리 대비 총 rest length. >1=늘어진 로프, <1=프리텐션 |
| constraintIterations | 12 | Jakobsen 반복 횟수. 높을수록 rigid |
| damping | 0.98 | frame 당 속도 보존율 |
| nodeGravity | 0.3 | 노드 자체 중력 (라이더 무관) |
| elasticFactor | 0 | tension-only 스프링 계수 (기본 0 = Jakobsen만) |
| riderInfluenceSpacing | 4 | 라이더 영향 반경 (= spacing × 이 값) |
| riderForceMul | 30 | 라이더 무게 → 노드 가속도 배율 |
| reboundSpeedPerMeter | 4 | sag 1m 당 Launch 속도 보너스 |
| colliderSubsample | 2 | sim → collider 노드 간격 |
| visualSubdivisions | 1 | LineRenderer Catmull-Rom 세분 |
