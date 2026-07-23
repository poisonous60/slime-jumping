# Soft Cloud (Jelly) 구름 — 세션 핸드오프 (2026-07-22)

> 다음 세션이 이어서 작업하기 위한 기록. 전체 설계·결정트리는
> [soft-cloud-jelly-design.md](soft-cloud-jelly-design.md), 플랜은
> `~/.claude/plans/floofy-cooking-lemur.md`.

## TL;DR

밟으면 말랑 들어갔다 반동하는 "젤리 구름" 플랫폼을 만들었다(대상: 씬의
`Meshy_AI_Low_Poly_Cloud_0722043008_generate_Collider2D`). 컴포넌트
`SlimeJumping_v2/Assets/Scripts/Platforms/SoftCloudPlatform.cs` 완성·컴파일·씬 배선 완료.
여러 번 피드백 반복(spring → snappy 튜닝 → 차징 억제 제거 → **authored 커브 구동**).

**세션 끝에 유저가 방향 전환을 선언** — 아래 "다음 세션 방향" 참고. 지금 코드는
동작하지만 유저가 원하는 최종 느낌은 아님.

## 2026-07-23 업데이트 — 바디 이동 + 국소화 + config 이전 (아래 "방향 전환" 섹션보다 우선)

유저 피드백 2건 처리:

1. **"구름 전체 위치가 옮겨져야 한다"** — `ComputeMotion`이 `rb.position` 그대로 반환하고 있었음(바디 채널 부재).
   착지 시 법선 방향 킥 + damped spring 복귀 추가. 스프링은 **적분이 아니라 해석해 평가**
   (`EvaluateSpring`) — 50Hz 명시적 적분은 첫 피크를 10~50% 놓쳐서(감쇠·주파수 클수록 악화)
   인스펙터의 "이동 거리" 숫자가 거짓말이 됨. `ImpulsePerPeakDistance`가 킥 속도를 역산해서
   `bodyRecoilDistance`가 **실제 최대 이동 거리**가 되게 함. 라이더 운반은 기존
   `PlatformMotor.DeltaPosition` → `PlatformCarrier.ApplyPositionCarry` 경로 그대로
   (dent는 `SetPath` 로컬 지오메트리, 바디는 `MovePosition` — 채널이 달라 이중적용 없음).
   `VelocityTransferScale=0` 유지라 반동이 발사에 안 실림.

2. **"전체가 부풀었다 꺼진다"** — 원인: idle breathing이 `for (i<n) target[i] += b`로
   **컨투어 138점 전부에 균일 가산**. 즉 실루엣 전체가 1.3Hz로 들숨/날숨. `riders.Count > 0`
   게이트라 "슬라임 올라탔을 때만" 보이는 것도 일치. → 라이더 발자국 안으로 접음
   (`amp = env + restDentDepth + breath`). **불변식: 발자국 밖 전 점에 가산하는 항 금지.**

3. **config 이전** — 모든 튜닝이 `SoftCloudConfig`(ScriptableObject)로 이동, 공유 에셋
   `Assets/Resources/SoftCloudConfig.asset` 자동 로드(ThinIceV2Config 패턴). 컴포넌트엔
   `configOverride`(비워두면 공유) + `visualMesh`만 남음. 에셋 없으면 코드 기본값 인메모리
   인스턴스로 폴백 + 경고.

**미검증**: Unity 미실행 상태로 작업 — 컴파일 확인 안 됨. 에셋 YAML은 손으로 작성(스크립트
GUID `7c4a1f6b9d2e4c3a8b5f0e1d6a7c2b93`). 유저 Play 판정 대기.

**튜닝 남은 레버**(국소성): `radiusMult` 씬값 8 → 에셋 기본 5로 낮춤(발자국 반경 0.2×5=1.0,
구름 폭 9.33·높이 4.0). `dentEnvelope`의 -0.35 아웃워드 오버슈트도 넓은 발자국과 합쳐지면
"부푼다"로 읽힘 → -0.15 정도 시도.

## ⭐ 이전 세션 방향 (참고 — 위 업데이트가 일부 선행 처리함)

유저 말 그대로: **"처음 충돌에서 잠깐 들어갔다 나오는 게 원하는 게 아니다. 전체적으로
물리적인 움직임을 원한다."**

해석:
- 지금 구현 = **착지 순간 1회 authored-커브 dent** (juice/애니메이션식) + 작은 resting
  dimple + idle. 이건 "이벤트 반응"이지 지속적 물리 시뮬이 아님.
- 유저가 원하는 것 = **전체가 물리적으로 계속 반응하는 소프트바디 느낌** — 슬라임 무게·이동에
  표면 전체가 연속적으로 물리 반응(진짜 젤리/소프트바디). 세션 중 우리가 spring(물리) →
  curve(저작)로 옮겼는데, 유저는 **다시 물리 쪽으로, 그것도 "전체적/연속적"으로** 원함.
- 후보 방향(다음 세션에서 유저와 확정): **coupled mass-spring 메시망 / Verlet / pressure
  soft-body** — per-point 독립 스프링(Q5-A)이나 one-shot 커브가 아니라, 정점끼리 연결된
  연속 물리. 세션 초기 grill에서 Q5-B(coupled net)를 "덜 필요"로 미뤘는데, 그게 이제 본선일 수 있음.
- 유저가 "다음 세션에서 말하겠다"고 함 → **바꾸기 전에 유저와 물리 모델 확정부터**. 임의로
  갈아엎지 말 것.

## 현재 구현 상태 (동작하지만 최종 아님)

`SoftCloudPlatform : PlatformMotor, IPlatformEffect`. **authored-커브 구동 + 3레이어 합성**:
1. **착지 envelope** — `dentEnvelope`(AnimationCurve, X=시간0~1/`dentDuration`, Y=부호 깊이:
   +안/−밖) × `depthByImpactSpeed.Evaluate(ImpactNormalSpeed)`. `OnSlimeAttach`에 타이머 리셋(원샷).
2. **resting/rolling dimple** — `restDentDepth` 라이더 추적(걷기 생동감).
3. **idle jiggle** — `Interaction/Breathing.cs` 재사용, 얹혀있는 동안 미세 숨쉬기.
   → 합성 target → `neighborCoupling` 스무딩(청크) → `Mathf.SmoothDamp`로 disp가 target 추종.

## 유지할 검증된 아키텍처 (드라이브 갈아엎어도 재사용)

drive(spring/curve/softbody)가 뭐가 되든 아래는 검증됨 — 다음 세션에서 **버리지 말 것**:

- **소스 = 2D 컨투어 필드**: root `PolygonCollider2D` rest path(138pts) 스냅샷. 콜라이더
  `SetPath` + 자식 비주얼 메시(2232 verts) 둘 다 필드 팔로우. **divide-visual-collider**
  (비주얼은 필드 읽기만, 콜라이더 미변경).
- **probe가 -100 fresh 콜라이더로 dent를 라이드**: `OnAfterMotion`(PlatformMotor order -100)에서
  변형 → `SetPath` 동기 반영 → SlimeController(order 0) probe가 최신 콜라이더 봄. **carry
  override 쓰면 안 됨**(non-translating deformer는 base carry=0, probe 위에 얹으면 dent 이중적용
  — Codex/audit가 잡음). blob 컨투어는 ClosestPoint 모호성 없어 **ISurfaceAnchorProvider 불필요**
  (Bamboo와 다름).
- **`maxSurfaceSpeed`** = 틱당 표면 이동 상한 → probe가 표면 못 놓침(디태치·차징취소 방지). 이게
  진짜 안전장치(차징 오버슈트 억제 아님 — 그건 제거했음, 진동 뚝 끊겨서).
- **멀티라이더**: `SlimeController` 인스턴스별 집합, `IPlatformEffect` Attach/Detach로 관리,
  가산 중첩. `PruneRiders`가 파괴된 슬라임 정리(Unity fake-null `== null`).
- **좌표**: 필드 world XY, 콜라이더 root-local, 비주얼 child-local(`rootToChildVec` 캐시).
- **비주얼**: 각 vert의 K-nearest 컨투어 가중치 Awake 1회 프리컴퓨트.
- **perf**: epsilon 게이팅 SetPath, 무라이더+rest 시 sleep.

## 결정·근거 로그

grill(Q1~Q14) + Codex 어드버서리얼 리뷰(6 finding) + code-audit(F1~F5) 전부
[soft-cloud-jelly-design.md](soft-cloud-jelly-design.md)에 근거·해소 기록됨. 핵심만:
- 콜라이더도 따라감(비주얼-only는 슬라임 이동/발사 시 desync). 풀 perimeter PolygonCollider2D.
- 진짜 물성/localized(no cheap approximation) — 임포트 저폴리 메시 정점 실제 변형.
- 발사 없음(`VelocityTransferScale=0`), 파고들기 없음, 이동저항 없음.

## 파일 변경 (이 세션)

- **신규**: `SlimeJumping_v2/Assets/Scripts/Platforms/SoftCloudPlatform.cs`.
- **씬 (미저장!)**: 대상 오브젝트에 `Rigidbody2D`(kinematic) + `SoftCloudPlatform` 추가.
  `EditorSceneManager.MarkSceneDirty`만 했고 **저장 안 함** — 유저의 stage2 미저장 작업과 섞여
  있어 임의 저장 안 했음. 유저가 판정 후 저장 결정.
- **docs**: `soft-cloud-jelly-design.md`(설계), 이 핸드오프.
- **plan**: `~/.claude/plans/floofy-cooking-lemur.md`.
- **memory**: `feedback_dont_grill_safety_guards.md` 추가.

## 현재 인스펙터 값 (씬 인스턴스)

`dentEnvelope`(7키: slow-in→bottom dwell→fast-out→apex hold→settle), `depthByImpactSpeed`(3키),
`dentDuration=0.4`, `restDentDepth=0.015`, `idleAmplitude=0.012`, `idleFrequency=1.3`,
`followSmoothTime=0.03`, `neighborCoupling=0.4`, `maxDeflection=0.2`, `maxSurfaceSpeed=3`,
`radiusMult=8`(유저 튜닝), `visualInfluenceRadius=0.6`, `restEpsilon=0.0008`.

## 리서치 (snowball, standard tier — 다음 세션 참고)

젤리 느낌 = squash&stretch 비대칭 타이밍 + easeOutElastic/Back/Bounce + "everything moves"
(The Floor Is Jelly: 착지마다 ground ripple). spring vs authored-curve tradeoff:
spring=고정 비대칭(재형상 불가), curve=완전 저작. **물리 방향으로 가면** The Floor Is Jelly류
연속 소프트바디·mass-spring·pressure 모델이 참고점. (출처: gamedevcheatsheet, kvin.me,
GameAnalytics — 링크는 plan/design doc에.)

## 미해결

- 유저 방향 전환("전체 물리적 움직임") — 다음 세션에서 모델 확정 후 구현.
- 씬 미저장.
- code-audit 결과 — **아래 섹션에 추가됨**(완료 시).

## code-audit 결과 (2026-07-22, code-audit-reviewer, 커브 구동 재작성분)

**기능 버그 0.** Medium 1(문서) + 관찰 2(무해).

- **[Medium] 마스터 문서 미인덱스** — `SlimeJumping_메커닉_설계_v6.md` / `프로토타입_구현_현황.md`에
  Soft Cloud 항목 없음(CLAUDE.md 필수). → `프로토타입_구현_현황.md` 플랫폼 섹션에 WIP 항목 추가함
  (이 핸드오프·design doc 링크). mid-pivot이라 "완료"가 아닌 WIP로 기재.
- **[관찰] Follow 아웃워드 클램프 없음** — `SoftCloudPlatform.cs:316`은 inward(maxDeflection)만 클램프,
  outward(반동, 음수) 미클램프. 현재 기본값에선 최악 아웃워드 ≈ -0.065 < 0.2라 무해. 단 `depthByImpactSpeed`/
  envelope overshoot을 더 깊게 튜닝하면 안전망 없음 → `Mathf.Clamp(disp[i], -maxDeflection, maxDeflection)`
  한 줄로 대칭 클램프 가능(다음 세션에 고려; 유저 pivot 중이라 이번엔 코드 미변경).
- **[관찰] 착지 1틱 지연** — `OnSlimeAttach`가 order -100 이후 호출돼 착지 틱엔 rider 아직 없음 → envelope가
  다음 틱부터 시작(~16ms, 커브 flat 시작 구간 안). 무해, 프레임정확 시작 원하면 참고.
- **확인 정상**: ImpactNormalSpeed는 OnSlimeAttach 시점 유효(Airborne.cs:178에서 먼저 세팅),
  envelope 원샷·재착지 재시작, SmoothDamp maxSpeed 바운드, vel 재사용 일관, Jacobi 2패스 정확,
  sleep/wake 정확, 멀티라이더 중첩, per-tick GC 0, `??`/`?.` Unity ref 없음, K-nearest·좌표변환 정상.
