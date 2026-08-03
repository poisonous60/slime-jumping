# CutShape — 로브가 몸 반경을 벗어남 (`ConMaxRadius` 0/0 축퇴)

**발견:** 2026-08-03 · **상태:** 수정 적용, 컴파일 통과, 유저 플레이 검증 대기
**증상 문서:** [cut-ceiling-jitter.md](cut-ceiling-jitter.md) 의 후보 원인 중 하나

---

## 1. 증상

Unity 콘솔에 간헐적으로:

```
[CutShape] lobe escapes EffRadius — broadphase, CCD and launch clearance assume it does not
  SlimeController:BuildLobes         (CutShape.cs:268)
  SlimeController:RebuildCutShape    (CutShape.cs:177)
  ← SetCutWounds  ← SlimeSplitSequence.PushCutShape  ← Update
  ← ApplyBodyScale ← SetBodyScale ← SlimeSplitSequence.UpdateCutPhase ← Update
```

두 호출 경로 모두에서 발생. `Debug.Assert` 는 에디터 전용이라 **빌드에서는 조용히 통과**하고, 벗어난 값이 그대로 `CircleCollider2D.radius` 에 써진다.

## 2. 불변식

`BuildLobes` 는 잘린 몸을 `K = disc(0,R) ∩ wound_j` 를 채우는 내접원(로브) union 으로 만든다. 브로드페이즈·CCD 스윕·launch clearance 가 전부 "로브는 몸 원 안에 있다"를 전제한다:

```
|centre_i| + r_i ≤ R
```

## 3. 근본 원인

[SlimeController.CutShape.cs](../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.CutShape.cs) `ConMaxRadius`, kind 0(원) 가지.

이 함수가 풀어야 할 조건은 "중심 `q − nu·r`, 반지름 `r` 인 원이 `disc(P,Rc)` 안에 들어감":

```
|w − nu·r| ≤ Rc − r        (w = q − P)
```

양변 제곱하면 `2·r·d ≤ Rc² − |w|²`, `d = Rc − w·nu`. **제곱하면서 전제 `Rc − r ≥ 0` 이 소실된다.**

| d | 상황 | 올바른 답 | 수정 전 코드 |
|---|---|---|---|
| `> 0` | 일반 | `(Rc²−|w|²)/(2d)` | 동일 ✅ |
| `≈ 0` | q 가 원 위 + nu 가 바깥 반경 방향 | **`Rc`** | `float.MaxValue` ❌ |

`d > 0` 가지는 추가 클램프가 필요 없다 — 반환값이 스스로 `Rc` 이하다:

```
Rc − (Rc²−|w|²)/(2d) = |w − Rc·nu|² / (2d) ≥ 0
```

`d = 0` 이면 부등식이 `0 ≤ Rc² − |w|²` 로 축퇴해 **모든 r 에 대해 참**이 된다. 그래서 기존 코드가 "제한 없음"으로 읽었다. 하지만 기하로 보면 `d = 0` + `|w| ≤ Rc` 는 `w·nu ≤ |w| ≤ Rc` 때문에 `|w| = Rc`, `nu = w/|w|` 를 강제한다 = **q 는 원 위, nu 는 그 바깥 반경 방향**. 내접원 중심은 `|centre − P| = Rc − r` 이므로 `0 ≤ r ≤ Rc` 에서만 성립한다.

## 4. 어떻게 로브가 밖으로 나가는가

`LobeAlong` 은 active 제약이 원이면 `r` 을 `conR[active]` 로 시작하고, 나머지 제약으로 min 을 건다.

- **active = 몸 원**: `r` 이 `R` 로 시작 → 무슨 일이 있어도 `|centre| + r = R`. 안전.
- **active = 막 원** (kind 0, `P ≠ 0`): `r` 이 막 반지름 `rc` 로 시작. 몸 원 호출이 `MaxValue` 를 돌려주면 **`r = rc` 가 살아남는다.**

```
centre = q − nu·rc = P          (|q − P| = rc 이므로)
|centre| + r = 2·rc − (off + b)
```

`rc = ((R+off) + w²(R−off)) / (2w)`. 예: `w = 0.5`, `off = 0.3R` → `rc = 1.475R` → `|centre| + r = 3.25R`. **R 단위로 초과한다.** 상한은 `BuildConstraints` 의 `rc < 1e4·R` 가드뿐 = 최대 1780 월드 유닛짜리 콜라이더.

## 5. 왜 값이 일치할 땐 안 터지나

`MaxValue` 가 나오려면 `d = R − q·nu < 1e-6`. `nu ∥ q` 는 `dir = ±n` 에서만 성립하고 그때 `q = n(off+b)` 이므로

```
d = R − (off + b) = (1 − w)(R − off)
```

`w → 1` 이어야 `d → 0` 인데, **`w → 1` 이면 `rc → R` 이라 escape 도 0 으로 간다.** 두 조건이 상호배타적이다.

`b`, `ρ₀` 가 `conR[0]` 과 **다른 R** 로 계산되면 배타성이 깨진다. `off + b = off + w(R_prev − off)` 가 `R_now` 에 도달하는 지점이 `w = (R_now−off)/(R_prev−off) < 1` 이고, 거기선 `rc` 가 아직 R 보다 훨씬 크다. Close 램프가 이 교차점을 **봉합마다 반드시 통과**하며, 프레임이 `|d| < 1e-6` 창에 떨어지면 터진다 → 간헐적.

그리고 `dir = base0 = n` 은 **로브 0 이 항상 쓰는 방향**이라 매 리빌드마다 평가된다.

## 6. 수정

### 6.1 1차 시도 — 폐기 (Codex 리뷰가 반증)

```csharp
return d < 1e-6f ? conR[i] : (conR[i] * conR[i] - w.sqrMagnitude) / (2f * d);
```

`float.MaxValue` 만 `conR[i]` 로 바꿨다. **틀렸다.** `1e-6` 은 **허용 밴드**지 정확히 0 이 아니고, `conR[i]` 는 `d == 0` 인 정확 축퇴점에서만 옳은 값이다. 밴드 안쪽(작은 양수 d)에서는 정확식이 `conR[i]` 보다 훨씬 작은 값을 낸다.

Codex 반례 (`codex exec --sandbox read-only`):

```
R = 1, 평면 상처 normal = (1,0), off = 0.9999995, b = 0, w = 0
로브 방향 dir ≈ (0.99999968, 0.0008)

ConExit(halfplane) = off/dir.x ≈ 0.99999982  → 반평면이 몸 원보다 먼저 active
q  ≈ (0.9999995, 0.0008),  nu = (1,0)
d  = R − dot(q,nu) = 5e-7  < 1e-6   → 새 가지 진입 → r = R = 1
centre ≈ (−5e-7, 0.0008)
|centre| + r ≈ 1.0008  >  R + 1e-4   ← 여전히 불변식 위반

정확식이 주는 값 = 0.36  (들어맞음)
```

### 6.2 적용본

밴드를 두는 것 자체가 잘못이었다. `d > 0` 이면 정확식이 **이미** `conR[i]` 이하가 보장되므로 잘라낼 이유가 없다.

```csharp
Vector2 w = q - conP[i];
float d = conR[i] - Vector2.Dot(w, nu);
float num = conR[i] * conR[i] - w.sqrMagnitude;
// d <= 0 forces |w| >= conR[i], so q sits on or past the rim: the squared form degenerates
// to 0 <= 0 and drops the r <= conR[i] premise it was derived under.
if (d <= 0f) return num > 0f ? conR[i] : 0f;
float r = num / (2f * d);
// conR[i] - r = |w - conR[i]*nu|^2 / 2d >= 0, so the clamp only absorbs float blow-up.
return r < conR[i] ? r : conR[i];
```

- `d > 0`: 정확식 그대로 → 정상 경로 회귀 0
- `d ≤ 0` 이면 `w·nu ≥ conR[i]` 이고 `w·nu ≤ |w|` 이므로 `|w| ≥ conR[i]` 강제 = q 가 림 위거나 밖
  - `num > 0` (림 위, 수치오차): `conR[i]` 가 참값
  - `num ≤ 0` (밖, 입력 불일치): `0` → `LobeAlong` 의 `!(r > 1e-5f)` 가 그 로브를 버림
- 최종 `Min` 은 `d → 0⁺` 에서의 float 발산만 흡수

반평면(kind 1)의 `den < 1e-6 → float.MaxValue` 는 **그대로 둔다.** 평면 위 q 에 접하는 원은 반지름과 무관하게 반평면 안에 있으므로 무한대가 맞다. (Codex 도 "이번 불변식 위반의 원인은 disc 가지"라고 확인. 다만 `den` 이 작은 양수일 때 과대추정하는 성질은 같아서, 별도 증상이 나오면 여기부터 볼 것.)

## 7. 남은 문제 — R 불일치 자체 (미수정)

수정은 "불일치가 생겨도 로브가 몸 밖으로 안 나간다"를 보장할 뿐, **불일치 자체를 없애지 않는다.** 경로 3개:

| # | 경로 | 성격 |
|---|---|---|
| 1 | `SetBodyScale` → `ApplyBodyScale` → `RebuildCutShape` 가 **지난 프레임 상처값**으로 돎 ([BodyScale.cs:35](../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.BodyScale.cs#L35)) | 1프레임 지연. Close 램프 프레임당 축소 ≈ 0.09% |
| 2 | `RebuildCutShape` 는 `R = slimeRadius × bodyScaleRatio × **pinchColliderScale**`, `PushCutShape` 는 `slime.SlimeRadius` = **pinch 미포함** ([CutShape.cs:174](../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.CutShape.cs#L174) vs [SlimeSplitSequence.cs:554](../../SlimeJumping_v2/Assets/Scripts/Effects/SlimeSplitSequence.cs#L554)) | **핀치 중 상시 불일치.** 가장 큼 |
| 3 | `RebuildCutShape` 는 FixedUpdate, `PushCutShape` 는 Update | 클록 차이 |

2번은 잘린 채로 압착되면 상처 기하가 몸보다 크게 남는다는 뜻이라 별도 판정 필요.

## 8. 검증 상태

| 단계 | 결과 |
|---|---|
| 대수 증명 (§3~5) | 통과 |
| Codex 적대적 리뷰 — 1차 수정본 | **반증**, 반례 §6.1 |
| Codex 적대적 리뷰 — 적용본(§6.2) | **미수행** |
| 컴파일 (`unity-cli editor refresh --compile`) | 에러 0 |
| 플레이 검증 | **미수행** |

Codex 는 플러그인 skill dispatch 가 깨져서 `codex exec --sandbox read-only` 직접 호출로 돌렸다 — [codex-plugin-windows-broker-hang.md](codex-plugin-windows-broker-hang.md).

플레이 판정 기준: 컷을 반복해도 콘솔에 `[CutShape] lobe escapes` 가 더는 안 뜨는지.
