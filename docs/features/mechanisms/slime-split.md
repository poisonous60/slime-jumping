# Slime Split — 잘림 = 작아진 둥근 슬라임

> **2026-06-10 전면 재설계.** 이전 "reach 컷 + 셰이더 클립 단면" 구조(이 문서의 구버전)와
> 같은 날 그 위에 얹은 품질 스택(A 한 몸 클립 / B 모션 wobble / C goo 단면)은 **전부 폐기**.
> 유저가 평면 단면 실루엣 자체를 기각("안 귀엽다") — 컷 평면을 어떻게 그리든 답이 없었다.
> 폐기 경위·실패 모드는 [slime-cut-redesign-brief.md](slime-cut-redesign-brief.md) 참조.

블레이드(Saw/Knife)에 베이거나 공중에서 스스로 분열하면 슬라임은 **부피를 잃고
출렁이며 다시 둥글어진 작은 슬라임**이 된다. 잘린 단면이라는 지속 상태가 없다 —
지속 상태는 그냥 "작고 동그란 슬라임"이고, heal은 크기 재성장이다.

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

### 2.3 이벤트: `SlimeSplitSequence` (재작성)

- `Begin(slime, entryVelocity)` — saw/knife 컷. 진입 속도에 수직(지터)으로 조각 사출.
  부피 `cutVolumeFraction`(현재 부피의 f) 손실:
  **조각 반경 = R·f^⅓, 본체 ratio ×= (1-f)^⅓** (부피 보존, 구 가정).
- `BeginSelfSplit(slime, ejectDir)` — 공중 셀프 분열(2단 점프 연료).
  `selfSplitVolumeFraction` 손실, 조각은 ejectDir로. **반동 임펄스는 게임플레이 호출자 몫.**
- 조각 = 기존 `SlimeChunk` 그대로 (자기 블롭 메쉬, sliver flatten → 표면장력 round-up →
  낙하 → melt + WorldTrail 얼룩). 크기만 손실 부피와 일치하도록 변경.
- **과도기 (유저 확정: 동시 다층, 비대칭)**: HOLD(`cutHoldDuration`) — 풀사이즈 + 부피-정확
  깊이(구면 cap 부피식 Newton 풀이)의 진짜 평평한 단면. → CLOSE(`closeDuration`) — **3레이어
  동시 진행, 각자 곡선**:
  - **L1 상처 덮임** (`closeSealCurve`): 클립 평면이 깊이→표면 밖으로 후퇴(단면이 가장자리부터
    점으로 닫힘) + 표면장력 입술(`lipBand/lipMag`) + 거친 찢김 페이드(`tearAmp`) + 닫히는
    가장자리에 새살 goo 띠(`sliceHealColor/Intensity/Width`). 도밍은 `cutRoundnessMax`(기본 0).
  - **L2 비대칭 정착** (`gatherAmount/gatherCurve`): 몸(비주얼)이 잘린 면 쪽으로 끌려갔다
    봉합되며 재중심 — 반대쪽 살이 흘러와 덮는 느낌. 콜라이더는 중심 유지.
  - **L3 작은 구** (`closeBodyCurve`): 물리 반경이 곡선 따라 램프(비주얼-충돌 괴리 없음),
    비주얼 스프링이 그 위에서 출렁.
  평면 OS 변환은 (live 회전 × rest scale × displayedBodyScale) 참조행렬 — 회전·축소 보정(월드
  고정+닫힘), squash/stretch 비보정(한 몸 변형). 닫히면 `_SliceCount=0` 영구.
- **재성장 (유저 확정: 조용한 균일 + 마지막 뿅)**: `recoveryCurve` 램프는 조용히, 완주 순간
  `regrowPopKick`이 스프링에 바깥 킥 — 한 번 출렁이며 풀사이즈 선언.
- heal: 단일 타임라인. `recoveryDuration` 동안 `recoveryCurve`로 ratio → 1,
  `launchRecoveryCurve`로 `SplitLaunchMultiplier` → 1 (크기와 점프력 독립 곡선).
  재절단은 현재 ratio에서 다시 깎고 타이머 리셋 — 자연 스태킹.
- 회귀: idle일 때 아무것도 안 건드림. `OnDestroy` 시 ratio/multiplier 복원.

## 3. 폐기된 것 (이번 재설계에서 제거)

| 제거 | 비고 |
|---|---|
| `SetSplitCutPlanes` / `ApplySplitCollisionShape` / cut-plane 충돌 | probe의 directional reach 기계(`SetCutShape`/`Reach`)는 호출자 0인 inert 상태로 잔존 (SelectPrimary에 엮여 있어 제거 리스크 > 이득) |
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
[컷—출렁임] bodyScaleSpringK/Damping/CutKick ·
[컷—잘린 면] cutHoldDuration/capVanishScale/transitionMaxDuration/cap 색 3종 ·
[조각] 사출/round-up/jiggle/melt/얼룩 군 ·
[회복] recoveryDuration + 곡선 2종, woundedJumpMultiplierMin.
SlimeVisual은 `splitSettings` 참조 필요(Setup 메뉴가 자동 와이어링).

## 6. 알려진 트레이드오프 / 미해결

- launch power·로프 sag는 크기 무관(상수) — 작은 슬라임도 같은 점프력.
  플레이 느낌 보고 `SplitLaunchMultiplier` 외 추가 스케일 여부 결정.
- probe skin류(contactSkin 등)는 절대값 유지 — ratio 0.6 수준에선 문제없을 것으로 예상, 회귀 메뉴로 확인.
- 메쉬 월드 Y 톱니 떨림(브리프 §4)은 컷과 무관한 비주얼 파이프라인 이슈로 잔존 — 별도 진단 건.
