# Unity 2D 플랫포머 로프 구현 완벽 가이드

**Verlet Integration 기반 커스텀 물리 시뮬레이션 + Procedural Mesh 렌더링 조합이 Pizza Tower 스타일 2D 플랫포머에서 줄(rope/vine/cable) 구현의 최적해다.** HingeJoint2D 체인은 불안정하고, Obi Rope는 URP 2D Renderer와 호환되지 않으며, Catenary/Bezier 곡선은 캐릭터 상호작용에 부적합하다. 인터랙티브 로프에는 Verlet Integration을, 순수 배경 장식용 전깃줄에는 Catenary 곡선을 사용하는 **하이브리드 전략**이 가장 합리적이다.

---

## 렌더링 방식 5가지, 어떤 걸 써야 하나

Unity에서 로프를 화면에 그리는 방법은 크게 5가지로 나뉜다. 각각의 특성이 뚜렷해서 프로젝트 요구사항에 따라 선택이 갈린다.

**LineRenderer**는 Unity 내장 컴포넌트로 `SetPositions(Vector3[])`에 점 배열을 넘기면 즉시 선을 그려준다. Width Curve로 두께 변화를 줄 수 있고, Texture Mode를 Tile로 설정하면 반복 패턴도 가능하다. URP에서는 `Sprites/Default` 또는 `Universal Render Pipeline/Unlit` shader를 Material에 할당해야 정상 렌더링된다. 구현 난이도가 **가장 낮지만**, 급격한 꺾임에서 thinning 현상이 발생하고 텍스처 스트레칭 문제가 있어 카툰 스타일의 디테일한 vine 텍스처에는 한계가 있다.

**Sprite Chain**은 SpriteRenderer + Rigidbody2D + HingeJoint2D를 가진 prefab을 일렬로 연결하는 방식이다. 각 segment를 개별 sprite로 그릴 수 있어 **pixel-perfect한 아트 표현**이 가능하고 segment별 애니메이션도 적용할 수 있다. 하지만 segment 수가 20개를 넘으면 Joint가 불안정해지고, 각 sprite가 별도 SpriteRenderer이므로 draw call이 증가한다.

**Procedural Mesh (Mesh Strip)**는 로프 경로를 따라 triangle strip 형태의 mesh를 코드로 직접 생성하는 방법이다. 각 경로 포인트에서 수직 방향으로 양쪽에 offset된 vertex 쌍을 만들고, 인접 쌍을 연결해 quad를 형성한다. UV 매핑을 정밀하게 제어할 수 있어 **vine/rope 텍스처가 왜곡 없이** 깔끔하게 입혀진다. 단일 mesh이므로 **1 draw call**로 렌더링되어 성능도 우수하다. URP Shader Graph와 결합하면 아웃라인, 흔들림 등 카툰 효과를 자유롭게 추가할 수 있다. 구현 복잡도가 높은 것이 유일한 단점이다.

**SpriteShape**는 Unity 2D 패키지에 포함된 spline 기반 도구로, 경로를 따라 sprite를 자동 타일링한다. 에디터에서 직관적으로 편집할 수 있고 EdgeCollider2D 자동 생성도 지원해서 **정적/준정적 장식용 vine이나 cable에 최적**이다. 런타임 동적 변형은 `SpriteShapeController.spline` API로 제어점을 수정할 수 있지만, 물리 시뮬레이션과의 연동은 수동 코딩이 필요하다.

**GPU Splines**는 GPU에서 spline vertex를 계산하여 mesh를 생성하는 고급 기법이다. CPU는 제어점만 업데이트하고 shader가 실제 vertex 위치를 계산하므로 **대량 로프 렌더링에 극도로 효율적**이다. 하지만 설정 복잡도가 높아 대부분의 프로젝트에서는 과도한 선택이다.

| 렌더링 방식 | 구현 난이도 | 성능 | 비주얼 퀄리티 | URP 호환 | 카툰 2D 적합도 |
|---|---|---|---|---|---|
| LineRenderer | ★☆☆ | 좋음 | 중간 | ✅ | ★★★☆☆ |
| Sprite Chain | ★★☆ | 나쁨 (대량 시) | 높음 | ✅ | ★★★★☆ |
| **Procedural Mesh** | **★★★** | **매우 좋음** | **매우 높음** | **✅** | **★★★★★** |
| SpriteShape | ★☆☆ | 좋음 | 높음 | ✅ | ★★★★☆ (정적) |
| GPU Splines | ★★★ | 최고 | 중~높음 | ✅ | ★★★☆☆ |

---

## 물리 시뮬레이션 4가지 방식의 결정적 차이

로프가 화면에서 어떻게 움직이는지를 결정하는 물리 시뮬레이션 방식은 4가지가 있다. **캐릭터가 줄에 매달려 처지게 만들 수 있는가**가 핵심 판단 기준이다.

**Verlet Integration**은 position-based 수치 적분법으로, 현재 위치와 이전 위치의 차이로 속도를 암묵적으로 계산한다. Thomas Jakobsen이 Hitman: Codename 47 개발 중 GDC 2001에서 발표한 방법론이다. 핵심 공식은 `next_pos = cur_pos + (cur_pos - old_pos) + acceleration * dt²`이며, 인접 노드 간 거리를 일정하게 유지하는 Distance Constraint를 **20~80회 반복** 적용하여 로프 형태를 만든다. 캐릭터 무게 적용이 극히 직관적인데, 가장 가까운 노드의 position에 `characterWeight * gravity * dt²`를 더하기만 하면 constraint solving이 자동으로 자연스러운 처짐을 생성한다. Unity 내장 물리를 사용하지 않으므로 **성능이 매우 우수**하며, Burst Compiler와 Jobs System을 활용하면 모바일에서도 문제없다.

**Unity Joint Chain (HingeJoint2D)**은 여러 Rigidbody2D 세그먼트를 HingeJoint2D로 연결하는 방식이다. Unity 포럼에서 반복적으로 보고되는 치명적 문제가 있다. 100개 이상의 링크에서 무게를 달면 체인이 끊어지고, DistanceJoint2D를 써도 설정 거리보다 늘어나며, Physics2D position iteration을 100으로 올려도 불안정하다. toqoz.fyi 블로그의 경고가 대표적이다: *"HingeJoint를 여러 개 연결해서 rope를 만들려고 하지 마라. 버그 투성이고 제어가 불가능하다."* **20개 세그먼트 이하의 짧은 체인에서만** 안정적으로 동작한다.

**Catenary 곡선**은 `y = a · cosh(x/a) + q` 공식으로 자체 무게에 의한 처짐을 수학적으로 정확하게 계산한다. 파라미터 `a`는 closed-form 해가 없어 bisection method로 수치 근사해야 하지만 일회성 계산이므로 **런타임 비용이 거의 0**이다. Alan Zucconi의 튜토리얼에서 Unity C# 구현체를 제공한다. 50개 이상의 배경 케이블도 문제없지만, 특정 지점에 외부 하중을 적용하려면 두 개의 catenary 세그먼트로 분할 + 힘 균형 방정식을 풀어야 해서 **동적 상호작용에는 부적합**하다.

**Bezier 곡선**은 `B(t) = (1-t)²·P0 + 2(1-t)t·P1 + t²·P2` (Quadratic) 형태로 제어점을 아래로 이동시켜 처짐을 표현한다. 구현이 가장 간단하고 항상 부드러운 곡선이 생성되지만, 물리적으로 정확하지 않고 캐릭터가 로프 위를 걷는 동적 상호작용은 어색하다.

| 평가 항목 | Verlet Integration | Joint Chain | Catenary | Bezier |
|---|---|---|---|---|
| 캐릭터 매달리기 | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐ | ⭐⭐ |
| 캐릭터가 위를 걷기 | ⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐ | ⭐⭐ |
| 배경 장식용 | ⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ |
| 50개 로프 성능 | ⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ |
| 안정성 | ⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ |

---

## 실제 2D 인디 게임은 어떻게 만들었나

**Pizza Tower**는 레벨 내 장식적 요소(배경의 전선, 케이블 등)를 **정적 sprite 타일**로 구현했다. 물리 시뮬레이션 없이 tilemap의 일부로 배치하며, 일부 매달린 오브젝트는 단순한 animated sprite이다. GameMaker Studio 2로 개발되어 모든 물리를 수동 코딩했다. 게임의 핵심은 Mach Run, Super Jump, Wall Run 같은 속도 기반 메커닉이지, rope physics가 아니다.

**Hollow Knight**의 Greenpath 지역에 등장하는 vine 플랫폼은 **static하게 매달린 오브젝트 + 공격 시 cut 이벤트 트리거** 패턴이다. vine sprite를 제거하면 플랫폼에 Rigidbody2D가 활성화되어 떨어지는 방식이며, 배경의 덩굴과 식물은 모두 parallax layer의 **정적 아트**다.

**Rain World**가 rope/vine 구현의 가장 인상적인 사례다. Slugcat이 잡고 올라가는 pole 시스템, 흔들리는 rope 오브젝트, 그리고 Daddy Long Legs의 촉수 물리가 있다. 촉수는 **curvy segment들이 float vector physics simulation으로 연결된 구조**이며, 지형 접촉 수에 따라 중력 효과가 변화한다. GDC 2016에서 Joar Jakobsson이 발표한 핵심 원칙은 **물리 시뮬레이션과 cosmetic layer의 분리**로, 단순한 collider로 물리를 처리하고 시각적 표현은 별도의 procedural animation layer에서 담당한다.

**Umihara Kawase** (1994, SFC)는 **2D rope physics의 원조**다. 고무 낚싯줄의 탄성이 게임의 핵심 메커닉이며, Verlet integration + elastic distance constraint로 유사하게 구현할 수 있다. SNES CPU에서도 rope physics 연산이 슬로우다운을 유발할 정도로 계산 집약적이었다.

**Celeste**와 **Cuphead**에는 전통적인 rope/vine 메커닭이 없다. Celeste의 Zip Mover는 직선 경로를 따르는 moving platform이고, Cuphead의 모든 환경 요소는 hand-drawn animation frame 기반이다.

---

## Unity Asset Store 솔루션과 오픈소스 대안

**Obi Rope ($37)**는 XPBD particle 기반 물리 엔진을 사용하는 가장 완성도 높은 상용 솔루션이다. 148개 리뷰와 3,087명 즐겨찾기를 보유하고, two-way rigidbody interaction(캐릭터 무게로 인한 sag), runtime rope 길이 변경, 자르기/찢기를 지원한다. **하지만 치명적 제한이 있다**: Obi가 생성하는 객체는 Sprite가 아니라 Mesh이므로 **URP의 2D Renderer 및 2D Lighting과 호환되지 않는다.** Built-in pipeline이나 URP 3D Renderer에서만 정상 동작한다.

**Rope Toolkit ($19.99)**은 Unity Jobs + Burst Compiler 기반 고성능 rope 물리 에셋이다. Built-in, URP, HDRP 모두 호환되고 full source code가 포함되어 커스터마이징이 가능하다. 다만 기본적으로 3D 중심 설계라 2D 프로젝트에는 추가 설정이 필요하다.

**2D Rope Editor ($4.99)**와 **Easy Ropes 2D ($18)**는 HingeJoint2D 기반으로 가격은 저렴하지만, 앞서 언급한 Joint Chain의 불안정성 문제를 그대로 가지고 있다.

오픈소스에서는 **toqoz.fyi 블로그 코드**가 2D Verlet rope 구현의 사실상 표준 레퍼런스다. Collision resolution, Burst 최적화, custom line rendering까지 다루는 가장 포괄적인 가이드다. **geegaz/Unity-Workshop---Rope-physics** GitHub 프로젝트는 Rigidbody chain, SpringJoint, Verlet 세 가지를 동일 조건에서 비교하여 Verlet이 가장 안정적이고 성능이 좋음을 실증적으로 보여준다. **samlletas/verlet-chain-system**은 바람 효과(VerletWave)와 LineRenderer 기반 렌더링을 포함하며, 캐릭터 의상(스카프, 망토) 시뮬레이션에도 활용 가능하다.

---

## 슬라임 무게로 줄이 처지는 표현, 이렇게 만든다

Verlet Integration에서 캐릭터 무게에 의한 로프 sag 구현은 놀라울 정도로 직관적이다. **핵심은 5단계**로 나뉜다.

먼저 **세그먼트 감지**: 캐릭터 위치에서 각 rope 노드까지의 거리를 계산해 가장 가까운 노드 인덱스를 찾는다. 더 정밀하게는 인접 노드 쌍에 대한 점-선분 거리를 구해 보간 비율(t)까지 계산할 수 있다.

다음 **하향 힘 적용**: `node.position.y -= characterWeight * dt²`로 해당 노드의 위치를 직접 수정한다. 인접 노드에도 influence radius 기반의 감쇠된 힘을 적용하면 자연스러운 곡선이 만들어진다. Verlet의 핵심 장점이 여기서 빛난다 — **명시적인 sag 애니메이션 코딩이 불필요**하고, constraint solving이 자동으로 부드러운 처짐을 생성한다.

```csharp
// Simulate() 내 캐릭터 무게 적용
float distToChar = Vector2.Distance(nodes[i].position, characterPos);
if (distToChar < influenceRadius) {
    float influence = 1f - (distToChar / influenceRadius);
    force.y -= characterWeight * influence;
}
```

**캐릭터의 로프 위 이동**은 세 가지 패턴이 있다. Position Lock(가장 가까운 노드에 고정, 입력에 따라 ±1 이동), Parametric Position(0.0~1.0 파라미터로 두 노드 사이를 lerp), Pin Constraint(특정 노드를 캐릭터 위치에 pin)이다. Pizza Tower 스타일의 과장된 반응을 원한다면 **characterWeight 값을 실제보다 크게 설정**하고, squash/stretch 효과를 sprite에 추가하면 생동감이 극대화된다.

주의할 점은 **반드시 FixedUpdate에서 시뮬레이션을 실행**해야 한다는 것이다. Variable timestep은 예측 불가능한 동작을 유발한다. 또한 40노드 × 80iteration = 3,200회 constraint solve/tick이므로, 긴 로프에서는 **Burst Compiler 사용이 필수적**이다.

---

## 권장 접근법: 두 가지 시나리오별 최적 조합

### 시나리오 A: 캐릭터가 상호작용하는 로프

슬라임 캐릭터가 매달리거나 올라탈 수 있는 인터랙티브 로프에는 **Verlet Integration + Procedural Mesh(또는 LineRenderer)** 조합이 최적이다.

- **물리**: Verlet Integration (노드 **15~25개**, constraint iteration **30~50회**)
- **렌더링**: 프로토타이핑 단계에서는 LineRenderer, 비주얼 완성 단계에서는 Procedural Mesh로 업그레이드
- **최적화**: Unity Burst Compiler + Jobs System 적용
- **시작점**: toqoz.fyi 블로그 코드를 기반으로 캐릭터 무게 적용 로직 추가

이 조합의 결정적 이점은 **URP 2D Renderer와 완벽 호환**된다는 점이다. LineRenderer는 Sorting Layer/Order를 지원하고, Procedural Mesh는 URP Shader Graph와 결합하여 아웃라인, 흔들림, 그라데이션 등 카툰 효과를 자유롭게 추가할 수 있다. Obi Rope($37)는 기능은 강력하지만 URP 2D Renderer 미지원이라는 치명적 제한 때문에 순수 2D 프로젝트에서는 **추천하지 않는다.**

### 시나리오 B: 순수 배경 장식 전깃줄

캐릭터와 상호작용하지 않는 배경의 전깃줄, 늘어진 케이블, 덩굴 장식에는 **Catenary 곡선 + LineRenderer(또는 SpriteShape)** 조합이 최적이다.

- **곡선 계산**: `y = a · cosh((x-p)/a) + q` (Alan Zucconi 튜토리얼 참조)
- **렌더링**: LineRenderer (간단한 cable) 또는 SpriteShape (유기적 vine)
- **미세 움직임**: 바람에 살랑거리는 효과가 필요하면 Bezier 제어점에 `sin(time)` 기반 미세 offset 추가
- **성능**: 런타임 비용 거의 0, **50개 이상**의 배경 케이블도 문제없음

SpriteShape는 에디터에서 spline 경로를 직관적으로 편집할 수 있고, 각도 범위에 따라 다른 sprite를 자동 배치하므로 **아티스트 친화적**이다. Pizza Tower 스타일의 핸드드로잉 느낌 vine 장식에 이상적이다.

### 핵심 리소스 정리

- **Verlet 로프 구현 바이블**: toqoz.fyi/game-rope.html (충돌, Burst 최적화, 렌더링까지 포괄)
- **Catenary 수학/구현**: Alan Zucconi "The Mathematics of Catenary" 시리즈
- **3가지 방식 비교 프로젝트**: geegaz/Unity-Workshop---Rope-physics (GitHub)
- **Procedural Mesh 기초**: Catlike Coding 튜토리얼 (catlikecoding.com)
- **Rain World 프로시저럴 애니메이션**: GDC 2016 발표 (GDC Vault 무료 시청)
- **Verlet 수학적 기초**: Owlree Blog "Simulating a Rope" (인터랙티브 설명)

---

## 결론

Pizza Tower 스타일 URP 2D 플랫포머에서 로프 구현의 핵심 인사이트는 세 가지다. 첫째, **HingeJoint2D 체인은 피해야 한다** — 다수의 개발자가 실전에서 불안정성과 제어 불가능을 보고했다. 둘째, Obi Rope 같은 상용 에셋이 강력하지만 **URP 2D Renderer 호환성 문제**가 있어 순수 2D 프로젝트에서는 커스텀 Verlet 구현이 더 현실적이다. 셋째, 인터랙티브 로프와 장식용 로프는 **근본적으로 다른 요구사항**을 가지므로 별도의 시스템으로 구현하는 것이 효율적이다. Verlet Integration의 가장 강력한 장점은 캐릭터 무게에 의한 처짐을 **명시적 애니메이션 없이** constraint solving만으로 자연스럽게 표현한다는 점이며, 이는 슬라임 캐릭터의 무게감을 전달하는 데 이상적이다.