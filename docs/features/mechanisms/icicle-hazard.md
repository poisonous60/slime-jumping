# IcicleHazard (고드름)

천장에 매달린 고드름. 슬라임이 감지 박스 안에 들어오면 진동 → 낙하 → 지면 충돌 시 파괴(파티클 + 사운드 + 콜라이더 비활성) → (옵션) 재생성.

낙하 중 고체 콜라이더가 함께 내려오므로 슬라임이 위에 올라탈 수 있음.

## 상태 머신
| 상태 | 동작 |
|------|------|
| Idle | 매 FixedUpdate `Physics2D.OverlapBox`로 슬라임 탐지. `SlimeController`가 박스 내에 있으면 Shaking 진입. |
| Shaking | `visualRoot`만 sin/cos 진동 (콜라이더는 정지). `shakeDuration` 후 Falling. 진입 시 `shakeStartSound` 재생. |
| Falling | 매 FixedUpdate `solidCollider.Cast(Vector2.down, dist, breakOnLayers)`로 스윕 → 충돌 시 거리만큼만 `MovePosition` 후 Broken 전환. 충돌 없으면 `MovePosition(rb.position + down*dist)`. 진입 시 `fallingSound`. Kinematic의 position 텔레포트는 static 지면과 contact를 만들지 않고, 고속 낙하 시 tunneling 위험도 있어 스윕으로 대체. |
| Broken | `impactSound` + `breakParticles.Play()`. `solidCollider`/`meshRenderer` 비활성. `respawnTime < 0` (one-shot) 이면 파티클 detach + `enabled = false`. 아니면 `respawnTime` 카운트다운 → 만료 시 `rb.position`을 spawn 위치로 복귀 후 `regenBlockingLayers` 오버랩 체크. 비어있으면 Regenerating, 막혀있으면 매 프레임 재시도. |
| Regenerating | `rb.position = spawnPosition`, `visualRoot.localScale`을 `0 → originalScale`로 ease-out 애니메이션 (`regenDuration`). 이 동안 `solidCollider`는 꺼져있어 슬라임이 통과 가능. 완료 시 Idle 진입 → 콜라이더/비주얼 복원. |

## 주요 필드

### 감지 박스 (local space)
- `detectionOffset` (Vector2): 고드름 원점 기준 박스 중심 오프셋.
- `detectionSize` (Vector2): 박스 X/Y 크기.
- `slimeLayer` (LayerMask): 탐지 레이어.

### 진동
- `shakeDuration`, `shakeAmplitude`, `shakeFrequency`.

### 낙하
- `fallSpeed` (units/s, 등속).
- `breakOnLayers`: 이 레이어와 충돌 시 Broken 전환. 슬라임 레이어는 제외해야 슬라임이 올라탈 수 있음.

### 재생성
- `respawnTime` (float): 파괴 후 재생성 대기 시간. **음수면 one-shot** (재생성 안 함, ThinIcePlatform 규칙과 동일).
- `regenDuration` (float): 재생성 애니메이션 길이 (scale 0→1 ease-out).
- `regenBlockingLayers` (LayerMask): 이 레이어가 스폰 위치에 겹쳐있으면 대기. 보통 슬라임 레이어 + 환경 레이어 포함 → 슬라임이 천장 아래 서있을 때 머리 위에 다시 튀어나오지 않음.

### 비주얼
- `visualRoot`: 진동/스케일 애니메이션 대상. 콜라이더와 분리 (프로젝트 규칙: visual/collider 분리).
- `meshFilter`, `meshRenderer`, `mesh`, `material`: Awake/OnValidate에서 mesh/material 주입.

### 콜리전
- `solidCollider`: 슬라임이 올라타는 실제 콜라이더. 낙하 중 함께 이동, Broken/Regenerating 동안 비활성, Idle 진입 시 복원.

### 오디오
- `shakeStartSound`, `fallingSound`, `impactSound` — 모두 `SoundService.PlayAt(SFX_Object)`.
- `soundVolume` (0~1).
- **Scene-wide 공유**: [`IcicleAudioManager`](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/IcicleAudioManager.cs) 1개를 씬에 두면 Awake 시점에 모든 IcicleHazard에 같은 clip set + volume을 브로드캐스트 (`ApplyAudioSettings`). `WaterZoneAudioManager` / `ThinIcePlatformAudioManager` 패턴과 동일. 개별 인스턴스마다 clip 할당 불필요. 컨텍스트 메뉴 `Apply To All Icicles`로 에디터 시간에도 수동 적용 가능.

### 파티클
- `breakParticles` (ParticleSystem): 파괴 순간 Play. one-shot 모드에서만 `SetParent(null, true)`로 분리 (오브젝트 비활성화 후 생존). 재생성 모드에선 그대로 자식으로 두어 다음 파괴에서 재사용.

## 설계 주의
- `Rigidbody2D` Kinematic. 낙하는 `MovePosition` + pre-cast sweep (tunneling 방지).
- `RequireComponent(typeof(Rigidbody2D))`.
- Idle 단계 감지는 OverlapBox 1회 쿼리 (트리거 존 불필요).
- Mesh/Material 필드는 OnValidate로 실시간 반영.
- Awake에서 `spawnPosition = rb.position`, `originalVisualScale = visualRoot.localScale` 캐시.
- Regen 중 슬라임이 지나가도 새 트리거는 안 됨(Idle 상태에서만 감지).

## 관련 파일
- [IcicleHazard.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/IcicleHazard.cs)
