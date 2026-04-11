# Unity URP 2.5D 플랫포머의 3D 물 표면 렌더링 비교

**하이브리드 방식(CPU 스프링 시뮬레이션 + GPU 셰이더 렌더링)이 이 프로젝트에 가장 적합하다.** 슬라임이 빈번하게 진입·이탈하는 게임 특성상 인터랙티브 리플 반응이 핵심인데, CPU 스프링은 물리적으로 자연스러운 파동을 만들고 GPU 셰이더는 투명도·굴절·폼 등 시각 효과를 처리할 수 있어 두 요구를 동시에 충족한다. Hollow Knight·Celeste 계열의 스타일라이즈드 2.5D에서는 사실적 해양 시뮬레이션이 아닌, **적절한 수준의 인터랙션과 분위기 있는 렌더링**이 중요하며, 실제로 Rain World와 Ori 시리즈가 유사한 하이브리드 접근을 사용한다.

---

## 네 가지 구현 방식의 핵심 특성

### A. 스프링 체인 수면 + 동적 Mesh 렌더링

이 방식은 수면을 **N개의 수직 스프링 포인트**로 분할하고, 각 스프링에 후크 법칙(Hooke's Law)과 감쇠(damping)를 적용한 뒤 인접 스프링 간 속도를 전파하는 고전적 기법이다. 2D/2.5D 인디 플랫포머에서 가장 널리 쓰이며, prime31 블로그, Ilham Effendi(Faerie Afterlight 개발자)의 Medium 튜토리얼, Envato Tuts+ 등에서 프로덕션 수준의 구현이 공개되어 있다.

**구현 핵심 패턴은** 각 스프링의 `velocity += tension * (baseHeight - currentHeight) - velocity * damping`으로 개별 업데이트하고, 별도 패스에서 `spread * (height[i] - height[i±1])`를 인접 스프링 속도에 가산하는 2단계 루프다. 슬라임 진입 시 `OnTriggerEnter2D`에서 충돌 좌표의 X범위에 해당하는 스프링들에 `Rigidbody2D.velocity.y * factor`를 velocity impulse로 분배한다.

메시 생성은 상단 N개(스프링 위치) + 하단 N개(수저 바닥) = **2N 정점**으로 구성된 strip을 만들고, 매 프레임 상단 정점의 Y값만 스프링 높이로 갱신한 뒤 `mesh.vertices = array`로 업로드한다. **일반적으로 월드 단위당 5개 스프링**(10유닛 풀 → 50스프링)이면 충분히 부드럽고, 100스프링까지도 CPU 부하는 **0.01ms 미만**으로 무시할 수 있다.

리플 품질은 물리 기반이므로 자연스러운 V형 파동 확산과 간섭 패턴이 자동으로 발생한다. 다만 셰이더 기반 시각 효과(투명도, 굴절, 깊이 기반 색상)를 적용하려면 별도 머티리얼 설정이 필요하며, **MeshRenderer에 URP 호환 머티리얼을 할당**하는 것만으로는 고급 수면 표현에 한계가 있다. 원형·다각형 형태는 1D 체인으로는 불가능하고, **N×M 2D 그리드**(8방향 이웃 전파)로 확장해야 한다.

---

### B. URP Shader Graph 버텍스 디스플레이스먼트 (GPU-only)

순수 GPU 방식은 세분화된 Quad/Plane 메시에 Shader Graph로 **사인파·Gerstner 파·노이즈 기반 버텍스 디스플레이스먼트**를 적용한다. Position(World) → Split(X,Z) → Add(Time) → Sine → Multiply(Amplitude) → Vertex Position으로 연결하는 노드 체인이 기본이며, Gerstner 파동은 수식이 복잡해 **Custom Function Node**에 HLSL로 작성하는 것이 일반적이다.

핵심 장점은 **CPU 시뮬레이션 부하가 전혀 없다**는 것이다. Daniel Ilett의 테스트에서 GTX 1070 기준 1024개 별도 워터 메시가 ~400fps로 구동되었다. 스타일라이즈드 2.5D 풀 크기(500~2000 정점)에서 GPU 비용은 완전히 무시할 수 있다.

**동적 리플 구현이 이 방식의 최대 약점이다.** 셰이더에 리플 정보를 전달하는 방법은 세 가지다:

- **Material.SetVector**: 충돌 좌표와 시작 시간을 Vector4로 전달, 셰이더에서 `distance(vertex, origin) - elapsed * speed`로 확장 링 계산. 단일 리플만 지원하며, 여러 리플은 배열이 필요해 Custom Function Node가 필수
- **RenderTexture 방식**: 직교 카메라로 수면을 내려다보며 리플 파티클을 별도 레이어에 렌더링 → 워터 셰이더가 이 텍스처를 디스플레이스먼트 소스로 샘플링. 무제한 동시 리플 가능하나 추가 카메라 + RT 메모리 비용 발생
- **MaterialPropertyBlock**: URP에서 **SRP Batcher와 비호환**이므로 성능 저하 발생, 단일 수체에서만 허용 가능

수학적 확장 링은 깔끔하고 대칭적인 리플을 만들어 스타일라이즈드 게임에는 오히려 잘 어울리지만, **스프링 시뮬레이션의 유기적 슬로싱·간섭 효과는 재현하지 못한다.** 비직사각형 메시에서는 월드 스페이스 좌표 기반 계산이므로 **동일 셰이더를 원형·다각형 메시에 그대로 적용 가능**하다는 강점이 있다.

URP 설정 시 주의점: Depth Texture와 Opaque Texture를 URP Pipeline Asset에서 활성화해야 깊이 기반 색상·폼·굴절이 작동하며, **Surface Type을 Opaque로 설정하되 Render Queue를 Transparent(3000)로 오버라이드**하는 트릭이 투명 정렬 문제를 피하는 핵심이다. URP Shader Graph에서 **테셀레이션은 지원되지 않으므로** 사전 세분화된 메시가 필수다.

---

### C. Unity 내장 Water System과 서드파티 에셋

Unity 내장 Water System은 **HDRP 전용**으로 URP에서는 사용할 수 없다. Boat Attack 워터 샘플은 2020 LTS 이후 사실상 방치 상태이며 Unity 6에서 깨지는 문제가 보고되고 있어 프로덕션 사용은 부적합하다.

서드파티 에셋 중 이 프로젝트와의 적합성을 평가하면:

- **Crest Ocean System** (~$100): FFT·Gerstner 파동, 리플 시뮬레이션, 코스틱스 등 풀스택 해양 시뮬레이션. 컴퓨트 셰이더 필수, PC/콘솔 타겟. 2.5D 플랫포머에는 **극단적 오버스펙**
- **Stylized Water 3** (Staggart, ~$49+Dynamic Effects $20 = ~$69): URP 전용, 스타일라이즈드 초점, Unity 6 지원(2026년 4월 기준 v3.2.6). Dynamic Effects 확장으로 **디컬 기반 수면 디스플레이스먼트**(파티클·트레일 렌더러로 리플 생성) 가능. 이 프로젝트에서 "사서 쓰는" 선택지 중 최적
- **Flat Kit** (~$40): 툰 셰이딩 풀 패키지에 워터 셰이더 포함. 스타일 적합성 우수하나 **동적 인터랙션 시스템 없음** — 리플은 별도 구현 필요
- **Water 2D Tool** (~$35): 2D/2.5D 전용, GPU 리플 시뮬레이션, `GenerateRippleAtPosition()` API 제공, 콜라이더 레이어 마스크 지원. **목적 적합도 최상**이나 3D 렌더링 품질은 SW3에 비해 제한적
- **AQUAS**: URP 미지원, 사실상 폐기

에셋 구매가 가능하다면 **Stylized Water 3 + Dynamic Effects**가 시각 품질과 인터랙션을 모두 해결하는 가장 빠른 경로다. 커스텀 구현을 원한다면 에셋 없이 하이브리드 방식을 직접 만드는 것이 더 나은 학습·제어 경험을 제공한다.

---

### D. 하이브리드: CPU 스프링 시뮬레이션 + GPU 셰이더 렌더링

하이브리드 방식은 CPU에서 스프링 물리를 계산하고 결과를 GPU 셰이더에 전달해 렌더링하는 패턴이다. **CPU는 인터랙션 물리를, GPU는 시각 효과를 각각 담당**하여 두 순수 방식의 장점을 결합한다.

**데이터 전달 방법 비교:**

| 전달 방식 | 구현 난이도 | URP 호환성 | 성능 | 추천도 |
|-----------|-----------|-----------|------|--------|
| **Texture2D** (SetPixelData + Apply) | 중 | ✅ 완벽 | 64~256 스프링: ~1KB 업로드, 무시 가능 | ⭐ **최추천** |
| **ComputeBuffer** (StructuredBuffer) | 상 | ✅ (SM 4.5+) | SetData()가 가장 효율적 | 스프링 수 많을 때 |
| **Material.SetFloatArray** | 하 | ✅ (CBUFFER 내) | 유니폼 배열 ~1024개 제한 | 소규모 OK |
| **MaterialPropertyBlock** | 하 | ⚠️ SRP Batcher 비호환 | 배칭 깨짐 | 비추천 |

**가장 실용적인 파이프라인은 Texture2D 방식이다.** `TextureFormat.RFloat`의 1D 텍스처(예: 128×1)를 생성하고, 매 프레임 `GetPixelData<float>(0)`으로 NativeArray에 직접 스프링 높이를 쓴 뒤 `Apply(false)`로 GPU에 업로드한다. 셰이더 측에서는 **Sample Texture 2D LOD 노드**(버텍스 스테이지에서는 LOD 명시 필수)로 높이맵을 읽어 Y 디스플레이스먼트에 적용한다.

Shader Graph만으로 구현이 가능하지만, ComputeBuffer/StructuredBuffer 접근은 **Custom Function Node에 HLSL을 작성**해야 한다. 텍스처 기반은 Shader Graph의 표준 노드만으로 완성할 수 있어 구현 난이도가 낮다.

프래그먼트 셰이더에서는 **Scene Depth로 깊이 기반 색상**(얕은/깊은 물 색상 보간), **깊이 차이 기반 교차선 폼**, **Scene Color 샘플링으로 굴절**, **노이즈/보로노이로 표면 디테일**을 추가한다. 이 모든 효과는 순수 CPU 메시 방식에서는 구현이 어렵거나 불가능하다.

---

## 방식별 장단점 비교표

| 평가 항목 | A. 스프링+Mesh | B. Shader GPU-only | C. 서드파티 (SW3) | D. 하이브리드 |
|-----------|--------------|-------------------|----------------|------------|
| **동적 리플 반응** | ⭐⭐⭐⭐⭐ 물리 기반, 자연스러운 간섭·슬로싱 | ⭐⭐⭐ 수학적 링, 깔끔하나 단조로움 | ⭐⭐⭐⭐ 디컬 기반, 파티클 연동 | ⭐⭐⭐⭐⭐ 스프링 물리 + 셰이더 효과 |
| **퍼포먼스** | ⭐⭐⭐⭐ CPU 미미, 메시 업로드 존재 | ⭐⭐⭐⭐⭐ GPU만 사용, 최소 비용 | ⭐⭐⭐ 에셋 오버헤드 존재 | ⭐⭐⭐⭐ 텍스처 업로드 미미 |
| **시각 품질** | ⭐⭐⭐ 기본 머티리얼만 가능, 고급 효과 제한 | ⭐⭐⭐⭐⭐ 투명도·굴절·폼 자유자재 | ⭐⭐⭐⭐⭐ 프로덕션 수준 | ⭐⭐⭐⭐⭐ 물리+시각 최상 |
| **형태 유연성** | ⭐⭐⭐ 직사각형 최적, 비정형은 2D 그리드 필요 | ⭐⭐⭐⭐⭐ 어떤 메시든 동일 셰이더 적용 | ⭐⭐⭐⭐ 커스텀 메시 지원 | ⭐⭐⭐⭐ 텍스처 매핑으로 비정형 대응 |
| **구현 난이도** | ⭐⭐⭐⭐ 낮음, 튜토리얼 풍부 | ⭐⭐⭐ 중간, Shader Graph 학습 필요 | ⭐⭐⭐⭐⭐ 구매 후 설정만 | ⭐⭐⭐ 중간~높음, 두 시스템 연결 |
| **유지보수성** | ⭐⭐⭐⭐ 단순한 코드 | ⭐⭐⭐⭐ 셰이더만 관리 | ⭐⭐⭐ 에셋 업데이트 의존 | ⭐⭐⭐⭐ 관심사 분리 명확 |

---

## 레퍼런스 게임들의 물 렌더링 방식

실제 출시된 2.5D 플랫포머 게임들의 물 구현은 이 프로젝트의 방향을 잡는 데 유용한 참고가 된다.

**Rain World**는 Unity 위에 Futile이라는 커스텀 2D 프레임워크를 쌓아 올린 독특한 구조에서, CPU 스프링 시뮬레이션 + 커스텀 셰이더(노이즈 텍스처 기반 왜곡·굴절)로 물을 렌더링한다. 깊이 버퍼 없이 레이어 기반으로 렌더링하면서도 인터랙티브한 수면 반응을 구현한 점이 인상적이며, 이 프로젝트의 하이브리드 방식과 가장 유사하다.

**Ori and the Blind Forest/Will of the Wisps**는 개발사 Moon Studios가 명시적으로 **"GPU simulated water"**라고 밝힌 방식을 사용한다. 3D 평면에 커스텀 셰이더를 적용하고, 캐릭터 진입 시 인터랙티브 표면 변형, 깊이 기반 흡수, 환경 반사·굴절을 구현했다. 이 프로젝트가 추구하는 "스타일라이즈드이면서 인터랙티브한 3D 물"의 최고 레퍼런스다.

**Hollow Knight**는 반투명 색상 오버레이 + 셰이더 기반 미세 UV 왜곡 + 파티클 이펙트(거품, 물방울)로 물을 표현하며, 복잡한 물리 시뮬레이션 없이 분위기 중심의 접근을 취한다. **A Short Hike**는 개발자가 직접 밝힌 대로 "플레이어를 따라다니는 평면 + 월드 스페이스 버텍스 애니메이션"이라는 가장 단순한 셰이더 방식을 사용한다. **Celeste**는 반투명 사각형 + 미세 애니메이션으로 픽셀아트 미학에 맞춘 미니멀 접근이고, **Cuphead**은 수작업 수채화 배경 + 프레임별 수작업 애니메이션으로 프로시저럴 물이 전혀 없다.

핵심 인사이트: **인터랙티브 리플이 있는 게임들(Rain World, Ori)은 모두 CPU/GPU 하이브리드 방식을 사용**하고, 순수 셰이더만 쓰는 게임들(Hollow Knight, A Short Hike)은 인터랙션 없이 분위기 중심이다. 슬라임의 빈번한 수중 진입·이탈이 게임플레이 핵심이라면 하이브리드가 올바른 선택이다.

---

## 추천 방식: 하이브리드(D) 구현 핵심 포인트

이 게임 컨텍스트에서 **하이브리드 방식(D)**을 추천하는 근거는 명확하다. 슬라임이 자주 물에 진입·이탈하므로 **시각적 리플 반응이 게임 느낌의 핵심**이며, 동시에 배경·지형과 동일한 3D 레이어에서 **투명도·깊이 색상·폼 등 셰이더 효과**가 필요하다. 순수 스프링+Mesh(A)는 셰이더 효과가 부족하고, 순수 GPU 셰이더(B)는 인터랙티브 리플이 제한적이다.

**구현 아키텍처 요약:**

```
[CPU] SpringSimulation.cs          [GPU] WaterSurface.shadergraph
  │                                       │
  ├─ FixedUpdate()                        ├─ Vertex Stage
  │   ├─ 각 스프링: Hooke + damping       │   ├─ Sample Texture 2D LOD (_HeightMap)
  │   ├─ 이웃 전파 (spread pass ×2~4)     │   ├─ height → Y displacement
  │   └─ 높이값 → NativeArray             │   └─ + 사인파 ambient wave 합성
  │                                       │
  ├─ Update()                             ├─ Fragment Stage
  │   ├─ NativeArray → Texture2D          │   ├─ Scene Depth → 깊이 기반 색상
  │   ├─ Apply(false)                     │   ├─ 깊이 차이 → 교차선 폼
  │   └─ Material.SetTexture              │   ├─ Scene Color + UV왜곡 → 굴절
  │                                       │   └─ 노이즈 → 표면 디테일
  └─ OnTriggerEnter2D()
      └─ 충돌 X좌표 → 해당 스프링 velocity impulse
```

**핵심 구현 스텝:**

1. **스프링 시뮬레이션** (C# MonoBehaviour): `WaterColumn` 구조체 배열로 `currentHeight`, `velocity`, `baseHeight`를 관리. `FixedUpdate()`에서 tension **0.025**, damping **0.06**, spread **0.08**로 시작해 튜닝. 월드 단위당 5개 스프링이 기본이며, 최대 100개면 충분

2. **높이맵 텍스처 업로드**: `Texture2D(springCount, 1, TextureFormat.RFloat, false)` 생성. `GetPixelData<float>(0)`으로 NativeArray 접근 후 스프링 높이를 정규화(-1~1)하여 기록. `Apply(false)`로 밉맵 없이 업로드. **128스프링 기준 512바이트/프레임** — 완전히 무시할 수 있는 전송량

3. **Shader Graph 구성**: Unlit Shader Graph 생성, Surface Opaque + Render Queue Transparent 오버라이드. Vertex Stage에서 `Sample Texture 2D LOD`로 높이맵 샘플 → Y offset에 `_MaxDisplacement` 곱셈 적용. **추가로** Time 기반 사인파(진폭 작게)를 합산하면 스프링 정지 상태에서도 ambient 물결이 살아있음

4. **슬라임 진입 감지**: 기존 BoxCollider2D Trigger의 `OnTriggerEnter2D`에서 Collider.bounds.center.x를 스프링 인덱스로 변환, `velocity += Rigidbody2D.velocity.y * splashFactor / affectedCount`로 분배. 진입과 이탈 모두 감지해 이탈 시에는 반대 방향 임펄스 적용

5. **비직사각형 대응**: 원형·다각형 수조가 필요하면 1D를 2D 그리드(예: 32×32)로 확장하고 텍스처를 32×32 RFloat로 변경. 셰이더에서 월드 스페이스 XZ 좌표를 UV로 매핑하면 메시 형태와 무관하게 작동. 경계 스프링은 높이를 고정(Dirichlet 조건)하거나 추가 감쇠 적용

6. **최적화**: 스플래시 후 타이머를 설정하고(~3초), 모든 스프링의 velocity가 임계값 이하로 떨어지면 시뮬레이션·텍스처 업데이트를 중단. 카메라 밖 수체는 시뮬레이션 비활성화. `RecalculateNormals()` 생략 (Unlit이므로 불필요)

7. **URP 체크리스트**: URP Pipeline Asset에서 Depth Texture ✅, Opaque Texture ✅ 활성화. 워터 메시는 **20×1~50×1 세분화**(2.5D 사이드뷰이므로 Z 방향 세분화 불필요)된 Quad 사용. 스플래시 파티클 이펙트를 충돌 지점에 인스턴스화하여 시각적 임팩트 강화

---

## 결론: 선택의 핵심 기준

네 방식의 차이는 결국 **"인터랙티브 리플의 물리적 품질"과 "렌더링 시각 효과의 풍부함"이라는 두 축** 사이의 트레이드오프로 귀결된다. 순수 스프링(A)은 리플은 훌륭하나 시각이 빈약하고, 순수 셰이더(B)는 시각은 화려하나 리플이 기계적이다. 서드파티 에셋(C) 중 Stylized Water 3 + Dynamic Effects는 구매 비용 ~$69로 빠른 프로덕션 경로를 제공하지만 커스텀 제어에 한계가 있다.

하이브리드(D)는 구현 난이도가 가장 높지만, **관심사 분리**(물리는 C#, 시각은 셰이더)가 명확하여 장기적 유지보수와 튜닝이 용이하다. Faerie Afterlight의 개발자가 공개한 스프링 시뮬레이션 코드를 출발점으로, URP Shader Graph의 깊이·굴절 기능을 조합하면 **1~2주 내에 프로덕션 수준 구현**이 가능하다. 만약 개발 시간이 제한적이라면 **Stylized Water 3 + Dynamic Effects** 에셋 구매가 차선이며, 향후 커스텀 인터랙션이 필요해질 때 하이브리드로 전환하는 점진적 접근도 유효하다.