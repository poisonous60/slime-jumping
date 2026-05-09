# SurfaceProbe 곡선 성능 분석 (2026-05-09)

대상: MultiContactSurfaceProbe + DeformablePlatform 성능 회귀  
측정: FixedUpdate 프레임 타임 ~8ms → ~33ms (슬라임이 대형 곡선 플랫폼을 이동할 때)  
프로파일러 기반 게임사이드 Δself 상위 항목 기준 분석

---

## 1. N 엣지에 따라 시간이 증가하는 이유 (근본 원인)

### 1-1. 동일 콜라이더 경로를 FixedUpdate당 두 번 완전 탐색

**CheckSurface 경로 (PopulateContacts):**

```
// MultiContactSurfaceProbe.cs:167-182 (AddContactsForCollider, PolygonCollider2D 분기)
for (int p = 0; p < pathCount; p++)
{
    poly.GetPath(p, pathBuffer);       // 네이티브 → 관리 복사
    BuildWorldPath(t, offset, pathBuffer, worldBuffer);   // N × TransformPoint
    float windingSign = ComputeWindingSign(worldBuffer);  // O(N) 면적 합산
    for (int j = 0; j < n; j++)
        TryAddPolygonEdgeContact(…);   // 엣지당 최근접점 + Dot 연산
}
```

`CheckSurface` → `PopulateContacts` → `AddContactsForCollider`는 SurfaceProbe.cs:113에서 호출된다.

**DepenetratePass 경로 (SnapToSurface):**

```
// MultiContactSurfaceProbe.cs:338-353 (CollectPushesForCollider, PolygonCollider2D 분기)
for (int p = 0; p < pathCount; p++)
{
    poly.GetPath(p, pathBuffer);       // 두 번째 네이티브 → 관리 복사
    BuildWorldPath(t, offset, pathBuffer, worldBuffer);   // 두 번째 N × TransformPoint
    float windingSign = ComputeWindingSign(worldBuffer);  // 두 번째 O(N) 합산
    for (int j = 0; j < n; j++)
        TryAddPolygonEdgePush(…);
}
```

`SnapToSurface` → `DepenetratePass` → `CollectPushesForCollider`는 SurfaceProbe.cs:476에서,  
그리고 SlimeController.cs:785에서 FixedUpdate 안에 호출된다.

또한 `DepenetratePass`는 SurfaceProbe.cs:475의 `for (int iter = 0; iter < 3; iter++)` 루프 안에서  
최대 3번 반복된다. 즉 한 번의 FixedUpdate에서 동일 폴리곤의 경로가 최대 **4회** (1회 CheckSurface + 3회 DepenetratePass 이터레이션) 완전 탐색될 수 있다.

**결론:** 대형 곡선 플랫폼은 여러 개의 PolygonCollider2D 조각(piece)으로 구성되어 있고,  
각 조각은 N개의 정점을 가진다. 콜라이더 개수 × 경로 수 × 정점 수 × (최대 4회 탐색)만큼  
비용이 복합적으로 증가한다. 이것이 Δself 1위(+24.56ms)인 `Physics2D.FindNewContacts`의  
C# 측 전구 증상이다.

### 1-2. `BuildWorldPath`의 호출당 O(N) TransformPoint 재빌드

```
// MultiContactSurfaceProbe.cs:504-511
private static void BuildWorldPath(Transform t, Vector2 offset,
    List<Vector2> localPath, List<Vector2> worldOut)
{
    worldOut.Clear();
    int n = localPath.Count;
    for (int i = 0; i < n; i++)
        worldOut.Add((Vector2)t.TransformPoint(localPath[i] + offset));
}
```

`BuildWorldPath`는 콜라이더 Transform이 이동하지 않았더라도 매 호출마다 N개 정점에 대해  
`Transform.TransformPoint`(내부 행렬 곱 + Vector2 캐스팅)를 수행한다. 캐싱 키가 없으므로  
"정지한 플랫폼"이라도 비용이 동일하다.

### 1-3. `ComputeWindingSign`은 O(N) — 경로 탐색마다 반복 계산

```
// MultiContactSurfaceProbe.cs:515-527
private static float ComputeWindingSign(List<Vector2> pts)
{
    int n = pts.Count;
    if (n < 3) return 1f;
    float twoA = 0f;
    for (int i = 0; i < n; i++)
    {
        Vector2 a = pts[i];
        Vector2 b = pts[(i + 1) % n];
        twoA += a.x * b.y - b.x * a.y;   // 쇼엘라체 공식, O(N) 루프
    }
    return twoA >= 0f ? 1f : -1f;
}
```

경로당 한 번 호출되며, `BuildWorldPath`와 마찬가지로 캐싱이 없다.  
50정점 폴리곤에서는 50번의 곱셈+덧셈이 CheckSurface 1번 + DepenetratePass 최대 3번 = 최대 4회 실행된다.

### 1-4. 콜라이더가 움직이지 않았을 때도 경로를 재빌드

`BuildWorldPath`의 입력은 `poly.transform`이고, 결과를 저장하는 캐시 키는 없다.  
변경된 여부를 확인하는 `Transform.hasChanged` 조건이나 버전 비교가 전혀 없으므로  
정지한 플랫폼에서도 매 FixedUpdate마다 전체 재빌드가 발생한다.

### 1-5. `List<Vector2>`를 통한 `GetPath` — 실질적인 할당은 없지만 내용 복사는 있다

`pathBuffer`와 `worldBuffer`는 인스턴스 수준 `List<Vector2>`로 선언되어(MultiContactSurfaceProbe.cs:35-36)  
재사용된다:

```
// MultiContactSurfaceProbe.cs:35-36
private readonly List<Vector2> pathBuffer = new List<Vector2>(64);
private readonly List<Vector2> worldBuffer = new List<Vector2>(64);
```

64를 넘는 정점 경로에서 List가 내부 배열을 리사이즈하면 할당이 발생한다.  
더 중요한 점은, `GetPath`가 항상 네이티브 → 관리 복사를 수행하며 이 비용은 N에 비례한다는 것이다.  
반면 `ignoredColliders.Contains(col)`(MultiContactSurfaceProbe.cs:51, 83)은 List 선형 탐색으로,  
콜라이더가 많을 때 불필요한 추가 비용이 된다(경미하지만 누적됨).

---

## 2. DeformablePlatform이 프로파일러에 나타나는 이유

**`IsEar` (+0.41ms Δself)와 `EarClip` 호출의 맥락:**

DeformablePlatform.cs:205-208을 보면:

```
void LateUpdate()
{
    if (dirty) { Rebuild(); dirty = false; }
}
```

`Rebuild`는 `dirty = true`일 때만 호출된다. `dirty`는 두 경로에서 설정된다:
- `ApplyImpactAt` (착지 충격, FixedUpdate에서) — 슬라임이 플랫폼에 착지할 때만
- `TickReset` (리셋 애니메이션 중) — `resetting == true`인 동안 매 FixedUpdate

즉 `IsEar` / `EarClip`은 **연속 매 프레임 실행되지 않는다**. 착지 직후 또는  
리셋 애니메이션 중(설정된 `resetDuration` 동안)에만 실행된다.

**프로파일러에 나타나는 이유:** 슬라임이 대형 곡선 플랫폼(여러 조각으로 구성)을 이동하는  
슬라이딩 시나리오에서 착지 프레임 또는 크리핑(attachedCreepPerSec)이 `dirty`를 설정하고,  
`Rebuild`가 DeformablePlatform.poly.SetPath를 호출한다.  
이 `SetPath`가 Box2D 내부의 contact 캐시를 무효화하여 **다음 FixedUpdate**의  
`Physics2D.FindNewContacts` 비용을 증가시킨다. 즉 `IsEar` 자체는 부차적 기여자이며,  
진짜 인과관계는 `SetPath` → contact 재발견 → `FindNewContacts` 스파이크이다.

---

## 3. 리팩터 제안 (동작 보존)

### (a) BuildWorldPath 결과를 FixedUpdate 범위 내에서 캐시

**대상 파일:** `MultiContactSurfaceProbe.cs`

**구체적 변경 방향:**  
`Dictionary<Collider2D, (int transformVersion, List<Vector2> worldPath, float windingSign)>`  
형태의 인스턴스 멤버를 추가한다. `transformVersion`은 Unity의 `Transform.hasChanged`를 매 FixedUpdate 시작 시 flip-and-clear하거나, 더 안전하게는 Transform의 localToWorldMatrix를 해시하는 방식을 쓴다.

`CheckSurface`와 `DepenetratePass`가 같은 콜라이더를 요청할 때, 현재 FixedUpdate에서  
이미 빌드된 캐시 엔트리가 있으면 `BuildWorldPath`와 `ComputeWindingSign` 호출을 건너뛴다.  
FixedUpdate가 끝나면(또는 다음 CheckSurface 진입 시) 캐시를 무효화한다.

**예상 Δself 감소:**  
`BuildWorldPath` (DepenetratePass branch) -0.72ms, `ComputeWindingSign` -0.26ms,  
`CollectPushesForCollider` -0.37ms, `AddContactsForCollider` 일부 — 합산 최대 **-1.7ms**.

**동작 보존 근거:**  
같은 FixedUpdate 안에서 콜라이더 Transform은 변경되지 않는다(FixedUpdate 실행 중 물리 단계는  
Rigidbody2D.position 쓰기가 완료된 후 다음 FixedUpdate까지 적용됨). 캐시가 FixedUpdate 경계마다  
갱신되면 항상 현재 프레임의 올바른 worldPath를 반환한다.

**위험/주의:**  
- Dictionary 룩업(해시) 비용이 추가됨. 콜라이더가 1-2개뿐인 단순 씬에서는 오히려 느릴 수 있다.  
  캐시는 콜라이더가 4개 이상인 씬에서만 효과적이다.  
- 이동하는 콜라이더(SpringyBranch EdgeCollider2D 등)는 `Transform.hasChanged`가  
  항상 true이므로 캐시 히트율이 낮다. 정지 플랫폼에서 이득이 집중된다.

---

### (b) 단일 버퍼 재사용 — 현재 이미 구현됨 (partial)

**현황 확인:**  
`pathBuffer`와 `worldBuffer`는 이미 인스턴스 멤버 `List<Vector2>`로 재사용되고 있다  
(MultiContactSurfaceProbe.cs:35-36). 즉 이 항목은 이미 적용되어 있다.

**잔여 할당 위험:**  
경로 정점 수가 64를 초과하면 `pathBuffer.Count` 증가 시 내부 배열이 재할당된다.  
`pathBuffer = new List<Vector2>(128)` 으로 초기 용량을 늘리는 것으로 충분히 방어된다.  
예상 Δself 감소: 미미 — 실질적으로 주요 할당 경로는 이미 차단되어 있다.

---

### (c) Transform 변경 시에만 경로 재빌드 (Transform.hasChanged 활용)

**대상 파일:** `MultiContactSurfaceProbe.cs`

**구체적 변경 방향:**  
`(a)` 캐시 전략의 무효화 조건으로 `transform.hasChanged`를 활용한다. Unity는  
Rigidbody2D가 이동하거나 Transform이 직접 변경될 때 이 플래그를 세운다.  
단, `hasChanged`를 읽은 후 반드시 `hasChanged = false`로 리셋해야 한다(그렇지 않으면  
항상 true).

대안: `PolygonCollider2D.GetTotalPointCount()`는 O(1)이므로 변경 감지 빠른 사전 필터로  
사용할 수 있으나, 위치 변경 없이 정점 수가 동일한 변형은 감지하지 못한다.

**예상 Δself 감소:**  
`(a)`와 시너지. 이동하지 않는 플랫폼에서 `GetPath` + `BuildWorldPath` 비용을 0에 가깝게 줄인다.

**동작 보존 근거:**  
정적 콜라이더의 worldPath는 Transform이 변경될 때만 갱신이 필요하다.  
`hasChanged`는 Transform의 position, rotation, scale 중 하나라도 변경되면 설정되므로  
회전 플랫폼/이동 플랫폼 모두 올바르게 처리된다.

**위험/주의:**  
- `hasChanged` 리셋은 다른 시스템과 공유되는 Transform에서 부작용을 일으킬 수 있다.  
  읽기 전용으로 사용하고 독립적 버전 키(localToWorldMatrix 스냅샷 비교)를 쓰는 것이 더 안전하다.
- `DeformablePlatform`은 `SetPath` 후 Transform을 건드리지 않으므로  
  `hasChanged`로는 감지되지 않는다 — 별도 `ShapeVersion` 카운터가 필요하다.

---

### (d) `Collider2D.GetShapes` / `PhysicsShapeGroup2D` 활용

**대상 파일:** `MultiContactSurfaceProbe.cs`

**구체적 변경 방향:**  
`poly.GetShapes(shapeGroup)`를 사용하면 내부 Box2D 형상 데이터를 직접 가져온다.  
Unity 공식 문서는 *"PhysicsShapeGroup2D를 재사용하면 형상/정점 수가 늘어나지 않는 한  
메모리 할당이 발생하지 않는다"*고 명시한다. 이 경우 `GetPath` → `BuildWorldPath` 과정이  
`GetShapes` 한 번 호출로 대체될 수 있다.

단, `GetShapes`가 반환하는 정점은 **Rigidbody2D 로컬 공간**에 있으므로,  
`PhysicsShapeGroup2D.localToWorldMatrix`로 월드 변환이 필요하다(TransformPoint보다  
Matrix4x4.MultiplyPoint3x4가 더 빠를 수 있다). CompositeCollider2D의 경우  
이미 월드 정점으로 처리되므로 `GetShapes`가 특히 유리하다.

**예상 Δself 감소:**  
`BuildWorldPath` 관련 Δself 전체 감소 가능. 단, 실제 속도 차이는 Unity 내부 구현에 달려 있어  
벤치마크 전까지 불확실하다.

**동작 보존 근거:**  
`GetShapes`는 콜라이더가 Box2D에 등록한 실제 형상을 반환한다.  
`GetPath`가 반환하는 경로와 위상적으로 동일하므로 edge 탐색 결과가 동일하다.

**위험/주의:**  
- `GetShapes`는 Unity 2022 이상에서 안정 API. Unity 6에서는 지원됨.  
- 삼각형(PhysicsShapeType2D.Polygon)으로 분해된 형상을 받으므로,  
  원래 경로의 "엣지" 개념과 다를 수 있다. CompositeCollider2D의 경우  
  Outline 모드에서 실제 윤곽 엣지가 나오지만 분해 모드에서는 삼각형 에지가 나올 수 있다.  
  **이 경로를 택하기 전에 반환 형상 유형을 검증해야 한다.**

---

### (e) 엣지 AABB 사전 컬링 (프로브 반경 이내 엣지만 처리)

**대상 파일:** `MultiContactSurfaceProbe.cs`

**구체적 변경 방향:**  
`TryAddPolygonEdgeContact`와 `TryAddPolygonEdgePush`가 현재 하는 첫 단계(`distSq > maxDistSq` 체크)를  
외부 엣지 루프로 올린다. 즉 `worldBuffer[j]`와 `worldBuffer[j+1]`의 AABB를  
`(pos, maxDist)` 원과 빠르게 교차 검사하여 확실히 범위 밖인 엣지는 `closest` 계산을 건너뛴다.

현재 코드에서도 `distSq > maxDistSq` 가드(MultiContactSurfaceProbe.cs:246-247, 421-422)가  
`closest` 계산 후에 있어, 범위 밖 엣지들은 이미 걸러지긴 한다. 추가 AABB 사전 컬은  
`closest` 계산(Dot, 클램프, 뺄셈) 자체를 아끼는 것이다.

**예상 Δself 감소:**  
`TryAddPolygonEdgePush` -1.10ms, `TryAddPolygonEdgeContact` -0.81ms 상당 부분.  
슬라임이 플랫폼 한쪽 끝에 있을 때 반대쪽 엣지를 건너뛸 수 있어 효과가 크다.

**동작 보존 근거:**  
AABB 사전 컬 조건은 `distSq > maxDistSq` 후처리와 논리적으로 동일하다.  
세그먼트 AABB와 원의 교차가 없으면 `closest`까지의 거리는 반드시 `maxDist` 초과이다.

**위험/주의:**  
AABB 검사는 보수적(false negative 없음)이므로 동작 변화는 없다.  
추가 AABB 계산이 오버헤드가 될 수 있으나 엣지 수가 많을수록 이득이 커진다.

---

### (f) `Physics2D.FindNewContacts` 스파이크 — TautLine.SyncShapes SetPath 연쇄

**근거:**  
프로파일러 Δself에 `TautLine.SyncShapes → PolygonCollider2D.SetPath_Internal +0.13ms`가  
나타난다. Unity 이슈 트래커에는 `PolygonCollider2D.SetPath`가 Box2D 내부 contact 캐시를  
무효화하여 다음 스텝의 `FindNewContacts` 비용을 폭발적으로 증가시킨다는 사례가 문서화되어 있다.  
(출처: issuetracker.unity3d.com/issues/performance-issue-when-using-polygoncollider2d-dot-setpath)

TautLine은 `[DefaultExecutionOrder(200)]`으로 SlimeController(기본 0) 이후 실행되므로,  
`SyncShapes`의 `SetPath` → contact 무효화 → **다음 FixedUpdate**의 `FindNewContacts` +24.56ms  
스파이크 시나리오가 정확히 맞아떨어진다.

DeformablePlatform의 `Rebuild`도 `SetPath`를 호출하므로(DeformablePlatform.cs:556-557)  
착지 프레임에서 같은 체인이 발생한다.

**구체적 변경 방향 (두 가지 독립적 접근):**

1. **TautLine**: 형상이 실제로 변경되었을 때만 `SetPath`를 호출한다.  
   이전 프레임의 dipDepth, riderT, anchorA/B 위치를 저장하고,  
   임계값 이하의 변화에서는 `polygon.SetPath` 호출을 건너뛴다.  
   로프가 정지 상태이거나 극히 미세하게 진동할 때 contact 재발견을 막는다.

2. **CompositeCollider2D 활용 고려**: 여러 PolygonCollider2D 조각이 하나의  
   CompositeCollider2D에 합쳐지면, 개별 `SetPath` 대신 composite의 `GenerateGeometry`  
   한 번으로 contact 무효화를 집약할 수 있다. 단, 이는 DeformablePlatform 아키텍처를  
   바꾸는 것이므로 범위를 벗어난다.

**예상 Δself 감소:**  
`Physics2D.FindNewContacts` -24.56ms의 상당 부분. 스파이크의 근본 원인에 해당.

**동작 보존 근거:**  
contact는 Box2D가 발견하므로 C# 코드의 논리적 동작은 변하지 않는다.  
변경된 것은 "얼마나 자주 Box2D에게 재발견을 요청하는가"이다.

**위험/주의:**  
임계값 설정이 잘못되면 로프가 실제로 변형되었는데도 contact가 갱신되지 않는  
눈에 보이는 버그가 발생할 수 있다. 임계값은 `slimeRadius * 0.01` 수준으로 매우 작게 잡아야 한다.

---

## 4. 관련 문서 및 커뮤니티 자료

### (a) / (c) 캐싱 관련

- **[Unity Scripting API: Collider2D.GetShapes](https://docs.unity3d.com/ScriptReference/Collider2D.GetShapes.html)**  
  → *"It is recommended to reuse the PhysicsShapeGroup2D because then memory allocations only occur if the number of shapes or vertices retrieved changes."*  
  `BuildWorldPath` 재빌드 대신 `GetShapes`로 캐시 우호적인 형상 접근이 가능함을 공식 문서가 직접 권장.

- **[Unity Discussions: What's the fastest alternative to PolygonCollider2D.SetPath()?](https://discussions.unity.com/t/whats-the-fastest-alternative-to-polygoncollider2ds-setpath-for-many-points/860670)**  
  → 고정점 수 콜라이더에서 per-frame `SetPath` 대신 정점을 Unsafe Array로 전달하는 패턴이  
  GC와 내부 복사 비용을 크게 줄인다는 커뮤니티 검증 사례.

### (d) GetShapes API

- **[Unity Scripting API: PhysicsShapeGroup2D](https://docs.unity3d.com/ScriptReference/PhysicsShapeGroup2D.html)**  
  → PhysicsShapeGroup2D는 재사용 가능한 용량-기반 컨테이너로, `GetShapes`와 조합하면  
  `GetPath` + TransformPoint 루프를 대체하면서 네이티브 박스2D 형상 데이터에 직접 접근 가능.

### (f) SetPath → FindNewContacts 체인

- **[Unity Issue Tracker: Performance issue when using PolygonCollider2D.SetPath()](https://issuetracker.unity3d.com/issues/performance-issue-when-using-polygoncollider2d-dot-setpath)**  
  → Unity가 "Postponed"으로 분류. 1300정점 경로에서 SetPath가 1.9초 걸리는 사례 수록.  
  SetPath는 Box2D 내부 contact 캐시를 무효화하며, 이것이 다음 `FindNewContacts` 스파이크의 원인.

- **[Unity Discussions: Physics 2D FindNewContacts job gets crazy when moving kinematic objects](https://discussions.unity.com/t/physics-2d-findnewcontacts-job-gets-crazy-when-moving-kinematic-objects/805358)**  
  → SetPath/콜라이더 변형이 contact-finding 비용을 n² 방식으로 증가시키는 메커니즘 설명.  
  "콜라이더 형상 변경 빈도를 줄이는 것"이 핵심 권고.

- **[Unity How-To: Enhanced physics performance for smooth gameplay](https://unity.com/how-to/enhanced-physics-performance-smooth-gameplay)**  
  → Unity 공식. 콜라이더 복잡도 감소, 불필요한 contact 재발견 방지, NonAlloc 쿼리 재사용을  
  가장 중요한 2D 물리 최적화 항목으로 나열.

---

## 5. 권장 적용 순서

| 순위 | 제안 | 예상 Δself 감소 | 구현 위험 |
|------|------|----------------|-----------|
| 1 | **(f) TautLine `SetPath` 조건부 스킵** | FindNewContacts -24.56ms 대부분 | 낮음 — 임계값 보수적으로 설정 |
| 2 | **(a) BuildWorldPath FixedUpdate 내 캐시** | BuildWorldPath+Winding+CollectPushes ~-1.7ms | 중간 — Dictionary 도입, 캐시 무효화 타이밍 |
| 3 | **(e) 엣지 AABB 사전 컬링** | TryAdd* 두 항목 ~-1.5ms | 낮음 — 순수 추가 조건, 하위 호환 |
| 4 | **(c) Transform.hasChanged 무효화** | (a)와 시너지, 정적 플랫폼 추가 감소 | 중간 — hasChanged 리셋 부작용 주의 |
| 5 | **(d) GetShapes 활용** | BuildWorldPath 완전 대체 가능 | 높음 — 형상 유형 검증 필요, 실험적 |
| 6 | **(b) 버퍼 초기 용량 증가** | 미미 (이미 대부분 해결됨) | 없음 |

**우선 (f)를 적용하라.** 가장 큰 Δself인 `Physics2D.FindNewContacts +24.56ms`는 C# 코드  
최적화로는 직접 건드릴 수 없는 네이티브 레이어이며, 그 원인이 되는 `SetPath` 호출 빈도를  
줄이는 것이 유일하게 근본적인 접근이다. (a)와 (e)는 나머지 C# 측 비용을 줄이는 방법으로  
(f) 이후 순차 적용한다.
