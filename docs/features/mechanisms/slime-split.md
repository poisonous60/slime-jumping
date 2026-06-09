# Slime Split — 베임 = 진짜 모양 변화

블레이드(Saw/Knife)에 베이면 슬라임은 **잘린 모양 그 자체가 된다 — 렌더·오클루전·충돌·부착·접지 전부.**
제거된 부분은 "없는 것"이다. 이 문서는 전체 아키텍처 / 수학 / 통합지점 / 폐기된 접근 / 튜닝 / 회귀안전을
기록한다.

- 상위: [saw-knife-hazard.md](saw-knife-hazard.md) (해저드 본체 + SliceEffect)
- 관련: [../slime/stunned.md](../slime/stunned.md), [../infra/external-velocity-two-channel.md](../infra/external-velocity-two-channel.md)

---

## 1. 요구사항 (설계 인터뷰로 정련된 것)

1. **잘림은 진짜 모양 변화.** "겉보기 연출"이 아니다. 제거된 부분은 모든 판정에서 없다:
   - 안 뜬다(구처럼 떠 보이면 안 됨).
   - 잘린 데서 **안 걸린다**(없는 부분에 물리가 걸려 멈추면 안 됨).
   - 잘린 쪽으로 **안 붙는다**(없는 부분이 벽붙기 판정 내면 안 됨).
   - **잘린 면으로 닿고 붙는다**(잘린 평면 = 진짜 슬라임 표면 → 지면/벽에 그 면으로 접지·부착).
   - 지형에 가렸을 때 흰색 오클루전 실루엣도 컷 반영.
2. **20–60s 지속, 조작 가능.** 잘린 채로 벽/천장/슬로프 붙어 다니고 차지·런치까지 한다.
3. **두 직교 축**:
   - Axis-A `sealRatio`: 한 프레임의 **모양**(열린 샤프 컷 ↔ 표면장력 봉합). 회복과 무관.
   - Axis-B 회복: **연속**, 디자이너 곡선으로 모양/파워 따로 회복.
4. **goo답게.** 직선 칼자국이 아니라 곡선 단면 + 바깥 입술 + 젖은 속.
5. **회귀 0.** 안 잘린 평소엔 byte-for-byte 동일.
6. 떨어져나간 조각(`SlimeChunk`)은 기존대로 유지.

---

## 2. 핵심 모델 — 방향별 reach (support function)

콜라이더는 `CircleCollider2D` 그대로 두되, **surface probe가 컷 모양으로 충돌**하게 만든다.
폴리곤 콜라이더(probe 전면 재작성)도, 내접원(점접촉+offset 버그)도 아니다.

**slime center는 rb.position 고정.** 바뀌는 것은 *어떤 표면을 향한 슬라임의 도달거리(reach)*뿐이다:

```
reach(d) = (disk 반경 R ∩ 모든 cut 반평면) 의 방향 d 로의 support 길이
```

`SurfaceProbe.Reach(contactNormal)` ( [SurfaceProbe.cs](../../../SlimeJumping_v2/Assets/Scripts/Physics/SurfaceProbe.cs) ):

```
d = -contactNormal          // contactNormal = 표면→슬라임. 슬라임이 표면을 향해 뻗는 방향 = -그것
h = R                       // disk
각 cut plane i (n_i = 제거쪽 단위법선, off_i > 0 = 중심→컷선 거리):
    dn = dot(d, n_i)
    if (dn > 0 && R*dn > off_i):                       // d가 제거쪽을 향하고, 컷이 실제로 도달을 제한할 때만
        planar = off_i*dn + sqrt(R²-off_i²)*sqrt(1-dn²)  // 컷 평면 위에서의 support
        h = min(h, planar)
reach = max(h, R*0.1)        // 극단 컷에서 0 방지
```

직관:
- **컷 쪽으로** (d·n>0) → reach = 컷 평면까지 → 슬라임이 **잘린 면으로 낮게 앉음**. (바닥컷이면 reach(아래)=off → 중심이 지면+off, 잘린 면이 지면에 닿음.)
- **수직/반대쪽** → reach = full R.
- **제거영역** → 그쪽 표면은 reach 밖 → contact 안 생김 → 안 걸림/안 붙음.
- **off → R** (컷이 메워짐) → reach → R → full circle 복귀.

수치 검증(R=0.5, 바닥컷 n=down off=0.225): floor 0.225 / 천장 0.5 / 벽 0.5 / 45°→floor 0.475 / uncut 0.5. ✓

**왜 offset 없이 reach만?** 내접원은 콜라이더를 kept쪽으로 옮겨(offset) 갓 잘린 순간 콜라이더가 떠
detect 실패 → airborne 지속 버그를 냈다. reach는 중심을 안 옮기므로 전환 pop/airborne이 없다. 슬라임은
잘린 직후 잘린 깊이만큼 **가라앉아** 잘린 면으로 다시 앉는다(= "잘린 만큼 아래로 들어간다").

### 2.1 probe 통합 (변환된 모든 지점)

`CheckSurface`에서 contact별 `contactReach[i] = Reach(contactNormals[i])`를 1회 채우고, 상수 radius를
쓰던 per-contact 지점을 전부 이걸로 교체. **broadphase(OverlapCircle)는 R(max) 유지** — 멀리 있는
콜라이더를 누락하지 않기 위해.

| 위치 | 변환 |
|---|---|
| `SelectPrimary` active 판정 | `dist <= contactReach[i] + skin` |
| `SelectPrimary` 연속/타/deepest pen (3곳) | `contactReach[i] - dist` |
| `EffectiveNormal` 가중 | `contactReach[i] + fullBand - dist` |
| `ProjectVelocityAgainstContacts` 가중 | `contactReach[i] + fullBand - dist` |
| `SnapToSurface` Phase A 잔여penetration | `Reach(d/dist) - dist` |
| `SnapToSurface` Phase B 앵커 | `pos = closest + n*Reach(n)` (잘린 면으로 앉음) |
| `SingleContactSurfaceProbe.DepenetratePass` | `Reach(toSlime/dist) - dist` |
| `MultiContactSurfaceProbe` push 3경로 | `Reach(...) - dist`, depth<=0 skip |

`cutPlaneCount==0` → `Reach`가 첫 줄에서 `return R` (부동소수 연산 0) → 모든 지점이 상수 R로 환원
→ **byte-for-byte 미컷과 동일**.

### 2.2 데이터 흐름 / 타이밍

```
SlimeSplitSequence.LateUpdate(order 110)         SlimeController.FixedUpdate
  컷 평면(world 2D: n.x, n.y, off) 계산   ──push──▶  SetSplitCutPlanes(저장)
                                                     DecideProbeMode()
                                                     ApplySplitCollisionShape() → probe.SetCutShape
                                                     UpdateSurfaceContact()  ← reach 사용
```

- 컷 평면 off는 **메쉬 비율 → 월드** 변환: `off = (dispOffset / extentOS) * slimeRadius`. 비주얼 메쉬와
  콜라이더의 스케일 차이를 타지 않음(probe의 R과 같은 단위).
- 적용은 **DecideProbeMode 직후** → 모드 스왑된 새 probe도 첫 CheckSurface 전에 모양을 받음(별도
  TransferShape 불필요).
- **1프레임 lag**: SSS LateUpdate→다음 FixedUpdate. 갓 잘린 첫 1프레임만 full circle — **히트 스턴이
  가림**, 허용(문서화).

---

## 3. 비주얼

### 3.1 셰이더 데이터 / include 구조
- **[SlimeSliceData.hlsl](../../../SlimeJumping_v2/Assets/Art/Shader/SlimeSliceData.hlsl)** (신규) — 모든
  slice uniform의 **단독 소유**(`#ifndef` 가드) + `SliceSignedDist`. `SlimeBody_QuibliDeformInject.hlsl`
  **첫 줄로 hoist**(SlimeDeform보다 먼저) → ShadowCaster 포함 모든 deform 패스가 심볼 해결.
  `SlimeSlice.hlsl`도 include(가드로 no-op). 정점 lip과 fragment clip이 **`SliceSignedDist`를 공유**.
- `_SliceCount==0` → 전 함수 no-op = 회귀 0.

### 3.2 곡선 단면 + 노이즈 (`SliceSignedDist`)
```
d = dot(osPosRest, n) - off                          // 평면 base (==기존)
d += (cutRoundness * seal) * dot(perp, perp)          // seal로 볼록하게 휨(둥근 단면)
d += (tearAmp * (1-seal)) * frozenNoise(osPosRest)    // 찢긴 경계(기본 0, 저주파, 시간항 없음=안 기어다님)
```
seal↑ → 둥글고 매끈, seal↓ → 평평하고(샤프) 약간 찢김.

### 3.3 wet 단면 (`SliceCap`, fragment, back-face)
opening의 back-face를 평평한 검정 대신 **rim(젖은 밝음)→deep(어두움)** 으로 셰이딩, 셀 3밴드.
`dmin = min |SliceSignedDist|` → `lerp(rim, deep, dmin/capDepth)`.

### 3.4 정점 lip (`SlimeDeform.hlsl` stage 6)
kept쪽 rim 밴드(`d∈[-lipBand,0]`)를 **normalOS 따라 바깥 볼록**으로 밀어 표면장력 입술 형성:
```
bump = smoothstep(-lipBand,0,d) * step(d,0) * saturate(seal)
deformedOS += normalOS * bump * lipMag
normalOS    = normalize(normalOS + cutNormal * bump * lipMag * 4)   // analytic 노멀 틸트(셀 ramp가 둥글게)
```
finite-diff 아님(셀 ramp에서 노이즈 → 기어다니는 밴드). `_SliceCount==0` no-op. (폐기된 SliceDome가
저폴리 흰 spike였던 건 노멀 미재계산 + 무한 dome 때문 — 여기선 얇은 밴드 + analytic 노멀.)

### 3.5 얼굴
[SlimeFaceSlice.shader](../../../SlimeJumping_v2/Assets/Art/Shader/SlimeFaceSlice.shader) — 눈/입을 world-plane
배열로 동일 절단. 가드 비활성(완전 컷). sink는 콜라이더 reach가 처리하므로 **별도 face transform sink 없음**.

---

## 4. 두 직교 축 (회복)

`SlimeSplitSequence.Update`:
```
tn          = saturate(timer / recoveryDuration)
healVisual  = recoveryCurve.Evaluate(tn)        // 모양 회복
healLaunch  = launchRecoveryCurve.Evaluate(tn)  // 파워 회복 (별도 곡선)
dispOffset  = lerp(offsetStart, offsetEnd, healVisual)   // 컷이 메쉬 밖으로 후퇴
SplitLaunchMultiplier = lerp(woundedJumpMultiplierMin, 1, healLaunch)   // freshest wound 기준
retire when timer>=dur && healVisual>=.999 && healLaunch>=.999
```
- Axis-A(`sealRatio`)는 timer 함수 아님 — 모양과 회복 독립.
- 기본 곡선 back-loaded("오래 유지 → 막판 회복"). 디자이너가 둘 다 그림.
- stacking: 재절단 = 새 wound(최대 3), 초과 시 가장-많이-회복된 wound evict.

---

## 5. 조작 feel (Tier 2)
- **런치 파워↓**: `SplitLaunchMultiplier` (위 healLaunch로 회복).
- **런치 방향 bias**: kept쪽(`-Σ woundN2D`)으로 small-angle 회전. `deg = launchDirBiasDeg * launchBiasFactor`,
  `launchBiasFactor = (1-minJump)/(1-woundedJumpMultiplierMin)`. **rope/bamboo/carrier(IsOnPlatform) 시 비활성**
  (관성 상속 안 흔듦). aim/power 항에만 적용. [SlimeController.Launch.cs](../../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.Launch.cs)
- **CoM offset은 폐기** — `rb`가 FreezeRotation + 모션 전부 analytic + torque 0 → `centerOfMass` 무효
  (코드 grep 확인). 비대칭 접지/부착은 reach가 자연 제공.

---

## 6. 파일 맵

| 파일 | 역할 |
|---|---|
| `Physics/SurfaceProbe.cs` | `Reach()` + `contactReach[]` + `SetCutShape/ClearCutShape` + 모든 per-contact reach 치환 |
| `Physics/Single/MultiContactSurfaceProbe.cs` | DepenetratePass / push 경로 reach 치환 |
| `Controller/SlimeController.cs` | FixedUpdate에 `ApplySplitCollisionShape()` 호출(DecideProbeMode 직후) |
| `Controller/SlimeController.Split.cs` (partial) | `SetSplitCutPlanes`/`ApplySplitCollisionShape`/`SetSplitLaunchBias`/`SplitLaunchMultiplier` |
| `Controller/SlimeController.Launch.cs` | 런치 방향 bias 적용 |
| `Effects/SlimeSplitSequence.cs` | wound 리스트, 컷 평면/2축/lip·cap MPB push, 내접원 아님 |
| `Effects/SlimeChunk.cs` | 떨어져나간 조각(불변) |
| `Settings/SlimeSplitSettings.cs` | 튜닝 SO |
| `Art/Shader/SlimeSliceData.hlsl` | slice uniform 단독 소유 + `SliceSignedDist` |
| `Art/Shader/SlimeSlice.hlsl` | fragment clip/cap/heal |
| `Art/Shader/SlimeDeform.hlsl` | 정점 deform stage 6 = lip |
| `Art/Shader/SlimeFaceSlice.shader` | 얼굴 컷 |

---

## 7. 튜닝 (`SlimeSplitSettings.asset`)

| 헤더 | 필드 | 의미 |
|---|---|---|
| Cut | `cutNormalJitterDeg`, `cutOffsetFraction` | 컷 평면 각 지터 / 중심offset(=조각 크기, 코어보존 위해 >0) |
| Cut shape (A) | `sealRatio` | 0 열린샤프 ↔ 1 둥근봉합 |
| | `cutRoundness` | seal=1에서 단면 휨 정도 |
| | `tearAmp`/`tearFreq` | 경계 노이즈(기본 0, 저주파) |
| | `lipBand`/`lipMag` | 표면장력 입술 밴드폭/돌출(정점) |
| Recovery (B) | `recoveryDuration` | 회복 총시간(~20–60s) |
| | `recoveryCurve` | 모양 회복 곡선 |
| | `launchRecoveryCurve` | 파워 회복 곡선(별도) |
| Slice look | `sliceCapColor`/`sliceCapRimColor`/`sliceCapDepth` | 단면 deep/wet rim/깊이 |
| | `sliceHealColor`/`sliceHealIntensity`/`sliceHealWidth` | 점액 차오름 fill |
| Jump+feel | `woundedJumpMultiplierMin` | 갓 잘림 런치 파워(기본 0.5) |
| | `launchDirBiasDeg` | 런치 방향 bias 최대각 |
| 조각/melt/wobble/stacking | (불변) | `pieceFinalScale`/melt/`maxWounds`/`recutMinInterval` 등 |

---

## 8. 회귀 안전
- `_SliceCount==0` → fragment + 정점 lip 전부 no-op. `cutPlaneCount==0` → probe `Reach==slimeRadius`로
  완전 동일(직접 수치 검증).
- 공유 `Mat_SlimeBody3D_Quibli`/`SlimeIcosphere`/`SlimeVisual` 핵심 미변경. body/face MPB는 instance
  MPB(공유 머티리얼 안 건드림). 조각(`SlimeChunk`)은 `sharedMaterial` 직접 쓰고 MPB 없음 → 새 uniform이
  안 샘.
- `SlimeController` 코어 미수정 — Split partial에만 추가(CLAUDE.md 규약).
- 코어 보존 불변식: `off>0`이라 중심(dot=0)은 어느 컷도 제거 못 함 → 블롭 안 사라짐(비치명).

---

## 9. 폐기된 접근 (다시 만들지 말 것)

| 접근 | 폐기 이유 |
|---|---|
| `IExternalController` FALL/REGEN (2개 실조각 분리) | 슬라임 장악, 과복잡 (2026-06-06 이전) |
| layered 비주얼 + `_SliceBottomGuard` (mesh-OS-y 30%) | 밑면만 안 잘려 **shelf** 어색 + OS-y라 벽/천장서 엉뚱한 밴드 보호(surface-aware 아님) |
| **내접원 콜라이더** (kept쪽 offset+축소) | 점접촉 + 갓 잘린 순간 콜라이더가 떠 **airborne 지속 버그**, 잘린 *면*에 못 앉음 |
| visual-sink (메쉬만 아래로) | 한 점만 닿음(기울면 떠 보임), 이동 물리와 호환 불명, 잘린 면 부착 안 됨 |
| contact-filter만 (원 콜라이더 유지 + 제거쪽 contact 무시) | 안 붙긴 하나 잘린 *면*으로 못 앉음(공중 부유 가능) |
| 폴리곤 콜라이더 (A) | probe 전체(5-step/slide/corner/ledge)가 원 가정 → 통째 재작성, 고위험 |
| CoM offset | FreezeRotation+analytic+torque 0 → 관측 효과 0 (무효) |
| conform / foot guard / 3-phase regen(hold→gather→fill) | reach가 접지 해결 / 단일 곡선으로 대체 |

→ **현재**: 방향별 reach(중심 고정) + 곡선단면+wet+lip + 2축.

---

## 10. 검증
- C# + 셰이더 컴파일 클린(`ShaderUtil.ShaderHasError==False`).
- `Reach()` 수치 검증(§2).
- **audit PASS** ([code-audit-reviewer]) — 잡아서 고친 것:
  1. `SurfaceProbe` Step3 deepest-pen이 상수 radius → `contactReach[i]` (컷측 contact 오선택).
  2. launch bias carrier 게이트 `carrier==null`(항상 false→bias 영영 안 켜짐) → `carrier.IsOnPlatform`.
  3. 죽은 `TransferShapeFrom` 제거.

## 11. 테스트
메뉴 `SlimeJumping/Effects/Setup Slime Split Test` → Play → `Trigger Split Now`. 확인:
1. 바닥컷 시 **잘린 면으로 낮게 앉음**(airborne X).
2. 잘린 면으로 지면/벽 **부착**.
3. 제거영역 **안 걸림/안 붙음**.
4. **미컷 이동 불변**(점프/벽타기/슬로프).
5. 단면 **둥근 lip + wet 속**.
연속 호출 시 stacking. `Scripts/Editor/SlimeSplitTestSetup.cs`, `SlimeFaceSliceSetup.cs`.
