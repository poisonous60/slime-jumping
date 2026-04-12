# Tier 1 명세 #001 — InteractableBase + 엘레베이터(#11)

> Claude Code 위임용 명세. 두 작업은 독립적이며 병렬 진행 가능.
> 작업 A는 후속 sit-pose/미디어/폭죽 인터랙터블의 기반.
> 작업 B는 작업 A에 의존하지 않음.

---

## 작업 A — InteractableBase 추상 클래스 + SlimeVisual.IsExternallyAnimated 플래그

### A-1. InteractableBase

**경로**: `Assets/Scripts/Interaction/InteractableBase.cs`

**목적**: 모든 IInteractable 구현체의 공통 코드 중복 제거. sit-pose, 미디어, 폭죽, 퍼즐문, 메모 등 후속 인터랙터블이 상속.

**책임**:
- `IInteractable` 구현
- 공통 직렬화 필드 노출 (animator, interactPrompt, cooldown)
- 쿨다운 관리 (`CanInteract`에서 시간 체크, `Interact()` 호출 시 쿨다운 시작)
- 서브클래스가 오버라이드할 훅 제공 — 실제 상호작용 동작은 서브클래스가 정의
- `Interactable` 레이어(10) 자동 검증 (Awake에서 레이어 미스매치 시 LogWarning)

**필드 (직렬화)**:
- `Animator animator` — 옵션. null 가능 (sit-pose 등 외부 컨트롤러가 직접 변형 주도하는 경우)
- `string interactPrompt` — UI 표시용 ("앉기", "재생", "올라타기" 등)
- `float cooldown` — 기본값 0. 폭죽처럼 연발 방지 필요한 경우 사용
- `bool startsActive` — 기본값 true. SignalSource로 켜고 끄는 케이스 대비

**API 시그니처 (서브클래스 오버라이드 지점)**:
```csharp
public abstract class InteractableBase : MonoBehaviour, IInteractable {
    // IInteractable 구현 (sealed override 권장 — 서브클래스가 OnInteract만 건드리도록)
    public virtual bool CanInteract(SlimeController slime); // 쿨다운 + IsActive + 서브클래스 추가 조건
    public void Interact(SlimeController slime);            // 쿨다운 시작 → OnInteract 호출
    public string InteractPrompt { get; }

    // 서브클래스용
    protected abstract void OnInteract(SlimeController slime);
    protected virtual bool ExtraCanInteract(SlimeController slime) => true;
    public bool IsActive { get; protected set; }            // SignalSource로 토글 가능
}
```

**주의**:
- IInteractable의 기존 시그니처 (`InteractPrompt` 프로퍼티명 등) 정확히 일치시킬 것. 가이드 #IInteractable 섹션 참조.
- `Interact()`가 OnInteract를 호출하는 시점에 쿨다운을 *먼저* 시작해야 함. OnInteract 내부에서 SetOverride 호출 시 슬라임의 InteractionDetector가 ClearCandidates 하므로, 쿨다운이 그 이전에 기록되어야 다음 ReleaseOverride 후 즉시 재호출 방지가 됨.

---

### A-2. SlimeVisual.IsExternallyAnimated 플래그

**대상 파일**: `Assets/Scripts/Visual/SlimeVisual.cs` (또는 SlimeVisual이 정의된 파일)

**목적**: 외부 컨트롤러(IExternalController)가 슬라임 변형을 직접 주도할 때, 일반 squash/stretch 파이프라인을 비활성화.

**사용 케이스**:
- sit-pose (#15 침대/소파/신사 등) — 컨트롤러가 슬라임을 의자 모양에 맞게 정적으로 배치
- 폭죽 발사 반동 (#15) — 컨트롤러가 일시적으로 짜부 → 튀어나가는 변형 직접 적용
- #31 파이프 전환 — 슬라임이 파이프 통과 모양으로 변형
- #29 녹다운 연출 — Timeline이 변형 주도

**API**:
```csharp
public class SlimeVisual : MonoBehaviour {
    public bool IsExternallyAnimated { get; set; }
    // ... 기존 코드
}
```

**구현 요구사항**:
- `IsExternallyAnimated == true`일 때, 매 프레임 squash/stretch/sag 계산 파이프라인 전체 스킵
- Body/Face 시스템 모두 적용 — Body 변형 비활성, Face는 외부에서 직접 morph target / blendshape / shader param 조작 가능하게 노출 필요할 수도 있음 (당장은 Body 비활성만)
- `IsExternallyAnimated`가 true → false로 전환되는 순간, 변형 상태 자연스럽게 복귀:
  - 옵션 1: 즉시 deformation 0으로 리셋 (기존 파이프라인이 다음 프레임부터 alive 변형 누적)
  - 옵션 2: 짧은 lerp (0.1~0.2초)로 기본 형태 복귀
  - **권장: 옵션 1.** 외부 컨트롤러가 release 시점에 슬라임을 적절한 지점에 두기로 하므로, 기본 형태 즉시 복귀가 가장 예측 가능.
- shader param들도 동일 정책 적용 (charging dip, egg-shape param 등 모두 0/기본값으로)

**주의**:
- 기존 파이프라인의 어느 함수에 가드를 넣을지는 코드 보고 결정. 가능한 진입점 한 곳(예: 매 프레임 squash/stretch를 누적하는 메인 메서드 진입부)에서 early-return.
- IsExternallyAnimated가 토글되는 동안 SurfaceProbe 계산은 *유지*되어야 함 (외부 컨트롤러가 ReleaseControl 후 정상 attach 복귀할 때 surface 정보 즉시 활용). SurfaceProbe는 SlimeVisual과 분리되어 있으므로 영향 없을 것으로 예상.

---

## 작업 B — 엘레베이터 (#11) RiderActivated WaypointPlatform

### B-1. 설계 결정 요약

| 항목 | 결정 |
|------|------|
| 트리거 | 슬라임 탑승 (RiderCount > 0) |
| 동작 | 핑퐁 + 웨이포인트 도착 시 대기 |
| 탑승 중 작동 조건 | RiderCount > 0이면 진행, 0이면 정지 |
| 탑승 이탈 시 | **현재 구간(다음 웨이포인트까지)은 마저 가고 정지.** 도착 후 대기 끝났을 때 RiderCount==0이면 거기서 멈춤. 다음 탑승자 오면 그 위치부터 재개. |
| 도착 시 효과 | 시각적 "덜컹" — 미세 오버슛+스냅백. 스프링-댐퍼 출렁 X. 카메라 임펄스 X. |
| 비주얼 모델 | 통짜 메시 (`Assets/Prebs/building/old_elev`). rig+애니메이션 클립 사용 금지. |
| W키 호출 | 사용 안 함. 따라서 IInteractable 구현 안 함. |

### B-2. 컴포넌트 구성

엘레베이터 prefab의 컴포넌트 스택:

```
ElevatorRoot (GameObject)
├─ WaypointPlatform           // 기존 클래스 사용
├─ RiderActivatedMotor        // 신규 — RiderCount 기반 진행/정지 제어
├─ PlatformRiderSensor        // 기존 — 자식 트리거 콜라이더 + RiderCount 노출
├─ PlatformCarrier            // 기존 — 슬라임 캐리
├─ ElevatorJoltVisual         // 신규 — 도착 시 시각 덜컹
└─ Visual (자식 GameObject, MeshRenderer)  // old_elev 메시. 본체 transform과 분리되어 ElevatorJoltVisual이 이걸 흔듦.
```

### B-3. 신규 클래스 1: RiderActivatedMotor

**경로**: `Assets/Scripts/Mechanisms/RiderActivatedMotor.cs`

**목적**: WaypointPlatform의 진행/정지를 PlatformRiderSensor.RiderCount 기반으로 게이팅.

**책임**:
- WaypointPlatform과 PlatformRiderSensor를 GetComponent로 참조
- WaypointPlatform이 "현재 웨이포인트로 이동 중인지 / 대기 중인지" 상태를 노출하는지 확인 필요. 노출하지 않으면 WaypointPlatform에 다음 중 하나 추가:
  - `bool IsTraveling { get; }` 프로퍼티
  - 또는 `OnSegmentArrived` 이벤트 (도착 직후 발화, 대기 시작 전)
- 게이팅 로직:
  - `IsTraveling == true`이면 항상 진행 허용 (현재 구간 마저 감)
  - 도착 → 대기 시작 → **대기 끝나는 순간** RiderCount 체크
    - RiderCount > 0: 다음 구간으로 진행
    - RiderCount == 0: 진행 안 함. WaypointPlatform을 "대기 무한 연장" 상태로 유지.
  - 정지 상태에서 RiderCount가 0 → 1+로 변하면 즉시 다음 구간으로 진행 시작

**구현 권장사항**:
- WaypointPlatform이 자체 진행 로직을 가지고 있다면, 진행 트리거를 외부에서 게이팅할 수 있는 인터페이스 추가가 필요할 수 있음. 두 가지 접근:
  - **접근 1 (선호)**: WaypointPlatform에 `Func<bool> CanAdvance` 콜백 슬롯 추가. RiderActivatedMotor가 Awake에서 `waypointPlatform.CanAdvance = () => sensor.RiderCount > 0`로 설정.
  - **접근 2**: WaypointPlatform을 상속한 `RiderActivatedWaypointPlatform` 신설. Override로 진행 결정 가로채기.
  - 접근 1을 선호 — WaypointPlatform이 #6 회전 플랫폼 등 다른 변형으로도 확장 예정이라 콜백 슬롯이 더 유연.

**WaypointPlatform 핑퐁/대기시간 미지원 시**:
- 가이드는 핑퐁/대기 지원한다고 명시. 만약 코드가 다르면 명세 어긋남이니 **즉시 보고 후 진행 중단.**

### B-4. 신규 클래스 2: ElevatorJoltVisual

**경로**: `Assets/Scripts/Mechanisms/ElevatorJoltVisual.cs`

**목적**: 웨이포인트 도착 시 시각적 "덜컹" 효과. 콜라이더/물리 본체는 정확히 멈추되, 시각 메시만 짧게 튕김.

**책임**:
- 인스펙터에서 시각 자식 Transform 참조 (`visualTransform`)
- WaypointPlatform의 `OnSegmentArrived` 이벤트 (또는 동등한 훅) 구독
- 도착 시점에 진행 방향 *반대*로 작은 로컬 오프셋 적용 후 빠르게 0으로 lerp:
  - 오프셋 크기: 기본값 0.05 유닛 (직렬화)
  - 지속시간: 기본값 0.12초 (직렬화)
  - 이징: ease-out (1 - (1-t)^2 등). 빠르게 튕긴 후 부드럽게 정착.
- 진행 방향은 WaypointPlatform에서 마지막 이동 방향 벡터를 가져옴. WaypointPlatform이 노출 안 하면 RiderActivatedMotor에서 캐싱해서 ElevatorJoltVisual에 전달.

**중요 — PlatformCarrier 간섭 차단**:
- PlatformCarrier는 본체 transform의 delta를 슬라임에 전달함. 시각 자식 Transform의 흔들림은 본체 transform과 무관해야 함 (그래야 슬라임이 함께 흔들리지 않음).
- 시각 메시는 PlatformCarrier가 본체로 인식하는 transform의 *자식*이어야 함. 본체 transform은 정확히 웨이포인트에 정지, 자식만 로컬 좌표에서 흔들림.
- 콜라이더는 본체에 부착 (자식 시각 메시에는 부착하지 않음).

**튜닝 노트**:
- 0.05 유닛 / 0.12초가 어색하면 사용자가 인스펙터에서 조정. 큰 출렁은 명시적으로 원하지 않으므로 이 값 이상 권장하지 말 것.

### B-5. 씬 배치 (사용자 작업)

이건 Claude Code 작업이 아니라 사용자가 Unity 에디터에서 할 작업 — 명세에 포함하는 이유는 prefab 구조를 코드 결정과 일치시키기 위함:

- `old_elev` 메시 prefab을 `Visual` 자식 GameObject에 배치
- 본체 Transform에 BoxCollider2D (탑승면) + WaypointPlatform + RiderActivatedMotor + PlatformCarrier
- PlatformRiderSensor용 자식 GameObject에 트리거 BoxCollider2D (탑승면 바로 위, 슬라임 감지용)
- ElevatorJoltVisual의 `visualTransform` 필드에 Visual 자식 할당
- WaypointPlatform 웨이포인트 2~3개 설정, 핑퐁 모드, 대기시간 1~2초 권장

---

## 작업 순서 권장

A-1 (InteractableBase) → A-2 (IsExternallyAnimated) → B (엘레베이터 전체)

또는 A와 B를 별도 PR/커밋으로 분리해서 병렬 진행. A는 빈 컴파일만 통과시키고 후속 작업에서 활용, B는 즉시 씬 배치까지 가능.

## 검증 체크리스트

작업 A:
- [ ] InteractableBase 상속한 더미 클래스로 컴파일 확인
- [ ] 쿨다운 0인 케이스, 양수인 케이스 모두 동작
- [ ] SlimeVisual.IsExternallyAnimated = true → 슬라임 변형 정지, false → 즉시 기본 형태 복귀

작업 B:
- [ ] 슬라임 미탑승 → 정지 유지
- [ ] 슬라임 탑승 → 다음 웨이포인트로 이동, 도착 → 대기 → 다음으로 (탑승 유지 시 무한 핑퐁)
- [ ] 이동 도중 슬라임 이탈 → 현재 구간 마저 가고 도착 후 정지
- [ ] 정지 상태에서 다른 슬라임(또는 같은 슬라임) 다시 탑승 → 즉시 다음 구간으로 진행
- [ ] 도착 시 시각 메시만 덜컹, 슬라임은 함께 흔들리지 않음
- [ ] 본체 Transform은 정확히 웨이포인트 좌표에 정지 (콜라이더 어긋남 없음)
