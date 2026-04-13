# VFX 튜닝 가이드

공용 VFX 인프라(`Base_Particle` variant 패턴 · `VFXColorPalette` · `ParticlePaletteBinder` · `ImpulseEmitter`)와 개별 이펙트의 튜닝 포인트를 정리한 문서. Play 모드에서 감각으로 맞춘 값을 Stop 후 prefab/asset에 입력하는 흐름이 기본.

---

## 0. Play 모드 튜닝 주의사항

- Unity는 Play 중 수정한 값이 Stop 시 **전부 되돌아간다**.
- Pause 걸고 마음에 드는 값 확인 → Stop 후 prefab/asset에 다시 입력해야 저장됨.
- ScriptableObject(`DefaultVFXPalette.asset`)와 `[ExecuteAlways]` 컴포넌트(`ParticlePaletteBinder`)는 에디터 모드에서 바로 반영됨. 색 튜닝은 Play 안 들어가도 됨.
- prefab 편집은 Project 창에서 prefab 더블클릭해 prefab 모드로 들어가서 하는 게 안전. 씬 인스턴스에서 override만 건드리면 다른 인스턴스에 안 퍼짐.

---

## 1. 공용 인프라

### 파일

| 자원 | 경로 |
|---|---|
| 팔레트 ScriptableObject | `Assets/Settings/VFX/DefaultVFXPalette.asset` |
| 팔레트 enum | `Assets/Scripts/Rendering/VFXType.cs` (append-only) |
| 팔레트 바인더 | `Assets/Scripts/Rendering/ParticlePaletteBinder.cs` (`[ExecuteAlways]`) |
| Impulse 래퍼 | `Assets/Scripts/Effects/ImpulseEmitter.cs` |
| 파티클 베이스 prefab | `Assets/Prebs/VFX/Base_Particle.prefab` |
| 참고 variant | `Assets/Prebs/VFX/Dust_Landing.prefab` |

### 팔레트 색 튜닝

- 해당 VFXType 엔트리의 `primary`/`secondary` 편집 → 파티클 start color가 min/max 두 색 사이에서 랜덤 샘플링.
- 보통 primary를 밝은/주요 색, secondary를 어두운/보조 색으로.
- 슬라임 색 등 오브젝트 색에 맞추는 경우: 해당 스프라이트에서 Color Picker로 색 뽑아서 primary, 밝기만 20~40% 내려서 secondary.
- 새 VFXType 추가 시 반드시 **enum 끝에 append**. 중간 삽입하면 기존 씬/prefab의 바인더가 잘못된 타입 가리킴.

### Impulse (카메라 흔들림)

- 씬에 `CinemachineImpulseSource` + `ImpulseEmitter`가 붙은 GO가 있어야 함. 해저드/이펙트 GO 자체에 붙여도 되고 별도 GO여도 됨.
- 카메라 쪽 VCam에는 `CinemachineImpulseListener` 필요 (이미 설정돼 있음).
- `ImpulseEmitter.Emit(force)` 한 번 호출로 이벤트 방출. force 스케일 기준:

| 상황 | 값 | 참고 위치 |
|---|---|---|
| JumpscareTrigger (풀스크린) | 1.5 | JumpscareTestSetup.cs |
| FireworkInteractable | 0.3 | FireworkTestSetup.cs |
| CameraFlashHazard | ~0.2 | CameraFlashHazard 인스펙터 |
| SliceEffect (Knife/Saw) | 0.2 | 해저드 인스펙터 `impulseForce` |

작은 피드백은 0.1~0.3, 스토리성 임팩트는 1.0+.

---

## 2. 파티클 prefab 튜닝 체크리스트

Base_Particle variant를 만들 때 건드리는 모듈 순서:

1. **Main**
   - `Duration`: burst가 한 번만 나가면 0.1 정도로 짧게.
   - `Loop`: OFF (one-shot).
   - `Play On Awake`: **OFF**. 스폰 스크립트가 명시적으로 `Play()` 호출해야 FixedUpdate 1-frame flash 없음.
   - `Start Lifetime`: min/max 값 범위 (Random Between Two Constants). 파티클 체공시간.
   - `Start Speed`: 분사 속도.
   - `Start Size`: 파편 크기. 너무 작으면 안 보이고 너무 크면 덩어리져 보임.
   - `Gravity Modifier`: 0 = 공중 정지, 1 = 물리 중력. 드롭렛 느낌은 0.3~0.8.
   - `Stop Action`: **None** (SFX 잘림 방지). `SliceEffect` 같은 수명 관리 스크립트가 Destroy. 그냥 파티클만 쓰면 `DestroyOnStop`도 OK.
   - `Max Particles`: burst 상한 + 여유. 기본 1000은 과함.

2. **Emission**
   - `Rate over Time`: 0 (지속 방출 아님).
   - `Bursts`: Count를 min/max 범위로. 한 번 터지고 끝.

3. **Shape**
   - `Cone`: 방향성 있는 분사. `Angle`은 퍼짐 정도(35° 기본). prefab의 local +Y가 cone 축이 되려면 `Rotation = (-90, 0, 0)`.
   - `Sphere`/`Hemisphere`: 사방 분사.
   - `Radius`: 스폰 영역 크기. 접촉점 느낌이면 작게(0.05).

4. **Color over Lifetime**
   - 끝에서 alpha 0으로 페이드. 갑자기 사라지지 않게.
   - Gradient에 alpha key 3개 정도: 0=1, 0.6=1, 1=0.

5. **Size over Lifetime** (선택)
   - 끝에서 살짝 줄이면 부드럽게 사라지는 느낌.

6. **Renderer**
   - `Default-ParticleSystem.mat` 기본 사용. 커스텀 머티리얼 쓸 거면 `Material Property Block`와 팔레트 바인더 호환 확인.
   - `Render Mode`: Billboard(카메라 바라봄) 기본. Stretched는 속도 방향으로 늘어남(슬래시 느낌).

---

## 3. SliceEffect (Knife/Saw 베임 연출)

### 파일

| 자원 | 경로 |
|---|---|
| prefab | `Assets/Prebs/VFX/Slice_Hit.prefab` |
| 컴포넌트 | `Assets/Scripts/Effects/SliceEffect.cs` |
| SFX 클립 폴더 | `Assets/Sound/Slice/` |
| 팔레트 엔트리 | DefaultVFXPalette.asset → `type: 6` (Slice) |
| 해저드 통합 | `KnifeHazard.cs`, `SawBladeHazard.cs` |

### 튜닝 포인트

#### A. Slice_Hit prefab — 파티클 감각

| 항목 | 기본 | 범위 |
|---|---|---|
| Emission Burst Count | 15~25 (min~max) | 10~30 |
| Start Speed | 3~6 u/s | 2~8 |
| Start Lifetime | 0.4~0.8s | 0.3~1.0 |
| Start Size | 0.05~0.15 | 0.03~0.25 |
| Gravity Modifier | 0.8 | 0.3~1.2 |
| Cone Angle | 35° | 20°~60° |
| Cone Radius | 0.05 | 0.02~0.1 |

- 버스트 많을수록 시원시원하지만 과하면 "터진다" 느낌. 슬라임 파편은 "떨어져 나간다" 정도가 적당.
- Gravity 높이면 금방 아래로 떨어져서 "점액" 느낌, 낮추면 공중에 떠서 "에너지" 느낌.

#### B. Slice_Hit prefab — SFX (SliceEffect 컴포넌트)

| 항목 | 기본 | 비고 |
|---|---|---|
| `hitClips` | 5개 (impactSoft_medium_000~004) | 배열. 랜덤 선택 |
| `sfxVolume` | 1.0 | 너무 튀면 0.7 |
| `pitchJitter` | 0.1 | 0 = 매번 같은 소리(지겨움), 0.2+ = 어지러움 |

AudioSource 설정(프리팹 내 AudioSource 컴포넌트):
- `Spatial Blend`: 1.0 (3D, 접촉점에서 들림). 2D 고정음 원하면 0.
- `Min/Max Distance`: 3/25 (거리 감쇠 시작/종료).
- `Rolloff Mode`: Linear.

#### C. 해저드 GO 인스펙터 — 물리 펀치 & 카메라

Knife/Saw 각각의 `Slice FX` 헤더:

| 항목 | Knife 기본 | Saw 기본 | 의미 |
|---|---|---|---|
| `sliceEffectPrefab` | Slice_Hit | Slice_Hit | 공용 |
| `impulseEmitter` | 해저드 GO 자체 | 해저드 GO 자체 | 씬 GO 참조, null이면 카메라 흔들림 없음 |
| `impulseForce` | 0.2 | 0.2 | 0.15~0.25 |
| `sliceKick` | 2 u/s | 3 u/s | 톱날이 더 불쾌해야 하므로 높게 |
| `bladeEdgeRef` (Knife만) | null → `transform.right` | — | 파편 방향 축 |

#### D. bladeEdgeRef 설정법 (Knife 전용)

Knife prefab의 visual mesh가 어느 방향을 향하느냐에 따라 다름. 절차:

1. 씬에서 Knife GO 선택.
2. 자식으로 빈 GameObject 추가 → 이름 `BladeEdgeRef`.
3. Scene 뷰에서 Move/Rotate로 돌려 **local +X (빨강 화살표)가 칼날 날(edge) 방향을 향하게** 배치. 위치는 상관없음, 회전만 중요.
4. Knife GO 인스펙터 → KnifeHazard → `Blade Edge Ref`에 드래그.

**검증**: Play 중 칼에 슬라임을 쏴서 파편이 **칼날 길이 방향**으로 튀는지 확인. 날 수직(박치기 방향)으로 튀면 ref가 90° 틀어진 거 → Y축 기준 90° 회전.

`bladeEdgeRef`를 null로 두면 Knife GO 자체의 `transform.right`를 사용. Knife GO 루트가 이미 edge 방향과 일치하면 굳이 안 만들어도 됨.

#### E. 색상 튜닝

- `Assets/Settings/VFX/DefaultVFXPalette.asset` → 리스트에서 `type: 6` 엔트리 찾기.
- `primary` = 슬라임 본체 색 (현재 placeholder 녹색).
- `secondary` = primary보다 20~40% 어둡게 (그림자 효과).
- 변경하자마자 prefab 에디터에서 바로 반영. 씬 뷰에서 prefab 인스턴스화해서 재생 눌러 확인.

---

## 4. 감각 체크리스트

재생하면서 판단:

1. **파편 방향 맞나** — 슬라임이 칼날 긁고 지나간 방향으로 튀나, 아니면 수직으로 튀나.
   - 수직이면: Knife는 `bladeEdgeRef` 재조정. Saw는 코드 부호 문제일 가능성 (톱날 중심 → 슬라임 radial에 수직).
2. **파편 양** — 20이 과하면 15, 시원찮으면 25.
3. **관성 기스** — `sliceKick` 낮으면 "밀렸나?" 정도, 높으면 궤적이 눈에 띄게 틀어짐. 목표는 **"계속 미끄러지는 느낌"**. 3 넘기면 과함.
4. **카메라 흔들림** — 0.2에서 보일락말락. 안 보이면 0.25, 과하면 0.15.
5. **SFX 반복 지겨움** — hitCooldown 1.2s라 중복은 없지만, pitchJitter 0이면 단조로움. 0.1 유지.
6. **파티클 색과 슬라임 색 매치** — 팔레트 Slice primary/secondary를 슬라임 본체 색에 맞춤.

---

## 5. 자주 걸리는 문제

| 증상 | 원인 | 해결 |
|---|---|---|
| 파티클이 **회색/분홍색** | 팔레트 바인더가 VFXType을 못 찾음 | DefaultVFXPalette.asset에 해당 type 엔트리 있는지 확인. 없으면 리스트에 Add |
| 파티클이 **첫 프레임에 엉뚱한 위치에서 번쩍** | PlayOnAwake가 ON 상태 | prefab 파티클 Main의 Play On Awake **OFF**, 스폰 스크립트가 `Play()` 호출 확인 |
| **파편 방향이 수직** | `bladeEdgeRef` 회전 잘못됨 | local +X가 날 방향 되도록 재회전 |
| **SFX 중간에 잘림** | `stopAction=DestroyOnStop`인데 클립이 파티클보다 김 | prefab의 stopAction을 None으로, SliceEffect 같은 수명 스크립트가 max(particle, clip) 대기 |
| **카메라 안 흔들림** | VCam에 ImpulseListener 없거나 Emitter가 null | SlimeVCam의 Extensions에 `CinemachineImpulseListener` 있는지 확인, 해저드의 `impulseEmitter` 필드 할당 확인 |
| **소리 너무 작음/방향 이상** | spatialBlend=1인데 카메라가 멀리 있음 | MinDistance 늘리거나 spatialBlend 0(2D)으로 |
| **enum append 깨짐** | 중간에 값 삽입 | 절대 중간 삽입/삭제 금지. 끝에만 append |

---

## 6. 새 "한 방 이펙트" 만들 때 체크리스트

SliceEffect 패턴을 다른 해저드(가시, 유리, 번개 등)에 복제할 때:

1. `VFXType` enum에 새 값 append → 팔레트 에셋에 엔트리 추가.
2. Base_Particle variant로 새 prefab 만들기 (emission/shape/gravity 조정).
3. `ParticlePaletteBinder` 추가, `vfxType`을 새 값으로.
4. 필요하면 전용 MonoBehaviour (SliceEffect 참고) 또는 단순 스폰이면 `LandingDustSpawner` 패턴 직접 사용.
5. 사운드 필요하면 AudioSource + 수명 코루틴 (`max(particle, clip)`).
6. 카메라 임팩트 필요하면 해저드 GO에 `ImpulseEmitter` 부착 후 해저드 스크립트에서 `Emit(force)` 호출.
7. 해저드 스크립트의 hit 지점에서 `SpawnXxx(prefab, contactPoint, direction)` 호출.

이 패턴이 "SliceEffect 구조"이고, 추후 다른 베임/찌름/부숨 류에 그대로 확장됨.
