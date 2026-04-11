# 로우폴리 파티클 톤 통일 컨벤션: Unity URP 2.5D 플랫포머 완전 가이드

**로우폴리 카툰 스타일 게임에서 파티클이 "따로 노는" 느낌을 주는 근본 원인은 색상·형태·렌더링 방식의 불일치이며, 이를 해결하는 핵심은 ScriptableObject 기반 글로벌 팔레트 + Shuriken 메시 파티클 + 커스텀 Shader Graph 삼각 구조다.** Synty POLYGON MINI 스타일의 2.5D 플랫포머에서 바람·물·불·먼지 파티클이 월드와 동일한 "미술 언어"를 공유하려면, 파티클 시스템을 독립된 이펙트가 아닌 환경 오브젝트의 연장선으로 설계해야 한다. 이 보고서는 GDC 강연, 인디 개발자 포스트모템, Unity 공식 문서, 그리고 TUNIC·A Short Hike 등 레퍼런스 게임 분석을 종합하여, 접근법 비교→근거 있는 선택→구현 권장사항 순으로 정리한다.

---

## 1. 파티클이 "같은 세계" 소속으로 보이게 하는 5가지 원칙

### 색상 팔레트: 제한이 곧 통일이다

Sparkball VFX 스타일 가이드(VFX Director David Hall)에 따르면, 파티클 팔레트는 **유사색·삼각배색(analogous/triadic) 변형 내로 제한**해야 한다. "더 많은 색을 넣으면 대비와 시각 노이즈만 증가할 뿐, 대부분의 경우 불필요하다." TUNIC의 Andrew Shouldice는 "채도가 매우 높은 색을 잡되, 약간 낮춰서 다른 채널도 존재감을 갖게 한다"는 접근법을 쓴다. 즉, **단일 채널 최대 채도가 아니라 따뜻한 광택감**을 목표로 한다.

그래디언트 처리에서도 차이가 있다. Hall은 하이라이트 영역의 그래디언트를 **"크런칭(crunching)"**하는 기법을 권장한다. 부드러운 블렌딩 대신 급격한 전환을 사용하면 "탁한 영역"이 사라지고 스타일라이즈드 게임의 하드 엣지 특성과 일치한다. 어두운 영역에서만 풍부한 그래디언트를 유지한다.

### 형태 언어: 월드의 기하학을 파티클에 복제

**파티클의 형태 언어(shape language)는 월드의 형태 언어를 반영해야 한다.** GDC 2017 "Art Directing VFX for Stylized Games"(Double Fine, Epic Games, Volition)에서 이 원칙이 명확히 제시된다. 로우폴리 월드가 각진·다면체적 형태를 쓴다면, 파티클도 삼각형·다이아몬드·육각형 같은 기하학적 형태여야 한다. 부드럽고 뿌연 연기 스프라이트는 즉시 "붙여넣은" 느낌을 준다.

Hall은 "넓고 단순한 형태에 두꺼운/얇은 선의 밸런스"를 강조한다. 핵심은 **가독성(readability)**이다. 플레이어의 시선은 게임 중 가장 중요한 정보만 찾으므로, 파티클은 적고 크고 명확해야 한다. 섬세하고 옅은 트레일 텍스처를 시도했을 때 "플레이어가 아예 보지 못했다"는 사례가 이를 증명한다.

### 렌더링 일관성: Unlit + 블룸 + 후처리 파이프라인 공유

로우폴리/카툰 스타일에서 파티클은 일반적으로 **Unlit(비조명)**으로 렌더링한다. 플랫 셰이딩된 표면 위에 조명 계산이 들어간 파티클을 올리면 미술 스타일이 깨진다. TUNIC은 하이브리드 접근법으로, 스크롤링 텍스처를 3D 메시 지오메트리(튜브)에 입혀 조명과 상호작용하게 하지만, 이는 전통적 빌보드 파티클과 다른 특수 기법이다.

**블룸(Bloom)**은 통합의 핵심 도구다. TUNIC에서 Shouldice는 블룸을 "스크롤링 텍스처 이펙트의 결함을 감추는 화려함"으로 활용한다. A Short Hike은 안개(fog), 엣지 디텍션, 색보정 후처리를 통해 파티클을 포함한 모든 요소를 저해상도 "바삭한 픽셀" 파이프라인으로 통일한다. **파티클과 월드가 동일한 후처리 스택을 거치는 것이 시각적 통합의 가장 강력한 수단**이다.

### 밀도와 스케일: "적게, 크게, 명확하게"

Synty POLYGON Particle FX Pack은 "퍼포먼스를 위한 경량 파티클"을 내세우지만, 이는 단순 최적화가 아닌 **의도적 디자인 선택**이다. 로우폴리 스타일은 소수의 크고 읽기 쉬운 파티클을 요구한다. Pontus Albrecht("How To Make Low Poly Look Good")의 핵심 인사이트: **"크기를 절반으로 줄이면 서브디비전도 절반으로 줄여라. 이것이 응집력을 만든다."** 메시 파티클을 쓸 경우, 작은 파티클은 더 단순한 기하학이어야 한다.

타이밍도 통일 요소다. Hall에 따르면 **"이펙트의 주요 액션은 수명의 첫 1/4 내에 완료"**되어야 하며, 선형 타이밍 대신 이즈인/이즈아웃 커브를 사용한다. 이 "스냅감 있는" 타이밍이 로우폴리 월드의 장난감 같은 반응성과 일치한다.

---

## 2. VFX Graph vs Shuriken: 이 프로젝트에서의 확실한 선택

### 세 가지 관점에서의 비교

| 기준 | Shuriken (Particle System) | VFX Graph |
|------|---------------------------|-----------|
| **연산 방식** | CPU | GPU (Compute Shader) |
| **처리 가능 파티클 수** | 수천 개 | 수백만 개 |
| **URP 지원** | ✅ 완전 프로덕션 레디 | ⚠️ 프리뷰 미졸업 (Unity 6.3 LTS 기준) |
| **모바일 지원** | ✅ 모든 모바일 디바이스 | ❌ OpenGL ES 미지원, 일부 고급 기기만 |
| **물리 상호작용** | ✅ 콜라이더·트리거·콜백 | ❌ 뎁스 버퍼만 |
| **메시 파티클** | ✅ Renderer 모듈에서 네이티브 | ✅ 가능하나 설정 복잡 |
| **Synty 에셋 호환** | ✅ Synty FX 팩이 Shuriken 기반 | ❌ Synty VFX Graph 팩 없음 |
| **학습 커브** | ⭐⭐⭐⭐⭐ 인스펙터 기반 | ⭐⭐⭐ 노드 기반 |
| **커뮤니티/에셋** | ✅ 방대한 튜토리얼·에셋 팩 | ⚠️ 성장 중이나 제한적 |
| **소형 인스턴스 다수 성능** | ✅ 인스턴스당 오버헤드 낮음 | ⚠️ 인스턴스당 기본 GPU 비용 높음 |

### 결론: Shuriken이 압도적으로 적합

**이 프로젝트에는 Built-in Particle System(Shuriken)을 사용해야 한다.** 근거는 다음과 같다.

첫째, **로우폴리 카툰 스타일은 파티클 수가 적다.** 스타일라이즈드 이펙트는 적고 크고 의도적인 파티클을 쓴다. 이펙트 하나당 **10~200개**, 화면 전체 동시 활성 **1,000개 미만**이 일반적이다. VFX Graph의 GPU 병렬 처리 능력은 이 규모에서 불필요한 오버헤드다. 포럼 벤치마크에서 200개의 소형 파티클 시스템 인스턴스를 동시 구동할 때 Shuriken이 VFX Graph를 **성능에서 앞선다.**

둘째, **모바일 호환성이 하드 리퀘어먼트다.** VFX Graph는 Unity 6.3 LTS(2026년 2월 빌드) 기준으로도 URP 모바일에서 프로덕션 레디가 아니다. OpenGL ES를 지원하지 않아 상당수 안드로이드 기기에서 동작하지 않는다. Unity 공식 문서는 명시적으로 *"대부분의 모바일 애플리케이션에는 Built-in Particle System을 사용하세요"*라고 안내한다.

셋째, **Synty 생태계와 직접 호환된다.** Synty의 "SIMPLE FX - Cartoon Particles"(무료)과 "POLYGON - Particle FX Pack"(120+ 이펙트) 모두 Shuriken 기반이다. Jean Moreno의 Cartoon FX Remaster 등 주요 스타일라이즈드 VFX 에셋 팩도 마찬가지다. 이들을 시작점으로 커스터마이징하면 개발 시간을 크게 단축할 수 있다.

VFX Graph가 적합한 경우는 고사양 PC/콘솔 전용, 수백만 파티클 규모의 시뮬레이션, 또는 리얼리스틱 이펙트를 구현할 때뿐이다.

---

## 3. 로우폴리 파티클의 구체적 구현 아키텍처

### 메시 파티클 vs 빌보드: 하이브리드가 정답

세 가지 접근법을 비교하면:

**메시 파티클(권장 주력)**은 Particle System → Renderer → Render Mode를 **Mesh**로 설정하고, 아이코스피어(~12 tri), 삼각형(2 tri), 다이아몬드, 나뭇잎 등 극저폴리 메시를 할당한다. Synty의 POLYGON Particle FX Pack과 Polygonal's Low-Poly Particle Pack이 이 방식을 사용한다. 월드 오브젝트와 동일한 면 기반 심미성을 유지하므로 **스타일 일관성이 가장 높다.** GPU Instancing은 Renderer 모듈의 "Enable Mesh GPU Instancing" 체크박스로 활성화하며, **SRP Batcher는 파티클 셰이더를 지원하지 않으므로** Dynamic Batching이 주요 배칭 메커니즘이다.

**빌보드 스프라이트**는 연기 퍼프, 원거리 먼지 모트, 글로우 이펙트처럼 카메라 각도 변화가 큰 보조 요소에 적합하다. 단, 텍스처는 반드시 **플랫 컬러 + 하드 엣지**로 제작해야 로우폴리 미학과 충돌하지 않는다. Stretched Billboard는 스파크·바람 줄기에 유용하다.

**트레일(Trails 모듈)**은 바람 줄기, 물 흐름, 이동 궤적에 탁월하다. Mode: Particles, Lifetime: 0.3~0.5로 설정하고, 별도의 Trail Material(URP Particles/Unlit)을 Renderer 모듈에 할당한다. "적은 파티클 + 긴 트레일"이 "많은 소형 파티클"보다 스타일라이즈드 바람 표현에 효과적이다.

**권장 하이브리드 구성:** 1차 요소(불꽃 혀, 물방울, 나뭇잎)는 메시 파티클, 2차 요소(반짝임, 먼지 모트, 글로우)는 빌보드, 선형 이펙트(바람 줄기, 물줄기)는 트레일.

### 글로벌 컬러 팔레트 시스템: 삼각 아키텍처

팔레트 통일의 핵심은 **단일 진실 공급원(Single Source of Truth)**을 확보하고, 이를 모든 파티클에 자동으로 전파하는 구조다. 세 가지 방법을 결합한 삼각 아키텍처를 권장한다.

**1단계: ScriptableObject가 정의(canonical source)**

```csharp
[CreateAssetMenu(fileName = "VFXPalette", menuName = "VFX/Color Palette")]
public class VFXColorPalette : ScriptableObject
{
    [Header("바람")] public Color windPrimary, windSecondary;
    [Header("불")]  public Color firePrimary, fireSecondary;
    [Header("물")]  public Color waterPrimary, waterSecondary;
    [Header("먼지")] public Color dustPrimary;
    [Header("공유 그래디언트")]
    public Gradient fireGradient, waterGradient, windGradient;
}
```

오픈소스 참고: **Swatchr**(GitHub, 116스타)는 ParticleSystem까지 지원하는 ScriptableObject 팔레트, **uPalette**(GitHub)는 색상 변경 자동 전파 Synchronizer를 제공한다.

**2단계: Shader.SetGlobalColor가 런타임에 전파**

```csharp
public class GlobalParticleColorManager : MonoBehaviour
{
    [SerializeField] private VFXColorPalette palette;
    static readonly int WindColorID = Shader.PropertyToID("_GlobalWindColor");
    static readonly int FireColorID = Shader.PropertyToID("_GlobalFireColor");

    void OnEnable() => ApplyPalette();

    public void ApplyPalette()
    {
        Shader.SetGlobalColor(WindColorID, palette.windPrimary);
        Shader.SetGlobalColor(FireColorID, palette.firePrimary);
        // ... 각 요소별 동일 패턴
    }
}
```

`Shader.SetGlobalColor`는 **모든 셰이더에 전역으로 적용**되므로, 개별 머티리얼 수정 없이 씬 전체 팔레트 스왑(낮/밤, 계절 변화)이 즉시 가능하다. Unity 공식 문서: "Global properties are used if a shader needs them but the material does not have them defined."

**3단계: 커스텀 Shader Graph가 전역 색상을 소비**

Shader Graph에서 Custom Function 노드로 `_GlobalFireColor` 등의 전역 프로퍼티를 읽고, Vertex Color와 곱해 파티클별 미세 변형을 허용한다. 이 방식은 "모든 불 파티클이 정확히 같은 베이스 톤이되, 버텍스 컬러로 개별 밝기/변주"를 구현한다.

**MaterialPropertyBlock 보충:** 같은 프리팹이지만 인스턴스별 다른 틴트가 필요할 때(예: 푸른 불 vs 붉은 불) 사용한다. URP에서 MaterialPropertyBlock은 SRP Batcher를 깨지만, **파티클 셰이더는 이미 SRP Batcher에서 제외**되므로 추가 페널티가 없다.

### 파티클 크기·속도·수명 스케일 컨벤션

Synty POLYGON MINI 캐릭터가 약 **1~2 Unity 유닛** 높이라는 전제 하에:

| 요소 | Start Size | Start Speed | Lifetime | Emission Rate | Gravity Modifier |
|------|-----------|-------------|----------|---------------|-----------------|
| **불** | 0.1~0.3 | 0.5~2.0 | 0.3~1.0초 | 5~15/초 | -0.1 (약간 상승) |
| **물 스플래시** | 0.05~0.15 | 1.0~4.0 | 0.2~0.8초 | 버스트 | 0.5~1.0 |
| **바람** | 0.05~0.2 | 2.0~5.0 | 0.5~1.5초 | 3~8/초 | 0 |
| **먼지** | 0.02~0.08 | 0.1~0.5 | 1.0~3.0초 | 1~5/초 | 0.05~0.1 |

핵심 규칙: 개별 파티클 크기는 **캐릭터 높이의 5~20%**, Start Size의 랜덤 범위는 **항상 Random Between Two Constants로 0.7x~1.3x**, 모든 요소에서 이 비율을 동일하게 유지해 "무게감"을 통일한다.

---

## 4. 바람·물·불·먼지: 요소별 로우폴리 구현과 레퍼런스

### 바람: TUNIC의 스크롤링 텍스처에서 배운다

TUNIC(Andrew Shouldice)은 전통적 파티클 시스템을 거의 쓰지 않는다. 대신 **3D 메시 지오메트리(튜브)에 스크롤링 텍스처**를 입혀 바람·에너지 흐름을 표현한다. 블렌더에서 제작한 긴 튜브를 UV맵핑하고, "비틀고, 회전하고, 왜곡하고, 복제"한다. 이 접근법은 파티클과 월드 기하학이 완전히 동일한 렌더링 파이프라인을 공유하게 만든다.

2.5D 플랫포머에서의 실용적 바람 구현: 삼각형/마름모 메시 파티클(나뭇잎·잔해)을 수평 방향으로 방출하되, **트레일 모듈**로 반투명한 흰색/연녹색 줄기를 남긴다. 풀·나무는 Noise 기반 버텍스 애니메이션으로 흔들리게 한다(Omno 개발자가 "noise based vertex animation"을 환경 움직임에 사용). 색상은 **월드 팔레트의 흰색~연녹색~연노랑** 범위, 높은 투명도.

### 물: Crossy Road의 기하학적 스플래시가 정답

Crossy Road는 물에 빠질 때 **하얀 큐브·삼각형의 기하학적 파편 버스트**로 스플래시를 표현한다. 이것이 로우폴리 물 이펙트의 교과서적 접근법이다. 부드러운 스프레이나 투명한 물방울 텍스처가 아니라, **면이 보이는 불투명한 기하학적 형태**가 핵심이다.

구현: 리플은 확장하는 링 메시(토러스 또는 애니메이션 플랫 디스크)로 플랫 셰이딩, 폼 파티클은 해안선 따라 흰색/연청색 작은 쿼드 산포, 폭포는 TUNIC 방식의 메시 스크롤링 텍스처 또는 경로를 따르는 메시 파티클 캐스케이드. Alba: A Wildlife Adventure도 물 표면에 미니멀한 기하학적 반사 이펙트를 사용한다.

### 불: 디졸브 셰이더가 알파 페이드를 대체

로우폴리 불의 핵심 기법은 **알파 페이드 대신 오파크(불투명) 디졸브 셰이더**를 사용하는 것이다. Shader Graph에서 그레이스케일 노이즈 마스크를 만들고, **Step 노드**로 하드 엣지 디졸브(스타일라이즈드)를 적용한다. AgePercent를 Custom Vertex Streams로 파티클 시스템에서 전달하여 디졸브 진행도를 제어한다. 어두운 픽셀이 먼저 사라지고 밝은 픽셀이 마지막에 사라진다.

티어드롭 형태의 로우폴리 메시 화염 + 작은 삼각형/다이아몬드 엠버 파티클이 조합의 기본형이다. Moonlit Studio의 튜토리얼은 "화염과 스파크에 동일한 디졸브 셰이더를 사용하면 시각적 통일"을 달성한다고 설명한다. 다이나믹 포인트 라이트(오렌지색)를 불 파티클 시스템에 부착하면 월드와의 통합이 강화된다.

### 먼지: 지형 팔레트에서 직접 색상 샘플링

발자국/착지 먼지는 **2~4개의 기하학적 파티클(플랫 원 또는 로우폴리 구)**을 짧은 수명(0.3~0.8초)으로 빠르게 확장 후 페이드한다. 색상은 반드시 **지형/바닥 컬러 팔레트에서 직접 샘플링**한다. 충격 먼지는 지면 높이에서 링 형태로 외부 확장 후 소멸하며, Sparkball의 "스냅감 있는 타이밍(첫 1/4에서 액션 완료)" 원칙을 따른다.

앰비언트 부유 먼지는 매우 작은 도트/기하학적 형태를 느린 드리프트 + 긴 수명으로 대기감을 조성한다. A Short Hike과 TUNIC 모두 이 앰비언트 파티클로 분위기를 구축하며, 이때 **후처리 파이프라인을 공유**함으로써 파티클이 자동으로 씬의 색감을 상속받게 만든다.

---

## 5. 팔레트 통일 자동화: 실용적 워크플로우 도구

### 프리팹 배리언트 계층 구조

단일 **Base_Particle 프리팹**에 공통 설정(머티리얼, 셰이더, 렌더 모드, 소팅 레이어)을 정의하고, **Prefab Variant**로 각 요소별 파생(Wind_Leaves, Fire_Campfire, Water_Splash 등)을 만든다. 베이스 프리팹의 변경은 모든 배리언트에 자동 전파되므로, 팔레트 변경 시 베이스만 수정하면 된다. 배리언트에서 변경된 속성은 인스펙터에서 **볼드 텍스트**로 표시되어 오버라이드 추적이 쉽다.

### 에디터 검증 도구

```csharp
[MenuItem("Tools/VFX/Palette Validator")]
static void ValidateParticles()
{
    var palette = AssetDatabase.LoadAssetAtPath<VFXColorPalette>("Assets/Data/VFXPalette.asset");
    var systems = FindObjectsOfType<ParticleSystem>();
    foreach (var ps in systems)
    {
        Color startColor = ps.main.startColor.color;
        bool isInPalette = /* palette 내 색상과 비교 */;
        if (!isInPalette)
            Debug.LogWarning($"[팔레트 위반] {ps.gameObject.name}: {startColor}");
    }
}
```

씬 내 모든 파티클 시스템을 스캔하여 팔레트 외 색상을 사용하는 시스템을 경고한다. CI/CD 파이프라인에 통합하면 팔레트 이탈을 자동으로 감지할 수 있다.

### 런타임 팔레트 바인더

`[ExecuteInEditMode]` 속성의 `ParticlePaletteBinder` 컴포넌트를 각 파티클 시스템에 부착하고, 열거형으로 타입(Fire, Water, Wind, Dust)을 지정하면 `OnValidate()`에서 ScriptableObject 팔레트의 색상과 그래디언트를 자동 적용한다. 인스펙터에서 팔레트를 교체하면 즉시 프리뷰가 갱신된다.

### 참고할 오픈소스와 상용 에셋

**NovaShader**(CyberAgent, GitHub)는 URP용 다기능 파티클 셰이더로, **Tritone/Pentone 톤 매핑**을 내장하여 휘도 기반 색상 조정이 가능하다. Flow Map, Dissolve, Animated Tint Map을 지원하며 커스텀 파티클 셰이더 아키텍처의 훌륭한 레퍼런스다. Jean Moreno의 Cartoon FX Remaster는 **파티클 실루엣 형태의 커스텀 메시를 사용해 오버드로우를 최적화**하는 접근법을 보여주며, 커스텀 에디터 툴도 함께 제공한다.

---

## 6. 반드시 피해야 할 흔한 실수 7가지

**가장 치명적인 실수는 "파티클마다 색상을 따로 하드코딩"하는 것이다.** 개별 파티클 시스템에 색상을 직접 지정하면, 팔레트 변경 시 수십 개의 프리팹을 일일이 수정해야 하고, 미세한 색상 차이가 누적되어 전체가 분열된다. 반드시 ScriptableObject 팔레트 + 바인더 컴포넌트 또는 Shader.SetGlobalColor를 통한 간접 참조를 사용해야 한다.

**리얼리스틱 텍스처의 유혹을 경계해야 한다.** 소프트하고 사실적인 연기/불 텍스처는 플랫 셰이딩된 로우폴리 메시와 즉시 충돌한다. "투명도가 아트 일관성을 깨고, 오파크 파티클은 갑자기 사라지는 느낌을 준다"는 커뮤니티의 발견에 따르면, 해결책은 **커스텀 디졸브 셰이더로 불투명 파티클을 스타일리시하게 소멸**시키는 것이다.

**어디티브 블렌딩의 남용**이 세 번째 함정이다. 어디티브 블렌딩은 여러 파티클이 겹칠 때 통제 불능의 밝기를 만들어 플랫 미학을 깨뜨린다. 대부분의 로우폴리 파티클은 **Alpha Blend**를 사용하고, 어디티브는 특정 글로우/마법 이펙트에만 한정해야 한다.

**"많고 작은" 대신 "적고 큰"을 선택하지 않는 실수.** 밀도 높은 파티클 클라우드는 사실적으로 보이지, 스타일라이즈드로 보이지 않는다. 시스템당 **5~15개/초** 방출이 적정하며, 50+ 방출은 오버드로우 성능 문제도 동반한다.

**2.5D에서 소팅 레이어를 무시하는 것**은 치명적이다. ParticleSystemRenderer의 Sorting Layer와 Order in Layer를 설정해야 하며, 전용 VFX 소팅 레이어(Background_VFX, Character_VFX, Foreground_VFX)를 만들어야 한다. Sorting Fudge 프로퍼티와 Sorting Group 컴포넌트를 조합하여 파티클-캐릭터-배경 간의 정확한 깊이 관계를 보장해야 한다.

**파티클 조명과 환경 조명의 불일치**, 그리고 **볼류메트릭한 파티클이 플랫 미학을 깨는 문제**도 흔하다. Unlit 파티클의 색상은 씬의 앰비언트 컬러/컬러 그레이딩에 수동으로 맞추거나, 셰이더에서 `RenderSettings.ambientLight`를 글로벌 프로퍼티로 전달하여 곱해야 한다.

---

## 결론: 구현 로드맵과 핵심 인사이트

이 프로젝트의 파티클 시스템은 다음 순서로 구축해야 한다. **1단계:** VFXColorPalette ScriptableObject 정의 + GlobalParticleColorManager 싱글턴 생성. **2단계:** 커스텀 Shader Graph(Unlit + 전역 컬러 참조 + Vertex Color 곱 + Step 디졸브) 제작. **3단계:** Base_Particle 프리팹 + 요소별 Prefab Variant 계층 구조 확립. **4단계:** 바람→먼지→물→불 순서로 구현(복잡도 오름차순). **5단계:** 에디터 검증 도구로 팔레트 일관성 자동 감시.

이 조사에서 발견한 가장 중요한 인사이트는, TUNIC·A Short Hike·Crossy Road 등 성공적인 로우폴리 게임들이 파티클을 "이펙트"가 아니라 **"월드 기하학의 작은 조각"**으로 다룬다는 점이다. 메시 파티클로 월드와 동일한 다면체적 형태를 공유하고, 글로벌 팔레트로 색상을 통일하고, 동일한 후처리 스택을 거치게 할 때, 파티클은 비로소 "같은 미술 언어"를 말하기 시작한다. 기술적 화려함이 아니라 **미적 절제와 시스템적 일관성**이 톤 통일의 본질이다.