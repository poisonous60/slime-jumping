# Unity 6 URP 소프트바디 슬라임: 스프링-매스 시스템 완전 구현 가이드

**7개 이하의 노드로 구성된 스프링-매스 시스템은 인디 2D 플랫포머에서 소프트바디 슬라임을 구현하는 가장 실용적인 접근법이다.** 중심 노드 1개 + 외곽 링 6개 구조에 압력 기반 볼륨 보존과 접착 스프링을 결합하면, Gish(2004)나 JellyCar와 유사한 물리적으로 반응하는 캐릭터를 만들 수 있다. Unity 6의 Box2D v3 멀티스레딩과 Burst Compiler를 활용하면 모바일에서도 16노드 소프트바디를 **0.3~0.5ms** 내로 시뮬레이션·렌더링할 수 있어, 인디 게임의 성능 예산 내에서 충분히 실현 가능하다.

---

## 스프링-매스 아키텍처: 7노드로 슬라임 구축하기

### 노드 배치 전략

소프트바디 슬라임의 핵심 구조는 **"1 Center + N Perimeter" 패턴**이다. Subject 13(itch.io)의 개발자가 실전에서 검증한 이 구조는, 원의 둘레에 균등 배치된 외곽 노드들이 중심 노드와 이웃 노드에 스프링으로 연결되어 블롭 형태를 유지한다.

**7노드 구성(1 중심 + 6 외곽)의 스프링 토폴로지:**

```
         N1
       /    \
     N6      N2
     |   C    |     C = 중심 노드 (mass 2.0)
     N5      N3     N1~N6 = 외곽 노드 (mass 1.0)
       \    /
         N4
```

스프링 연결은 세 종류로 구성된다. **방사형 스프링** 6개(중심↔외곽), **엣지 스프링** 6개(인접 외곽 노드 간), **교차 보강 스프링** 3개(N1↔N4, N2↔N5, N3↔N6)로 총 **15개 스프링**이 최적이다. 교차 보강 스프링은 소프트바디가 뒤집히는(inversion) 현상을 방지하는 핵심 요소다. 5노드(1+4)도 가능하지만 원형에서 벗어나며, 외곽 노드가 적을수록 메시 보간에 더 많은 세분화가 필요하다.

```csharp
void CreateBlobNodes(int perimeterCount, float radius) {
    // 중심 노드 — 더 무거운 질량으로 안정성 확보
    nodes.Add(new Node { position = Vector2.zero, mass = 2.0f });
    
    // 외곽 노드를 원형으로 배치
    for (int i = 0; i < perimeterCount; i++) {
        float angle = (2f * Mathf.PI * i) / perimeterCount;
        Vector2 pos = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        nodes.Add(new Node { position = pos, mass = 1.0f });
    }
    
    // 방사형 스프링 (중심 ↔ 외곽)
    for (int i = 1; i <= perimeterCount; i++)
        AddSpring(0, i, radius, radialStiffness, radialDamping);
    
    // 엣지 스프링 (인접 외곽 노드)
    for (int i = 1; i <= perimeterCount; i++) {
        int next = (i % perimeterCount) + 1;
        float restLen = Vector2.Distance(nodes[i].position, nodes[next].position);
        AddSpring(i, next, restLen, edgeStiffness, edgeDamping);
    }
    
    // 교차 보강 스프링 (반대편 노드) — 반전 방지
    if (perimeterCount == 6) {
        for (int i = 1; i <= 3; i++)
            AddSpring(i, i + 3, radius * 2f, crossStiffness, crossDamping);
    }
}
```

### 적분 방법: Semi-implicit Euler 권장

lisyarus의 소프트바디 물리 튜토리얼에서 검증된 **Semi-implicit(Symplectic) Euler**가 게임용 소프트바디에 가장 적합하다. 핵심 차이는 **새로운 속도로 위치를 갱신**한다는 점으로, 명시적 Euler보다 에너지 보존이 뛰어나다. Verlet 적분은 위치 기반 제약(천, 밧줄)에 강하지만, 힘 기반 소프트바디에서는 감쇠 제어가 직관적이지 않다.

```csharp
void FixedUpdate() {
    float dt = Time.fixedDeltaTime;
    
    ApplyGravity();          // 1. 중력
    ApplySpringForces();     // 2. 스프링 힘 (Hooke's Law + 방향성 감쇠)
    ApplyPressureForces();   // 3. 압력 (볼륨 보존)
    ApplyAdhesionForces();   // 4. 접착력 (활성화 시)
    
    // Semi-implicit Euler 적분
    foreach (var node in nodes) {
        node.velocity += (node.force / node.mass) * dt;
        node.position += node.velocity * dt;  // 새 속도로 위치 갱신
        node.force = Vector2.zero;
    }
    
    ResolveCollisions();     // 5. 충돌 처리
}
```

### 스프링 힘 계산의 핵심: 방향성 감쇠

lisyarus 블로그에서 강조하는 중요한 기법이 있다. 감쇠력은 **스프링 방향 성분에만 적용**해야 한다. 전체 속도를 감쇠하면 소프트바디의 이동과 회전까지 억제되어 "진흙 속을 걷는" 느낌이 된다.

```csharp
void ApplySpringForces() {
    foreach (var spring in springs) {
        Node a = nodes[spring.nodeA];
        Node b = nodes[spring.nodeB];
        
        Vector2 delta = b.position - a.position;
        float distance = delta.magnitude;
        if (distance < 0.0001f) continue;
        Vector2 direction = delta / distance;
        
        // Hooke's Law
        float springForce = spring.stiffness * (distance - spring.restLength);
        
        // 방향성 감쇠: 스프링 축을 따른 상대 속도만 감쇠
        float relVelAlongSpring = Vector2.Dot(b.velocity - a.velocity, direction);
        float dampingForce = spring.damping * relVelAlongSpring;
        
        Vector2 force = direction * (springForce + dampingForce);
        a.force += force;
        b.force -= force;
    }
}
```

**안정성 한계**: Semi-implicit Euler에서 `k × dt² < 1`이어야 시스템이 폭발하지 않는다. `dt = 0.02s`(50Hz FixedUpdate) 기준 **최대 안정 k ≈ 2500**이다. 더 높은 강성이 필요하면 서브스텝핑이나 Implicit Euler를 사용해야 한다.

---

## 볼륨 보존, 접착, 스쿼시 & 스트레치 구현

### 압력 모델로 볼륨 보존하기

Maciej Matyka의 논문 "Pressure Model of Soft Body Simulation"(2003)에서 제안된 방법이 사실상의 표준이다. **Shoelace 공식으로 현재 면적을 계산**하고, 이상 기체 법칙에 기반한 압력을 각 엣지에 법선 방향으로 가한다.

```csharp
float CalculatePolygonArea() {
    float area = 0f;
    int n = perimeterNodes.Count;
    for (int i = 0; i < n; i++) {
        int j = (i + 1) % n;
        area += perimeterNodes[i].position.x * perimeterNodes[j].position.y;
        area -= perimeterNodes[j].position.x * perimeterNodes[i].position.y;
    }
    return Mathf.Abs(area) * 0.5f;
}

void ApplyPressureForces() {
    float currentArea = CalculatePolygonArea();
    float pressure = pressureConstant / currentArea; // 이상 기체 법칙 단순화
    
    int n = perimeterNodes.Count;
    for (int i = 0; i < n; i++) {
        int j = (i + 1) % n;
        Vector2 edge = perimeterNodes[j].position - perimeterNodes[i].position;
        float edgeLength = edge.magnitude;
        
        // 외향 법선 (엣지에 수직, 바깥 방향)
        Vector2 normal = new Vector2(-edge.y, edge.x).normalized;
        
        // 힘 = 압력 × 엣지 길이, 양쪽 노드에 절반씩 분배
        Vector2 force = normal * pressure * edgeLength * 0.5f;
        perimeterNodes[i].force += force;
        perimeterNodes[j].force += force;
    }
}
```

면적이 줄어들면 압력이 증가하여 바깥으로 밀어내고, 면적이 늘어나면 압력이 감소하여 수축한다. GameDev.net의 "Slime" 스레드에서 정리된 레시피 — "(1) 원형 파티클 배치, (2) 이웃 스프링 연결, (3) 면적 계산, (4) 휴지 면적 설정, (5) 매 프레임 면적 재계산 후 압력 적용" — 가 가장 실용적인 구현 경로다.

### 표면 접착(adhesion) 구현

Gish의 "sticky" 모드에서 영감을 받은 접착 메커니즘은, **접촉한 외곽 노드에서 접촉점까지 임시 스프링을 생성**하는 방식이다. 이 스프링은 최대 힘 임계값을 초과하면 끊어진다.

```csharp
void OnNodeCollision(int nodeIndex, Vector2 contactPoint) {
    if (!isStickyMode) return;
    
    activeAdhesions.Add(new AdhesionPoint {
        nodeIndex = nodeIndex,
        worldAnchor = contactPoint,
        stiffness = 150f,        // 접착 강성
        maxForce = 80f           // 이 힘을 초과하면 떨어짐
    });
}

void ApplyAdhesionForces() {
    for (int i = activeAdhesions.Count - 1; i >= 0; i--) {
        var adh = activeAdhesions[i];
        Vector2 delta = adh.worldAnchor - nodes[adh.nodeIndex].position;
        Vector2 force = delta * adh.stiffness;
        
        if (force.magnitude > adh.maxForce) {
            activeAdhesions.RemoveAt(i);  // 접착 해제
            continue;
        }
        nodes[adh.nodeIndex].force += force;
        nodes[adh.nodeIndex].velocity *= 0.92f; // 접착점 속도 감쇠
    }
}
```

**중력에 의한 처짐(droop)**은 접착과 중력의 자연스러운 상호작용으로 구현된다. 천장에 붙은 슬라임의 상단 노드가 접착 스프링으로 고정되면, 하단 노드들은 중력에 의해 늘어지고, 스프링 시스템과 압력 모델이 무한 늘어남을 방지하면서 **눈물방울 형태**가 자연스럽게 생성된다.

### 스쿼시 & 스트레치: 물리 기반 자동 발생

별도의 스쿼시/스트레치 코드가 필요하지 않다. 스프링-매스 시스템은 이를 **물리적으로 자동 생성**한다. 착지 시 하단 노드가 지면에 막히면 상단 노드가 관성으로 계속 내려오면서 스프링이 압축되고, 압력 모델이 수평 방향으로 밀어내 **자연스러운 스쿼시**가 발생한다. 점프 시에는 중심 노드에 상향 임펄스를 주면, 외곽 노드가 관성으로 뒤처지면서 **세로 스트레치**가 발생한다.

chrismarch/SoftBodySimulation에서 제안하는 기법으로, 점프 직전에 **짧은 예비 스쿼시**를 넣으면 게임 필이 크게 향상된다:

```csharp
void Jump() {
    centerNode.force += Vector2.down * preSquashForce; // 0.08초간 하향 압축
    StartCoroutine(DelayedLaunch());
}

IEnumerator DelayedLaunch() {
    yield return new WaitForSeconds(0.08f);
    centerNode.velocity += Vector2.up * jumpSpeed;
}
```

---

## Unity 빌트인 물리 vs 커스텀 물리: 실전 비교

**Rigidbody2D + SpringJoint2D** 접근법은 프로토타이핑에 최적이지만, 프로덕션에서는 한계가 있다. Subject 13 개발자가 보고한 **중심 노드 붕괴 문제**(높은 힘에서 스프링이 극복당해 중심이 외곽을 관통)가 대표적이다. SpringJoint2D의 `frequency`와 `dampingRatio`는 인스펙터에서 직관적으로 조정 가능하지만, 압력 모델이나 접착 같은 커스텀 힘을 추가하기 어렵다.

| 기준 | Unity 빌트인 (SpringJoint2D) | 커스텀 물리 | 하이브리드 (권장) |
|------|---------------------------|-----------|----------------|
| 프로토타이핑 속도 | ★★★★★ | ★★☆☆☆ | ★★★☆☆ |
| 볼륨 보존 | 불가 | ★★★★★ | ★★★★★ |
| 접착 구현 | 어려움 | ★★★★★ | ★★★★★ |
| 충돌 처리 | 자동 | 수동 구현 필요 | Unity 충돌 활용 |
| 성능 제어 | 제한적 | 완전 제어 | 완전 제어 |
| 커스텀 힘 추가 | 제한적 | ★★★★★ | ★★★★★ |

**권장 하이브리드 접근법**: 커스텀 스프링 물리로 시뮬레이션하되, 각 노드에 Kinematic Rigidbody2D + CircleCollider2D(Trigger)를 부착하여 Unity의 충돌 감지 시스템을 활용한다. 충돌 해소는 `OnTriggerStay2D`에서 수동으로 처리한다.

**참고할 만한 GitHub 저장소**: **chrismarch/SoftBodySimulation**(356스타, 슬라임 특화), **kwanchangnim/Jello-Physics**(JellyCar 원작자 기반, JelloPressureBody 클래스 포함), **Ideefixze/Softbodies**(여러 접근법 비교 학습용).

---

## 파라미터 튜닝: ω와 ζ로 게임 필 제어하기

Erin Catto(Box2D 창시자)의 GDC 2011 발표 "Soft Constraints"에서 강조한 핵심 원칙이 있다. **raw k와 c 값 대신 각진동수(ω)와 감쇠비(ζ)로 파라미터화하라.** 이 두 값은 질량 독립적이고 직교적(orthogonal)이어서, 하나를 바꿔도 다른 하나에 영향을 주지 않는다.

- **ω = 2π × frequency_Hz**: 스프링의 반응 속도. 높을수록 "빠르고 팽팽한" 느낌
- **ζ (감쇠비)**: 0이면 영원히 진동, 0~1이면 바운스(젤리), 1이면 임계 감쇠(빠른 수렴), 1 초과면 과감쇠(끈적이는 이동)

| 슬라임 동작 | 주파수 (Hz) | ζ (감쇠비) | 체감 |
|------------|-----------|---------|-----|
| 대기 젤리 흔들림 | 2~4 | 0.15~0.3 | 지속적 출렁임 |
| 착지 스쿼시 반동 | 5~8 | 0.2~0.4 | 빠른 반응, 오버슈트 |
| 고무공 탄성 | 8~15 | 0.3~0.5 | 적당한 진동 |
| 벽 접착 유지 | 1~3 | 0.7~1.0 | 느리고 무거운 느낌 |
| 끈적이는 이동 | 2~5 | 1.5~3.0 | 점성 있는 움직임 |

**무조건 안정한 Implicit Euler** (Allen Chou 공식)를 사용하면 서브스텝핑 없이도 높은 강성을 안전하게 사용할 수 있다:

```csharp
// Allen Chou의 Implicit Euler Spring — 어떤 dt에서도 안정
void Spring(ref float x, ref float v, float target, float zeta, float omega, float dt) {
    float f = 1.0f + 2.0f * dt * zeta * omega;
    float oo = omega * omega;
    float hoo = dt * oo;
    float hhoo = dt * hoo;
    float detInv = 1.0f / (f + hhoo);
    float detX = f * x + dt * v + hhoo * target;
    float detV = v + hoo * (target - x);
    x = detX * detInv;
    v = detV * detInv;
}
```

Game Developer의 "Instant Game Feel — Springs Explained" 기사에서 정리된 튜닝 원칙: **감쇠가 낮으면 바운스 증가, 주파수가 높으면 반응성 증가** — 이 두 축만으로 대부분의 게임 필을 커버할 수 있다.

---

## 성능 최적화: Job System과 Burst로 15~40배 가속

7노드 단일 슬라임이라면 표준 C#으로도 충분하지만, 다수의 소프트바디나 모바일 타겟에서는 **Unity Job System + Burst Compiler** 조합이 결정적 차이를 만든다. Eric Hu의 벤치마크에서 Boids 시뮬레이션(유사한 파티클 워크로드) 1000개 에이전트 기준, 표준 C# **67.9ms** → Job+Burst **4.55ms**로 약 **15배 가속**이 측정되었다.

```csharp
[BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
public struct SpringForceJob : IJobParallelFor {
    [ReadOnly] public NativeArray<float2> positions;
    [ReadOnly] public NativeArray<float2> velocities;
    [ReadOnly] public NativeArray<int2> springPairs;
    [ReadOnly] public NativeArray<float> restLengths;
    [ReadOnly] public float springK, dampingC;
    [WriteOnly] public NativeArray<float2> springForces;
    
    public void Execute(int i) {
        int a = springPairs[i].x, b = springPairs[i].y;
        float2 delta = positions[b] - positions[a];
        float dist = math.length(delta);
        float2 dir = delta / math.max(dist, 0.0001f);
        
        float stretch = dist - restLengths[i];
        float2 sf = springK * stretch * dir;
        float2 df = dampingC * math.dot(velocities[b] - velocities[a], dir) * dir;
        springForces[i] = sf + df;
    }
}
```

핵심 최적화 규칙: `NativeArray<T>`와 `Allocator.Persistent`로 **제로 GC 할당**을 유지하고, `Unity.Mathematics`의 `float2`/`float3`로 **SIMD 벡터화**를 활성화하며, 스프링 힘 계산(병렬) → 힘 축적(순차) → 적분(병렬)의 **2-pass 패턴**으로 레이스 컨디션을 방지한다.

### FixedUpdate 설정과 보간

`Time.fixedDeltaTime = 1f/60f`로 렌더 프레임과 맞추는 것이 권장된다. 물리 스텝 사이의 시각적 떨림을 방지하려면 `Update()`에서 보간한다:

```csharp
void Update() {
    float t = (Time.time - Time.fixedTime) / Time.fixedDeltaTime;
    renderPosition = Vector2.Lerp(previousPosition, currentPosition, t);
}
```

### Unity 6 신기능 활용

**Unity 6.3**(2025년 12월)에서 **Box2D v3** 통합으로 2D 물리의 멀티스레드 성능이 개선되었고, **Unity 6.4 베타**에서는 `Collider2D`가 `NativeArray<>`를 반환하는 새로운 물리 쿼리 오버로드가 추가되어 Burst 호환 충돌 쿼리가 가능해졌다. 커스텀 스프링 물리에서 환경 충돌을 Unity 2D 물리로 처리하는 하이브리드 접근법의 효율이 크게 향상된다.

---

## 게임 레퍼런스에서 배우는 설계 교훈

### Gish: 물리를 게임플레이로 만든 선구자

Gish(2004, Chronic Logic)는 **16개 파티클에 각 5개 본드(스프링)**로 구성된 소프트바디 캐릭터를 사용했다. **200Hz 고정 타임스텝**으로 안정성을 확보하고, 4가지 모드(sticky, slick, solid, jump)가 각각 물리 파라미터(마찰, 강성, 질량)를 변경하는 방식으로 **조합적 게임플레이 깊이**를 만들어냈다. Alex Austin의 핵심 교훈: *"물리로 게임플레이를 만드는 건 정말 어렵다. 대부분의 창발적 행동은 재미없다. 물리를 제어 가능하고 플레이 가능하게 만드는 것이 핵심이다."*

### JellyCar: 소프트바디 전용 물리 엔진

JellyCar(Walaber/Tim FitzRandolph)는 **리지드바디 없이 포인트 매스와 스프링만으로** 모든 것을 구축했다. 핵심 기술인 **Shape Matching** — 현재 노드 위치를 이상적 기준 프레임과 비교하여 부드럽게 복원하는 방식 — 은 소프트바디가 뒤집히거나 붕괴하는 문제를 해결한다. 충돌 처리도 단순해지는데, 전체 형상의 접촉 매니폴드를 계산할 필요 없이 **개별 관통 노드만 밀어내면** 바디가 자연스럽게 변형된다.

Shape Matching의 핵심 공식:

```
θ = atan2(Σ(rᵢ × qᵢ), Σ(rᵢ · qᵢ))
// rᵢ = 현재 위치 - 질량 중심, qᵢ = 이상적 위치
// θ만큼 회전한 이상적 위치 방향으로 스프링 힘 적용
```

### "가짜로 할 수 있으면 가짜로 하라"

Slime Rancher는 진정한 소프트바디 물리를 쓰지 **않는다**. 리지드바디 + PhysX의 표준 물리 위에 **셰이더 기반 시각적 출렁임**(서브서피스 스캐터링, 블롭 그림자)과 행동 AI(배고픔, 공포, 어그로)를 결합해 "말랑한 느낌"을 만든다. Kyle Gabler(World of Goo)의 원칙과 일맥상통하는 이 접근법은, **변형이 게임 메커닉이 아니라 미학인 경우** 가장 효율적이다.

반면, 변형 자체가 게임플레이인 경우 — 좁은 틈 통과, 벽 점착, 충격 분열 — 는 진정한 물리 시뮬레이션이 필요하다. 이것이 Gish와 Slime Rancher의 근본적 차이다.

---

## URP에서 소프트바디 메시 렌더링하기

### 커스텀 메시 + Catmull-Rom 보간 (권장)

Sprite Shape는 런타임 수정 시 **렌더링이 사라지는 알려진 버그**가 있어 소프트바디에 부적합하다. 대신 **MeshFilter + MeshRenderer에 Catmull-Rom 스플라인 보간**을 적용하는 것이 가장 안정적이다.

외곽 노드 6개 사이를 Catmull-Rom으로 보간하면 ~48~96개의 매끄러운 윤곽 정점이 생성되고, 중심점과 함께 삼각형 팬(triangle fan)으로 메시를 구성한다. UV는 각도 기반 투영으로 매핑한다.

```csharp
public class SlimeMeshRenderer : MonoBehaviour {
    Mesh mesh;
    Vector3[] vertices;
    Vector2[] uvs;
    int[] triangles;
    
    void Start() {
        mesh = new Mesh();
        mesh.MarkDynamic(); // GPU 버퍼를 빈번한 갱신에 최적화
        GetComponent<MeshFilter>().mesh = mesh;
        
        int maxVerts = numNodes * interpolationSteps + 1;
        vertices = new Vector3[maxVerts]; // 사전 할당, 재사용
        uvs = new Vector2[maxVerts];
        triangles = BuildTriangleFan(maxVerts); // 한 번만 생성
        mesh.triangles = triangles;
    }
    
    void LateUpdate() {
        // Catmull-Rom으로 외곽 보간 → vertices 배열 갱신
        UpdateVerticesFromNodes();
        mesh.vertices = vertices; // 캐시된 배열 재사용 — GC 0
        mesh.uv = uvs;
        mesh.RecalculateBounds();
    }
}
```

**Unity 6.3부터 URP 2D Renderer가 MeshRenderer를 2D 스프라이트 셰이더로 렌더링할 수 있게 되었다.** 이전 버전에서는 `unity_SpriteColor`와 `unity_SpriteProps`가 설정되지 않아 MeshRenderer가 2D Renderer에서 보이지 않는 문제가 있었는데, 6.3에서 해결되었다. 이전 버전이라면 URP Unlit 투명 셰이더를 직접 작성하거나, Shader Graph의 Sprite Unlit/Lit 타겟을 사용해야 한다.

### 슬라임 시각 효과를 위한 셰이더 기법

슬라임다운 외관을 위해 세 가지 접근법이 있다. **SDF 기반 내부 광택**(Cyanilux 튜토리얼)은 알파 채널에 SDF를 저장하고 Smoothstep으로 글로우 폭을 제어한다. **스크린 스페이스 굴절**(URP Opaque Texture 필요)은 반투명 젤리 느낌을 준다. **메타볼 접근법**(각 노드를 소프트 서클로 렌더링 → 임계값 처리)은 슬라임이 분열·합체하는 메커닉이 있을 때 최적이다.

슬라임 한 개의 예상 렌더링 비용: 스프링 물리 ~0.1ms + 스플라인 보간 ~0.05ms + 메시 갱신 ~0.01ms + 셰이더 ~0.1~0.3ms = **총 약 0.3~0.5ms**. 10개 이상의 슬라임이 필요한 경우 메시 갱신에 Job System을 적용하면 된다.

---

## 최종 구현 아키텍처와 권장 사항

**물리 레이어** (`FixedUpdate`, 1/60s): 중력 → 스프링 힘(방향성 감쇠) → 압력(볼륨 보존) → 접착 → Shape Matching(선택) → Semi-implicit Euler 적분 → 하이브리드 충돌 처리. **입력/제어**: 중심 노드에 수평 힘으로 이동, 상향 임펄스로 점프(예비 스쿼시 딜레이 포함), sticky 모드 토글로 접착 활성화. **렌더링 레이어** (`LateUpdate`): 노드 위치 → Catmull-Rom 보간 → 삼각형 팬 메시 → URP Sprite Lit/Unlit 셰이더.

7노드 소프트바디는 이 아키텍처의 최소 유효 구현이다. 게임 필이 부족하면 외곽 노드를 8~12개로 늘리고, 압력 상수와 감쇠비를 조정하라. 변형이 게임 메커닉의 핵심이라면 Shape Matching을 반드시 추가하여 반전 방지를 보장하라. 변형이 미학적 요소에 그친다면, Slime Rancher처럼 셰이더 기반 접근이 훨씬 효율적이다. 핵심 참고 자료로는 **lisyarus 블로그**(이론), **chrismarch/SoftBodySimulation**(Unity 코드), **Jello-Physics**(전체 라이브러리), **JellyCar Deep Dive**(설계 철학)를 우선 검토할 것을 권장한다.