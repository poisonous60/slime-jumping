# 얇은 얼음 v2 (Thin Ice v2)

`ThinIcePlatform` (v1)의 후속. **Pogostuck 2 결의 동작**(점프 시 즉시 부서짐, 일정 시간 버티면 자동 부서짐) + **그룹 단위 respawn box** + URP 절차적 Voronoi 셰이더 기반 균열 + 공유 generic shard 메쉬 라이브러리로 구성.

기존 `ThinIcePlatform.cs`(v1)는 손대지 않고 그대로 보존 — 두 시스템을 stage별로 골라 사용 가능.

## 핵심 차이 (v1 → v2)

| 항목 | v1 | v2 |
|---|---|---|
| 텔레그래프 | 위치 셰이크 + 색조 변화 | URP Voronoi 셰이더로 점진적 셀 경계 emissive + 표면 전체에서 mesh 파편 낙하 |
| 부서짐 | 알파 페이드 + 작은 파티클 1샷 | **공유 generic shard mesh** N개를 collider bounds 안 random 점에 instance + 발사 + 스핀 |
| 부서짐 트리거 | 타이머 단일 | 타이머 OR 점프(`SlimeController.OnLaunch`) OR 비정상 detach |
| 재생성 | per-platform `respawnTime` 타이머 | **Zone**(BoxCollider2D 트리거)에 속하면 zone-exit 시 일괄 regen, 없으면 영구 |
| 셰이크 / hitstop / flash | 없음 | hitstop 영구 OFF, 셰이크/flash는 옵션(default OFF) |

## State machine

```
Idle → Cracking → Shattered → (zone) Regenerating → Idle
                            ↘ (orphan) 영구
```

| 상태 | 동작 | 시청각 |
|---|---|---|
| Idle | 대기 | `_CrackProgress = 0` |
| Cracking | `crackingDuration`(default 2.0s) 카운트다운. 셰이더 `_CrackProgress` 0→1 ramp. debrisVFX emission rate를 progress에 맞춰 ramp | `crackSound` 1회, 셀 경계가 점차 진하게 emissive cyan, 표면 전체에서 mesh 파편 낙하 |
| Shattered | 즉시 처리: visualRenderer/콜라이더 OFF, **collider bounds 안 random 점에 N개 shard 인스턴스화**(random 회전/스케일/속도), frostCloudVFX.Play(), (옵션)flash·shake | `shatterSound` 1회 |
| Regenerating | `regenDuration`(0.6s) 동안 `_CrackProgress` 1→0 역재생. 콜라이더는 끝까지 OFF | `regenSound` 1회 |

### 트리거 규칙

- **Idle → Cracking**: 슬라임이 `Attached` 상태로 이 platform의 collider에 붙음 (선택적 각도 필터)
- **Cracking → Shattered (점프)**: `SlimeController.OnLaunch` 이벤트 시점에 `slime.SurfaceCollider`가 platform의 collider 중 하나면 즉시 발화. 슬라임의 launch velocity 일부(`jumpVelocityInherit` default 0.3)가 shard에 상속됨
- **Cracking → Shattered (타이머)**: `crackingDuration` 만료
- **Cracking → Shattered (비정상 detach)**: `Attached`/`Charging`이 아닌 상태로 떠나면 (외부 force, gravity flip 등) 즉시 발화

## 컴포넌트 / 자산

```
SlimeJumping.Platforms.ThinIceV2
├─ ThinIcePlatformV2          (runtime; state machine, OnLaunch 구독, shard spawn)
├─ ThinIceRespawnZone         (runtime; BoxCollider2D trigger + Rigidbody2D Kinematic)
├─ ThinIceV2Config            (ScriptableObject; 공유 자산 single source of truth)
├─ ThinIceShardSet            (ScriptableObject; shard mesh 리스트 — Config가 참조)
└─ ThinIceShardRuntime        (per-shard 인스턴스; 수명 + 알파 페이드)

SlimeJumping.Platforms.ThinIceV2.EditorOnly  (편집기)
├─ ThinIceV2ConfigGenerator    (메뉴: Config + DebrisVFX/FrostCloudVFX prefab 일괄 생성)
├─ ThinIceSharedShardSetGenerator  (메뉴: 공유 shard 자산 생성)
├─ ThinIcePlatformV2Editor      (자체 인스펙터 — Config 상태 표시 + 부족분 경고)
└─ ThinIcePlatformV2TestSetup   (메뉴: 테스트 씬 자동 셋업)
```

### Config (single source of truth)

`ThinIceV2Config` ScriptableObject가 모든 공유 자산을 한 곳에 모음:
- `shardSet` — 공유 shard mesh 라이브러리
- `shardRuntimePrefab` — shard 런타임 prefab (MeshFilter + MeshRenderer + Rigidbody2D + ThinIceShardRuntime)
- `debrisVFXPrefab` — telegraph debris ParticleSystem prefab (Awake에서 platform 자식으로 instantiate)
- `frostCloudVFXPrefab` — shatter cloud ParticleSystem prefab (Awake에서 platform 자식으로 instantiate)

**캐논 경로**: `Assets/Resources/ThinIceV2Config.asset`. `ThinIcePlatformV2.Awake()`에서 `Resources.Load<ThinIceV2Config>("ThinIceV2Config")`로 자동 로드 → **per-platform 슬롯 0개**. 다른 톤의 얼음 등 변형이 필요하면 platform의 `configOverride` 슬롯에 다른 Config을 지정.

생성/갱신 메뉴: `SlimeJumping/Thin Ice v2/Generate Default Config and Prefabs` — idempotent. 기존 prefab은 안 건드리고, Config asset만 prefab/shardSet 참조를 갱신.

**TerrainShape2D 의존성 없음.** v2 platform은 임의의 Renderer + Collider2D 조합을 받음 — 단순 Box, 사전에 author한 PolygonCollider, TerrainShape2D-Save&Install 결과(컴포넌트는 베이크 후 제거) 모두 사용 가능. TerrainShape2D는 작화 도구일 뿐 runtime 의존성 X.

## Generic 공유 Shard 시스템

핵심 아이디어: 매 platform마다 모양에 맞춘 shard 메쉬를 사전 분할하지 않고, **모든 platform이 공유하는 작은 generic 셀-shape 메쉬 라이브러리**(default 24개)를 한 번만 만들어 재사용. 부서질 때:

1. Platform의 `Collider2D.bounds` 안에서 random N개 (default 16개) 좌표 샘플
2. 각 좌표에 공유 shard 라이브러리에서 무작위로 1개 골라 인스턴스화
3. Random rotation + scale variance + radial outward velocity (점프-shatter 시 슬라임 launch velocity 일부 상속)

이렇게 하면:
- per-platform 작업 0
- 셰이더 셀 패턴과 시각적 톤 일치 (모양은 같은 패밀리, 위치만 random)
- 각 shard에 두께가 있어(default 0.08) 굴러갈 때 3D 청크처럼 보임
- 어떤 모양 platform에도 즉시 동작

### Shard 메쉬 구조

각 generic shard는 **5~8각 불규칙 다각형 + Z 두께 0.08**로 구성:
- 앞면 fan triangulation (front center fan)
- 뒷면 fan triangulation (reverse winding)
- 사이드 strip (각 변마다 quad 1개)
- 메쉬 원점은 다각형 centroid → 회전 자연스러움

알고리즘 (1샷):
```
for i in [0, count):
    sides = Random(5..8)
    for each angle slot: jittered radius around basRadius (±radiusJitter)
    convex polygon → recenter to centroid → extrude on Z → mesh
    save as sub-asset of SharedThinIceShardSet
```

같은 `siteSeed` → 같은 메쉬 라이브러리 (재생성해도 결과 안정).

### 공유 자산 생성

메뉴 `SlimeJumping/Thin Ice v2/Generate Shared Shard Set` (1회 클릭):
- `Assets/GeneratedShards/SharedThinIceShardSet.asset` 생성 (또는 덮어쓰기)
- 24개 shard mesh가 sub-asset으로 직렬화

기본값 (소스에서 조정 가능):
- count = 24
- baseRadius = 0.10 (local units)
- radiusJitter = 0.45 (반경 ±45% 변동)
- thickness = 0.08

## 셰이더 (URP HLSL, Quibli toon lighting 베이스)

`Assets/Shaders/ThinIceV2.shader` — Quibli `StylizedLighting.hlsl`의 `MainLight_half`를 include 해서 main light direction + shadow attenuation을 받고, `_GradientRamp` 텍스처(Quibli 컨벤션)에 NdotL을 lookup 해서 toon shading 적용. 그 위에 절차적 Voronoi (F2-F1 셀 경계)를 emissive로 합성.

```
properties:
  // Quibli toon lighting
  _GradientRamp       — NdotL → ramp 색상 lookup (Quibli StylizedLit과 동일 컨벤션, 다른 머티리얼과 ramp 공유 가능)
  _SelfShadingSize    — Quibli NdotLTransitionPrimary와 동일 — 그림자 영역 확장 오프셋
  _LightContribution  — 0=ramp 그대로, 1=ramp × (lightColor × shadowAtten)

  // Ice base
  _BaseColor          — 얼음 색 (default 하늘색 85% 알파, ramp와 곱셈)

  // Crack overlay (additive emissive, 라이팅 무관)
  _CrackColor         — 셀 경계 emissive (default 진한 cyan)
  _CrackProgress      — 0~1, 런타임에 MaterialPropertyBlock으로 갱신
  _CellScale          — 셀 밀도 (default 6.0)
  _BorderEdge         — 기본 경계 두께
  _BorderGrow         — progress가 1일 때 추가 두께
  _AlphaCutAt         — 경계가 얼마나 투명해지는지
  _Alpha              — 인스턴스 알파 (shard 페이드용)
  _WorldSpaceSampling — 1=world, 0=object (platform = 1, shard = 0)
```

### Quibli include 전략

전체 `StylizedLit` 패스(`Lighting_DR.hlsl`/`LitForwardPass_DR.hlsl`)는 `DR_RIM_ON`/`DR_SPECULAR_ON`/`DR_GRADIENT_ON` 등의 DR_* shader feature 매트릭스가 ShaderGraph에 묶여 있어서 일반 `.shader`에 그대로 포함하기는 부담. 대신 **작고 자체 완결적인 `StylizedLighting.hlsl`만 include** — `MainLight_half` (light direction + 그림자) + `NDotL_half` (NdotL transition 공식)만 가져와서 Quibli/Stylized Lit이 쓰는 NdotL→ramp 매핑과 동일한 식으로 사용. Quibli가 그 헬퍼를 갱신하면 자동 반영됨.

세부 동작:
- Pass: `LightMode = UniversalForward`, `Blend SrcAlpha OneMinusSrcAlpha`, `ZWrite Off`, `Cull Off`
- multi_compile: `_MAIN_LIGHT_SHADOWS`, `_MAIN_LIGHT_SHADOWS_CASCADE`, `_SHADOWS_SOFT`, fog, instancing
- 투명 표면이라 `_SURFACE_TYPE_TRANSPARENT` define — Quibli helper의 screen-space shadow branch를 우회 (transparent는 SSS shadow texture에 안 씀)
- Ramp 슬롯이 비면 default `"white"` 텍스처 → ramp 효과 없이 light color × shadowAtten만 적용 (안전한 fallback)
- 절차적 Voronoi, **world-space XY로 샘플링이 기본** — non-uniformly scaled platform(예: 3:0.6 비율 cube)에서도 셀 비율이 정사각형으로 유지됨
- shard는 작고 회전·이동하니 object-space 사용 — `ThinIceShardRuntime.Awake()`가 MPB로 `_WorldSpaceSampling=0` 설정
- F2-F1 거리로 셀 경계 마스크 → progress가 클수록 경계 두께 + emissive 강도 증가
- 경계에서 알파를 약간 떨어뜨려 "갈라진" 인상

## Layer 정책

**신규 레이어 0개.**

| 대상 | 처리 |
|---|---|
| Platform 본체 collider | 기존 Environment(7) 또는 Default(0) — slime이 ground로 인식하는 레이어 |
| Zone trigger box | `isTrigger=true` + `OnTriggerEnter/Exit2D`에서 `GetComponentInParent<SlimeController>()`로 필터 + dedupe 카운트 |
| Shard | **Collider2D 자체를 안 붙임** — Rigidbody2D만 있어도 중력/속도/스핀 동작. 무충돌 보장, 풀/매트릭스 없음 |

## Optional FX (default OFF)

| 토글 | 효과 |
|---|---|
| `useEmissiveFlash` | shatter 직후 0.05s간 `_CrackColor`를 `flashIntensity`(default 4×) 배 강도로 boost |
| `useCameraShake` | shatter 시 `ImpulseEmitter.Emit(shakeForce)` (Cinemachine impulse) |
| `hitstop` | **영구 OFF** — Pogostuck-style launch arc 흐름을 끊지 않음 |

## Telegraph debris VFX

`ThinIcePlatformV2.Awake()`에서 자동으로 collider **world bounds**에 맞춰 ParticleSystem `Box` shape로 재구성:
- emit shape = `Box`, scale = world bounds size 그대로 `(world.size.x, world.size.y, 0.05)`
- shape.position = `world.center - debrisVFX.transform.position` (PS GO 기준 오프셋)
- 주의: `ParticleSystem.shape.scale`은 parent lossyScale을 무시하고 절대 단위로 동작 — 따라서 world bounds size를 직접 넣어야 표면 전체 분포
- emit rate = 0 (런타임에 `_CrackProgress * debrisPeakRate`로 갱신)
- 메쉬 파티클 (built-in Cube) → 작은 청크가 표면 전체에서 떨어짐
- gravity 1, lifetime 0.8~1.2s, simulation space World

## 작화 흐름 (디자이너용)

**1회 셋업** (프로젝트 전체):
- 메뉴 `SlimeJumping/Thin Ice v2/Generate Default Config and Prefabs` 실행 → `Assets/Resources/ThinIceV2Config.asset` + `Assets/Prebs/ThinIceV2/{DebrisVFX,FrostCloudVFX}.prefab` + `SharedThinIceShardSet.asset` + `Assets/Settings/ThinIceV2/ThinIceV2_DefaultRamp.png` (toon ramp PNG, cool-blue→white step) 일괄 생성. Ramp은 `Assets/Shaders/ThinIceV2.mat`의 `_GradientRamp` 슬롯이 비어 있을 때만 자동 할당 — 디자이너가 수동으로 다른 ramp을 지정했으면 안 건드림. 다른 자산도 idempotent (이미 있으면 prefab 건드리지 않음).

**Platform 추가** (per-instance):
1. Platform GameObject 생성 — 단순 Cube primitive 또는 TerrainShape2D-baked mesh
2. MeshRenderer 머티리얼을 `Assets/Shaders/ThinIceV2.mat`로 설정 (toon ramp는 1회 셋업에서 자동 할당됨)
3. `ThinIcePlatformV2` 컴포넌트 추가 → **끝**. 공유 자산은 Config에서 자동 로드
4. (옵션) Config Override 슬롯에 다른 `ThinIceV2Config`를 지정해서 변형(다른 톤/밀도 등) 사용
5. (옵션) Zone 트라이얼: 부모 GO에 `ThinIceRespawnZone`을 두고 그 자식으로 ThinIce를 배치 → zone-exit 시 일괄 regen

## 테스트 시나리오 (메뉴: SlimeJumping/Platform/Setup ThinIcePlatformV2 Test)

자동 생성:
- Floor + walls + catch floor
- `ThinIceRespawnZone` 박스 (가운데 4개 platform 포함)
- 5개 변형: Default(2.0s) / Fast(0.8s) / Large(2.5s) / Upper(1.5s) / **Orphan**(zone 밖 → 영구)
- 모두 공유 `SharedThinIceShardSet` 참조

검증 절차:
1. Play
2. **Cracking**: 슬라임 부착 → 셀 경계가 점차 진해지고 mesh 파편이 표면 전체에서 떨어짐
3. **Jump-shatter**: 1초 정도 후 점프 → 24개 shard 라이브러리에서 16개 random 선택, collider bounds에 random 분포로 발사 + frost cloud
4. **Timer-shatter**: Default(2.0s) 위에서 가만히 → 자동 부서짐
5. **Zone-respawn**: Zone 안 4개 platform 부수고 zone 박스 밖으로 점프 → 모두 동시에 cells 사라지며 재사용 가능
6. **Orphan one-shot**: Orphan 부수고 다시 가도 영영 사라진 채로 유지

## 향후 작업 후보

- 사운드 잔향 layer (얼음 흩어지는 잔잔한 소리)
- Shard 풀링 (현재는 매 shatter마다 Instantiate/Destroy)
- 디자이너용 시드 미리보기 — Generic Shard Set의 다른 seed 시각 비교
