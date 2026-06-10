# 슬라임 베임(컷) 연출 — 재설계 브리프

> **🔒 종결 (2026-06-10).** 이 브리프의 전제("잘린 면이 진짜 표면으로 지속된다")가 유저에 의해
> 기각됨 — 평면 단면 실루엣은 어떻게 그려도 안 귀엽다. 확정된 새 비전: **잘림 = 출렁이며
> 다시 둥글어진 작아진 슬라임** (지속 단면 없음, collider.radius 실제 축소 + 비주얼 스프링).
> 구현·아키텍처는 [slime-split.md](slime-split.md)(전면 개정판) 참조. 아래 내용은 폐기된
> 클립/reach 구조의 실패 기록 + 계측 데이터 아카이브로만 유효. §4의 "메쉬 월드 Y 톱니 떨림"은
> 컷과 무관한 비주얼 파이프라인 이슈로 여전히 미해결 — 별도 진단 건으로 유효.

> 새 세션에서 **잘리는 표현을 처음부터 다시** 만들기 위한 참고 문서.
> 기존 구현 문서: [slime-split.md](slime-split.md) (현재 reach 기반 구조 — 이 브리프는 그걸 **대체/재설계**하는 관점).
> 작성 맥락: 현재 reach+셰이더클립 구조로는 (1) 몸 떨림 (2) 잘린 면 떠보임("투명망토") (3) 낙하 시 컷 flicker (4) 점프 변형(squash/stretch)과 안 어울림 — 이 4개를 못 잡아서, 구조째 다시 가기로 함.

---

## 0. 한 줄 비전

> **블레이드에 베인 슬라임이 "진짜 잘린 한 덩어리"로 보이고 움직인다 — 잘린 면이 지면에 닿아 앉고, 몸의 점프 변형(squash/stretch)과 한 몸처럼 출렁이며, 정적인 칼자국이 아니라 살아있는 점액 단면이다.**

핵심 감성: "몸 전체가 있는데 밑부분만 투명하게 안 보이는" 느낌이 **절대 아니어야** 한다. 잘린 부분은 *없는 것*이고, 남은 몸은 잘린 면을 *진짜 표면*으로 가진 채 슬라임답게 행동해야 한다.

---

## 1. 하드 요구사항 (체크리스트)

새 구현은 아래를 전부 만족해야 한다. (✅ = 반드시, ⚠️ = 현재 실패 중)

1. ⚠️ **잘린 면이 지면/벽에 닿아 앉는다.** 몸이 잘린 깊이만큼 가라앉아 *잘린 평면*이 바닥에 접한다. 떠 있으면 안 됨("투명망토 갭" 금지).
2. ⚠️ **안 떨린다.** 정지 상태에서 몸(비주얼)이 미세 진동하면 안 됨. 낙하/이동 중에도.
3. ⚠️ **컷이 깜빡이지 않는다.** 낙하/이동 중 "잘렸다 안 잘렸다" flicker 금지.
4. ⚠️ **점프 변형과 한 몸.** 몸이 squash/stretch/늘어날 때 잘린 면도 **같이 변형**한다. 강체 평면이 변형하는 블롭에 박혀 따로 노는 느낌 금지.
5. ✅ **정적이지 않고 자연스럽게 움직인다.** 잘린 단면이 점액처럼 미세하게 출렁/장력. 단, 자유진동으로 "기어다니는"(crawling) 노이즈는 금지 — 모션(가속도/임팩트/런치) 구동이어야 함.
6. ✅ **슬라임답게(goo).** 직선 칼자국이 아니라 곡선 단면 + 바깥 입술(lip) + 젖은 속(wet cap).
7. ✅ **월드 고정 방향.** 벽을 타거나 이동방향으로 비주얼이 회전해도 잘린 면은 **월드에서 안 돈다**. (수평 칼자국이 벽에서 수직으로 돌면 안 됨. burn 연출도 같은 함정이 있었음 — burn은 피부 자국이라 몸 따라 도는 게 맞지만, 컷은 반대다.)
8. ✅ **face 연동.** 눈/입도 같은 잘린 면으로 잘린다(잘린 쪽 눈은 사라짐).
9. ✅ **잘린 채로 조작 가능, 20–60s 지속.** 잘린 채 벽/천장/슬로프 붙어 다니고 차지·런치.
10. ✅ **회귀 0.** 안 잘렸을 땐 기존과 byte-for-byte 동일(미컷 이동/점프/벽타기 불변).
11. ✅ **잘린 영역은 모든 판정에서 없다.** 안 걸림(없는 부분에 물리 안 멈춤), 안 붙음(없는 부분이 벽붙기 판정 안 냄), 오클루전 실루엣도 컷 반영.

---

## 2. 명시적 비-목표 (이건 싫다)

- ❌ **클립만 한 "투명 밑면".** rest-pose 평면으로 잘라 안 보이게만 하고 몸은 풀 사이즈로 떠 있는 것.
- ❌ **정적 칼자국.** 한 번 잘린 모양이 그대로 굳어 안 움직이는 것.
- ❌ **비주얼/콜라이더 상호 의존.** 충돌 모양을 비주얼 트랜스폼에서 끌어오거나, 비주얼을 충돌에서 끌어오는 것 (CLAUDE.md: *Divide visual and collider. Don't let them affect each other*).
- ❌ **물리공간 + 렌더공간 혼합.** rb.position(물리)과 보간된 렌더 트랜스폼을 한 계산에 섞기 → flicker 원인.
- ❌ **자유진동 노이즈(시간항).** 단면이 기어다니는 저주파 노이즈. (모션 구동만 허용.)

---

## 3. 현재 구조 & 실패 모드 (왜 갈렸나 — 반복 금지)

### 현재 구조 요약 (slime-split.md)
- **콜라이더**: `CircleCollider2D` 유지. `SurfaceProbe.Reach(contactNormal)` 가 (disk R ∩ 컷 반평면)의 방향별 support 길이를 리턴 → 잘린 쪽으로는 reach가 컷 평면까지만. 중심은 `rb.position` 고정.
- **비주얼**: 셰이더가 `osPosRest`(rest-pose 오브젝트공간)를 컷 평면으로 `clip()`. 컷 평면 `_SlicePlaneOS`.
- **데이터 흐름**: `SlimeSplitSequence.LateUpdate` 가 컷 평면(월드 2D)을 계산 → `SetSplitCutPlanes` → `SlimeController.FixedUpdate` 가 probe에 적용.

### 실패 모드 (이번 세션에서 계측으로 확인)
1. **컷이 비주얼 body 트랜스폼에 묶임** → 회전 따라 돔. (원인: `_SlicePlaneOS` = 컷 당시 OS 고정 + 충돌 평면을 `bodyFilter.transform.TransformDirection`으로 매 프레임 재계산.) → **Phase 1에서 월드 고정으로 부분 수정함**(회전은 멈췄다고 유저 확인).
2. **메쉬 트랜스폼이 정지 상태에서 톱니파로 떨림** → 몸이 미세 진동 = "떨림"의 정체. **rb.position은 완전 안정**인데 메쉬 월드 Y만 떪. (즉 떨림은 물리가 아니라 **비주얼 deform 파이프라인** 쪽.)
3. **비주얼 컷 중심 ≠ 충돌 중심.** 렌더 메쉬 원점이 rb보다 약 **0.055(=27% R) 위**에 있음. 비주얼 컷을 메쉬 원점 기준으로 자르면 충돌이 앉히는 높이보다 위에서 잘림 → 잘린 면이 바닥에서 뜸 = "투명망토".
4. **컷이 점프 변형과 따로 놂.** 클립이 `osPosRest`(변형 전)를 씀 → 몸이 squash/stretch 해도 컷 경계는 안 따라감.
5. **flicker.** 떨리는 메쉬 트랜스폼에서 컷 offset을 매 프레임 끌어오면(특히 물리위치와 혼합 시) 컷 면이 깜빡임.

> **교훈**: 현재 구조는 "컷 정의"가 여러 곳(셰이더 OS, 충돌 월드, 비주얼 트랜스폼)에 흩어져 서로/떨리는 트랜스폼에 의존한다. 이게 모든 버그의 공통 뿌리.

---

## 4. 계측으로 확인된 사실 (새 구조에서도 유효한 데이터)

이번 세션에서 read-only debug probe(`SlimeSplitJitterProbe`)로 확인한 수치. 새 세션에서 가정 검증에 재사용 가능.

- **R(슬라임 반경) ≈ 0.20.**
- **rb.position 은 정지 시 완전 안정** (`dHeight=0`, `reach` 고정). → 떨림은 물리 아님.
- **메쉬 월드 Y 는 정지 시에도 톱니파 진동** (±~0.04, 주기 ~4프레임). `body3d.localPosition`(=-0.002)·`rb.y` 둘 다 고정인데 메쉬 월드 Y만 떪 → 체인 중간 트랜스폼(body3d scale/rotation, 또는 보간, 또는 중간 스크립트)이 떠는 것. **아직 어느 트랜스폼인지 미확정**(scale/rotation/interpolation 판별 직전에 중단).
- **메쉬 원점이 rb보다 ~0.055 위** (`meshDY`). → 비주얼 컷을 rb 중심으로 안 맞추면 무조건 뜸.
- **현재 컷은 얕음**: 바닥컷 시 `reachFrac ≈ 0.97` (off ≈ 0.97R). 즉 바닥 sliver만 제거 → 큰 컷을 원하면 `cutOffsetFraction`(컷 깊이, off/R)을 낮춰야. (off 작을수록 깊게 잘리고 더 가라앉음.)
- **카메라는 rb(프록시) 추종** → rb 안정이라 카메라 타겟 자체는 안 떪(`dCam=0`). 유저가 느낀 "카메라 떨림"은 실제로 **슬라임 몸의 떨림**.

---

## 6. 폐기/안티패턴 (다시 만들지 말 것)

slime-split.md §9 의 폐기 목록(IExternalController FALL/REGEN, 내접원 콜라이더, visual-sink, 폴리곤 콜라이더, CoM offset 등)은 그대로 유효. 추가로 이번 세션에서:

| 안티패턴 | 이유 |
|---|---|
| 컷 평면을 비주얼 트랜스폼에서 매 프레임 끌어오기 | 트랜스폼 회전/떨림이 컷에 샘. (Phase 1에서 끊음.) |
| 비주얼 컷에 `transform.InverseTransformPoint(물리위치)` 혼합 | 물리공간+보간 렌더공간 혼합 → flicker. |
| 컷 깊이를 메쉬 extent OS로 재느라 방향마다 달라지게 | 월드 단위(R 기준)로 통일이 안전. |
| 떨리는 메쉬 위에 컷 비주얼만 손보기 | 근본(메쉬 떨림) 안 고치면 무한 반복. |

---

## 7. 검증 도구 (이미 만들어둠 — 재사용)

- **Debug probe**: `Assets/Scripts/Debug/SlimeSplitJitterProbe.cs` (read-only, controller에서 pull만). 로깅: `reach/reachFrac`(앉는 높이·뜸), `dHeight`(rb 진동), `dMesh/meshDY`(비주얼 떨림·오프셋), `dCam`(카메라 타겟), `bodyUpDev`(컷 vs 표면 회전), `[SplitVis]` 줄에 `meshWY/rootTY/scaleY/eulerZ`(떨림 출처 판별).
- **메뉴**: `SlimeJumping/Effects/Trigger BOTTOM Slice Now (Play Mode)` — 밑만 자르기(수평 entry → 수직 컷). 일반 `Trigger Split Now` 도 있음.
- **드라이버**: `node .claude/skills/unity-diagnose/driver.mjs {compile|attach|logs <tag>|clear|state}`.
- **셰이더 검증**: `ShaderUtil.ShaderHasError`.

---

## 8. 현재 작업트리 상태 (새 세션에서 결정할 것)

이번 세션에서 건드린 것 — 새 세션은 keep/revert 판단:

- `Effects/SlimeSplitSequence.cs`: 컷을 **월드 고정**으로 변경(회전 버그 수정). Wound에 `normalWorld` 저장, offset 월드 단위화, LateUpdate에서 비주얼 OS(방향만)·face WS·충돌 모두 월드 평면에서 파생. → **회전 수정은 유효, 유지 권장.** (떨림·float·flicker는 미해결.)
- `Debug/SlimeSplitJitterProbe.cs`: 신규 진단 컴포넌트. **Slime GameObject에 부착됨(MainScene 저장됨)** — 재설계 끝나면 떼거나 유지.
- `Editor/SlimeSplitTestSetup.cs`: `Trigger BOTTOM Slice Now` 메뉴 추가. 유지 권장(테스트 편의).
- **미커밋.** Phase 2(딴 구조)로 갈 거면 SlimeSplitSequence 변경은 참고만 하고 새로 짜도 됨.

## 8b. 진행 상황 (2026-06-10)

품질 스택 **A→C→B 구현 완료** (유저 지시로 §9 순서 대신 비주얼 품질 우선, 떨림 진단은 보류):
- **A 한 몸 클립**: OS 평면 pullback을 참조 행렬(live 회전 × body3d **rest** 스케일)의 Mᵀ로 교체 —
  회전 보정(요구 7) + squash/stretch·정점 deform은 태움(요구 4). `SlimeSplitSequence.LateUpdate`.
- **C goo 단면**: `SliceCap`에 wet meniscus 라인 + frozen marbling.
- **B 모션 wobble**: `_SliceWobble` — 속도변화−중력 여기 envelope, 정지/자유낙하 = 0 (§2 준수).
상세: [slime-split.md §3.2–3.3b](slime-split.md). 미해결: 메쉬 떨림(§4)·비주얼 컷 중심 vs rb 오프셋(§4 meshDY).
