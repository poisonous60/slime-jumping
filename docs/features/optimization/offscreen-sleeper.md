# OffscreenSleeper

화면 밖에 있는 객체를 `GameObject.SetActive(false)` 로 자동 비활성화해 idle 시뮬 비용을 회수하는 인프라. 슬라임이 카메라 viewport 밖 충분히 멀리 있는 동안 `TautLine.SyncShapes` / `WaterZone_v2.SimulateWaves` 등 매 FixedUpdate 무조건 실행되는 작업을 정지시킨다.

## 목적과 한계

**잡는 비용:**
- 매 FixedUpdate 무조건 실행되는 시뮬레이션/재빌드 (TautLine spring 적분 + SetPath, WaterZone 정점 시뮬, ClothField verlet 적분 등)
- 화면 밖 시각 효과 (SawBladeHazard 회전, WaterfallImpactVFX 갱신)
- 화면 밖 AI 추적 (TurretHazard, CCTVTracker)

**잡지 못하는 비용:**
- [SurfaceProbe_곡선_성능_분석_2026-05-09.md](../../SurfaceProbe_곡선_성능_분석_2026-05-09.md)의 33ms 회귀. 그건 슬라임이 *on-screen* 곡선 플랫폼을 슬라이딩할 때 발생하는 SurfaceProbe 측 비용이며, OffscreenSleeper는 그 시나리오에서 모든 sleeper가 active이므로 영향 없음. 33ms는 별도 작업 (BuildWorldPath FixedUpdate 캐시 / AABB 사전 컬링)으로 처리.

**측정 가치:** 절감폭 미측정. 인프라/문서화 정비 자체가 1차 목표.

## 동작 불변식

1. **슬라임 null = 토글 정지**: respawn / scene 전환 / 첫 spawn 전에는 매니저가 토글 결정 안 함. 모든 sleeper는 현 상태 유지. 안전 default.
2. **Hysteresis**: enable < disable. 슬라임이 enable 안에 들어와야 켜지고, disable 밖으로 나가야 꺼짐. 두 반경 사이는 *현재 상태 유지* → 경계 jitter에서 thrash 방지.
3. **Sleeping 중 bounds 동결**: 비활성 Renderer.bounds는 zero/stale일 수 있고 `transform.hasChanged`도 안 뜨므로, sleep에 들어갈 때 마지막 active bounds를 캐시하고 그것만 사용.
4. **PolygonCollider2D 비활성도 함께**: SetActive(false)는 GameObject 단위라 collider도 broadphase에서 빠짐 → contact pair 자체가 사라져 `FindNewContacts` 후보군에서 제외. 단 재활성 시 첫 SetPath 1회분 contact 캐시 무효화는 발생 (진입당 1회, 누적 안 됨).

## 아키텍처

```
SlimeController ──(OnSpawned/OnDespawned)──→ OffscreenSleeperManager
                                                  │
                                                  │  매 FU
                                                  ↓
                                       각 sleeper에 대해:
                                       slime ↔ sleeper.bounds 거리 검사
                                       hysteresis 토글
                                                  │
                                                  ↓
                                       OffscreenSleepable.SetSleeping(bool)
                                                  │
                                                  ↓
                                       gameObject.SetActive(!sleep)
```

**거리 측정 기준점**: `Renderer.bounds ∪ Collider2D.bounds`의 ClosestPoint. 둘을 합치는 이유 — 일부 후보(TautLine)는 콜라이더가 LineRenderer보다 큼. Renderer만 쓰면 슬라임이 콜라이더 끝에 닿아 있는데 시각적으로 화면 밖이라 비활성화되는 사고 가능.

**카메라 반경 자동 계산** (Cinemachine + ortho/persp 양쪽 지원):
```
camHalfHeight = ortho ? cam.orthographicSize : abs(cam.z - slimePlaneZ) * tan(fov/2)
camHalfWidth  = camHalfHeight * cam.aspect
viewDiagonal  = sqrt(camHalfWidth² + camHalfHeight²)
enableRadius  = viewDiagonal × marginMultiplier        // 기본 1.3
disableRadius = enableRadius × hysteresisFactor        // 기본 1.5
```

## 컴포넌트

| 컴포넌트 | 역할 |
|---------|------|
| `OffscreenSleeperManager.cs` | 씬에 1개 배치. sleeper 리스트 보유, 매 FU 거리 검사 + 토글. `[DefaultExecutionOrder(-9999)]`로 sleeper OnEnable보다 먼저 Awake 보장. `RuntimeInitializeOnLoadMethod`로 마커 누락 GameObject 안전망 스캔 |
| `OffscreenSleepable.cs` | 후보 GameObject에 부착. OnEnable에서 self-register. bounds 캐시 + SetActive 토글 owner. per-instance override 필드 노출 (`forceAlwaysActive`, `marginMultiplierOverride`, `manualBoundsOverride`) |
| `OffscreenSleeperCandidates.cs` | 13종 candidate 타입 단일 source of truth. 매니저(런타임 폴백) + 에디터 툴(일괄 부착)이 공유 |
| `OffscreenSleepableBatchTool.cs` | 에디터 메뉴 `Tools/Optimization/...`. Dry Run + 실제 부착. 모든 prefab/scene 순회, 멱등 |

## 새 컴포넌트 추가 시 결정 트리

```
1. Renderer 또는 Collider2D 있는가?
   No  → 부착 X (화면 밖 개념 자체 없음)
   Yes → 2

2. Update / FixedUpdate / LateUpdate에 매 프레임 작업 있는가?
   No  → 부착 X (이득 없음)
   Yes → 3

3. 이미 self-gating 되어 있는가? (예: trackedSlime==null이면 즉시 return)
   Yes → 부착 X (이득 거의 0)
   No  → 4

4. 화면 밖에서도 시간이 진행되어야 게임플레이가 자연스러운가?
   (예: 진자가 흔들려야 함, 뱀이 길 따라 이동, 무게 플랫폼 하강, 투사체 비행)
   Yes → 부착 X (Tier 2 제외 부류)
   No  → 5

5. 화면 밖 freeze가 시각/게임플레이에 허용되는가?
   No  → 부착 X
   Yes → 부착 O. Tier 분류:
        - 정지/물리 진동형 (rest로 settle, e.g. spring 시뮬) → Tier 1
        - 시간 누적 회전 (e.g. SawBlade, RotatingPlatform) → Tier 2A (다시 보면 정지 각도)

부착 후:
- prefab에 OffscreenSleepable 추가 (또는 Tools/Optimization 배치 툴 실행)
- 특수 케이스(보스 영역, 컷씬 등)는 Force Always Active 체크
- OffscreenSleeperCandidates.CandidateTypes에도 추가 (런타임 폴백 / 배치 툴 화이트리스트)
```

## 13종 분류 (현재)

### Tier 1 — freeze 무해

| 컴포넌트 | 매 프레임 작업 |
|---|---|
| `TautLine` | spring 적분 + SetPath (contact 캐시 무효화 트리거) |
| `WaterZone_v2` | SimulateWaves N정점, UpdateMeshVerts |
| `WaterCurrentZone` | TautLine 유사 패턴 |
| `ClothField` | verlet 적분 |
| `SawBladeHazard` | Update 회전 (시각만) |
| `WaterfallImpactVFX` | LateUpdate 경로 점검 |
| `ElevatorJoltVisual` | 시각 흔들림 |
| `PushDoorV2` / `PushDoor` | LateUpdate 시각 갱신 |
| `TurretHazard` | Update 트레이서/조준 |
| `CCTVTracker` | Update 헤드 회전 |

### Tier 2A — freeze 시각적 약간 어색하나 화면 밖이라 무관

| 컴포넌트 | 비고 |
|---|---|
| `RotatingPlatform` | 다시 보면 정지 각도에서 재개 (어차피 화면 밖 → 안 보임) |
| `SnowField` | 이미 strip 단위 self-gating, 추가 안전망 |

### 명시적 제외

| 이유 | 컴포넌트 |
|---|---|
| 별도 매니저 (zone 이탈이 복원 트리거) | `DeformablePlatform` (`DeformableRespawnZone` 사용) |
| 게임플레이 timing 영향 (화면 밖에서도 진행 필요) | `PendulumPlatform`, `SnakeSpine`, `WeightDescendPlatform` |
| 이미 self-gating (trackedSlime==null 즉시 return) | `ThinIcePlatformV2`, `CrumblingPlatform`, `IcicleHazard`, EnvironmentalZone 베이스 (ColdZone, DragZone, WindZone2D 등) |
| 투사체 — 화면 밖 비행 필요 | `Snowball`, `JumpBall` |
| Spawner — 화면 밖 spawn 필요 | `SnowballSpawner`, `*Spawner` |
| Trigger-only (이미 비용 0) | `JumpPad`, `PipeEntrance`, `GraniteBlastTrigger`, `SpikeHazardV2` |
| 씬-단위 효과 (별 처리 필요) | `RainWorldZone` |
| Deprecated | `SpringyBranchPlatform_v2` |

## 알려진 위험 (플레이테스트에서 발견 시 대응)

OffscreenSleeper는 13종 컴포넌트 각각의 OnDisable/OnEnable 안전성을 *사전 감사하지 않았다*. 실제 SetActive 토글 시 다음 패턴이 문제될 수 있음 — 발견 시 표 갱신 + 대응:

| 패턴 | 증상 | 대응 |
|---|---|---|
| ParticleSystem 자식 보유 | 재활성 시 emission burst 가능 | `OnDisable`에 `ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear)` 추가, 또는 적용 제외 (Force Always Active = true) |
| 쿨다운/타이머 (TurretHazard 등) | 비활성 중 timer freeze → 재활성 시 즉발사 | `OnEnable`에서 cooldown reset, 또는 적용 제외 |
| 외부 시스템 핸들 (AudioSource loop, IgnoredColliderList 등록 등) | leak / leftover 가능 | `OnDisable` cleanup 추가 |
| 상태 머신 (CCTVTracker `captured` 등) | 화면 밖 동안 게임 의도와 어긋남 | 적용 제외 |

발견 시:
1. 해당 컴포넌트 파일에 OnDisable cleanup 1–2줄 추가, 또는
2. 그 인스턴스만 Force Always Active = true (prefab override), 또는
3. `OffscreenSleeperCandidates.CandidateTypes`에서 타입 제거 (전체 인스턴스 적용 제외)

발견된 케이스를 위 표에 기록.

## 단일 씬 가정

현재 본 프로젝트는 **MainScene 하나**만 사용. 이 plan은 그에 맞춰 매니저를 *씬 배치 싱글톤*으로 두고 `DontDestroyOnLoad` 적용 안 함.

**MainScene 외 씬 추가 시 점검:**
1. 매니저를 추가 씬에도 배치할지 vs `DontDestroyOnLoad` 적용해 단일 인스턴스 유지할지 결정
2. `SceneManager.sceneLoaded` 이벤트로 sleeper 리스트 정리 여부
3. Additive scene loading 시 sleeper 등록/해제 race condition 점검
4. Loading screen / 메뉴 씬에서는 매니저 비활성화 권장 (의미 없는 토글 방지)

## Force Always Active 사용 예시

특수 영역에서 OffscreenSleeper 게이트 우회:
- 보스 영역 — 모든 메카닉이 카메라 줌아웃에서도 정확히 작동해야 함
- 시네마틱 컷씬 — 카메라가 슬라임에서 분리되어 멀리 있는 객체를 비추는 동안
- 디버그/QA — 토글 동작 검증 시 일시 우회

해당 인스턴스의 OffscreenSleepable에서 `Force Always Active` 체크 → 매니저는 그 인스턴스를 즉시 active로 강제하고 거리 검사 skip.

## 측정 결과

(구현 후 unity-cli exec로 베이스라인 + 적용 후 비교 수치 추가)

- 베이스라인 (적용 전): _TODO_
- 적용 후: _TODO_
- Δ FixedUpdate: _TODO_
- Δ FindNewContacts: _TODO_
