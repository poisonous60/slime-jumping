# Unity 2D 플랫포머를 위한 천 시뮬레이션 완전 가이드

**커스텀 Verlet 시뮬레이션이 가장 유력한 선택지다.** Unity의 2D 물리(Box2D)와 3D 물리(PhysX)는 완전히 분리된 시스템이어서, 2D 슬라임 캐릭터와 3D 천의 직접 상호작용은 어떤 내장 기능으로도 불가능하다. 이 간극을 메울 수 있는 방식은 크게 네 가지인데, 각각 "물리 상호작용 가능 여부"와 "구현 비용"의 트레이드오프가 크게 다르다. 배경 전용 연출에는 셰이더 기반 가짜 천이 압도적으로 효율적이고, 슬라임과의 충돌 상호작용이 필요한 천에는 Burst로 최적화한 Verlet 질점-스프링 시스템이 가장 실용적이며, 개발 시간을 단축하려면 Obi Cloth가 유일하게 2D 콜라이더를 네이티브 지원하는 서드파티 솔루션이다.

---

## 네 가지 주요 방식 한눈에 비교

아래 표는 각 방식의 핵심 특성을 요약한다. "2D 콜라이더 상호작용"은 이 프로젝트의 가장 결정적 요건이다.

| 방식 | 2D 콜라이더 상호작용 | 성능 (PC) | 구현 난이도 | 바람/배경 전환 | 비용 |
|------|---------------------|-----------|------------|---------------|------|
| Unity 내장 Cloth | ❌ 불가 (3D 전용) | 양호 | 낮음 | 제한적 | 무료 |
| Verlet 질점-스프링 (직접 구현) | ✅ 완전 지원 | 양호→우수 (Burst 시) | 높음 | 자유로움 | 무료 |
| 셰이더 Vertex Displacement | ❌ 불가 (순수 시각) | 최고 | 낮음 | 최적 | 무료 |
| Obi Cloth (서드파티) | ✅ 대부분 지원 | 우수 | 낮음 | 양호 | $47 |
| Magica Cloth 2 (서드파티) | ⚠️ 자체 콜라이더만 | 우수 | 낮음 | 양호 | $35 |

---

## Unity 내장 Cloth 컴포넌트의 구조적 한계

Unity의 `Cloth` 컴포넌트는 **NVIDIA PhysX** 위에 구축된 3D 전용 시스템이다. `SkinnedMeshRenderer`가 반드시 필요하며, 일반 `MeshRenderer`에 붙이면 자동으로 교체된다. 충돌 감지는 **`SphereCollider`와 `CapsuleCollider`만** 지원하고, `BoxCollider`나 `MeshCollider`조차 사용할 수 없다. 가장 치명적인 문제는 **`Collider2D`, `Rigidbody2D`와 일체 상호작용이 불가능**하다는 점이다.

`Cloth.capsuleColliders` 배열과 `Cloth.sphereColliders`(`ClothSphereColliderPair[]`) 배열에 명시적으로 등록한 콜라이더만 충돌에 참여한다. 또한 **단방향 시뮬레이션**이어서 천이 콜라이더에 반응하지만, 반대로 천이 콜라이더에 힘을 가하지는 않는다. 바람 처리도 `WindZone`은 작동하지 않으며, `Cloth.externalAcceleration`(일정 방향 바람)과 `Cloth.randomAcceleration`(난류)을 `Vector3`로 직접 설정해야 한다.

정점별 구속 조건은 `ClothSkinningCoefficient`의 `maxDistance`(스킨드 메시 위치로부터 최대 이탈 거리, **0이면 고정**)와 `collisionSphereDistance`(표면 침투 방지 구 반경)로 제어한다. 에디터의 Paint/Gradient 모드로 정점별 값을 칠할 수 있다.

**2D 게임에서 사용하려면** 슬라임의 `CircleCollider2D`를 미러링하는 `SphereCollider` 프록시 오브젝트를 매 프레임 동기화해야 하는데, 이는 유지보수 부담이 크고 프레임 타이밍 불일치로 인한 떨림이 발생할 수 있다. 따라서 이 프로젝트에는 **비추천**이다.

---

## Verlet 질점-스프링 시스템이 핵심 솔루션인 이유

커스텀 Verlet 시뮬레이션은 **2D 물리와 3D 렌더링의 간극을 가장 우아하게 해결**한다. 천을 N×M 격자의 질점(particle) 배열로 모델링하고, 질점 사이를 세 종류의 스프링으로 연결한다: **구조 스프링**(인접 격자), **전단 스프링**(대각선), **굽힘 스프링**(한 칸 건너). Verlet 적분은 속도를 명시적으로 저장하지 않고 현재 위치와 이전 위치의 차이로 속도를 내재적으로 유도하므로, 위치 보정(충돌 해소 등)이 자동으로 다음 프레임 속도에 반영되어 **안정성이 Euler보다 훨씬 높다**.

핵심 공식은 단 한 줄이다:

```
새_위치 = 2 × 현재_위치 - 이전_위치 + 가속도 × Δt²
```

거리 구속 조건(distance constraint)은 반복적 이완(iterative relaxation)으로 해결한다. 프레임당 **5~30회 반복**이 일반적이며, 반복 횟수가 높을수록 천이 뻣뻣해지지만 CPU 비용이 증가한다.

**2D 콜라이더와의 충돌은 `Physics2D` API로 직접 구현한다.** 각 천 질점의 XY 좌표를 `Physics2D.OverlapCircle(particlePos2D, radius, layerMask)`에 넘기면 슬라임의 `CircleCollider2D`든 `PolygonCollider2D`든 형태에 무관하게 감지된다. 침투 시 `Collider2D.ClosestPoint()`로 최단 탈출 벡터를 구해 질점을 밀어낸다. Z축은 렌더링에만 사용하고 충돌 판정은 XY 평면에서만 수행하므로, 2D 게임플레이와 3D 시각 품질을 동시에 확보할 수 있다.

렌더링에는 `Mesh` 클래스의 `vertices`, `triangles`, `uv`를 매 프레임 갱신하고, `MeshFilter` + `MeshRenderer`로 3D 메시를 표시한다. `mesh.MarkDynamic()` 호출은 필수 최적화다. 법선은 `mesh.RecalculateNormals()`로 재계산한다.

### 성능 프로파일

순수 C# `MonoBehaviour`로 구현하면 PC에서 **200~500 질점** 수준이 60fps의 한계선이다. 여기에 **Unity Job System + Burst Compiler**를 적용하면 **5~10배 가속**이 가능하다. 위치 갱신 단계는 `IJobParallelFor`로 병렬화할 수 있고, 구속 조건 해소는 데이터 의존성 때문에 `IJob`(단일 스레드)이지만 Burst 컴파일만으로도 극적인 개선이 있다. Verlet 로프 구현 사례에서 40노드 기준 **3.7ms → 0.8ms**로 약 4.5배 향상이 보고되었다. Burst 적용 시 **1,000~2,000+ 질점**이 PC에서 충분히 실시간 가능하며, 커튼·깃발 하나에 10×20=200 질점이면 상당히 사실적인 결과가 나온다.

GPU 컴퓨트 셰이더(`ComputeShader` + `ComputeBuffer`)로 전환하면 **10,000+ 질점**까지 처리할 수 있으나, 구현 복잡도가 크게 증가하고 이 프로젝트 규모에서는 과잉이다.

### 바람 및 배경 모드 전환

Verlet 시스템에서 바람은 단순히 모든 질점에 `Vector3` 외력을 가하면 된다. Perlin 노이즈로 시간·위치에 따른 변화를 주면 자연스러운 바람이 된다. **배경 전용 모드**로 전환할 때는 충돌 검사 로직만 비활성화하면 성능이 추가 확보된다. 고정 질점(상단 행 pinning)의 패턴을 바꿔 깃발(좌측 열 고정), 커튼(상단 행 고정), 넝마(상단 중앙 한 점 고정) 등 다양한 형태를 하나의 시스템으로 표현할 수 있다.

---

## 셰이더 기반 가짜 천은 배경 연출의 최적해

`Vertex Displacement Shader`는 정점 셰이더 단계에서 사인파·노이즈 함수로 정점 위치를 변형하여 **CPU 비용 제로**로 펄럭이는 천 효과를 만든다. Shader Graph에서는 Master Stack의 Vertex Position 출력에 Time → Sine/Gradient Noise → Multiply(진폭) → Multiply(버텍스 컬러 마스크) 노드를 연결하면 **10~20개 노드로 완성**된다.

버텍스 컬러의 R 채널을 마스크로 사용하면 고정부(빨강=0)와 자유부(빨강=1)를 구분할 수 있다. DCC 툴에서 칠하거나 오브젝트 스페이스 Y 좌표 기반으로 자동 그라디언트를 적용하는 방법 모두 유효하다.

**물리 상호작용은 원칙적으로 불가능**하다. GPU에서 변형된 정점 위치를 CPU가 알 수 없기 때문이다. 다만 동일한 노이즈 함수를 CPU 측에서 샘플링하여 근사 위치를 계산하는 우회법이 있으나, 이는 셰이더 전용 방식의 장점을 상쇄한다.

**이 방식은 슬라임과 상호작용하지 않는 먼 배경의 깃발·커튼·넝마에 적합하다.** 바람에 펄럭이는 깃발 10개, 배경 커튼 5개 등을 셰이더 하나로 커버할 수 있어 성능 예산을 거의 소모하지 않는다.

---

## 서드파티 솔루션 비교: Obi Cloth vs Magica Cloth 2

### Obi Cloth ($47, Virtual Method)

**XPBD**(Extended Position-Based Dynamics) 기반의 범용 파티클 물리 엔진이다. CPU(Burst + Jobs)와 GPU(컴퓨트 셰이더) 이중 실행을 지원한다. 이 프로젝트에서 가장 중요한 특징은 **`ObiCollider2D` 컴포넌트를 통해 대부분의 2D 콜라이더(`CircleCollider2D`, `BoxCollider2D`, `CapsuleCollider2D`, `EdgeCollider2D`)를 네이티브 지원**한다는 점이다. 단, `PolygonCollider2D`는 미지원이다.

`ObiRigidbody` 컴포넌트를 사용하면 **양방향 상호작용**(천이 리지드바디에 힘을 가하고, 리지드바디가 천을 민다)이 가능하다. 물리 기반 공기역학(양력 + 항력)과 `ObiSphericalForceZone`을 통한 방향성 바람도 내장되어 있다. 찢어짐(tear), 풍선/부피 구속, 셀프 콜리전 등 고급 기능도 제공한다.

### Magica Cloth 2 ($35, Magica Soft)

**Unity Job System + Burst Compiler** 기반으로 네이티브 플러그인 없이 동작한다. BoneCloth(트랜스폼 계층 기반, 머리카락·꼬리 등에 적합)와 MeshCloth(메시 정점 기반, 실제 천 표면) 두 가지 모드를 제공한다.

**2D 워크플로를 명시적으로 지원**하며 Unity 2D Animation Package와 호환된다. 다만 자체 콜라이더 시스템(`MagicaSphereCollider`, `MagicaCapsuleCollider`, `MagicaPlaneCollider`)을 사용하므로, Unity의 표준 2D 콜라이더와 직접 상호작용하지 않는다. 슬라임의 위치에 `MagicaSphereCollider`를 동기화하는 방식으로 우회할 수 있으나, Obi만큼 매끄럽지는 않다.

**두 에셋 모두 전체 C# 소스 코드가 포함**되어 있어(Obi의 핵심 "Oni" 라이브러리는 C++14, 별도 라이선스) 수정이 가능하다. 개발 시간 단축이 우선이라면 Obi Cloth가 2D 콜라이더 네이티브 지원 면에서 확실한 우위에 있다.

---

## 인디 게임들은 천 시뮬레이션을 어떻게 처리했나

대부분의 성공적인 2D 플랫포머는 **실시간 천 시뮬레이션을 사용하지 않는다**. 이 사실은 "천 시뮬레이션이 필수인가"를 재고하게 하는 중요한 맥락이다.

**Hollow Knight**(Unity)은 기사의 망토를 포함한 모든 애니메이션을 **포토샵에서 한 장씩 그린 프레임 애니메이션**으로 처리했다. 속편 Silksong도 호넷 캐릭터에 400프레임 이상의 수작업 스프라이트를 사용한다. **Cuphead**(Unity)은 **50,000장 이상의 손그림 프레임**(기네스 기록)을 24fps "on the ones"로 촬영한, 절대적인 수작업 방식이다. **Blasphemous**(Unity)도 참회자의 로브를 포함해 전부 픽셀아트 프레임 애니메이션이며, 개발진이 "3D 요소나 고급 셰이더를 의도적으로 피했다"고 밝혔다.

이와 대조적으로 **Rain World**(Unity)은 **프로시저럴 물리 기반 애니메이션의 교과서**다. 슬러그캣과 모든 생물의 신체 부위가 Verlet 스타일의 질점-스틱 시스템으로 실시간 계산된다. 촉수, 꼬리, 몸체가 관성·중력·접촉에 반응하며, 애니메이션 자체가 AI 행동과 결합되어 있다. GDC 2016 Animation Bootcamp 강연 "Rainworld Animation Process"에서 Joar Jakobsson이 이 시스템을 상세히 설명했다.

**Celeste**(커스텀 XNA 엔진)에서 매들린의 머리카락은 **체인-팔로우 시스템**이다. 원형 노드들이 머리 위치를 따라가며 `StepPerSegment` 값으로 지연을 조절한다. 바람 구간에서는 `StepPerSegment`를 바람 방향에 따라 사인 곡선으로 변형한다. 이 방식은 구현이 간단하면서도 자연스러운 후행 모션을 만들어낸다.

**Ori and the Blind Forest / Will of the Wisps**(Unity)은 환경의 흔들리는 식물·커튼·연기를 **파티클 시스템과 커스텀 셰이더**로 처리했다. 개발진은 Unity의 파티클 시스템과 유연한 렌더링 파이프라인을 핵심 도구로 꼽았다.

**Dead Cells**(커스텀 엔진)은 독특한 접근법으로, **3ds Max에서 3D 모델을 골격 애니메이션한 뒤 저해상도로 래스터화하여 픽셀아트 스프라이트로 변환**한다. 천·망토도 3D 파이프라인에서 처리되며, 프레임 타이밍 수정이 수 분 안에 가능한 장점이 있다.

---

## Unity 공식 문서 및 주요 레퍼런스 정리

Unity `Cloth` 컴포넌트의 공식 레퍼런스는 두 곳이다:

- **매뉴얼**: `docs.unity3d.com/Manual/class-Cloth.html` — 에디터 UI, 구속 조건 페인팅, 콜라이더 설정 설명
- **스크립팅 API**: `docs.unity3d.com/ScriptReference/Cloth.html` — `Cloth`, `ClothSphereColliderPair`, `ClothSkinningCoefficient` 등 전체 API

주요 API 타입은 `UnityEngine.Cloth`(메인 컴포넌트), `ClothSphereColliderPair`(구/원뿔형 캡슐 콜라이더 쌍), `ClothSkinningCoefficient`(정점별 `maxDistance` + `collisionSphereDistance`), `UnityEngine.ClothModule`(모듈 식별자)이다. Unity 6.3 LTS 기준으로 폐기 예정 없음이 확인되었다.

Unity 포럼에서 반복적으로 확인되는 핵심 사항은: Wind Zone이 Cloth에 작동하지 않는 점, `externalAcceleration`이 Unity 2019.2+에서 간헐적으로 동작하지 않는 버그, 그리고 Cloth가 `SphereCollider`/`CapsuleCollider` 이외의 콜라이더를 지원하지 않는다는 점이다.

오픈소스 참고 프로젝트로는 GitHub의 `EricHu33/Verlet-Integration-In-Unity`(Job System 활용 천+로프), `levifussell/SimpleUnityClothSimulator`(Jakobsen 2003 기반, 구/캡슐 충돌), `JUSTIVE/GPU-Cloth-Simulation`(GPU 컴퓨트 셰이더 질점-스프링), `mattatz/unity-verlet-simulator`(GPU Verlet) 등이 있다.

---

## 권장 접근법: 이중 레이어 전략

이 프로젝트의 요건(2D 플랫포머, 슬라임 2D 물리 충돌, 배경 모드 전환, 3D 렌더링, PC 전용)에 가장 적합한 전략은 **두 가지 방식의 조합**이다.

**상호작용 천(슬라임 충돌)에는 Burst 최적화 Verlet 질점-스프링 시스템을 직접 구현한다.** 10×20=200 질점 규모의 메시를 `FixedUpdate`에서 시뮬레이션하고, `Physics2D.OverlapCircle()`로 슬라임의 2D 콜라이더와 충돌을 감지한다. 이 방식은 Unity의 분리된 2D/3D 물리 시스템을 완전히 우회하면서, 3D `Mesh` + `MeshRenderer`로 시각적 품질을 확보한다. 상단 행 질점을 고정하면 커튼, 좌측 열을 고정하면 깃발, 한 점만 고정하면 넝마가 된다.

**비상호작용 배경 천(먼 배경의 깃발·커튼)에는 Vertex Displacement Shader를 사용한다.** CPU 비용 제로로 수십 개의 배경 요소를 동시에 펄럭이게 할 수 있다. Shader Graph에서 Time + Noise + 버텍스 컬러 마스크 조합으로 10분 안에 프로토타입이 가능하다.

**개발 시간이 부족하거나 구현 리스크를 줄이고 싶다면**, Obi Cloth($47)가 확실한 대안이다. `ObiCollider2D`로 슬라임의 `CircleCollider2D`를 직접 등록할 수 있어 브릿지 코드 없이 2D 충돌이 작동한다. 다만 `PolygonCollider2D` 미지원과 서드파티 의존성을 감수해야 한다.

실행 순서를 정리하면:

- **1단계**: Shader Graph로 배경 천 프로토타입을 빠르게 만들어 비주얼 톤을 확정한다
- **2단계**: 200 질점 규모의 Verlet 천을 순수 C# `MonoBehaviour`로 구현한다. `Physics2D.OverlapCircle` 충돌 검사까지 포함하여 슬라임과의 상호작용을 검증한다
- **3단계**: 성능이 부족하면 `IJobParallelFor`(위치 갱신)와 `[BurstCompile] IJob`(구속 해소)을 적용한다. PC 전용이므로 200 질점 규모에서는 Burst 없이도 충분할 가능성이 높다
- **4단계**: 바람은 Perlin 노이즈 기반 `Vector3` 외력으로 구현하고, 배경 모드 전환 시 충돌 로직만 `enabled = false` 처리한다

이 이중 레이어 전략은 **Rain World의 프로시저럴 물리 철학**(상호작용하는 요소)과 **Ori의 셰이더 기반 환경 연출**(배경 요소)을 결합한 것으로, 2D 플랫포머에서 3D 천이라는 독특한 요구사항에 가장 효율적으로 대응한다.

## 결론

Unity에서 2D 플랫포머용 3D 천 시뮬레이션은 "2D/3D 물리 분리"라는 엔진 구조적 제약이 모든 설계 결정의 출발점이다. 내장 `Cloth` 컴포넌트는 이 제약을 넘을 수 없고, 서드파티 중에서는 Obi Cloth만이 `ObiCollider2D`로 이 간극을 해소한다. 직접 구현한다면 Verlet 질점-스프링이 `Physics2D` API를 통해 가장 자연스럽게 2D 물리와 연동된다. 대부분의 인디 2D 플랫포머가 실시간 천 시뮬레이션 대신 프레임 애니메이션이나 프로시저럴 체인을 선택했다는 사실은, 천 시뮬레이션의 "재미 요소" 대비 구현 비용을 냉정히 평가해야 한다는 교훈을 준다. 그러나 슬라임이 천을 밀치는 상호작용이 게임플레이의 핵심 재미라면, Verlet + Shader의 이중 레이어가 가장 균형 잡힌 투자다.