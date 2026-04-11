# 레인월드 스타일 카메라 시스템 Unity 구현 가이드

**레인월드의 카메라는 "부드러운 팔로우"가 아니라 "고정 스크린 + ±20px 미세 흔들림"이라는 점이 핵심이다.** 바닐라 레인월드에서 카메라는 플레이어를 따라가지 않는다. 각 룸에 사전 정의된 고정 카메라 위치(스크린)에 잠기고, 슬러그캣의 움직임에 따라 **최대 ±20픽셀**만 미세하게 기울어지는 "lean" 효과를 줄 뿐이다. 이 프로젝트는 2D 물리 + 3D 렌더링의 2.5D 구조이므로, 레인월드의 프리렌더 배경 기반 시스템을 그대로 복제할 필요는 없지만, **핵심 느낌**인 "룸 구속 → 스크린 스냅 → 파이프 전환"은 충실히 재현할 수 있다. 현재 `SlimeCameraFollow.cs`의 SmoothDamp 기반 구조에서 출발해 커스텀 FSM 방식으로 확장하는 것이 가장 효율적인 경로다.

---

## 레인월드 카메라의 실제 작동 원리

레인월드는 Unity를 렌더링 셸로만 사용하고, 카메라 시스템 전체를 **`RoomCamera`** 클래스로 직접 구현했다. Futile이라는 2D 프레임워크 위에서 동작하며, Cinemachine이나 Unity 네이티브 카메라 시스템과는 완전히 별개다.

**룸 내 카메라 동작**: 각 룸은 레벨 에디터에서 정의된 **`cameraPositions` 배열**(Vector2[])을 갖는다. 기본 단일 스크린 룸 크기는 **72×43 타일(1440×860px)** 로, 네이티브 해상도 **1366×768** 보다 약간 크다. 이 여유 공간이 lean 효과의 범위다. 카메라 위치는 룸 파일에 선언된 좌표에서 **절대 ±20px 이상 벗어나지 않도록 clamp**된다. 이 미세한 움직임이 3개 레이어(전경·중경·배경)에 서로 다른 비율로 적용되어 패럴랙스 깊이감을 만든다.

**멀티 스크린 룸에서의 전환**: 룸이 화면보다 클 경우 여러 카메라 위치가 존재하며, 슬러그캣이 스크린 경계를 넘으면 카메라가 **즉시 스냅(하드 컷)** 한다. 페이드도 슬라이드도 없다. 이 점은 커뮤니티에서도 "시각적 어지러움"으로 자주 비판받는 요소이며, **SBCameraScroll** 모드가 모든 스크린 텍스처를 하나로 병합해 부드러운 스크롤을 추가한 이유이기도 하다.

**룸 간 전환(파이프/숏컷)**: `ShortcutHandler` 클래스가 관리한다. 슬러그캣이 파이프에 진입하면 약 10프레임의 "흡입" 애니메이션이 재생되고, 화면이 **`_transitionColor` 셰이더 유니폼을 통해 페이드**되며, 새 룸의 텍스처와 카메라 위치가 로드된 후 출구 파이프 가장 가까운 카메라 위치로 스냅된다.

**파이프 내 이동 시각 효과**: 룸 내부에 표시된 점선(대시) 경로를 따라 **세그먼트가 순차적으로 점등**된다. 점등 세그먼트 수는 생물체 크기에 따라 달라진다 — 작은 생물 2개, 일반 3개, 대형(레드 센티피드) **5개**. 파이프 입구 심볼도 플래시되며, 색상은 생물 종류에 따라 다르다.

---

## 세 가지 구현 접근법 비교

### 접근법 1: Cinemachine VCam 전환 방식

**개요**: 룸마다 `CinemachineVirtualCamera`를 하나씩 배치하고, 각각에 `CinemachineConfiner2D`를 붙여 해당 룸의 BoxCollider2D를 경계로 지정한다. 플레이어가 룸 트리거에 진입하면 해당 VCam의 Priority를 올리고, CinemachineBrain이 자동으로 블렌딩을 처리한다.

```csharp
// RoomTrigger.cs — 각 룸의 BoxCollider2D 트리거에 부착
public class RoomTrigger : MonoBehaviour
{
    [SerializeField] CinemachineVirtualCamera roomCam;
    
    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
            roomCam.Priority = 20;  // 활성화
    }
    
    void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
            roomCam.Priority = 10;  // 비활성화
    }
}
```

**룸 경계 관리**: 각 룸에 CompositeCollider2D(정적 Rigidbody2D, Unsimulated)를 두고 CinemachineConfiner2D의 BoundingShape2D로 지정한다. 카메라 뷰포트 가장자리가 경계 밖으로 나가지 않도록 자동 처리된다.

**전환 연출**: CinemachineBrain의 DefaultBlend로 제어한다. 레인월드 스타일 하드 컷이면 블렌드 타임을 **0초**로, 슬라이드면 0.3~0.5초 EaseInOut으로 설정한다. 단, **공간적으로 멀리 떨어진 룸 간 블렌딩은 중간 지형이 노출**되는 "fly-through" 아티팩트가 발생하므로 페이드 오버레이가 필수다.

**장점**: 카메라 코드를 거의 작성할 필요 없음, 룸별 독립 설정(오쏘 사이즈, 댐핑, 데드존 등) 가능, CinemachineConfiner2D가 화면 가장자리 구속을 자동 처리.

**단점**: 룸 100개 이상이면 VCam 오브젝트가 히어라키를 복잡하게 만든다(성능은 괜찮지만 관리 부담). 현재 SmoothDamp 기반 코드와의 통합 시 기존 코드를 거의 폐기해야 한다. 레인월드 특유의 ±20px lean 효과나 커스텀 관성을 넣으려면 결국 CinemachineExtension을 작성해야 하므로 "반쪽 Cinemachine"이 될 위험이 있다.

**이 프로젝트 적합도**: ★★★☆☆ — Cinemachine을 현재 사용하지 않으므로 도입 오버헤드가 크고, 레인월드의 고유한 카메라 동작을 재현하려면 결국 커스텀 코드를 많이 써야 한다.

---

### 접근법 2: 커스텀 카메라 FSM (Cinemachine 없이)

**개요**: 현재 `SlimeCameraFollow.cs`를 확장하여 상태 기계(FSM)로 구조화한다. 핵심 상태는 **Follow**(룸 내 추적), **Transition**(룸 전환), **Locked**(고정/컷씬), **PipeTravel**(파이프 연출) 4가지다.

```csharp
public class RainWorldCamera : MonoBehaviour
{
    public Transform target;
    
    [Header("Follow Settings")]
    public float smoothTime = 0.35f;         // SmoothDamp 시간
    public float leanAmount = 0.3f;          // 레인월드식 ±lean 범위 (월드 단위)
    
    [Header("Transition")]
    public float transitionDuration = 0.4f;
    
    private Vector3 velocity;
    private Bounds currentRoomBounds;
    private CamState state = CamState.Follow;
    private float transTimer;
    private Vector3 transStart, transEnd;
    
    enum CamState { Follow, Transition, Locked, PipeTravel }
    
    void LateUpdate()
    {
        switch (state)
        {
            case CamState.Follow:
                FollowUpdate();
                break;
            case CamState.Transition:
                TransitionUpdate();
                break;
            case CamState.Locked:
                break; // 정지
            case CamState.PipeTravel:
                break; // 페이드 코루틴이 처리
        }
    }
    
    void FollowUpdate()
    {
        // 1. 목표 위치 계산 (플레이어 위치 + lean 오프셋)
        Vector3 desired = new Vector3(
            target.position.x,
            target.position.y,
            transform.position.z
        );
        
        // 2. SmoothDamp로 부드럽게 추적 (레인월드의 "뒤처지는" 느낌)
        Vector3 smoothed = Vector3.SmoothDamp(
            transform.position, desired, ref velocity, smoothTime
        );
        
        // 3. 룸 경계로 클램프
        transform.position = ClampToBounds(smoothed);
    }
    
    Vector3 ClampToBounds(Vector3 pos)
    {
        float halfH = Camera.main.orthographicSize;
        float halfW = halfH * Camera.main.aspect;
        
        float minX = currentRoomBounds.min.x + halfW;
        float maxX = currentRoomBounds.max.x - halfW;
        float minY = currentRoomBounds.min.y + halfH;
        float maxY = currentRoomBounds.max.y - halfH;
        
        // 룸이 카메라보다 작으면 중앙 고정
        if (minX > maxX) pos.x = currentRoomBounds.center.x;
        else pos.x = Mathf.Clamp(pos.x, minX, maxX);
        
        if (minY > maxY) pos.y = currentRoomBounds.center.y;
        else pos.y = Mathf.Clamp(pos.y, minY, maxY);
        
        return pos;
    }
    
    void TransitionUpdate()
    {
        transTimer += Time.deltaTime / transitionDuration;
        float t = Mathf.SmoothStep(0f, 1f, transTimer);
        transform.position = Vector3.Lerp(transStart, transEnd, t);
        
        if (transTimer >= 1f)
        {
            state = CamState.Follow;
            velocity = Vector3.zero; // 관성 리셋
        }
    }
    
    // 인접 룸 전환 (슬라이드)
    public void SlideToRoom(Bounds newBounds)
    {
        state = CamState.Transition;
        transStart = transform.position;
        currentRoomBounds = newBounds;
        transEnd = ClampToBounds(new Vector3(
            target.position.x, target.position.y, transform.position.z));
        transTimer = 0f;
    }
    
    // 원격 룸 전환 (파이프 — 페이드 필요)
    public void SetRoomImmediate(Bounds newBounds)
    {
        currentRoomBounds = newBounds;
        velocity = Vector3.zero;
        transform.position = ClampToBounds(new Vector3(
            target.position.x, target.position.y, transform.position.z));
        state = CamState.Follow;
    }
    
    public void SetState(CamState newState) => state = newState;
}
```

**레인월드식 "뒤처지는" 관성 구현**: `smoothTime`을 **0.3~0.5초**로 설정하면 카메라가 플레이어보다 약간 늦게 따라오는 느낌이 난다. 핵심은 `Vector3.SmoothDamp`가 프레임 레이트 독립적인 스프링-댐퍼 시뮬레이션이라는 점이다. Lerp 기반(`Lerp(pos, target, Time.deltaTime * speed)`)보다 훨씬 자연스럽고 안정적이다. 현재 `SlimeCameraFollow.cs`가 이미 SmoothDamp를 쓰고 있으므로, 기존 감각을 그대로 유지하면서 FSM만 감싸면 된다.

**룸 경계 관리**: 기존 BoxCollider2D 트리거를 그대로 활용한다. 트리거 진입 시 `RainWorldCamera.SlideToRoom(trigger.bounds)` 또는 `SetRoomImmediate()`를 호출한다.

```csharp
// RoomZone.cs — 기존 BoxCollider2D 트리거에 부착
public class RoomZone : MonoBehaviour
{
    private BoxCollider2D roomCollider;
    
    void Awake() => roomCollider = GetComponent<BoxCollider2D>();
    
    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            var cam = FindObjectOfType<RainWorldCamera>();
            cam.SlideToRoom(roomCollider.bounds);
        }
    }
}
```

**장점**: 현재 코드베이스와 가장 자연스럽게 통합된다. 외부 의존성 없음. 레인월드 특유의 lean, 관성, 스크린 셰이크를 모두 세밀하게 제어할 수 있다. 디버깅이 간단하다.

**단점**: Confiner 로직(화면 가장자리 클램프)을 직접 작성해야 한다(위 코드에 포함됨). 고급 기능(카메라별 데드존, 룩어헤드 등)이 필요하면 추가 구현 필요.

**이 프로젝트 적합도**: ★★★★★ — 현재 SmoothDamp 기반 코드를 FSM으로 감싸기만 하면 되므로 마이그레이션 비용이 가장 낮다.

---

### 접근법 3: Cinemachine + 커스텀 Extension 하이브리드

**개요**: 단일 CinemachineVirtualCamera에 CinemachineConfiner2D를 붙이고, 런타임에 BoundingShape2D를 교체한다. 추가로 `CinemachineExtension`을 작성해 lean 효과나 축 잠금 같은 레인월드 특유의 동작을 주입한다.

```csharp
// CinemachineLeanEffect.cs — 레인월드식 ±lean 오프셋
[SaveDuringPlay]
public class CinemachineLeanEffect : CinemachineExtension
{
    public float maxLean = 0.3f;  // 최대 오프셋 (월드 단위)
    public float leanSpeed = 2f;
    
    private Vector2 currentLean;
    
    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam,
        CinemachineCore.Stage stage,
        ref CameraState state,
        float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Finalize) return;
        
        var target = vcam.Follow;
        if (target == null) return;
        
        // 플레이어 위치 기반 lean 방향 계산
        Vector2 dir = (Vector2)target.position - (Vector2)state.RawPosition;
        Vector2 targetLean = Vector2.ClampMagnitude(dir * 0.1f, maxLean);
        currentLean = Vector2.Lerp(currentLean, targetLean, deltaTime * leanSpeed);
        
        var pos = state.RawPosition;
        pos.x += currentLean.x;
        pos.y += currentLean.y;
        state.RawPosition = pos;
    }
}
```

**Confiner 교체 코드**:
```csharp
public class RoomConfinerSwapper : MonoBehaviour
{
    [SerializeField] CinemachineConfiner2D confiner;
    
    public void SwitchRoom(Collider2D newBounds)
    {
        confiner.m_BoundingShape2D = newBounds;
        confiner.InvalidateCache(); // 캐시 재계산 — 비용 주의
    }
}
```

**주의 사항**: Unity 공식 문서가 명시하고 있다 — *"런타임에 BoundingShape2D를 교체하는 것보다, 여러 VCam을 두고 블렌딩하는 것이 훨씬 효율적이다."* `InvalidateCache()`의 초기 계산 비용이 크기 때문이다. 단, 룸이 모두 단순 BoxCollider2D라면 이 비용은 미미하다(복잡한 PolygonCollider2D일 때 문제).

**장점**: 히어라키가 깔끔하다(VCam 1개). Cinemachine의 인프라(임펄스, 노이즈 프로필, 타임라인 연동)를 활용 가능. Extension 시스템으로 모듈화가 좋다.

**단점**: Cinemachine 파이프라인 내부 이해 필요(Body, Aim, Noise, Finalize 스테이지). 2.x와 3.x 간 API 차이가 크다. 런타임 confiner 교체 비용 주의.

**이 프로젝트 적합도**: ★★★☆☆ — Cinemachine 도입 비용 대비 얻는 이점이 커스텀 FSM 대비 크지 않다.

---

## 연결 통로 연출 구체 구현

레인월드의 연결 통로(파이프) 전환은 크게 세 단계다: **진입 애니메이션 → 점선 점등 → 페이드/스냅**. 단일 씬 구조에서 원격 룸 간 전환 시 중간 지형이 노출되지 않도록 하는 것이 핵심이다.

**전체 시퀀스 수도코드**:
```csharp
public class PipeTransition : MonoBehaviour
{
    [SerializeField] CanvasGroup fadeOverlay;     // 검은 화면
    [SerializeField] PipeDotEffect dotEffect;     // 점선 점등 UI
    [SerializeField] float fadeTime = 0.3f;
    [SerializeField] float dotTravelTime = 0.8f;
    
    public IEnumerator ExecuteTransition(
        SlimeController player,
        RainWorldCamera cam,
        Transform exitPoint,
        Bounds destRoomBounds)
    {
        // 1단계: 플레이어 입력 차단 (IExternalController 활용)
        var extCtrl = player.GetComponent<IExternalController>();
        extCtrl.TakeControl();
        
        // 2단계: 진입 애니메이션 (플레이어가 파이프로 빨려들어감)
        yield return PlaySuckInAnimation(player, 0.3f);
        
        // 3단계: 페이드 아웃
        yield return Fade(0f, 1f, fadeTime);
        
        // 4단계: 텔레포트 (화면이 완전히 검을 때)
        player.TeleportTo(exitPoint.position);
        cam.SetRoomImmediate(destRoomBounds);
        yield return null; // 물리/렌더링 정착 대기
        
        // 5단계: 점선 점등 연출 (검은 화면 위에 표시)
        yield return dotEffect.PlayDotTravel(dotTravelTime);
        
        // 6단계: 페이드 인
        yield return Fade(1f, 0f, fadeTime);
        
        // 7단계: 출구 애니메이션 + 입력 반환
        yield return PlayPopOutAnimation(player, 0.2f);
        extCtrl.ReleaseControl();
    }
    
    IEnumerator Fade(float from, float to, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            fadeOverlay.alpha = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }
        fadeOverlay.alpha = to;
    }
}
```

**점선 점등 효과 구현**: Canvas에 작은 원형 Image들을 점선 경로를 따라 배치하고, 순차적으로 밝기를 올리는 방식이 가장 실용적이다.

```csharp
public class PipeDotEffect : MonoBehaviour
{
    [SerializeField] Image[] dots;         // 인스펙터에서 배열로 배치
    [SerializeField] int litWindowSize = 3; // 동시 점등 개수
    [SerializeField] Color dimColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
    [SerializeField] Color litColor = Color.white;
    
    public IEnumerator PlayDotTravel(float duration)
    {
        gameObject.SetActive(true);
        int total = dots.Length;
        float progress = 0f;
        float speed = (total + litWindowSize) / duration;
        
        // 모든 점 흐리게 초기화
        foreach (var d in dots) d.color = dimColor;
        
        while (progress < total + litWindowSize)
        {
            for (int i = 0; i < total; i++)
            {
                float dist = progress - i;
                dots[i].color = (dist >= 0 && dist < litWindowSize)
                    ? Color.Lerp(litColor, dimColor, dist / litWindowSize)
                    : dimColor;
            }
            progress += Time.deltaTime * speed;
            yield return null;
        }
        
        gameObject.SetActive(false);
    }
}
```

**중간 지형 노출 방지**: 페이드 오버레이의 alpha가 **1.0(완전 불투명)**인 상태에서만 텔레포트를 수행한다. Canvas를 Screen Space - Overlay 모드(Sort Order 999)로 설정하면 모든 렌더링 위에 표시되므로 카메라가 중간 지형을 렌더링하더라도 화면에는 보이지 않는다. 추가 안전장치로 `Camera.enabled = false`를 텔레포트 전후에 토글할 수 있지만, 오버레이만으로 충분하다.

**`IExternalController` 연동**: 전환 시작 시 `TakeControl()`로 SlimeController 입력을 탈취하여 전환 중 플레이어가 이동하지 못하게 하고, 전환 완료 후 `ReleaseControl()`로 반환한다. `TeleportTo()`는 Phase 4(화면이 검을 때)에서 호출하면 된다.

---

## 왜 커스텀 FSM이 이 프로젝트에 최적인가

**현재 코드베이스와의 정합성이 결정적이다.** `SlimeCameraFollow.cs`가 이미 SmoothDamp 기반으로 동작하므로, 이 위에 상태 분기(Follow/Transition/Locked/PipeTravel)만 추가하면 레인월드 카메라의 핵심 동작을 모두 구현할 수 있다. Cinemachine을 새로 도입하면 기존 SmoothDamp 로직을 폐기하고, 패키지 의존성을 추가하고, Cinemachine 파이프라인 동작 방식을 학습해야 한다 — 그런데 결국 레인월드 특유의 lean 효과나 관성을 구현하려면 CinemachineExtension을 직접 작성해야 하므로 **"Cinemachine이 대신 해주는 것"이 별로 없다.**

Cinemachine이 진정한 가치를 발휘하는 경우는 **다양한 카메라 행동 간 자동 블렌딩**(예: 3인칭 → 1인칭 전환, 컷씬 카메라 등)이 필요할 때다. 룸 기반 2D 카메라에서는 CinemachineConfiner2D의 화면 가장자리 클램프가 유일한 이점인데, 이 로직은 위 `ClampToBounds()` 함수 **10줄**로 대체된다.

**Cinemachine이 유리한 유일한 시나리오**: 향후 보스전 전용 카메라, 줌 인/아웃, 카메라 흔들림(Impulse) 같은 고급 연출이 대량으로 필요해지는 경우. 그때 도입해도 늦지 않다.

---

## 단계별 구현 순서 제안

| 단계 | 작업 | 예상 난이도 |
|------|------|-------------|
| **1** | `SlimeCameraFollow.cs`를 `RainWorldCamera.cs`로 리팩터링 — FSM 구조 도입, Follow 상태에서 기존 SmoothDamp 유지 | 낮음 |
| **2** | `ClampToBounds()` 추가 — 현재 룸의 BoxCollider2D.bounds로 카메라 위치 제한 | 낮음 |
| **3** | `RoomZone.cs` 작성 — 기존 BoxCollider2D 트리거에서 룸 전환 감지, 인접 룸이면 `SlideToRoom()`, 원격 룸이면 페이드 전환 | 중간 |
| **4** | Transition 상태 구현 — SmoothStep 기반 카메라 슬라이드 | 낮음 |
| **5** | 페이드 오버레이 시스템 구축 — Canvas + CanvasGroup + 코루틴 | 낮음 |
| **6** | 파이프 전환 시퀀스 구현 — `PipeTransition.cs` (진입 → 페이드 → 텔레포트 → 페이드 인) | 중간 |
| **7** | 점선 점등 효과 추가 — `PipeDotEffect.cs` | 중간 |
| **8** | Lean 효과 추가 — Follow 상태에서 플레이어 이동 방향으로 ±lean 오프셋 | 낮음 |
| **9** | 폴리시 — 스크린 셰이크, 룸별 카메라 설정(오쏘 사이즈 등) 데이터화 | 중간 |

---

## 핵심 vs 생략 가능 요소

"레인월드 카메라 느낌"을 내기 위해 **반드시** 구현해야 하는 요소와 생략 가능한 요소를 구분하면 다음과 같다.

**반드시 구현 (느낌의 80%를 결정)**: 룸 단위 카메라 구속(ClampToBounds)이 가장 중요하다. 카메라가 룸 밖을 절대 보여주지 않는 것만으로도 레인월드의 폐쇄적·밀실적 분위기가 살아난다. 두 번째로 **룸 전환 시 하드 컷 또는 빠른 슬라이드**(0.3초 이하)가 필수다. 세 번째로 **SmoothDamp의 smoothTime을 0.3~0.5초로 설정**하여 카메라가 플레이어보다 "느릿하게" 따라오는 관성감을 준다.

**권장 구현 (느낌의 15%)**: 파이프 전환 시 페이드 + 점선 점등 연출. 이것이 없으면 "레인월드 오마주"라는 인상이 약해진다. Lean 효과(±소량 오프셋)도 미묘하지만 깊이감에 기여한다.

**생략 가능 (나머지 5%)**: 바닐라 레인월드의 "멀티 스크린 룸 내 하드 컷"(프리렌더 배경이 아니므로 불필요), 파이프 입구 심볼 플래시 색상 차별화, 생물 크기별 점등 세그먼트 수 차이. 이들은 레인월드의 세밀한 디테일이지 "느낌"의 핵심이 아니다.

---

## 결론: 최소 노력으로 최대 레인월드 느낌을 얻는 법

레인월드 카메라의 본질은 놀랍도록 단순하다 — **고정 스크린 + 미세 흔들림 + 하드 컷 전환**. 화려한 카메라 워크의 부재 자체가 게임의 불안하고 제한된 시야를 만드는 디자인이다. 이 프로젝트에서는 이를 "SmoothDamp 기반 부드러운 추적 + 엄격한 룸 클램프"로 재해석하면 된다. 기존 `SlimeCameraFollow.cs`에 FSM 껍데기와 `ClampToBounds()`를 추가하는 것이 **1단계이자 가장 임팩트 있는 변화**이며, 여기에 페이드 전환 코루틴과 점선 UI 연출을 쌓아 올리면 레인월드의 숏컷 파이프 경험을 충실히 재현할 수 있다. Cinemachine은 이 특정 사용 사례에서는 **해결책보다 우회로**에 가깝다.