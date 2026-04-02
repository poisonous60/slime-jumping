# Unity 2.5D 슬라임 플랫포머 완전 제작 가이드

**에셋 없이 시작하는 학부생도 Blender 모델링부터 URP 셰이더, Cinemachine 카메라, 스쿼시/스트레치 애니메이션까지 전 과정을 밟아 슬라임 캐릭터를 완성할 수 있다.** 핵심은 로우폴리 구체 변형으로 모델을 만들고, DOTween 또는 코루틴 기반 스케일 조작으로 물리 반응형 애니메이션을 구현하며, Shader Graph 툰 셰이더와 Cinemachine 카메라로 2.5D 비주얼을 완성하는 파이프라인이다. 이 보고서는 각 단계의 구체적 설정값, 복사 가능한 코드, 무료 리소스를 모두 포함한다.

---

## 1. Blender로 슬라임 모델을 만드는 가장 빠른 방법

### UV Sphere에서 시작하는 단계별 워크플로

Blender를 열고 기본 큐브를 삭제(`X`)한 뒤, `Shift+A` → Mesh → **UV Sphere**를 추가한다. 좌하단 팝업에서 **Segments: 16, Rings: 8~12**로 설정하면 약 **512 삼각형**의 기본 구체가 생성된다. 이 정도가 2.5D 슬라임의 이상적인 시작점이다.

**핵심 변형 과정:**

1. `Tab`으로 Edit Mode 진입 → 상단 바에서 **Proportional Editing**(파란 원 아이콘, 단축키 `O`) 활성화
2. Falloff 타입을 **Smooth**로 설정
3. 하단 버텍스 선택 → `S` → `Shift+Z`로 XY 평면에서 바깥으로 스케일 (넓고 납작한 바닥 생성)
4. 최상단 버텍스 선택 → `G` → `Z`로 약간 아래로 이동 (마우스 휠로 영향 반경 조절)
5. 바닥면 완전 평탄화: 하단 버텍스 선택 → `S` → `Z` → `0`

**선택적 마무리:** Modifier Properties에서 **Subdivision Surface** 추가(Viewport: 1~2)하면 부드러운 젤리 외형이 완성된다. 만족하면 `Ctrl+A`로 Apply. Object Mode에서 우클릭 → **Shade Smooth**를 적용하면 로우폴리 메시가 훨씬 매끄럽게 보인다.

### 얼굴 표현: 텍스처 vs 지오메트리

| 방식 | 장점 | 추가 폴리곤 | 추천 대상 |
|------|------|-------------|-----------|
| **텍스처 페인팅** (추천) | 폴리곤 추가 없음, 표정 변경 용이 | 0 | 초보자 |
| **별도 구체 눈** | 3D 깊이감, 독립 애니메이션 가능 | ~100~200 tris | 2.5D 시차 활용 시 |
| **하이브리드** (최적) | 눈은 지오메트리, 입은 텍스처 | ~100 tris | 최종 품질 추구 시 |

텍스처 방식은 UV 언래핑 후(`U` → **Smart UV Project**, Angle Limit: 66°) GIMP나 Krita에서 눈·입을 직접 그리면 된다. **1024×1024 px** 해상도면 충분하다.

### 무료 슬라임 에셋 즉시 활용하기

직접 모델링이 부담스러우면 **CC0(퍼블릭 도메인) 라이선스** 무료 에셋을 바로 사용할 수 있다.

| 리소스 | 에셋명 | 특징 |
|--------|--------|------|
| **Quaternius** (poly.pizza) | Slime Enemy | 애니메이션 포함 로우폴리 FBX, CC0 |
| **OpenGameArt** | Little Slime | .blend 파일 + 리그 + 걷기 애니메이션, CC0 |
| **itch.io** | Low Poly Slimes | 다양한 색상 슬라임 팩, 무료 |
| **Kenney.nl** | Platformer Kit (140개 에셋) | 레벨 환경 구축용 3D 블록, CC0 |
| **Unity Asset Store** | Slime Character (2D) | 무료 2D 스프라이트, 프로토타이핑용 |

Asset Store 검색 키워드: `"slime"`, `"blob"`, `"jelly character"`, `"low poly creature"`, `"cute monster"`. 에셋 선택 시 **URP 호환 여부**, **5,000 tris 이하**, **FBX 포맷 제공**을 반드시 확인한다.

### 로우폴리 스타일을 권장하는 4가지 이유

2.5D 슬라임 캐릭터에는 **500~1,500 삼각형**이 최적이다. 첫째, GPU 버텍스 처리 부하가 극소화되어 프레임당 200만 폴리곤 예산 중 여유가 충분하다. 둘째, 물리 기반 변형(소프트바디, 스쿼시/스트레치)이 적은 버텍스에서 훨씬 빠르게 작동한다. 셋째, 초보자의 모델링 실수가 스타일리시한 선택으로 보인다. 넷째, Kenney·Quaternius 등 무료 에셋과 시각적 통일성을 유지하기 쉽다.

---

## 2. 스쿼시/스트레치 애니메이션의 세 가지 구현법

슬라임 캐릭터의 생동감은 **스쿼시(찌그러짐)/스트레치(늘어남)** 효과에서 나온다. 구현 방법은 세 가지이며, 물리 플랫포머에는 **코드 기반** 또는 **DOTween 기반**이 가장 적합하다.

### 구체적인 스케일 값 레퍼런스

모든 값은 **부피 보존 원칙**(X × Y × Z ≈ 1.0)을 따른다.

| 효과 | X | Y | Z | 변형 시간 | 복원 시간 |
|------|---|---|---|-----------|-----------|
| **착지 스쿼시 (약)** | 1.15 | 0.85 | 1.15 | 0.06s | 0.15s |
| **착지 스쿼시 (중)** | **1.3** | **0.7** | **1.3** | 0.08s | 0.2s |
| **착지 스쿼시 (강)** | 1.5 | 0.5 | 1.5 | 0.1s | 0.3s |
| **점프 스트레치** | **0.8** | **1.25** | **0.8** | 0.06s | 0.15s |
| **발사 스트레치** | 0.6 | 1.4 | 0.6 | 0.05s | 0.25s |
| **유휴 흔들림** | 1.05 | 0.95 | 1.05 | 0.8s | 0.8s (루프) |

**중요한 아키텍처 원칙:** 비주얼 Transform은 반드시 물리 바디의 **자식 오브젝트**로 분리해야 한다. 스케일 변경이 콜라이더에 영향을 주지 않도록 하기 위함이다.

```
SlimeCharacter (Rigidbody + Collider)  ← 물리 처리, 스케일 변경 안 함
  └── SlimeVisual (MeshRenderer)       ← 스쿼시/스트레치 적용 대상
```

### 방법 A: Animator + Animation Clip

Unity Animation 창(`Window → Animation`)에서 직접 키프레임을 찍는 방식이다. 디자이너가 시각적으로 커브를 조절할 수 있어 고정된 연출에 적합하다.

**착지 스쿼시 클립 예시 (60fps 기준):**
- Frame 0: Scale (1, 1, 1)
- Frame 3: Scale (1.3, 0.7, 1.3) — 최대 찌그러짐
- Frame 12: Scale (1, 1, 1) — 원래 복원

Animator Controller에 `Idle`, `JumpStretch`, `LandSquash` 상태를 만들고, Trigger 파라미터 `"Jump"`과 `"Land"`로 전환한다. 코드에서 `animator.SetTrigger("Land")`로 호출한다.

### 방법 B: 코루틴 기반 런타임 스케일 조작

물리 이벤트에 **동적으로 반응**하는 방식으로, 낙하 속도에 비례한 강도 조절이 가능하다.

```csharp
using System.Collections;
using UnityEngine;

public class SlimeSquashStretch : MonoBehaviour
{
    [Header("비주얼 설정")]
    public Transform visualTransform;        // 자식 비주얼 오브젝트

    [Header("착지 스쿼시")]
    public Vector3 landSquashScale = new Vector3(1.3f, 0.7f, 1.3f);
    public float landSquashDuration = 0.15f;

    [Header("점프 스트레치")]
    public Vector3 jumpStretchScale = new Vector3(0.8f, 1.25f, 0.8f);
    public float jumpStretchDuration = 0.12f;

    [Header("발사 스트레치")]
    public Vector3 launchStretchScale = new Vector3(0.6f, 1.4f, 0.6f);
    public float launchStretchDuration = 0.1f;

    [Header("물리 감지")]
    public LayerMask groundLayer;
    public float maxImpactVelocity = 15f;

    private Rigidbody rb;
    private Vector3 originalScale;
    private Coroutine currentCoroutine;
    private bool isGrounded, wasGrounded;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (visualTransform == null) visualTransform = transform;
        originalScale = visualTransform.localScale;
    }

    void Update()
    {
        wasGrounded = isGrounded;
        isGrounded = Physics.Raycast(
            transform.position, Vector3.down, 0.15f, groundLayer);

        // 착지 감지
        if (!wasGrounded && isGrounded) OnLanded();

        // 유휴 흔들림
        if (currentCoroutine == null && isGrounded)
        {
            float wobble = Mathf.Sin(Time.time * 2f) * 0.05f;
            visualTransform.localScale = new Vector3(
                originalScale.x + wobble,
                originalScale.y - wobble,
                originalScale.z + wobble);
        }
    }

    void OnLanded()
    {
        // 낙하 속도에 비례하여 강도 계산
        float impact = Mathf.Abs(rb.linearVelocity.y);
        float intensity = Mathf.Clamp01(impact / maxImpactVelocity);
        if (intensity < 0.1f) return;

        Vector3 target = Vector3.Lerp(originalScale, landSquashScale, intensity);
        PlayEffect(target, landSquashDuration);
    }

    public void OnJump() => PlayEffect(jumpStretchScale, jumpStretchDuration);
    public void OnLaunch() => PlayEffect(launchStretchScale, launchStretchDuration);

    void PlayEffect(Vector3 targetScale, float duration)
    {
        if (currentCoroutine != null) StopCoroutine(currentCoroutine);
        visualTransform.localScale = originalScale;
        currentCoroutine = StartCoroutine(SquashStretchRoutine(targetScale, duration));
    }

    IEnumerator SquashStretchRoutine(Vector3 target, float duration)
    {
        float t = 0f;
        // 1단계: 빠르게 변형 (전체 시간의 35%)
        while (t < 1f)
        {
            t += Time.deltaTime / (duration * 0.35f);
            float eased = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
            visualTransform.localScale = Vector3.Lerp(originalScale, target, eased);
            yield return null;
        }
        // 2단계: 탄성 복원 (전체 시간의 65%)
        t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / (duration * 0.65f);
            float elastic = EaseOutElastic(Mathf.Clamp01(t));
            visualTransform.localScale = Vector3.Lerp(target, originalScale, elastic);
            yield return null;
        }
        visualTransform.localScale = originalScale;
        currentCoroutine = null;
    }

    float EaseOutElastic(float t)
    {
        if (t == 0f || t == 1f) return t;
        float p = 0.3f;
        return Mathf.Pow(2f, -10f * t) *
               Mathf.Sin((t - p / 4f) * (2f * Mathf.PI) / p) + 1f;
    }
}
```

PlayerController에서 호출:
```csharp
void Jump()
{
    rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
    GetComponent<SlimeSquashStretch>().OnJump();
}
```

### 방법 C: DOTween으로 간결하게 구현

DOTween은 `transform.DOScale()`로 한 줄 트위닝이 가능해 코드가 **절반 이하**로 줄어든다.

**설치:** Asset Store에서 "DOTween (HOTween v2)" 무료 임포트 → `Tools → Demigiant → DOTween Utility Panel` → "Setup DOTween..." 클릭

```csharp
using UnityEngine;
using DG.Tweening;

public class SlimeSquashDOTween : MonoBehaviour
{
    public Transform visualTransform;
    private Vector3 originalScale;
    private Tween currentTween;

    void Start()
    {
        originalScale = visualTransform.localScale;
    }

    // 착지 스쿼시 — Ease.OutElastic으로 젤리 복원
    public void OnLand(float impactIntensity)
    {
        currentTween?.Kill();
        Vector3 squash = Vector3.Lerp(
            originalScale, new Vector3(1.3f, 0.7f, 1.3f), impactIntensity);

        Sequence seq = DOTween.Sequence();
        seq.Append(visualTransform.DOScale(squash, 0.08f).SetEase(Ease.OutQuad));
        seq.Append(visualTransform.DOScale(originalScale, 0.25f).SetEase(Ease.OutElastic));
        currentTween = seq;
    }

    // 점프 스트레치 — Ease.OutBack으로 약간 오버슈트
    public void OnJump()
    {
        currentTween?.Kill();
        Sequence seq = DOTween.Sequence();
        seq.Append(visualTransform.DOScale(
            new Vector3(0.8f, 1.25f, 0.8f), 0.08f).SetEase(Ease.OutQuad));
        seq.Append(visualTransform.DOScale(originalScale, 0.2f).SetEase(Ease.OutBack));
        currentTween = seq;
    }

    // 발사 — Ease.OutBounce로 바운스 정착
    public void OnLaunch()
    {
        currentTween?.Kill();
        Sequence seq = DOTween.Sequence();
        seq.Append(visualTransform.DOScale(
            new Vector3(0.6f, 1.4f, 0.6f), 0.06f).SetEase(Ease.OutExpo));
        seq.Append(visualTransform.DOScale(originalScale, 0.3f).SetEase(Ease.OutBounce));
        currentTween = seq;
    }

    // 간편 한 줄 스쿼시 (프로토타이핑용)
    public void QuickSquash()
    {
        currentTween?.Kill();
        currentTween = visualTransform.DOPunchScale(
            new Vector3(0.3f, -0.3f, 0.3f), 0.3f, vibrato: 4, elasticity: 0.5f);
    }

    void OnDestroy() => currentTween?.Kill();
}
```

**DOTween 이징 함수 추천:**

| 상황 | 변형 Ease | 복원 Ease | 느낌 |
|------|-----------|-----------|------|
| 착지 | `OutQuad` | **`OutElastic`** | 젤리같은 탄성 복원 |
| 점프 | `OutQuad` | `OutBack` | 살짝 오버슈트 |
| 발사 | `OutExpo` | `OutBounce` | 극적 시작 + 바운스 |
| 피격 | `OutExpo` | `OutElastic` | 강한 충격감 |

---

## 3. URP 툰 셰이더를 적용하는 세 가지 경로

URP는 기본적으로 PBR(물리 기반 렌더링)을 사용하므로 **내장 툰 셰이더가 없다**. 카툰 느낌을 내려면 외부 에셋, Shader Graph, 또는 Unity 공식 Toon Shader 패키지를 활용해야 한다.

### 경로 1: 무료 에셋 사용 (가장 빠름)

| 에셋 | 출처 | 특징 |
|------|------|------|
| **(URP) Simple Toon Shader** | Asset Store (무료) | 즉시 적용 가능, **814+ 즐겨찾기**, URP 전용 |
| **DELTation URP Toon Shader** | GitHub (MIT) | 2~3단 램프, 노말맵, 림 라이팅, 아웃라인 지원 |
| **NiloToonURP (Tutorial)** | GitHub (MIT) | 학습용으로 최적, 간결한 코드 |
| **Cyanilux Custom Lighting** | GitHub (무료) | Shader Graph용 서브그래프, 램프 텍스처 지원 |

Package Manager Git URL로 설치 가능:
```
https://github.com/Delt06/urp-toon-shader.git?path=Packages/com.deltation.toon-shader
```

### 경로 2: Unity 공식 Toon Shader 패키지

**com.unity.toonshader** (Unity-Chan Toon Shader 3)를 Package Manager에서 설치하면 **3단 컬러 밴딩**(Base Color, 1st Shade, 2nd Shade), 림 라이팅, MatCap을 인스펙터에서 바로 제어할 수 있다. 프리뷰 상태이지만 셰이더 코딩 없이 즉시 툰 룩을 구현할 수 있는 공식 솔루션이다.

### 경로 3: Shader Graph로 직접 만들기

Project 우클릭 → Create → Shader Graph → URP → **Unlit Shader Graph**로 시작한다. Lit이 아닌 **Unlit**을 선택해야 PBR 라이팅이 자동 적용되지 않는다.

**핵심 노드 연결 순서:**

1. **Main Light Direction** 노드 (Unity 2022.2+에서 기본 제공) → **Negate**
2. **Normal Vector** (World) → **Dot Product** ← 라이트 방향 = **NdotL** 값 산출
3. NdotL을 `* 0.5 + 0.5`로 **0~1 범위**로 리맵
4. **Step** 노드 (Edge = **0.5**) 적용 → 0 또는 1의 하드 셀 셰이딩 경계 생성
5. **Lerp** 노드: A = Shadow Color 프로퍼티, B = Base Color × Base Texture, T = Step 결과
6. **Fresnel Effect** (Power: **3~4**) → Step(Edge: 0.5) → × Rim Color → **Add**로 림 라이팅 추가
7. 최종 결과를 Fragment의 **Base Color**에 연결

Step 대신 `Smoothstep(0, 0.01, NdotL)`을 사용하면 앨리어싱 없이 약간 부드러운 경계를 만들 수 있다. 다단계 셰이딩이 필요하면 **Gradient** 노드 + **Sample Gradient**를 사용한다.

---

## 4. Cinemachine으로 2.5D 카메라를 완성하는 방법

### 기본 설정

`Window → Package Manager → Unity Registry → Cinemachine` 설치 후, `GameObject → Cinemachine → Cinemachine Camera`로 가상 카메라를 생성한다. Main Camera에 **CinemachineBrain**이 자동 추가된다.

**2.5D 플랫포머 필수 설정:**
- **Follow**: 플레이어 오브젝트 연결
- **Body**: **Framing Transposer** 선택 (2.5D의 핵심)
- **Aim**: **Do Nothing** (카메라 회전 방지)

### 권장 Framing Transposer 수치

| 설정 | 권장값 | 설명 |
|------|--------|------|
| **Camera Distance** | **13** | Z축 플레이어 거리 |
| **Tracked Object Offset Y** | 1~2 | 플레이어 중심보다 약간 위 |
| **X Damping** | 0.5~1 | 수평 추적 부드러움 |
| **Y Damping** | **2** | 수직 추적 (높을수록 둥둥) |
| **Screen Y** | 0.4 | 플레이어를 화면 중앙 약간 아래 배치 |
| **Dead Zone** | 0.1~0.2 | 카메라 미반응 영역 |
| **Soft Zone** | 0.4~0.6 | 댐핑 적용 추적 영역 |

**Dead Zone**은 플레이어가 자유롭게 움직여도 카메라가 따라가지 않는 영역이고, **Soft Zone**은 댐핑을 적용하며 부드럽게 따라가는 영역이다. Hard Limit 밖으로는 플레이어가 절대 나갈 수 없다.

### Confiner로 카메라 경계 제한

1. 빈 GameObject 생성 → **Polygon Collider 2D** 추가 → **Is Trigger = true**
2. 콜라이더 포인트를 레벨 경계에 맞게 편집
3. 별도 Layer("Confiner") 생성 후 Physics 2D에서 충돌 비활성화
4. Virtual Camera → Extensions → **Add Extension → CinemachineConfiner2D**
5. **Bounding Shape 2D**에 경계 오브젝트 연결

### Impulse로 카메라 흔들림 추가

**설정 순서:**
1. Virtual Camera에 **CinemachineImpulseListener** 확장 추가 (Gain = 1)
2. 플레이어에 **CinemachineImpulseSource** 컴포넌트 추가 (Raw Signal: **6D Shake**)

**이벤트별 권장 설정:**

| 이벤트 | Amplitude | Sustain | Decay |
|--------|-----------|---------|-------|
| 점프 | 0.1~0.2 | 0.02s | 0.1s |
| 일반 착지 | **0.2~0.4** | 0.05s | 0.15s |
| 강한 착지 | 0.5~1.0 | 0.1s | 0.3s |
| 대시/발사 | 0.15~0.3 | 0.03s | 0.1s |

```csharp
using UnityEngine;
using Cinemachine; // CM 3.x에서는 Unity.Cinemachine

public class PlayerCameraEffects : MonoBehaviour
{
    [SerializeField] private CinemachineImpulseSource jumpImpulse;
    [SerializeField] private CinemachineImpulseSource landImpulse;
    [SerializeField] private float hardLandThreshold = 15f;
    private float lastYVelocity;
    private Rigidbody rb;

    void Start() => rb = GetComponent<Rigidbody>();
    void Update() => lastYVelocity = rb.velocity.y;

    public void OnJump()
    {
        jumpImpulse.GenerateImpulse(Vector3.down * 0.2f);
    }

    public void OnLand()
    {
        float fallSpeed = Mathf.Abs(lastYVelocity);
        float force = fallSpeed > hardLandThreshold ? fallSpeed * 0.05f : 0.3f;
        landImpulse.GenerateImpulse(Vector3.down * force);
    }
}
```

---

## 5. Blender에서 Unity까지 에셋 임포트 워크플로

### FBX 익스포트 설정 (실수 방지 체크리스트)

Blender와 Unity의 좌표계가 다르다(Blender: Z-Up, Unity: Y-Up). **잘못된 설정은 모델이 100배 커지거나 90° 회전되는 원인**이 된다.

**Blender 내 사전 작업:**
1. `Ctrl+A` → **All Transforms** 적용 (Rotation: 0, Scale: 1 확인)
2. Edit Mode → 전체 선택(`A`) → Mesh → Merge → **By Distance** (중복 버텍스 제거)

**FBX Export 대화상자 (`File → Export → FBX`):**

| 설정 항목 | 권장값 | 이유 |
|-----------|--------|------|
| Scale | **1.00** | 실제 크기 유지 |
| Apply Scalings | **FBX Units Scale** | 100배 스케일 문제 방지 |
| Forward | **-Z Forward** | Unity 좌표계 일치 |
| Up | **Y Up** | Unity 좌표계 일치 |
| Apply Transform | **☑ 체크** | 회전 오류 방지 |
| Object Types | **Mesh** (+ Armature) | 불필요한 카메라/라이트 제외 |
| Path Mode | **Copy** + 폴더 아이콘 클릭 | 텍스처 임베드 |

이 설정을 **프리셋으로 저장**(`+` 버튼)하면 매번 재설정할 필요가 없다.

**Unity Import Inspector:**
- **Model 탭**: Scale Factor 1, Convert Units ☑, Import Normals: Import
- **Rig 탭**: 뼈대 없는 슬라임은 Animation Type → **None**
- **Materials 탭**: Extract Textures/Materials 버튼으로 별도 추출 가능

`.blend` 파일을 직접 드래그해도 Unity가 자동 변환하지만, FBX가 설정 제어와 이식성 면에서 더 안정적이다.

### URP 머티리얼 설정

새 프로젝트는 **3D (URP) 템플릿**으로 생성하는 것이 가장 간편하다. 기존 프로젝트라면 `Edit → Render Pipeline → Universal Render Pipeline → Upgrade Project Materials to URP Materials`로 일괄 변환한다.

슬라임 카툰 룩 머티리얼 설정:
- Shader: `Universal Render Pipeline/Lit`
- **Smoothness: 0.85~0.95** (광택 있는 젤리 느낌)
- **Metallic: 0** (비금속)
- Base Map Color: 밝고 채도 높은 색상 (예: 선명한 초록)
- Specular Highlights: **활성화**

### Prefab 구성 권장 구조

```
SlimeCharacter (Prefab) ─ Rigidbody + Capsule Collider + PlayerController
├── SlimeVisual ─────── MeshRenderer + 머티리얼 (스쿼시/스트레치 대상)
├── GroundCheck ─────── 빈 오브젝트 (발밑에 위치, 지면 감지용)
├── SlimeVFX ────────── Particle System (슬라임 궤적)
└── CameraTarget ────── 빈 오브젝트 (Cinemachine Follow 대상)
```

**Rigidbody 권장 설정:** Mass: 1, Use Gravity: ☑, **Freeze Rotation X, Z: ☑** (2.5D에서 넘어짐 방지), Freeze Position Z: ☑ (Z축 이동 제한). 콜라이더는 **Capsule Collider** 또는 **Sphere Collider**를 사용하고, 동적 Rigidbody에 Mesh Collider는 피한다.

Prefab Variant(`우클릭 → Create → Prefab Variant`)를 활용하면 기본 슬라임에서 색상·속도만 다른 "불 슬라임", "얼음 슬라임" 등을 효율적으로 관리할 수 있다.

---

## 6. 9~12주 학습 로드맵과 무료 리소스

### 단계별 학습 순서

| 단계 | 기간 | 목표 | 핵심 리소스 |
|------|------|------|-------------|
| **Phase 1: Unity 기초** | 1~2주 | 씬 구성, GameObject, C# 기본 | Unity Learn "Essentials" Pathway, Roll-a-Ball |
| **Phase 2: 2.5D 물리 이동** | 1~2주 | Rigidbody 이동, 점프, 지면 감지 | Brackeys 플랫포머 시리즈 (레거시) |
| **Phase 3: Blender 모델링** | 2~3주 | 슬라임 모델 + UV 언래핑 | Blender Guru 도넛 튜토리얼, Imphenzia |
| **Phase 4: 임포트 + 머티리얼** | 1주 | FBX 임포트, URP 머티리얼, Prefab | 본 보고서 Section 5 참조 |
| **Phase 5: 애니메이션** | 2주 | 스쿼시/스트레치, DOTween, 파티클 | Blackthornprod 게임 주스 영상 |
| **Phase 6: 카메라 + 폴리시** | 1~2주 | Cinemachine, 포스트프로세싱, 사운드 | Cinemachine 공식 문서 + 샘플 씬 |

### 유튜브 핵심 채널

**영어 채널:** **Code Monkey**가 현재 가장 활발하며, "Kitchen Chaos" 무료 코스(10시간+)가 초보자에게 최적이다. **Brackeys**는 2024년 복귀했으나 Godot로 전환했고, Unity 레거시 영상(460개+)은 기초 개념 학습에 여전히 유효하다. **Sebastian Lague**의 "Introduction to Game Development" 플레이리스트는 알고리즘적 사고에 탁월하다. **Blackthornprod**는 스쿼시/스트레치, 화면 흔들림 등 **게임 주스(Game Feel)** 구현에 특화되어 있다. **Imphenzia**는 로우폴리 모델링을 Unity 게임개발 관점에서 가르친다.

**한국어 채널:** **골드메탈**이 한국 Unity 튜토리얼 채널 중 가장 인기 있으며 기초부터 프로젝트까지 다룬다. **유니티코리아 공식** 채널은 최신 기술 소식과 중급 이상 콘텐츠를 제공한다. **레트로**, **조연2 게임 작업실**도 한국어 Unity 학습에 추천된다. **유니티스퀘어**(unitysquare.co.kr)는 Unity Korea 공식 한국어 리소스 허브다.

### 무료 학습 플랫폼 요약

- **Unity Learn** (learn.unity.com): Essentials → Junior Programmer → Creative Core 순서로 진행
- **Catlike Coding** (catlikecoding.com): 텍스트 기반 심화 튜토리얼 (셰이더, 절차적 메시 등)
- **DOTween 공식 문서** (dotween.demigiant.com/documentation.php): DOScale, DOPunchScale, Sequence API
- **Cinemachine 공식 문서** (docs.unity3d.com/Packages/com.unity.cinemachine): 샘플 씬 포함
- **Udemy 무료 강의**: "Building Unity 2D Platformer" (기초 + C#)
- **Code Monkey 무료 코스** (unitycodemonkey.com/freecourses.php): Kitchen Chaos 등 다수

---

## 결론: 파이프라인을 관통하는 핵심 원칙

이 전체 파이프라인에서 반복적으로 등장하는 원칙이 세 가지 있다. 첫째, **물리와 비주얼의 분리** — Rigidbody/Collider는 부모에, 스케일 변형은 자식 비주얼에 적용해야 물리 시뮬레이션이 깨지지 않는다. 둘째, **부피 보존** — 스쿼시 시 X·Z를 키우면 Y를 줄여 X × Y × Z ≈ 1.0을 유지해야 질량감이 느껴진다. 셋째, **속도 비례 반응** — 낙하 속도, 발사 힘에 비례하여 변형 강도와 카메라 흔들림을 스케일링하면 물리적 일관성이 생긴다.

로우폴리 슬라임 하나를 Blender에서 만들어 Unity에 넣고, DOTween으로 스쿼시/스트레치를 붙이고, 무료 툰 셰이더로 렌더링하고, Cinemachine으로 카메라를 잡는 이 네 단계를 순서대로 밟으면, 에셋 없이 시작해도 **9~12주 안에 완성도 있는 2.5D 슬라임 플랫포머 프로토타입**을 만들 수 있다.