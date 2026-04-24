# 로프 (Rope — Verlet + 반동 Launch)

두 앵커 사이에 매달린 위치 기반 Verlet 로프. 라이더가 타면 자연스럽게 처지고, 차징 중에도 계속 처지며, Launch 시 누적된 sag displacement가 속도 보너스로 변환되어 고무줄 반동.

## 물리 모델
- **노드**: `segmentCount` 개 mass point. 양끝은 `anchorA`/`anchorB`에 pin. `pos[i]`, `prevPos[i]` Verlet pair.
- **FixedUpdate 파이프라인**: `simulationSubsteps` 만큼 `StepSimulationOnce(dt/N)` 반복. 한 substep 안에서:
  1. Verlet integration — `vel = (pos - prev) * damping`, + gravity step, + rider step.
  2. Tension-only elastic pull — `len > restLen`일 때만 `elasticFactor × excess`만큼 인접 노드끼리 당김.
  3. Jakobsen distance constraint — `constraintIterations`만큼 반복 projection. 각 iter 의 correction 은 `(1 - stretchSoftness)` 로 스케일되어 softness > 0 이면 rope 가 load 에서 더 늘어난 equilibrium 으로 수렴. 매 iter 마지막에 pin 재확정.
  4. Laplacian smoothing — `zigzagSmoothing > 0` 이면 각 free node 를 양 이웃 평균으로 lerp. Zigzag 모드(adjacent node alternation) 만 감쇠, 큰 스케일 sag/swing 은 유지.

## 콜라이더/비주얼 분리
- `colliderSubsample`로 EdgeCollider2D를 sim의 1/N 노드만 사용 (법선 지터 억제).
- `visualSubdivisions`로 LineRenderer만 Catmull-Rom 세분 (시뮬 비용 없음).

## 라이더 상호작용
- `ReportRiderContact(col, pos, mass, vel)`가 `SlimeController`에서 **Attached 또는 Charging** 상태마다 호출. Freeze 없음.
- 가까운 노드들에 `riderForceMul × mass × gravity × falloff`만큼 가속도 버퍼 주입. 영향 반경 = `segmentRestLen × riderInfluenceSpacing`.
- Charging 중에도 sag 유지 → 발사 직전의 처짐이 에너지 저장량.

### 라이더 속도 인계 (밀착)
로프는 `PlatformMotor`가 아니라 per-node Verlet sim 이므로 `PlatformCarrier.GetEffectiveVelocity()` 경로로는 0이 잡힌다. 그대로 두면 슬라임 착지 → 라이더 무게로 로프 콜라이더가 아래로 처짐 → 슬라임은 옛 y 좌표에 남아 **공중에 뜬다**. 해결:
- `SlimeController.FixedUpdate` 끝에서 `currentRope != null && Attached/Charging` 이면 `currentRope.SampleVelocityAt(rb.position)` 을 `rb.linearVelocity` 에 가산.
- `SampleVelocityAt` 는 contact 지점 근처 세그먼트의 `(pos - prevPos) / dt` 를 선형 보간.
- 결과: 로프가 처지면 슬라임도 같이 처지고, 출렁이면 같이 출렁인다. 피드백 루프는 Verlet damping + Jakobsen 로 평형점에 수렴.
- 스케일링 없음 (carrierFriction 미적용) — 로프는 테이프처럼 100% 밀착.

## Launch 반동 (새 에너지 경로)
`SlimeController.Launch()`에서 기본 `dir × power` 계산 후:
```
sag = rope.GetMaxSagDistance()   // anchor-A → anchor-B 직선에서의 최대 수직 변위
bonus = sag × rope.ReboundSpeedPerMeter
bonus = min(bonus, rope.MaxReboundSpeed)   // 캡
velocity += launchDir × bonus
```
- `reboundSpeedPerMeter` (기본 4): 로프별 튜닝 파라미터. 0이면 반동 없음.
- `maxReboundSpeed` (기본 18): 로프가 슬라임에게 전달할 수 있는 **최대 속도 캡**. 이 캡은 라이더 속도 인계(이하 "밀착")에도 적용되어, 크게 휘두르는 로프가 무한정 속도를 주지 못함. `SpringyBranchPlatform.maxTipSpeed` 와 같은 역할. 0 = 무제한.
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
| constraintIterations | 12 | Jakobsen 반복 횟수. 높을수록 수렴 빠름 |
| **stretchSoftness** | **0** | **Jakobsen projection 완화. 0=rigid, 0.2-0.4=rubbery. "얼마나 늘어날지" 의 메인 knob — iter 건드리지 말고 이걸 조절** |
| damping | 0.98 | Verlet 속도 보존율. **얼마나 오래 출렁일지** 제어 (크면 오래 바운스, 작으면 빨리 정착) |
| **simulationSubsteps** | **2** | **FixedUpdate 당 substep 수. 빠른 움직임에서 진동 억제** |
| **zigzagSmoothing** | **0** | **Laplacian 평활화. 0.1-0.2 로 고주파 떨림 제거** |
| nodeGravity | 0.3 | 노드 자체 중력 (라이더 무관) |
| elasticFactor | 0 | tension-only 스프링 계수 (기본 0 = Jakobsen만). `stretchSoftness` 와 중복 기능이므로 대부분 0 권장 |
| riderInfluenceSpacing | 4 | 라이더 영향 반경 (= spacing × 이 값) |
| riderForceMul | 30 | 라이더 무게 → 노드 가속도 배율 |
| reboundSpeedPerMeter | 4 | sag 1m 당 Launch 속도 보너스 |
| maxReboundSpeed | 18 | 로프가 슬라임에 전달하는 속도 하드캡 (밀착 + rebound 둘 다). `SpringyBranch.maxTipSpeed` 와 동일. 0 = 무제한 |
| colliderSubsample | 2 | sim → collider 노드 간격 |
| visualSubdivisions | 1 | LineRenderer Catmull-Rom 세분 |

## 튜닝 가이드

| 증상 | 원인 | 대응 |
|------|------|------|
| 슬라임이 타도 로프가 거의 안 처짐 | Jakobsen 이 rigid | `stretchSoftness` 0.1 → 0.4 로 올림 |
| 너무 오래 출렁거림 | damping 이 1 에 가까움 | `damping` 0.98 → 0.94 |
| 너무 빨리 멈춤, 고무줄 같은 반동 사라짐 | damping 이 너무 낮음 | `damping` 0.94 → 0.98 |
| 빠르게 흔들 때 떨림/지직거림 | 고주파 모드 excited | `simulationSubsteps` 2 → 4, 필요하면 `zigzagSmoothing` 0.1 |
| 최대 sag 도달 시간 느림 | 스프링이 무름 (stretchSoftness 크고 damping 높음) | `riderForceMul` 올려 초기 하중 강화, 또는 `damping` 낮춰 빨리 정착 |
| 최대 sag 도달 시간 빠름 (너무 급격) | 스프링이 딱딱, 하중 급 | `stretchSoftness` 올려 부드럽게, 또는 `riderForceMul` 낮춤 |
