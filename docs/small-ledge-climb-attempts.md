# 작은 턱 부드럽게 넘어가기 — 시도 기록 (handoff)

**상태:** 2026-05-11 — **재설계 적용, 사용자 테스트 대기.** 아래 "2026-05-11 재설계" 참조. 이전 4단계 버전(2026-05-10)에서 두 가지 문제가 보고됨 → 그걸 고친 버전. 이전 버전의 분석/가설은 "## 이전 버전(2026-05-10) 기록 — 아카이브" 아래에 보존 (재설계로 대부분 해소됨).

같은 세션에서 별개 feature 인 **buried-collider rejection** (`surfaceProbeRejectBuriedContacts`) 도 구현됨 — 그건 작동 확인됨 (`buried-rej` 카운터). 아래 "관련 파일" 의 변경 중 일부는 그쪽 것.

---

## 2026-05-11 재설계

### 고친 문제 (사용자 보고)
1. `ledge-pass` 가 작은 턱뿐 아니라 여러 곡면에서 광범위하게 발생.
2. `ledge-pass` 가 떴을 때 예상한 움직임 대신 슬라임이 제자리에서 멈춤.

### 원인
- **#1:** 판정이 사실상 `contactClamped[i]` 하나뿐 → 볼록 폴리곤의 모든 facet 꼭짓점이 다 잡힘. 높이 조건 `radius − dist·dot(n,up) ≤ stepHeight` 은 꼭짓점에 clamp 되면 `dist≈radius, dot≈1` 이라 거의 항상 `≈0 ≤ stepHeight` → 무의미. 게다가 `lowLedgePassthroughCount++` 가 `vn = dot(v,n)` 체크보다 위에 있어서 그 contact 가 속도를 막지도 않는데 카운트됨.
- **#2:** clip 을 skip 해서 `velocity` 는 forward 인데, 그 다음 teleport 의 CCD sweep (`CircleCastNonAlloc`, `SlimeController.OutputVelocity`)이 `surfaceCollider` 만 제외 → 작은 턱은 surfaceCollider 가 아니라(primary 는 슬로프) CCD 가 슬라임을 턱 면에 flush 하게 박음. `QueriesStartInColliders:1` 라 닿아 있으면 distance≈0 → `rb.position += dir·max(0, 0−contactSkin) ≈ +=0` → 전진 0 → position-based wedge drain 이 `slideVelocity=0` → 완전 정지가 안정 상태.

### 새 판정 — `MultiContactSurfaceProbe.IsSmallLedgeVertex` (edge-enumeration 시점, per-contact `contactLowLedge[]` 플래그)
한 contact 가 작은 턱 = ALL of:
1. closest point 가 폴리곤 vertex 에 clamp (`uRaw<0 || uRaw>1`) — 큰 벽 제외(슬라임 중심이 벽 정면 면 interior 높이 → clamp 안 됨).
2. 그 vertex 가 슬라임 발에서 `0 ~ smallLedgeStepHeight` — 높은 턱 / 아래 edge 제외.
3. **vertex 바로 위(`vertex + gravityUp·max(2·contactSkin, 0.5·stepHeight)`)가 그 콜라이더 밖** — `OverlapPoint` 단일 호출(raycast 아님 → `QueriesStartInColliders` 무관). 턱 위가 빈 공간이어야 함. **이게 핵심 추가** — tessellate 된 큰 벽(짧은 edge 로 쪼개진)은 짧은 edge 의 top vertex 가 stepHeight 안에 들어와도 위에 벽이 계속됨 → `OverlapPoint=inside` → 제외. 솔리드 폴리곤 벽도 동일. 콜라이더 topology 에 무관.

`vn<0`(실제로 막는 중) 인지는 속도가 있는 `ProjectVelocityAgainstContacts` 에서 본다 (조건 d). probe 로 안 끌고 감.

### 새 허용 (3군데)
1. **`ProjectVelocityAgainstContacts`**: 루프 순서를 `same-surface skip → vn≥0 skip → band skip → weight skip → (작은 턱이면) clip skip + count → clip` 으로 재배치. `lowLedgePassthroughCount` 는 **실제로 skip 한 clip 수**만 셈 (#1 해결).
2. **`SlimeController.OutputVelocity` teleport CCD**: CircleCast hit 루프에 `if (surfaceProbe.IsLowLedgeCollider(h.collider)) continue;` 추가 (`surfaceCollider` 제외와 나란히) — 슬라이드가 슬라임을 lip 안으로 밀어넣게 둠 → Phase A 가 들어올림 (#2 해결, 핵심 조각).
3. **`TryAddPolygonEdgePush` 의 tangent-strip lift** 를 같은 `IsSmallLedgeVertex` 로 게이트 (detection 로직 한 군데로 통일). 동작은 이전과 동일 (bisector push 의 tangent 성분 제거 → 순수 "표면 따라 위로").

### 유지(이미 있던 것, 이제 발동 기회 생김)
- `SnapToSurface` Phase B re-anchor tolerance loosen (`lowLedgePassthroughCount>0` 시 `+= stepHeight`).
- position-based wedge drain (`SlimeController.OutputVelocity`) — 진짜 못 넘는 obstacle 에서만 발동하는 backstop (정상 climb 시 net≈의도 라 안 터짐).

### 다음 세션 — 테스트 결과에 따라
- **OK 면:** default true 로 올릴지 사용자와 논의 + 회귀 케이스(`docs/faceted-terrain-jitter-attempts.md`) 재확인.
- **여전히 안 넘으면:** Phase A lift 양이 부족한지(`along = dot(bisectorPush, lastGoodNormal)` 가 살짝 침투 시 거의 0), 또는 `lastGoodNormal` 이 슬로프가 아니라 턱 normal 인지(hyp 3, 아래 아카이브) 디버그. Phase A iter 수 늘리기 / lift depth 를 vertex 까지의 수직 거리 기반으로 더 적극적으로.
- **분석형 콜라이더(Box 등) step:** 아직 미지원 (polygon/composite 만) — 필요하면 analytical 경로에서 "closest point 가 box corner 인가" 판정 추가.
- **`OverlapPoint` 위쪽 eps 값 (`max(2·contactSkin, 0.5·stepHeight)` ≈ 0.075):** 너무 크면 진짜 lip 인데 위에 뭔가 있어서 false-negative, 너무 작으면 tessellated 벽 false-positive. 테스트하며 튜닝.

---

## 이전 버전(2026-05-10) 기록 — 아카이브

> 아래는 재설계 전 4단계 버전의 분석. 재설계로 #1(광범위 ledge-pass)과 #2(제자리 멈춤 — teleport CCD 가 막던 것)는 해소됨. Phase A lift 부족(hyp 2) / `lastGoodNormal` 이 턱 normal(hyp 3) 가설은 아직 유효 — 재설계 버전도 안 넘으면 거기부터.

---

## 문제

슬라임(2D circle collider, `slimeRadius` = 0.4)이 슬로프를 슬라이드하다가 작은 턱/bump 의 가파른 **정면 면 edge** 에 부딪히면 멈춰버림:

1. `MultiContactSurfaceProbe` 가 그 정면 면 edge 에서 contact 를 emit (normal 이 슬라임 쪽으로 향함 = 진행방향 막음).
2. `SurfaceProbe.ProjectVelocityAgainstContacts` 가 그 normal 에 속도를 클립 → forward 슬라이드 ≈ 0.
3. `SlimeController.Attached.cs` 의 wedge drain (`velocity.sqrMagnitude < 0.01f` → `slideVelocity = 0`) 이 누적 슬라이드 에너지를 비움 → 완전 정지가 안정 상태가 됨.

목표: **일정 높이(`smallLedgeStepHeight`) 이하의 작은 턱은 부드럽게 넘어가기.** 부수 목표: 같은 메커니즘으로 faceted(저폴리곤) 볼록 곡면의 각 facet step 도 매끈하게 통과.

(이 문제는 이전에 `docs/faceted-terrain-jitter-attempts.md` 에서 다룬 떨림 fix 와 같은 파이프라인 — edge enumeration + soft-band EffectiveNormal + teleport-then-snap + (옵션) buried rejection — 위에 얹는 거라, 그 회귀 케이스들을 깨지 않게 조심해야 함.)

---

## 거부된 접근들

### A — step offset (캐릭터 컨트롤러식)
forward 속도가 작은 턱에 막히면 그 프레임에 `rb.position += baseNormal × stepHeight`.
→ **거부됨** (사용자): faceted terrain 에서 step-detect 가 프레임마다 깜빡임 → 위치 점프 → 떨림.

### B — synthetic ramp contact
장애물 contact 의 normal 을 합성 램프 normal (`slerp(baseNormal, ledgeTopNormal, h/stepHeight × maxRampAngle)`) 로 교체. 턱 높이는 raycast 로 측정. Phase A push 도 합성 normal 방향으로.
→ **거부됨** (audit, `code-audit-reviewer`): 구조적 막힘.
- 합성 contact 가 `EffectiveNormal` / `ProjectVelocityAgainstContacts` / `SelectPrimary` / Phase A push 4개 서브시스템을 동시에 만족시킬 수 없음 — 각자 다른 normal 값을 원함.
- 특히 합성 contact 를 얕게(`radius − 0.5×contactSkin`) 만들면 → `SelectPrimary` 에서 더 깊은 바닥 contact 한테 짐 → `SnapToSurface` Phase B 가 매 프레임 슬라임을 바닥에서 `radius` 거리로 anchor → 들어올린 게 매 프레임 취소됨. 자기상쇄. "합성을 깊게 만들어 primary 시키자" → Phase B 가 그 collider(=수직벽)에 anchor → 슬라임을 벽 안으로 끌어들임. 반대 효과.
- 이 프로젝트는 `m_QueriesStartInColliders: 1` (Physics2DSettings) → raycast 가 ledge collider 안에서 시작하면 내부 면을 distance≈0 으로 맞춰서 높이 측정 오염.
- 합성 normal 이 EffectiveNormal 에 높은 가중치로 들어가면 faceted 곡면 떨림 회귀 위험.

audit 권고: **B 폐기, "velocity-clip suppression" 으로** — 작은 턱 contact 의 속도 클립만 끄고 나머지(EffectiveNormal/SelectPrimary/Phase A/Phase B)는 안 건드림, Phase A depenetration 이 들어올리게 둠. raycast 없이 polygon vertex 데이터(이미 순회 중)로 판정. + "Phase A 가 충분히 들어올리는지는 프로토타입으로 확인 필요" 라고 함.

---

## 채택한 접근 — velocity-clip suppression (+ audit 권고 + 반복 추가)

### 설정 (`SlimeSettings.cs`)
- `surfaceProbeSmoothSmallLedges` (bool, **default false** — opt-in)
- `smallLedgeStepHeight` (float, Min 0, default 0.15) — "작은 턱" 판정 기준 높이

### 판정
contact 가 **polygon vertex 에 clamp** 됐고(= `uRaw < 0 || uRaw > 1`, closest point 가 edge 끝점), 그 vertex 가 슬라임 발(`rb.position − gravityUp × slimeRadius`)에서 `smallLedgeStepHeight` 이내 → "작은 턱".
- `ProjectVelocityAgainstContacts` 에선 contact 데이터만 있으므로 vertex 위치를 normal·dist 로 역산: clamped contact 의 normal 은 bisector `(rb.position − vertex)/dist` 이므로 `vertex = rb.position − n·dist` → `height above foot = radius − dist·dot(n, gravityUp)`.
- `TryAddPolygonEdgePush` 에선 `closest` 가 곧 vertex 라 `dot(closest − foot, gravityUp)` 직접.
- **왜 clamped 만?** 큰 벽은 슬라임 중심이 그 정면 면 edge 의 interior 높이에 걸려서 closest point 가 clamp 안 됨 → 자연히 제외됨 → 거기선 정상적으로 멈춤. (← 이게 동시에 한계: 슬라임 반지름보다 큰 턱도 같은 이유로 못 잡음. 아래 "안 되는 이유" 참조.)

### 4단계 처리
1. **`SurfaceProbe.ProjectVelocityAgainstContacts` Step 2** — 작은 턱 contact 에 대한 속도 클립을 **skip** → forward 슬라이드 속도 보존. (Step 1 의 `fallbackNormal` 클립은 그대로 → 슬라임은 슬로프에 tangent 유지.)
2. **`MultiContactSurfaceProbe.TryAddPolygonEdgePush`** — 작은 턱 vertex 의 bisector push 에서 `lastGoodNormal`(이전 프레임 primary surface normal ≈ 슬로프 normal)의 **tangent 성분을 잘라냄** → `normal = lastGoodNormal`, `depth = dot(bisectorPush, lastGoodNormal)` → push 가 순수 "표면 따라 위로 들어올리기" 가 됨. (plain bisector push 는 슬라임 접근 중일 때 위+뒤를 가리켜서 그 "뒤" 성분이 forward 슬라이드를 상쇄 → 슬라임이 lip 앞에서 정지하던 원인.) `dot ≤ 0` 이면 push drop.
3. **`SurfaceProbe.SnapToSurface` Phase B re-anchor tolerance** — `lowLedgePassthroughCount > 0` 인 프레임엔 re-anchor 허용 거리를 `radius + contactSkin` → `radius + contactSkin + smallLedgeStepHeight` 로 늘림. Phase B 는 `dist(slime, primary) > tolerance` 일 때 슬라임을 primary 표면에서 `radius` 로 도로 anchor 하는데, 평소 tolerance 면 슬라임이 슬로프 위로 `contactSkin`(~0.02) 밖에 못 올라감 → 작은 턱도 못 넘음. loosen 하면 `stepHeight` 까지 올라가고, 턱 위로 올라가면 슬로프 contact 가 inactive 되면서 primary 가 턱으로 전환 → Phase B 가 턱에 anchor → 안착. passthrough 끝나면 tight tolerance 복귀.
4. **`SlimeController.cs` FixedUpdate teleport-then-snap 블록 (position-based wedge drain)** — `lowLedgePassthroughCount > 0` 인 프레임에 net 변위(`rb.position − posBeforeTeleport`)가 의도 변위(`finalVel·dt`)의 40%(`< 0.16 × |delta|²`) 미만이면 = 사실상 끼임 → `slideVelocity = 0`. clip 을 skip 했으니 `velocity` 가 크게 남아 `FixedUpdateAttached` 의 `velocity²<0.01` wedge drain 이 발화 못 함 → slideVelocity 가 계속 적분돼서 다음 charge 때 phantom launch 로 터지는 걸 막음. (사용자 보고: "점프하면 앞으로 빠르게 날아가" — 이게 그 증상.)

### 디버그
- `SurfaceProbe.LowLedgePassthroughCount` (virtual, base 는 0; `ProjectVelocityAgainstContacts` 에서 pass 0 에 카운트) — clip skip 된 contact 수.
- `SlimeStateDebugUI` per-contact 줄 끝에 `ledge-pass: N` (0 이면 안 뜸). (참고: `buried-rej: N` 도 같은 줄 — buried feature 용.)

---

## 현재 상태 / 안 되는 이유 — 가설

`surfaceProbeSmoothSmallLedges` 켜고 작은 턱에 굴리면 **`ledge-pass: 1` 은 뜨는데 슬라임이 안 넘어감.** (이전 스크린샷에서 본 contact 들: `[0] 5.4° [1]~ 42.6° [2]~ 56.7° [3] 24.7°` — `~` 는 inactive 마크. `ledge-pass: 1` 이면 그 중 하나만 작은 턱 판정.) 점프 폭발 문제는 4번 추가 후 별도 확인 필요(미확인).

다음 세션에서 가려야 할 가설들 (우선순위 순):

1. **턱이 슬라임 반지름보다 큼.** 슬라임 중심이 그 정면 면 edge 의 interior 높이에 걸리면 closest point 가 clamp 안 됨 → "작은 턱" 판정 자체가 안 됨 → clip skip 안 됨 → 여전히 멈춤. `ledge-pass: 1` 만 뜨고 다른 가파른 contact(`[1]~ 42.6° [2]~ 56.7°` — 이게 non-clamped 면)들은 그대로 클립. **→ detection 을 "clamped 여부" 가 아니라 "edge 의 top vertex(둘 중 gravityUp 기준 높은 쪽)가 stepHeight 이내" 로 바꿔야 함.** 이러면 per-contact `contactLowLedge[]` 플래그를 `MultiContactSurfaceProbe.TryAddPolygonEdgeContact` 에서 set 해야 함 (지금은 `ProjectVelocityAgainstContacts` 에서 normal·dist 로 역산하는데, 그 역산은 clamped 일 때만 vertex 를 정확히 줌). **단점:** tessellated 벽(여러 짧은 edge 로 쪼개진 큰 벽)은 각 짧은 edge 의 top vertex 가 stepHeight 이내 → 슬라임이 벽을 기어오를 위험. TerrainShape2D / mesh-to-collider 가 벽을 어떻게 tessellate 하는지 확인 필요.

2. **Phase A lift 가 여전히 부족.** tangent-strip 후 push 는 매 프레임 `dot(bisectorPush, lastGoodNormal)` = `(radius − dist) × dot(n, lastGoodNormal)` 만큼만 올라감. 살짝만 침투하면(`dist ≈ radius`) 거의 안 올라감. 그리고 forward 슬라이드가 매 프레임 슬라임을 턱 안으로 더 밀어넣는데 Phase A 가 못 따라잡으면 슬라임이 점점 깊이 박힘 → 다른 contact(top edge 등)가 잡혀서 클립 → 멈춤. → Phase A 의 작은-턱 push depth 를 키우거나(예: `dot(bisectorPush, lastGoodNormal)` 대신 vertex 까지의 수직 거리 기반으로 더 적극적으로), 또는 iter 수를 늘리거나.

3. **`lastGoodNormal` 이 슬로프가 아니라 턱 normal.** 이전 프레임에 턱 contact 가 primary 였으면 `lastGoodNormal` = 턱 bisector → tangent-strip 이 no-op (`dot(bisectorPush, bisector) = depth`, normal 안 바뀜) → lift 없음. 의도적으로 "안 나빠지지만 안 도움" 으로 설계했지만, 만약 이게 자주 발생하면 → base normal 을 `lastGravityUp` 으로 바꾸거나, `EffectiveNormal` 의 클램프-제외 버전을 따로 노출.

4. **Phase B loosen 으로 올라가도 primary 가 안 바뀜.** 슬라임이 stepHeight 까지 올라가면 슬로프 contact 의 `dist > radius + contactSkin` → inactive → `SelectPrimary` 에서 제외 → 턱이 primary 가 돼야 하는데... 만약 그 프레임에 턱 contact 가 ceiling 으로 판정되거나(가파른 normal), 또는 active 가 그것 하나뿐이라 step 1 으로 바로 선택되긴 하는데 그 다음 Phase B 가 턱의 ClosestPoint(=정면 vertex)에 anchor → 슬라임을 vertex 에서 `radius` 거리로 → 수평 방향이라 옆으로 밀어냄(턱에서 멀어짐). 슬라임 중심이 vertex **위쪽**에 있어야 ClosestPoint 가 위로 anchor 함. 그 전환 타이밍이 안 맞으면 진동. → 디버그로 primary 가 언제 뭐로 바뀌는지 봐야 함.

5. **teleport CCD 가 막음.** `SlimeController.cs` FixedUpdate 의 teleport 블록에 `Physics2D.CircleCastNonAlloc` CCD 가 있음 — `rb.position += (delta/dist) × max(0, clamped − contactSkin)`. 만약 그 CCD 가 작은 턱 collider 를 hit 으로 잡으면 슬라임이 턱 앞에서 `clamped` 거리까지만 전진 → 더 못 감. `if (h.collider == surfaceCollider) continue;` 라 primary 는 무시하지만, 턱이 primary 가 아니면 CCD 가 막음. **→ 작은 턱으로 판정된 collider 를 CCD 에서도 제외해야 할 수도.** (확인 필요 — 이게 가장 의심스러움. clip 을 skip 해서 velocity 는 forward 인데 슬라임이 안 움직이면 → teleport 자체가 막힌 것.)

---

## 다음 세션 할 일 (우선순위)

1. **디버그 가시화 강화** — 한 프레임에:
   - `ledge-pass` 가 어떤 contact index 인지, 그 contact 의 normal 각도 / dist / clamped 여부
   - 그 프레임의 teleport CCD 가 clamp 됐는지 (intended delta vs actually-moved-by-CCD)
   - Phase A 가 그 collider 에 적용한 net push 벡터 (방향/크기)
   - Phase B 가 발화했는지, primary collider 가 뭔지
   - net 변위 vs 의도 변위 비율
   → 이게 있어야 위 5개 가설 중 뭔지 가려짐.

2. **턱 크기 확인** — 테스트 중인 턱이 슬라임 반지름의 몇 분의 1 인지. 반지름보다 크면 가설 1 → detection 재설계(clamped → edge-top-vertex, `contactLowLedge[]` 배열).

3. **teleport CCD 가 막는지 직접 확인** — teleport 블록에서 CCD clamp 값 로깅. 막으면 작은-턱 collider 를 CCD 에서 제외.

4. 위 4개로도 안 되면 — 더 적극적 접근 재검토: 작은 턱 감지 시 `rb.position` 을 직접 살짝 위+앞으로 nudge (A 의 하드닝 버전 — 히스테리시스로 깜빡임 억제, 속도화로 점프 방지). 사용자가 처음에 A 를 떨림 때문에 거부했지만, 히스테리시스+속도화로 떨림을 잡을 수 있으면 재고 가능.

5. 다 안 되면 — `docs/faceted-terrain-jitter-attempts.md` 의 "다음에 시도" 1번(콜라이더 메쉬 자체를 지형 생성 단계에서 작은 step decimation/smooth)으로 우회. 런타임 비용 0, 가장 깨끗하지만 작화 도구 작업 필요.

---

## 관련 파일

- `SlimeJumping_v2/Assets/Scripts/Settings/SlimeSettings.cs` — `surfaceProbeSmoothSmallLedges`, `smallLedgeStepHeight` (그리고 buried feature 의 `surfaceProbeRejectBuriedContacts`, `surfaceProbeBuriedTestBias`). 둘 다 default false.
- `SlimeJumping_v2/Assets/Scripts/Physics/SurfaceProbe.cs` — `contactLowLedge[]` 필드 + `IsContactLowLedge(i)` / `IsLowLedgeCollider(col)` 접근자, `ProjectVelocityAgainstContacts` (재배치된 루프 + 작은-턱 clip skip), `SnapToSurface` Phase B (passthrough 시 tolerance loosen), `LowLedgePassthroughCount` / `lowLedgePassthroughCount`.
- `SlimeJumping_v2/Assets/Scripts/Physics/MultiContactSurfaceProbe.cs` — `IsSmallLedgeVertex(col, vertex, radius)` (판정: 높이 + `OverlapPoint` 위쪽 빈공간), `TryAddPolygonEdgeContact` (→ `contactLowLedge` set), `MergeOrAddContact` (`lowLedge` 파라미터 전달/dedupe), `TryAddPolygonEdgePush` (`IsSmallLedgeVertex` 로 게이트된 tangent-strip lift), `PromotePerCollider` (`contactLowLedge=false`) + buried rejection (`IsContactBuried`, `StashRejected`, ...).
- `SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.cs` — `OutputVelocity` teleport CCD sweep 에 `IsLowLedgeCollider` 제외 추가 + position-based wedge drain (`posBeforeTeleport`).
- `SlimeJumping_v2/Assets/Scripts/Debug/SlimeStateDebugUI.cs` — per-contact 줄 끝 `ledge-pass: N` / `buried-rej: N` 표시.
- `docs/features/slime/surface-attach.md` — "Buried-collider Rejection (opt-in)", "Small-ledge Passthrough (opt-in)" 섹션 (구현된 동작 설명).
- `docs/faceted-terrain-jitter-attempts.md` — 같은 파이프라인의 떨림 fix 히스토리 / 회귀 케이스 (깨면 안 되는 것들).

## 컨텍스트: 최근 리펙토링 영향

이 작업 중간에 `SlimeController` 관련 리펙토링이 있었음 (사용자가 함):
- `FixedUpdateAttached` 가 슬림해짐 — `ComputeSlideSpeed(angle, dt, gravityScale)`, `UpdateStickTransition()`, `CheckFrictionDetach(dt)` 헬퍼로 분리.
- 크롤(WASD 표면 이동)이 제거됨 — `crawlVelocity` 는 이제 착지 시 남은 모멘텀이 0으로 감쇠하는 채널일 뿐. 슬라이드 중력 + 차징 슬링샷이 유일한 이동 수단.
- `SurfaceProbe` 에서 `currentSnapNormal` 캐시가 제거됨 (`phaseAStripTangentComponent` 설정/코드와 함께) — 그래서 작은-턱 lift 는 `lastGoodNormal` 을 base normal 로 씀.
