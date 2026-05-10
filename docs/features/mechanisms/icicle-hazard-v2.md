# IcicleHazard v2 (고드름)

v1과 동일한 상태머신/감지/리젠 흐름을 그대로 두고, 두 가지를 갱신했다.

1. **낙하 속도를 그래프로 노출**: 등속 `fallSpeed` (v1) 대신 `fallSpeedCurve: AnimationCurve` (시간[s] → units/sec). 마지막 키프레임 시간 이후에는 마지막 값을 유지 → 자연스러운 "느리게 시작 → 가속 → 등속" 프로파일을 author 가능.
2. **충격 시 셔터 효과**: ThinIce v2의 부서짐 효과(`SpawnShards` + `frostCloudVFX` + 셔터 사운드 + 옵션: emissive flash, camera shake)를 그대로 옮겨와 고드름이 지면 충돌 시 메시 셔드가 사출되도록 함. ThinIce 의 `ThinIceShardSet` / `ThinIceShardRuntime` 타입을 재사용하지만 에셋 인스턴스는 v2 전용 슬롯(`shardSet` / `shardRuntimePrefab` / `frostCloudVFXPrefab`)에 배정해 향후 고드름 전용 메시/VFX로 분기 가능.

v1 ([`IcicleHazard.cs`](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/IcicleHazard.cs))은 그대로 유지된다 — v2는 형제 파일이며 기존 씬은 영향받지 않는다.

## 상태 머신

| 상태 | 동작 |
|------|------|
| Idle | v1과 동일. `Physics2D.OverlapBox` 슬라임 탐지 → Shaking. |
| Shaking | v1과 동일. `visualRoot` sin/cos 진동, `shakeDuration` 후 Falling. |
| Falling | **변경**. 매 FixedUpdate `fallElapsed` 누적 → `SampleFallSpeed(fallElapsed)`로 현재 속도 계산 → `dist = speed * fixedDeltaTime`. v1과 동일한 `solidCollider.Cast(Vector2.down, dist, breakOnLayers)` 스윕으로 충돌 판정 → 충돌 시 거리만큼만 `MovePosition` 후 Broken. `breakImmunityDuration` 초기 grace 동일. 충격 직전 속도(`lastFallSpeed`)는 셔드 사출에 inherit 용으로 캐시. |
| Broken | **변경**. `EnterBroken()` 호출: ① `impactSound` 재생, ② `SpawnShards(lastFallSpeed)` — `solidCollider.bounds` 중심 기준 outward radial 사출 + `Vector2.up * shardUpwardBias` + `Vector2.down * lastFallSpeed * impactVelocityInherit` (downward inherit), ③ `frostCloudVFXPrefab` 1회 스폰 (parent 안 함, PS Stop Action으로 자가 destroy), ④ `shatterSounds[]` 랜덤 1개 재생, ⑤ 옵션: emissive flash (`useEmissiveFlash`), camera shake (`useCameraShake` + `impulseEmitter`), ⑥ `solidCollider`/`meshRenderer` 비활성. 이후 v1과 동일한 respawn 카운트다운. |
| Regenerating | v1과 동일. `visualRoot.localScale` 0→1 ease-out, 콜라이더 OFF 유지, 완료 시 Idle. |

## Preset

[`IcicleHazardV2Preset`](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/Configs/IcicleHazardV2Preset.cs) ScriptableObject. `Create → SlimeJumping/Icicle/Hazard V2 Preset`. v1 preset의 모든 섹션(detection / shake / respawn / audio)을 미러링하고 추가:

- **Fall**: `fallSpeedCurve: AnimationCurve` (default 3-key: `(0, 1.5)`, `(0.6, 6)`, `(1.2, 6)` — 느린 시작 → 가속 → 6 u/s 등속).
- **Shatter — shard launch**: `shardSet`, `shardRuntimePrefab`, `shardCount` (16), `shardSpeedMin/Max`, `shardUpwardBias` (0.7, ThinIce 기본 1.0보다 낮음 — 고드름은 위로 튀기보다 박힌 인상), `shardSpinDeg`, `shardLifetimeMin/Max`, `shardFadeWindow`, `shardScaleRange`, `impactVelocityInherit` (0.3 — 충격 시 속도의 30%를 셔드 downward에 inherit).
- **Shatter — VFX/audio/shake**: `frostCloudVFXPrefab`, `shatterSounds[]`, `useEmissiveFlash` + `flashColor` + `flashDuration` + `flashIntensity`, `useCameraShake` + `shakeForce`. **scene ref**인 `impulseEmitter`는 컴포넌트에 잔류 (preset 제외).

Inline 필드 덮어쓰기 규칙은 v1과 동일 — preset 슬롯이 바뀐 OnValidate + Awake에서만 일괄 적용. `_lastAppliedPreset` 캐시로 같은 preset 유지 시 inline 수정 보존.

## 낙하 속도 그래프 (`fallSpeedCurve`)

```csharp
public float SampleFallSpeed(float t)
{
    if (fallSpeedCurve == null || fallSpeedCurve.length == 0) return 0f;
    float lastKeyTime = fallSpeedCurve[fallSpeedCurve.length - 1].time;
    float clamped = Mathf.Min(t, lastKeyTime);   // 마지막 키 이후 final value 유지
    return Mathf.Max(0f, fallSpeedCurve.Evaluate(clamped));  // 음수 출력 클램프
}
```

- **계약**: 곡선의 마지막 키프레임 시간을 cutoff 으로 보고 그 이후엔 마지막 값을 유지한다. Curve asset의 `postWrapMode` 와 무관 — 곡선 작성 실수로 인해 고드름이 갑자기 멈추거나 거꾸로 솟아오르는 사고 방지.
- **Author 가이드**: t=0부터 시작하는 곡선을 작성. 첫 키가 t>0이면 그 사이엔 곡선 자체의 pre-wrap 값이 사용되니 권장하지 않음. 음수 출력은 0으로 clamp 되므로 "ease-in" 같은 곡선의 언더슈트도 안전.
- **빈 곡선**: `length == 0` 이면 `SampleFallSpeed`가 0을 반환 → 낙하 중 거리가 0이라 고드름이 멈춰있음. `OnValidate`에서 경고 로그.

## 셔터 효과

[`IcicleHazardV2.SpawnShards`](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/IcicleHazardV2.cs) 는 [`ThinIcePlatformV2.SpawnShards`](../../../SlimeJumping_v2/Assets/Scripts/Platforms/ThinIceV2/ThinIcePlatformV2.cs)와 동일한 구조:

- `solidCollider.bounds` 내부에서 `shardCount` 개수만큼 random spawn point 추출.
- `shardSet.shards[]` 라이브러리에서 random mesh 선택, `shardRuntimePrefab` instantiate, `MeshFilter.sharedMesh` swap.
- `Rigidbody2D.linearVelocity = outDir * Random.Range(speedMin, speedMax) + Vector2.up * upBias + inherit`.
- `inherit = Vector2.down * fallSpeedAtImpact * impactVelocityInherit` — ThinIce는 점프 속도 inherit, v2 고드름은 충격 직전 낙하 속도의 일부를 downward로 bleed.
- `Rigidbody2D.angularVelocity = Random.Range(-shardSpinDeg, shardSpinDeg)`.
- `ThinIceShardRuntime.Begin(life, fadeWindow)`로 수명/페이드 시작 — 셔드는 자가 Destroy.

방어 가드: `shardRuntimePrefab` / `shardSet.shards` / `solidCollider` 중 하나라도 없으면 셔드 스폰만 건너뛰고 `frostCloudVFX` / 사운드 / shake / collider-disable은 그대로 진행 (v1처럼 silent break로 graceful degrade).

`frostCloudVFX`는 prefab의 ParticleSystem이 자가 Stop Action(Destroy 등)으로 정리하도록 author. parent 안 함 → 고드름이 respawn 되어도 cloud 인스턴스는 영향 없음.

## 주요 필드 (v1과의 diff)

| 필드 | v1 | v2 |
|------|----|----|
| Fall speed | `float fallSpeed = 6f` | `AnimationCurve fallSpeedCurve` |
| Break visual | `ParticleSystem breakParticles` | `shardSet` + `shardRuntimePrefab` + `frostCloudVFXPrefab` |
| Audio | `impactSound` 단일 | `impactSound` + `shatterSounds[]` (랜덤) |
| Optional FX | 없음 | `useEmissiveFlash`, `useCameraShake` (`ImpulseEmitter` per-instance) |

나머지(detection, shake, respawn, audio manager 연동)는 v1과 동일.

## 설계 주의

- v1과 동일한 CLAUDE.md 규칙: `rb.position`만 사용, UnityEngine.Object 참조에 `??`/`?.` 사용 금지, visual/collider 분리 (`visualRoot`만 진동/스케일).
- `IcicleAudioManager`와의 관계는 v1과 동일하지만, 매니저는 현재 v1 `IcicleHazard`만 broadcast 대상으로 인식한다 — v2를 매니저로 묶고 싶다면 매니저 측에서 `IcicleHazardV2.ApplyAudioSettings`도 호출하도록 별도 작업 필요 (이번 변경 범위 밖).
- `impulseEmitter`는 scene ref이므로 prefab 인스턴스마다 카메라의 emitter를 wire 해야 함.
- `flashColorProperty` 기본값은 URP/Lit 호환 `_BaseColor`. ThinIce처럼 `_CrackColor`를 쓰려면 string 필드를 변경.

## 관련 파일

- [IcicleHazardV2.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/IcicleHazardV2.cs)
- [IcicleHazardV2Preset.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/Configs/IcicleHazardV2Preset.cs)
- [IcicleHazard.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/IcicleHazard.cs) (v1, 미수정)
- [ThinIcePlatformV2.cs](../../../SlimeJumping_v2/Assets/Scripts/Platforms/ThinIceV2/ThinIcePlatformV2.cs) (셔터 효과 원본)
- [ThinIceShardSet.cs](../../../SlimeJumping_v2/Assets/Scripts/Platforms/ThinIceV2/ThinIceShardSet.cs) / [ThinIceShardRuntime.cs](../../../SlimeJumping_v2/Assets/Scripts/Platforms/ThinIceV2/ThinIceShardRuntime.cs) (재사용 타입)
