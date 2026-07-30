# Slime Split — 잘림 = 작아진 둥근 슬라임 + 자크식 회수

> **2026-06-10 전면 재설계**(reach 컷 + 셰이더 클립 단면 폐기, 경위는
> [slime-cut-redesign-brief.md](slime-cut-redesign-brief.md)) 후 **2026-06-11 2차 개편**:
> ① 상처 봉합을 "클립 평면 후퇴(스캔 룩)" → **메니스커스 막(표면장력) 차오름**으로 교체,
> ② 조각 melt/얼룩 폐기 → **조각 = 잔존하는 회수 아이템(롤 자크식)**, heal = 흡수 게이트.

블레이드(Saw/Knife)에 베이거나 공중에서 스스로 분열하면 슬라임은 **부피를 잃고
출렁이며 다시 둥글어진 작은 슬라임**이 된다. 잘린 단면이라는 지속 상태가 없다 —
지속 상태는 "작고 동그란 슬라임 + 바닥 어딘가의 내 조각"이고, heal은 **조각을 주워
담는 것**(흡수)이다. 조각을 영영 잃으면(낙사) 느린 자연 회복으로 전환된다.

- 상위: [saw-knife-hazard.md](saw-knife-hazard.md) (해저드 본체 + SliceEffect)
- 관련: [../slime/stunned.md](../slime/stunned.md)

---

## 1. 비전 (유저 확정, 2026-06-10)

```
잘리기 전          잘린 직후(출렁)        정착 상태
   ●●●●●            ●●●●●
  ●●●●●●●          ●●●●●●●        ●●●●
  ●● ● ●●●   →     ●● ● ●●   →    ●● ● ●
  ●●●●●●●          ~~~~~~~        ●●●●●
   ●●●●●          (출렁이며 뭉침)   (작고 동그람)
```

- 단면/클립/cap 없음. 잘린 부분은 진짜 조각으로 날아가고, 남은 몸은 작은 둥근 슬라임.
- 2단 점프(공중 셀프 분열) 트로프와 일치: 슬라임 분열 = 작아짐.
- face는 body 자식이라 몸과 같이 스케일 → **튜닝해둔 face 비율이 모든 크기에서 유지**(귀여움 보존).

## 2. 아키텍처 — 3층

### 2.1 물리: BodyScale (`SlimeController.BodyScale.cs`)

물리 진실은 스칼라 하나. `SetBodyScale(ratio)` / `BodyScaleRatio`(1 = 풀사이즈, `MinBodyScale=0.25` 안전 하한).

- `SlimeRadius` 프로퍼티 = `settings.slimeRadius × bodyScaleRatio` (**effective 반경**)로 전환.
  CircleCollider2D 반경 즉시 반영 + `SurfaceProbe.SetRadiusScale` 푸시.
- 반경 파생 길이 전부 effective 추종: probe broadphase/reach/depenetration(`EffRadius`),
  CCD CircleCast, 로프 락, 대나무 그랩 앵커, 코너 호라이즌, launch clearance,
  IgnoredColliderList, snow 발밑 샘플. 외부 시스템(ClothField/TautLine/GravityField/Spike/SnowField)은
  radius 라이브 read라 자동 적응.
- **작아진 슬라임이 진짜 그 크기로 충돌·부착·앉음.** 땅 위에서 자랄 땐 콜라이더가 프레임당
  미세 확장 → 일반 depenetration이 밀어올림. 특수처리 0.
- 회귀: ratio=1이면 모든 식이 기존과 동일.

### 2.2 비주얼: 스케일 스프링 (`SlimeVisual` + `SlimeVisualSettings`)

표시 스케일이 물리 ratio를 **underdamped 스프링**(WaterZone_v2 수식: Hooke + damping,
semi-implicit Euler, dt 클램프)으로 추적:

- 컷 = 물리 ratio 계단 하락 → 스프링이 오버슛하며 **출렁이며 뭉침**. 추가로
  `bodyScaleCutKick` × 손실량의 안쪽 속도 킥 — 깊은 컷일수록 크게 출렁.
- heal = 물리 ratio 완만 상승 → 스프링이 팝 없이 따라 성장.
- 적용: bulk scale/offset에 균일 곱(body 2D + body3d, body3d는 Z 포함 3축).
  velStr 빼기도 같은 비율로 스케일(접합부 오차 방지).
- **튜닝 (출렁임 정도, 유저 노출)**: `bodyScaleSpringK`(빠르기, ω≈√k),
  `bodyScaleSpringDamping`(2√k = 임계감쇠; 낮을수록 출렁), `bodyScaleCutKick` —
  **`SlimeSplitSettings`에 위치** (SlimeVisual이 `splitSettings` 참조로 읽음, 미배선 시 스냅).
- **face 명시 스케일**: face는 body의 형제(`SlimeVisual/Face`)라 body 스케일이 안 닿음 —
  `UpdateFaceOffset`에서 `faceRestScale × displayedBodyScale` 직접 적용 + drift 오프셋도 같이 축소.
  → face 비율이 모든 크기에서 유지.

### 2.3 이벤트: `SlimeSplitSequence` (2026-06-11 메니스커스 + 흡수 게이트로 재작성)

- `Begin(slime, entryVelocity)` — saw/knife 컷. 진입 속도에 수직(지터)으로 조각 사출.
  부피 `cutVolumeFraction`(현재 부피의 f) 손실:
  **조각 반경 = R·f^⅓, 본체 ratio ×= (1-f)^⅓** (부피 보존, 구 가정).
- `BeginSelfSplit(slime, ejectDir)` — 공중 셀프 분열 API. 코드 잔존하나 **기능 보류**
  (2026-06-11: 셀프 스플릿 시스템은 만들지 않기로 — 호출자 없음).
- **상처 = 메니스커스 막 (스캔 룩 제거)**: 컷 평면은 부피-정확 깊이(구면 cap Newton 풀이)에
  **월드 고정 — 다시는 움직이지 않는다**. 봉합 모션 전부가 막(膜)의 것:
  - 셰이더 `SliceSignedDist`(SlimeSliceData.hlsl)가 평면이 아니라 **고정 림 원에 앵커된
    구면 멤브레인** SDF. 멤브레인 apex 높이 `b`: 음수 = 오목 파임, 양수 = 볼록 차오름,
    `b = cap 높이` 도달 시 멤브레인 ≡ 몸 구면 → 클립 면적 정확히 0. 상처는 평면 슬라이드가
    아니라 **림→중심 동심 봉합**으로 닫힌다.
  - **봉합 = 보이는 수축에 잠금 (2026-06-11 2차 피드백)**: CLOSE 동안 진행도 `w`는 독립
    곡선이 아니라 **DisplayedBodyScale(스프링 지연 포함)의 수축 진행에서 직접 파생** —
    몸이 줄어드는 만큼 정확히 닫혀서 "몸은 이미 작은데 상처가 아직 열려있는" 탈동기화가
    구조적으로 불가능. SETTLE에서만 스프링(`meniscusSpringK/Damping`, Close 말미 닫힘
    속도를 시드로)이 마지막 출렁 오버슈트를 만든다. `closeSealCurve` 제거됨.
  - **HOLD 퀴버**: 잘린 순간 단면+림이 고주파 감쇠 진동(`quiverAmp/Freq/Decay/SpatialFreq`
    → 셰이더 `_SliceWobble`) — 정지 프레임이 아니라 때린 젤리의 잔동. 단면은 살짝 오목한
    상태로 시작.
- **과도기 페이즈**: HOLD(오목+퀴버) → CLOSE(L1 메니스커스 = 수축 잠금 · L2 비대칭 정착
  `gatherAmount/gatherCurve` · L3 물리 반경 램프 `closeBodyCurve` + 비주얼 스프링 출렁) →
  SETTLE(막 스프링 마지막 출렁, 클립 면적 0 도달) → RELAX(점성 가라앉기: 부풀음의 **발이
  퍼지며 높이가 낮아짐**(`sealRelaxSpread`) + 위로만 살짝 튕기는 바운스(`sealRelaxBounce`,
  반전 함몰 없음) + 저주파 슬로시 잔물결, 다 녹은 뒤에야 `_SliceCount=0` — 림 원에 기하학적으로
  고정된 입술이 한 프레임에 꺼지며 모양이 점프하던 문제의 해결책, `sealRelax*` 5종).
  표면장력 입술(`lipBand/lipMag`)·찢김 페이드(`tearAmp`)는 멤브레인 SDF를 그대로
  소비하므로 자동 추종. **새살 goo 발광 띠는 제거**(회복 진행이 빛으로 티남 — 유저 기각,
  `_SliceHeal` 상시 0). 평면 OS 변환 참조행렬은 기존과 동일.
- **heal = 흡수 게이트 (자크식)**:
  - `NotifyChunkAbsorbed(dir, vol)` — 조각이 닿으면 그 조각이 가져간 부피만큼 **즉시 회복**
    (`mergeDuration` smoothstep 램프). 연출: 닿은 지점에 멤브레인을 표면 밖으로 밀어넣은
    **합체 벌지**(클립 0, lip/wobble만 점등 — 발광 없음) + `absorbPopKick` 전신 출렁 +
    `mergeWobbleKick` 국소 잔물결. **컷 과도기 중 도착하면 그 부피를 CLOSE 램프의 목적지
    (postScale)에 합산** — 수축이 회복으로 한 동작으로 블렌드, "다 줄어든 뒤 다시 커짐"
    2단계 없음 (2026-06-11 2차 피드백). 새 컷이 머지를 끊으면 잔여 부피 먼저 정산.
  - `NotifyChunkLost(vol)` — 조각 영구 분실(낙사): 그 부피가 `slowPool`로 전환,
    `slowRecoveryDuration`(풀 부피 기준) 속도의 **느린 자연 회복** — 소프트락 불가.
- **점프력 = 몸집 파생**: `SplitLaunchMultiplier`는 매 프레임 현재 `BodyScaleRatio`에서
  계산(`woundedScaleRef`→`woundedJumpMultiplierMin` 선형 매핑). 독립 회복 타이머 없음 —
  흡수로 몸이 돌아오면 점프력도 같이 돌아온다.
- 재절단은 현재 ratio에서 다시 깎음 — 자연 스태킹(조각도 하나 더 생김).
- 회귀: idle일 때 아무것도 안 건드림. 완전 회복 시 조용히 리셋. `OnDestroy` 시 복원.

### 2.4 조각: `SlimeChunk` (잔존-회수 아이템으로 재작성)

melt/WorldTrail 얼룩 **삭제**. 라이프사이클:

1. **FLYING** — sliver flatten → 표면장력 round-up(기존). 추가로 **절단면**(-사출방향,
   mesh OS -Y)에 본체와 같은 메니스커스 상처를 푸시 — 잘린 단면이 보였다가 round-up과
   함께 동심 봉합(`pieceCutFaceFrac`, 같은 스프링 상수).
2. **RESTING** — 착지 후 무기한 잔존. `restBreathAmp/Freq` 아이들 숨쉬기 워블.
   (받침이 사라져 다시 떨어지면 FLYING 복귀.)
3. **ATTRACTING** — 슬라임이 `absorbPullRadius` 안에 들어오면 중력 끄고 자석 흡인
   (`absorbPullAccel/MaxSpeed`), 셰이더 velocity-stretch에 `absorbStretchBoost`를 얹어
   본체 쪽으로 길쭉하게 늘어나며 끌려옴. 스폰 직후엔 `absorbArmDelay` + "몸 밖으로 한 번
   나갔는가" 체크로 무장 — 같은 프레임 재흡수 방지.
4. **소멸** — 흡수(`NotifyChunkAbsorbed`) 또는 연속 공중 `severedMaxAirTime` 초과 = 낙사
   (`NotifyChunkLost`). 부피는 정확히 한 번만 정산(`resolved` 가드).

**플레이어와 절대 충돌하지 않음**: 스포너가 조각 콜라이더에 `excludeLayers = 슬라임 레이어`
(엔진 레벨, 수명 무관). 과거의 "조각이 밑에 깔리면 무한 airborne" 버그는
① 타이머식 ignore 리스트가 런치 클리어런스 sweep(`RegisterOverlapping`)에 전부 wipe되어
조각 충돌이 부활하고 ② probe는 슬라임 레이어를 못 봐 부착 판정이 안 되던 것 —
`IgnoredColliderList.RegisterOverlapping`이 Passthrough 항목을 보존하도록 함께 수정
(KnifeHazard 통과 중 런치 시의 동일 잠재 버그도 해결).

### 2.5 접지: 잘린 몸 = CircleCollider2D 여러 개 (2026-07-30 재설계)

증상: 밑동이 잘리면 슬라임이 공중에 뜬다. 몸 반경 0.1776 인데 보이는 단면은 중심에서 0.0547 —
**123 mm(몸 지름의 35 %) 가 공중**이다.

원인은 두 층이 서로 다른 몸을 본다는 것: Box2D 는 `CircleCollider2D`(non-trigger, Dynamic) 로 밀어내고,
`SurfaceProbe` 는 `rb.position` 중심 `EffRadius` 짜리 **수학 원**을 본다. 콜라이더만 바꾸면 프로브가
다시 밀어올리고, 프로브만 바꾸면 솔버와 싸운다. **그래서 둘 다 같은 도형을 보게 한다.**

- **도형**: `K = disc(EffRadius) ∩ wound_j`. wound 는 막이 부풀면 원판(`disc(x_m·n, Rc)`), 평평하거나
  파였으면 반평면(`{p·n ≤ off+b}`). 셰이더가 그리는 값(`off`, `b`, `rho0`)을 그대로 물리 단위로 받아
  쓰므로 콜라이더 = 그려진 것.
- **채우기**: 각 원을 ∂K 위에 접하게 앵커하고 나머지 제약으로 반지름을 `min` 클립 →
  **union ⊆ K 가 구성상 보장**. 슬라임은 조금 잠길 수는 있어도 **뜨는 건 구조적으로 불가능**.
  앵커는 ∂K 를 **호 길이 균등**으로 나눠 잡고, 위상을 최신 상처 법선에 고정해 **한 원이 항상 단면에 접한다**
  → 평평한 지면 위 안착 높이는 정확.
- **내접 불변식**: 몸 제약에 앵커한 원은 `|c| + r = EffRadius` 가 항등적으로 성립 → 모든 lobe 가
  `EffRadius` 원 안. 브로드페이즈·CCD·런치 클리어런스가 그대로 유효한 근거.
- **프로브 연동**: `Reach()` 는 **상수 `EffRadius` 유지**. 대신 보고하는 거리를 바꾼다 —
  `contactDistances = min_k(dist(C_k, feature) − r_k) + EffRadius`. **lobe 마다 자기 최근접점**을
  뽑는다 — 중심 최근접점을 재사용하면 옆으로 치우친 lobe 의 간격을 `√(d²+s²) − d` 만큼 과대평가해서
  이미 닿아 있는 lobe 가 떨어져 있다고 보고한다. 하위 소비처 7곳이 무수정으로 올바른 값을 얻고,
  `gap = 0` 은 Box2D 의 원-대-도형 정지 조건 그 자체라 두 solver 가 같은 점으로 수렴한다.
  (방향 의존 `Reach` 는 채택하지 않음 — support 는 vertex 접촉을, radial 은 face 접촉을 각각 최대
  87 mm 틀린다.)
- **여분 원은 상처와 함께 켜고 끈다**: 미컷 상태에서 살아 있으면 몸 안에 묻혀 있어도 존 트리거를
  드나들어 효과의 시작·종료 시점이 달라진다. 끄는 대가는 하나 — `Physics2D.IgnoreCollision` 은 쌍
  단위이고 비활성화하면 소실되므로, 활성 집합이 바뀔 때마다 `IgnoredColliderList.ReapplySuppression()`
  으로 재무장한다(칼 통과는 컷 시작과 같은 프레임에 그 쌍을 만든다).
- **재빌드 시점**: `FixedUpdate` 의 `UpdateSurfaceContact` 직전 + `ApplyBodyScale` 끝. 상처는 `Update()`
  에서 진행하므로 렌더 프레임마다 쓰면 물리 스텝당 0~3 회로 흔들린다.
- **콜라이더가 여러 개라서 같이 고친 것**: `IgnoredColliderList` 가 전체 배열을 ignore,
  텔레포트 CCD 스윕을 lobe 별로(`QueriesStartInColliders`=1 이라 풀 원 스윕은 제거된 쐐기 안 지형에
  거리 0 을 보고해 컷 내내 고착), 런치 클리어런스도 lobe 별, `IsSmallLedgeVertex` 의 발을
  `BodyExtentAlong(-up)` 으로, `KnifeHazard` 통과 판정을 전체 콜라이더로, 존 트리거 핸들러들은
  겹침 카운터로(Enter/Exit 가 콜라이더 수만큼 온다).
- **노브**(`SlimeSettings`): `cutColliderCount`(기본 6, 1 = 기능 끔) · `cutColliderArcSamples`(64) ·
  `cutColliderMinRadiusFrac`(0.05).
- **검증**: `scratchpad/cut-union-builder.mjs`, `cut-union-rules.mjs`, `final-rule.mjs` (Unity 불필요).
  반경 3종 × 봉합 21단계 × 컷각 8종 × 상처 1·2개에서 union ⊆ K, 내접, 단면 접촉, 아문 뒤 단일 원을 assert.
  배치 규칙 비교 실측(안착 높이 오차, 몸 반경 0.1776 환산):

  | 원 개수 | 1 상처 | 2 상처 |
  |---|---|---|
  | 3 | 48.9 mm | 62.5 mm |
  | 4 | 27.0 mm | 61.9 mm |
  | **6** | **13.1 mm** | **28.3 mm** |
  | 8 | 21.6 mm | 18.6 mm |

  기준선(현재 뜨는 양) 122.9 mm. 6개 = **9.4배 개선**.

### 2.6 얼굴 절단 + 조각 얼굴 파편 (2026-07-30)

얼굴은 마스크가 아니라 **실제로 잘려서 조각과 함께 날아간다**. 셰이더 수정 0 — `SlimeFaceSlice.shader`
(월드 clip, `_SliceCountWS` / `_SlicePlaneWS[3]` / `_SliceBottomGuardWS`)와 `Mat_SlimeFaceSlice` 는 이미
프리팹의 Eyes/Mouth 에 배선돼 있었고 C# 피더만 없었다.

- **본체**: `SlimeSplitSequence.PushFaceSlices()` 가 rest-OS 상처 평면을 막 정점(`off + b`, 봉합 시계
  그 자체)에서 월드로 밀어준다. 앵커는 `bodyFilter.transform` — 얼굴 스프라이트가 같은 렌더 체인에
  달려 있어 보간 변위가 공통모드로 소거된다. `rb.position` 을 쓰면 이동 중 컷 라인이 얼굴 위를 헤엄친다.
- **조각**: `Face` 를 통째로 복제해 chunk **루트**에 붙인다(Mesh 자식은 round-up/jiggle 로 비균등
  스케일이라 스프라이트가 찌그러진다). `SlimeFaceShard` 가 컷 평면을 자기 프레임에 얼려두고 매 프레임
  월드로 재구성 → 조각의 이동·회전을 따라간다. 평면 **부호를 뒤집어** 본체가 버린 쪽만 남긴다.
- 파편은 `_SliceBottomGuardWS` 를 끈다(켜져 있으면 낙하 중 얼굴이 통째로 되살아난다). 조각이 둥글어지는
  진행도로 알파 페이드 — 후퇴시키면 같은 눈이 화면에 두 번 남는다.

## 3. 폐기된 것 (이번 재설계에서 제거)

| 제거 | 비고 |
|---|---|
| `SetSplitCutPlanes` / `ApplySplitCollisionShape` / cut-plane 충돌 | **2026-07-30 부활** — 단 콜라이더 기하는 그대로(폐기 목록 준수). §2.5 참조 |
| 셰이더 클립 푸시 전체 (`_SlicePlaneOS`/`SliceCap`/`_SliceWobble`/heal tint/face WS clip) | 셰이더 파일(`SlimeSlice.hlsl` 등)은 잔존하나 `_SliceCount=0` 영구 no-op |
| launch 방향 bias (`SetSplitLaunchBias`) | 작아진 슬라임은 lopsided가 아님 |
| 설정 필드: cutOffsetFraction, sealRatio, cutRoundness, tear/lip, slice look 색상군, cut wobble군, maxWounds, pieceFinalScale | `SlimeSplitSettings` 정리. 기존 asset의 해당 직렬화 값은 무시됨 |
| 유지 | `SplitLaunchMultiplier`(점프력 회복), `SlimeChunk` 전체, recovery 곡선 2종, recutMinInterval |

§9 폐기 목록(IExternalController 분할, 내접원 콜라이더, visual-sink, DMC 등)은 구버전 문서/브리프 참조.

## 4. 테스트

- `SlimeJumping/Effects/Setup Slime Split Test` — 씬 와이어링 (기존).
- `Trigger Split Now (Play Mode)` — 블레이드 컷.
- `Trigger Self Split Now (Play Mode)` — 공중 분열 (조각 아래로, 반동 없음 — 비주얼/부피만).
- `Body Scale 0.6 / 1.0 (Play Mode)` — 시퀀스 우회하고 물리 ratio 고정. **이동 회귀 체크용**:
  작은 크기로 크롤/런치/벽/천장/슬로프가 전부 동작해야 함.
- 진단: `SlimeSplitJitterProbe`(read-only) 잔존.

## 5. 튜닝 표면

전부 `SlimeSplitSettings` (한국어 헤더로 섹션 정리):
[컷—부피] cutVolumeFraction/selfSplitVolumeFraction/jitter/recutMinInterval ·
[컷—출렁임] bodyScaleSpringK/Damping ·
[컷—메니스커스] meniscusSpringK/Damping, cutConcaveDepthFrac ·
[컷—퀴버] quiverAmp/Freq/Decay/SpatialFreq ·
[컷—과도기] cutHoldDuration/closeDuration + closeBodyCurve(봉합이 이 수축에 잠금), gather 군 ·
[컷—상처 look] tear/림(`lipBand`+`filletMag`+`mergeBulgeMag`)/`meniscusSealMargin`/cap 색 군 ·
[조각] 사출(pieceSizeScale)/round-up(piecePerpExpandMax)/jiggle/절단면(pieceCutFaceFrac)/잔존(restBreath)/흡수(absorbPull·Arm·Stretch) ·
[흡수—합체] mergeDuration/absorbPopKick/mergeWobbleKick/mergeFaceFrac ·
[회복] slowRecoveryDuration, woundedJumpMultiplierMin/woundedScaleRef (몸집 파생).
SlimeVisual은 `splitSettings` 참조 필요(Setup 메뉴가 자동 와이어링).
2026-06-11 제거: melt/얼룩 군, recoveryDuration/recoveryCurve/launchRecoveryCurve/regrowPopKick,
cutRoundnessMax (멤브레인으로 대체), closeSealCurve(봉합=수축 잠금), sliceHeal 색/강도/폭
(발광 띠 제거). 기존 asset의 해당 직렬화 값은 무시됨.

### 5.1 림 변위 (2026-07-30 재작업)

`_LipMag` 전역 스칼라 → **슬롯별 부호 배열 `_SliceLip[]`**. 음수 = 안쪽 필렛(컷 상처), 양수 = 바깥
벌지(흡수 합체). 두 연출이 같은 프레임에 같은 스칼라를 공유해서 서로를 죽이던 것을 끊음.

- 게이트가 `SliceSignedDist`(= `min(평면, 막)`)였던 것이 증상 2의 주범이었다. 막이 몸 구면에 접근하면
  그 min 이 캡 **전체**에서 0 에 가까워져 링이 아니라 면 전체가 0.12R 밖으로 밀렸고, `_SliceSeal` 이
  곱해져 **아물수록 커졌다.** → 막 항 없는 `SlicePlaneDist` 신설(영점집합 = 고정 림 원).
- 필렛 프로파일은 **양끝에서 0인 험프**(`4t(1−t)`). rest-space clip 경계는 안 움직이므로 림 자체를
  변위시키면 표면이 경계에서 떨어져 그 틈으로 뒷면 cap 이 보인다(빈 공간). 림은 제자리, 안쪽 살만 꺼짐.
- 필렛은 `1 − seal` 게이트(갓 잘려 림이 날카로울 때 최대, 아물면서 소멸), 벌지는 `seal` 게이트.
- 찢김 노이즈는 **단측**(`max(0, nz)`) — 찢김은 살이 떨어져 나가는 것이므로 안쪽으로만 파인다.
  음수 lobe 는 살을 평면 밖으로 돌출시켰다.
- `meniscusW` 는 1 로 클램프(K=90/D=9 는 임계 19 미달 → 1.18 까지 오버슛). 하드코딩 1.06 → `meniscusSealMargin`.
  물리 쪽은 이 여유를 쓰지 않는다(막이 살을 넘으면 뜸이 된다).

에셋 현재값(2026-07-30 유저 판정): `cutConcaveDepthFrac 0`(단면 중앙이 파여 보임 → 기각),
`quiverAmp 0`, `tearAmp 0.05`(들쭉날쭉은 유지 확정), `filletMag 0`, `mergeBulgeMag 0.06`.

## 6. 알려진 트레이드오프 / 미해결

- launch power·로프 sag는 크기 무관(상수) — 작은 슬라임도 같은 점프력.
  플레이 느낌 보고 `SplitLaunchMultiplier` 외 추가 스케일 여부 결정.
- probe skin류(contactSkin 등)는 절대값 유지 — ratio 0.6 수준에선 문제없을 것으로 예상, 회귀 메뉴로 확인.
- 메쉬 월드 Y 톱니 떨림(브리프 §4)은 컷과 무관한 비주얼 파이프라인 이슈로 잔존 — 별도 진단 건.
