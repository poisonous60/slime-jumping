# Tier 1 명세 #002 — Sit-pose / 미디어 / 동작형 / 점프스케어 / 카메라플래시 / CCTV / Hazard

> Claude Code 위임용 일괄 명세. 각 섹션은 독립적이라 묶음/분할 위임 모두 가능.
> **전제**: 명세 #001의 InteractableBase + SlimeVisual.IsExternallyAnimated 완료 후 진행.
> 단, §6 (점프스케어/카메라플래시 공용 인프라), §8 (CCTV), §9 (Hazard)는 InteractableBase 의존 없음 — 병렬 진행 가능.
>
> **진행 상황** (2026-04-12):
> - 묶음 A (§1 Impulse 인프라 + §5 점프스케어 + §6 카메라플래시) ✅ 완료
> - 묶음 B (§2 Sit-pose + §3 미디어 + §4 동작형) — 미착수
> - 묶음 C (§7 CCTV + §8 Hazard) — 미착수

---

## §1. 공용: Cinemachine Impulse 인프라 ✅ 구현 완료 (묶음 A)

- Cinemachine 3.x 설치 완료. SampleScene Main Camera에 `CinemachineBrain`, SlimeVCam에 `CinemachineCamera + PositionComposer + CinemachineImpulseListener`.
- `ImpulseEmitter` 래퍼 ([Assets/Scripts/Effects/ImpulseEmitter.cs](../../SlimeJumping_v2/Assets/Scripts/Effects/ImpulseEmitter.cs)) 구현. 튜닝 프리셋은 [기능_구현_가이드.md 공용 VFX 인프라 §3](../기능_구현_가이드.md) 참조.

---

## §2. Sit-pose 인터랙터블 (#15) — 침대/신사/소파/모닥불

**경로**: `Assets/Scripts/Interaction/SitPoseInteractable.cs`

### 패턴
1개 generic 클래스 + per-prefab 인스펙터 설정. 4개 prefab 모두 같은 클래스 사용:
- BedDou (침대), japanes (신사 앞), Sofa (소파), Bonfire (모닥불 옆 자리)

### 클래스 설계
```
SitPoseInteractable : InteractableBase, IExternalController
```

**직렬화 필드**:
- `Transform sitAnchor` — 슬라임이 앉는 월드 위치/회전. 인스펙터에서 자식 GameObject 할당.
- `bool freezePhysics` — 기본 true. 앉은 동안 슬라임 velocity/gravity 영향 정지.
- `bool consumeWInput` — 기본 true. 일어날 때 W 입력이 즉시 다른 인터랙터블 발동시키지 않게 한 프레임 차단.

### 동작 흐름
1. **W 첫 입력 (Interact 호출)**:
   - `slime.SetOverride(this)` 호출 (자동으로 InteractionDetector.ClearCandidates 됨)
   - `slime.GetComponent<SlimeVisual>().IsExternallyAnimated = true`
   - 슬라임을 sitAnchor 위치/회전으로 텔레포트 (`OverrideVelocity(Vector2.zero)` 그리고 transform 직접 설정)
2. **앉아있는 동안 (ExternalUpdate)**:
   - W 키 입력 폴링 (`Input.GetKeyDown(KeyCode.W)`)
   - 입력 감지 → ReleaseControl 시퀀스 진입
3. **ExternalFixedUpdate**:
   - `freezePhysics == true`이면 `OverrideVelocity(Vector2.zero)` 매 프레임 호출 (혹시 잔류 속도 있을 시)
4. **Release 시퀀스**:
   - `slime.ReleaseOverride(this)`
   - `SlimeVisual.IsExternallyAnimated = false` (즉시 기본 형태 복귀)
   - `consumeWInput`이 true면 1프레임 동안 입력 소비 플래그 (ReleaseOverride 직후 슬라임 입력 시스템에 한 프레임 차단 신호 — 구현 어렵다면 sitAnchor 근처 다른 인터랙터블이 없게 prefab 배치로 회피)

### 주의
- **GravityField orphan 방지** (가이드 IExternalController 주의사항): sitAnchor가 GravityField 영역 안에 있으면 release 시 슬라임이 그 zone 안에서 깨어남. 일반 케이스라 문제없을 것이나, 만약 sitAnchor가 zone 경계에 있으면 release 후 위치 확인 필수.
- **시각**: IsExternallyAnimated가 true인 동안 슬라임은 정적 형태. 추가 변형(예: "앉은 모양으로 살짝 납작") 원하면 SlimeVisual에 별도 sit-pose 모드 추가 필요 — 이번 명세 범위 밖, 일단 기본 형태로 앉음.
- 모닥불 옆 자리 (Bonfire)는 별도 IHazardSource를 가질 가능성 있음 (모닥불 자체) — 이건 §9에서 다루지 않고 미래 작업. 현재는 모닥불 prefab의 "옆 자리" sitAnchor만 SitPoseInteractable로.

---

## §3. 미디어 인터랙터블 (#15) — TV / 축음기

### §3.1 TV (TV LED prefab)

**경로**: `Assets/Scripts/Interaction/MediaInteractable_TV.cs`

```
MediaInteractable_TV : InteractableBase
```

**직렬화 필드**:
- `AudioSource audioSource` — TV 본체에 부착, spatialBlend=1 (3D)
- `AudioClip onClip` / `AudioClip offClip` — null 가능 (한 쪽만 있어도 동작)
- `Renderer screenRenderer` — TV 화면 영역 메시 (통짜 모델이면 머티리얼 인덱스로 분리)
- `Material onMaterial` / `Material offMaterial` — 화면 텍스처 교체용
- `int screenMaterialIndex` — 기본 0

**동작**:
- `OnInteract`: 토글 (`isOn = !isOn`)
  - audioSource.clip = isOn ? onClip : offClip; audioSource.Play() / Stop()
  - screenRenderer.materials[screenMaterialIndex] 교체
- override 사용 안 함 (W 누르고 즉시 슬라임은 자유)

**노트**: WebView는 본 작업 단계로 미룸 (대화.md 명시).

### §3.2 축음기 (phonograph prefab)

**경로**: `Assets/Scripts/Interaction/MediaInteractable_Phonograph.cs`

```
MediaInteractable_Phonograph : InteractableBase
```

**직렬화 필드**:
- `AudioSource audioSource`
- `AudioClip[] tracks` — 여러 곡 선택 시 순환 재생용. 1개도 OK.
- `Transform discTransform` — 회전할 디스크 자식 Transform
- `float discRPM` — 기본 33.3 (인스펙터 조정)

**동작**:
- `OnInteract`: 재생 중이면 정지, 아니면 다음 트랙 재생 (인덱스 순환)
- `Update`: 재생 중일 때만 `discTransform.Rotate(Vector3.forward, discRPM * 6 * dt)` (6 = 360/60)
- override 사용 안 함

**노트**: 디스크 회전은 외부 애니메이션 클립 아님 — 코드 기반 회전이라 비주얼 변형 원칙 위배 X.

---

## §4. 동작형 인터랙터블 (#15) — 세탁기 / 폭죽

### §4.1 세탁기 (Washing prefab)

**미해결 결정 1개 — 사용자 확인 필요**:

- **옵션 A**: 슬라임이 세탁기에 들어가서 같이 회전 (sit-pose 응용 + 슬라임 회전). 통짜 모델이라 세탁기 *내부* 회전은 못 보여줘서 슬라임 자체 회전으로 표현.
- **옵션 B**: 슬라임은 세탁기 위에 앉는 sit-pose, 세탁기 본체에 미세 진동 (스프링-댐퍼 흔들림). "켜기" 토글식.

**권장: 옵션 B.** 이유:
1. 통짜 모델이라 내부 회전이 어차피 안 보임
2. 세탁기를 "켜고 끄는" 인터랙션이 더 자연스러움 (한번 누르고 자유롭게 돌아다닐 수 있음)
3. 옵션 A는 sit-pose + 회전 변형 추가 작업 — Tier 2급으로 격상될 수 있음

**옵션 B 명세**:
```
ApplianceInteractable_Washing : InteractableBase
```
- 토글 인터랙션 (override 없음)
- 켜진 동안 본체 Transform에 작은 sin 진동 (amplitude 0.02, freq 8Hz 기본)
- 진동은 시각 자식 Transform에만 적용 (콜라이더 영향 없게 — 엘레베이터 ElevatorJoltVisual 패턴과 동일 원칙)
- AudioClip 켜질 때 루프 재생 (옵션)

옵션 A를 원하면 알려줘 — 별도 명세 필요.

### §4.2 폭죽 (firework prefab)

**경로**: `Assets/Scripts/Interaction/FireworkInteractable.cs`

```
FireworkInteractable : InteractableBase
```

**직렬화 필드**:
- `Vector2 launchVelocity` — 슬라임에 가할 외부 속도. 기본 (0, 18). 인스펙터에서 조정.
- `bool launchAwayFromCenter` — 기본 false. true면 폭죽-슬라임 방향 벡터로 발사.
- `ParticleSystem fireworkVFX` — VFXType=Fire 또는 폭발 프리팹
- `AudioSource audioSource` + `AudioClip launchSfx`
- `ImpulseEmitter impulseEmitter` — §1 참조. force 0.3 권장.
- `float interactCooldown` — 기본 1.5초 (InteractableBase의 cooldown 필드 사용)

**동작 (OnInteract)**:
1. `slime.AddExternalVelocity(launchVelocity)` — `launchAwayFromCenter == true`이면 (slime.position - this.position).normalized * launchVelocity.magnitude
2. fireworkVFX.Play()
3. audioSource.PlayOneShot(launchSfx)
4. impulseEmitter.Emit(0.3f)
5. (쿨다운은 InteractableBase가 자동 처리)

**노트**:
- override 사용 안 함 — 슬라임은 발사 직후 일반 Airborne 상태로 자유 비행
- AddExternalVelocity는 일반 채널이라 BlockAttach 자동 처리 안 됨. 폭죽 발사 후 즉시 옆 벽에 붙는 게 어색하면 인스펙터에서 launchVelocity 충분히 크게 설정.

---

## §5. 점프스케어 (#19) — Jumpscare prefab ✅ 구현 완료 (묶음 A)

- [JumpscareTrigger.cs](../../SlimeJumping_v2/Assets/Scripts/Mechanisms/JumpscareTrigger.cs) + [JumpscareCanvasController.cs](../../SlimeJumping_v2/Assets/Scripts/UI/JumpscareCanvasController.cs)
- 슬라임 식별은 `GetComponentInParent<SlimeController>()` (프로젝트 컨벤션, Tag 미사용)
- Canvas는 공유 루트 `FullscreenEffectCanvas > JumpscareEffect` 자식에 배치 — [기능_구현_가이드.md 공용 VFX 인프라 §4](../기능_구현_가이드.md) 참조
- 에디터 테스트 setup: `SlimeJumping > Setup Jumpscare Test`

---

## §6. 카메라 플래시 (#25) — studio_camera prefab ✅ 구현 완료 (묶음 A)

- [CameraFlashHazard.cs](../../SlimeJumping_v2/Assets/Scripts/Mechanisms/CameraFlashHazard.cs) + [FlashCanvasController.cs](../../SlimeJumping_v2/Assets/Scripts/UI/FlashCanvasController.cs)
- 명세와 달라진 부분:
  - **주기 타이머 제거** → 트리거 진입 1회 발동(`oneShot` 기본 true). JumpscareTrigger 패턴 통일.
  - **self-contained**: `CameraFlashHazard`가 `[RequireComponent(Collider2D)]` + `OnTriggerEnter2D` 직접 처리. 별도 area 컴포넌트 없음.
  - 사운드 책임 분리: 본체 `AudioSource`(3D positional)만 재생, FlashCanvas는 시각(디머 흰 페이드 α 0.5 + 뷰파인더 오버레이)만.
  - 뷰파인더는 4 모서리 L 브래킷 + 중앙 십자선 UI Image 10개로 자동 생성 (에셋 불필요).
- 에디터 테스트 setup: `SlimeJumping > Setup Camera Flash Test`
- 미구현: RenderTexture 스냅샷 (명세 보류). Light2D bulb 펄스는 구현되지만 URP 2D Renderer 미구성 환경에선 비가시.

---

## §7. CCTV (#26) — Surveilla prefab

**경로**: `Assets/Scripts/Mechanisms/CCTVTracker.cs`

**카테고리**: 인테리어. **SlimeController 무접촉.** 게임플레이 영향 없음.

### 컴포넌트 구성
- Surveilla prefab + CCTVTracker 컴포넌트
- 천장 매달림 인테리어. 콜리전:
  - **Z=0 (게임플레이 평면)**: BoxCollider2D를 천장면에 부착 (슬라임이 본체에 부딪힐 수 있게)
  - **그 외 Z (배경/전경)**: SpriteRenderer (또는 MeshRenderer)만, 콜라이더 없음
  - 이건 prefab variant 두 개로 분리하거나 인스펙터에서 토글하는 식. 권장: prefab variant 분리.

### CCTVTracker 클래스
```csharp
public class CCTVTracker : MonoBehaviour {
    [SerializeField] Transform headTransform;     // 회전할 카메라 헤드 자식
    [SerializeField] Transform target;            // null 가능 — null이면 SlimeController.Instance 자동 탐색
    [SerializeField] float maxAngleDeg = 90f;     // 회전 가능 범위 (양쪽 ±max)
    [SerializeField] float angularSpeed = 90f;    // deg/s, 추적 속도 한계
    [SerializeField] float idleAngle = 0f;        // 타겟 없을 때 복귀 각도
    [SerializeField] float trackingRange = 20f;   // 이 거리 밖이면 idle로 복귀
}
```

**동작 (Update)**:
1. 타겟 위치 확인 (자동 탐색 시 SlimeController 한 번만 캐싱)
2. 거리 > trackingRange 또는 타겟 null → idleAngle 향해 lerp (angularSpeed 제한)
3. 그 외 → `Mathf.Atan2((target.position - this.position).y, (target.position - this.position).x)` 계산
4. headTransform의 forward 축 (또는 prefab의 헤드 forward 정의에 맞게)이 그 각도 향하게 회전
5. maxAngleDeg 클램프 (this.transform.right 기준 상대 각도)
6. angularSpeed로 회전 속도 제한 (Quaternion.RotateTowards)

**노트**:
- 슬라임이 CCTV 본체에 닿아도 어떤 효과도 없음 — 순수 장식. (Z=0 케이스에서 BoxCollider2D는 단순 지형 콜라이더로 작동. SurfaceTag로 일반 surface 지정.)

---

## §8. Hazard 일괄 — #3 원형톱날 / #12 칼

### §8.1 원형톱날 (BLADE1 prefab)

**경로**: `Assets/Scripts/Mechanisms/SawBladeHazard.cs`

**패턴**: 기존 `SpikeHazard.cs` 참조 + 회전 + 회전 가속도 전달.

```
SawBladeHazard : MonoBehaviour, IHazardSource
```

**직렬화 필드**:
- `Transform bladeTransform` — 회전할 톱날 자식
- `float rotationRPM` — 기본 600 (시각용)
- `float knockbackForce` — 기본 25
- `float stunDuration` — 기본 1.0
- `float spinTransferStrength` — 기본 8. 톱날 회전 방향 접선 속도를 슬라임에 전달.

**IHazardSource 구현**:
```csharp
HazardEffect GetHazardEffect() {
    Vector2 toSlime = (cachedSlime.position - bladeTransform.position).normalized;
    Vector2 tangent = new Vector2(-toSlime.y, toSlime.x) * Mathf.Sign(rotationRPM);
    return new HazardEffect {
        knockbackForce = knockbackForce,
        knockbackDirection = (toSlime * 0.5f + tangent * 0.5f).normalized, // 반경 + 접선 합성
        stunDuration = stunDuration,
        damageType = DamageType.SawBlade
    };
}
bool IsActive => true;
```

**Update**:
- `bladeTransform.Rotate(Vector3.forward, rotationRPM * 6 * dt)` (외부 클립 X, 코드 회전)

**충돌 처리**:
- 자식 CircleCollider2D (트리거) — `OnTriggerEnter2D`에서 슬라임 캐싱 후 ApplyHazardEffect
- SpikeHazard.cs의 충돌 처리 패턴 그대로 따라가되, GetHazardEffect에서 슬라임 위치 참조하므로 호출 직전 cachedSlime 셋팅 필요. 또는 GetHazardEffect에 SlimeController 인자 추가하는 식으로 인터페이스 확장 검토 — **단, 인터페이스 변경은 다른 IHazardSource 구현체(SpikeHazard) 영향 있으니 신중. 기존 인터페이스 유지하고 cachedSlime 패턴으로 가는 것 권장.**

**비주얼**:
- 톱날 가장자리 trail 효과 (선택, Particle System Trail 모듈)
- HDR Emission 살짝 (위험 표시)

### §8.2 칼 (cleaver prefab)

**경로**: `Assets/Scripts/Mechanisms/KnifeHazard.cs`

**패턴**: 칼날면은 IHazardSource, 옆면은 SurfaceTag로 일반 표면 (attach 가능).

### 콜라이더 구성
- **칼날면**: 자식 GameObject, EdgeCollider2D 또는 BoxCollider2D (얇음), Trigger=true, KnifeHazard 컴포넌트
- **옆면**: 칼 본체 BoxCollider2D, Trigger=false, SurfaceTag로 일반 surface 지정

### KnifeHazard 클래스
```
KnifeHazard : MonoBehaviour, IHazardSource
```

**직렬화 필드**:
- `float velocityThreshold` — 기본 4. 임계치 이상 충돌 속도일 때만 위험 발동.
- `float knockbackForce` — 기본 15
- `float stunDuration` — 기본 1.2

**IHazardSource 구현**:
```csharp
HazardEffect GetHazardEffect() => new HazardEffect {
    knockbackForce = knockbackForce,
    knockbackDirection = Vector2.zero, // gravityUp 자동
    stunDuration = stunDuration,
    damageType = DamageType.Generic // 또는 새 DamageType.Slash 추가 검토
};
bool IsActive => true;
```

**충돌 로직**:
- `OnTriggerEnter2D` 또는 OnTriggerStay2D
- 슬라임 SlimeController 가져온 뒤, 슬라임 velocity 또는 `relativeVelocity` 매그니튜드 체크
  - SlimeController에 현재 속도 노출 API가 있는지 확인 필요 (가이드의 공개 API에 명시 안 됨 — 코드 보고 결정. 없으면 Rigidbody2D.velocity 직접 접근)
- 임계치 이상: ApplyHazardEffect (슬라임 분리 연출은 추후, 일단 stun+knockback)
- 임계치 이하: 효과 없음 (옆면 collider로 자연스레 attach)

**비주얼 변형 원칙 노트**:
- 칼은 정적 모델. 회전이나 흔들림 없음 (현재 디자인). 향후 "칼이 떨어지는" 동적 hazard 원하면 별도 명세.

**노트**:
- DamageType에 Slash 추가하면 비주얼/사운드 분기 더 명확. 일단 Generic으로 시작하고 필요 시 추가 검토.

---

## 작업 순서 권장

세션 한 번에 다 위임하든 묶어서 위임하든 자유. 의존성 그래프:

```
명세 #001 (InteractableBase, IsExternallyAnimated)
   ├── §2 Sit-pose
   ├── §3 미디어
   ├── §4 동작형 (폭죽 → §1 ImpulseEmitter 의존)
   │
명세 #002 §1 (ImpulseEmitter)
   ├── §4 폭죽
   ├── §5 점프스케어
   └── §6 카메라플래시

(독립)
   ├── §5 점프스케어 (FlashCanvas는 §6과 패턴 공유)
   ├── §6 카메라플래시
   ├── §7 CCTV
   ├── §8.1 톱날
   └── §8.2 칼
```

**권장 묶음**:
1. **묶음 A (UI 인프라)**: §1 ImpulseEmitter + §5 JumpscareCanvas + §6 FlashCanvas — 셋이 패턴 공유, 한 번에 잡기
2. **묶음 B (인터랙터블)**: §2 + §3 + §4 — InteractableBase 활용 일관성
3. **묶음 C (Hazard + 인테리어)**: §7 CCTV + §8.1 톱날 + §8.2 칼 — 모두 게임플레이 영향만/없음, IInteractable 무관

---

## 미해결 결정사항 (사용자 응답 필요)

1. **§4.1 세탁기**: 옵션 A (슬라임이 세탁기 안 들어감) vs **옵션 B (sit-pose + 본체 진동, 권장)**
2. **§4.2 폭죽**: launchVelocity 기본값 (0, 18) — 위쪽 발사. 폭죽-슬라임 방향 발사 원하면 launchAwayFromCenter=true.
3. **§8.2 칼**: DamageType.Slash 추가 vs Generic 사용. 일단 Generic으로 진행, 비주얼/사운드 차별화 필요해지면 추가.
4. **§5/§6 캔버스 분리**: JumpscareCanvas / FlashCanvas 분리 권장. 단일 Canvas로 묶고 싶으면 알려줘.

위 4개 중 §4.1만 명시적 답이 필요. 나머지는 권장값으로 진행해도 OK.

---

## 검증 체크리스트 (요약)

- [ ] §1 ImpulseEmitter — 메인 VCam에 ImpulseListener 부착, Emit() 호출 시 카메라 흔들림 확인
- [ ] §2 Sit-pose — W로 앉기/일어남, 일어난 직후 다른 인터랙터블 자동 발동 안 함, 슬라임 변형 정지/복귀 자연스러움
- [ ] §3 TV/축음기 — 토글 동작, 디스크 회전 코드 기반
- [ ] §4 폭죽 — 발사 + VFX + 임펄스 + 쿨다운
- [ ] §4 세탁기 — (옵션 B) 토글 시 본체 진동, 콜라이더 영향 X
- [ ] §5 점프스케어 — 트리거 진입 시 풀스크린 + 사운드 + 흔들림, 일회성 동작
- [ ] §6 카메라플래시 — 주기적 플래시, 범위 내 슬라임 stun, knockback 0, RenderTexture 미구현 확인
- [ ] §7 CCTV — 슬라임 추적, 거리 밖 idle, 본체 충돌 시 효과 없음 (Z=0 케이스 일반 surface)
- [ ] §8.1 톱날 — 회전 + 접촉 시 반경+접선 합성 넉백
- [ ] §8.2 칼 — 칼날면 임계치 이상 위험, 옆면 attach 가능
