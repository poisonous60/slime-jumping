# LowGravityZone (저중력 구간)

구간 내부에서 중력 **크기**에만 배율을 건다. 방향은 안 건드림 — 방향은 [gravity-field.md](gravity-field.md) 소관이라 둘을 겹쳐 놓으면 방향은 GravityField, 크기는 LowGravityZone 이 결정한다.

## Effective Gravity 아키텍처

`GravityField`(방향)와 **독립된 두 번째 스택**이다.

| 채널 | 스택 | 산출 |
|------|------|------|
| 방향 | `gravityFieldStack` | `gravityUp` / `gravityRight` |
| 크기 | `lowGravityStack` | `GravityScale` → `GravityMagnitude` |

- `GravityScale` = 스택 최상위 zone 의 `gravityScale` (LIFO, 마지막 진입이 이김). 비면 1.
- `GravityMagnitude` = `|settings.gravity| × GravityScale`.
- `GravityVector` = `-gravityUp × GravityMagnitude`.
- 중력 크기를 읽는 코드는 전부 `GravityMagnitude` / `GravityVector` 를 경유한다. `Mathf.Abs(settings.gravity)` 직접 읽기는 금지.

## 자동으로 따라오는 것 (별도 코드 없음)
| 대상 | 저중력에서의 거동 |
|------|-----------------|
| Airborne 낙하 적분 | 덜 떨어짐. W/S 홀드·`risingGravityScale` 배율은 이 위에 곱해짐 |
| Stunned 낙하 | 동일 |
| Charging 코요테 낙하 | 동일 |
| 경사면 슬라이드 (`ComputeSlideSpeed`) | 느려짐 |
| friction detach `gravityHelp` | 접착 예산 축소 → 코너에서 더 잘 떨어짐 |
| 중력 sag (`SlimeVisual`) | 덜 처짐. 분모는 baseline `Gravity` 라 배율이 그대로 sag 에 반영된다 |
| 에너지 변환 spawn distance | 현재 배율로 shadow speed 계산 |

## 명시적으로 배선한 슬라임-측 소비자
| 파일 | 처리 |
|------|------|
| `WaterZone` / `WaterZone_v2` | 부력 × `GravityScale`. 부력은 ρVg 라 중력에 비례 — 안 곱하면 저중력 물에서 슬라임이 밖으로 쏘아 올려진다 |
| `SlimeChunk` | 매 틱 본체 `GravityVector` 를 읽는다. 조각은 잘려나간 슬라임 질량이라 본체와 같이 떠야 한다. 본체 소멸 시 스폰 값 폴백 |
| `AfterimageZone` | 축 계산이 `-GravityVector.normalized` → `GravityUp`. `gravityScale = 0` 이면 전자가 영벡터로 붕괴한다 |

## 스코프 외 (일부러 안 건드림)
Rope / RopeV2 / RopeV3 / TautLine / ClothField / BambooPlatform(+V3) / SpringyBranch / SwingingBell / PendulumPlatform 은 각자 `Physics2D.gravity` 로 자기 노드·진자를 돌린다. 배율을 먹이면 rest shape·주기가 바뀌어 튜닝이 전부 깨진다.
플랫폼 파편(FallingTrain debris, ThinIce shard, GraniteBlastMortar 포탄, BambooOnThinIce 시체)도 실제 `Rigidbody2D.gravityScale` 이라 제외.

→ **진자형 발판은 저중력존 안 배치 비추.** 발판만 정상 중력으로 움직인다.

## 진입/탈출
velocity 보존, 가속도만 즉시 바뀐다. 블렌드·페이드 없음 — 실제 저중력 포켓과 같은 C0 거동이고 `GravityField` 방향 전환도 스냅이다.

## 겹침 정책
`GravityField` 와 동일. 디자인상 금지, 안전망으로 LIFO. 곱연산이 아니다 (두 존을 겹치면 0.35×0.35=0.12 로 통제 불가).

## 스택 소속은 카운터가 아니라 실측
`ApplyEffect` 가 매 FixedUpdate `Collider2D.Distance(...).isOverlapped` 로 실제 겹침을 재고, 그 결과로 `PushLowGravityZone`/`PopLowGravityZone` 을 부른다. 둘 다 멱등이라 양방향 자가 복구된다.

`EnvironmentalZone` 의 `overlappingColliderCount` 는 **추론**이라 콜라이더 on/off · `IgnoreCollision` · 존 재활성 · cut 로브 토글에서 샌다. 한 번 새면 0 으로 안 떨어져 슬라임이 구역을 나가도 저중력이 영구히 남는다. 카운터는 "검사할 가치가 있는가"(`trackedSlime`)만 정하고, 소속은 실측이 정한다.

`trackedSlime` 은 실측이 false 여도 유지한다 — 지우면 Unity 쪽 장부가 이미 어긋난 경우 재진입 enter 가 안 와서 반대로 갇힌다. 존 하나당 프레임당 `Distance` 1회라 비용은 무시할 수준.

## 텔레포트 / override 연동
`TeleportTo` 와 `ReleaseOverride` 가 `ResyncLowGravityZones()` 를 부른다. `gravityFieldStack.Clear()` 와 달리 **양방향** 정리다:

1. 스택에 있던 zone 마다 `ForceExitTracking()` — 존 쪽 `trackedSlime` 이 남으면 재진입 시 조기 반환으로 영영 push 가 안 된다
2. `Physics2D.SyncTransforms()` — `m_AutoSyncTransforms: 0` 이라 방금 쓴 `rb.position` 이 아직 브로드페이즈에 없다
3. 바디 콜라이더(`BodyColliders`, 잘리면 로브 여러 개)마다 `Collider2D.Overlap` → 겹치는 zone 을 `ForceEnterTracking`

덕분에 저중력존 **안으로** 텔레포트/리스폰해도 저중력이 걸린 채 시작한다.
`TeleportTo` 에서는 `rb.position = worldPos` **뒤에** 호출해야 한다 (앞에 두면 떠나온 자리를 훑는다).

피격(`ApplyHazardEffect`)은 제자리 넉백이라 스택을 건드리지 않는다.

## 알려진 근사
존 경계를 **관통하는 비행**의 에너지 변환(`ComputeEnergyConversionSpawnDistance`)은 착지 시점 배율로 shadow speed 를 계산한다. 같은 함수의 `heightDelta` 가 이미 현재 `gravityUp` 으로 투영해 GravityField 경계에 대해 같은 근사를 쓰고 있어 일관성을 맞춘 것.

## 파라미터
| 파라미터 | 타입 | 비고 |
|---------|------|------|
| `gravityScale` | float [0, 1] | 0.35 = 원래의 35%. 1 = 무효과, 0 = 무중력 |
| `showVisualEffect` | bool (기본 true) | 끄면 자식 `Visual` 을 비활성화. 스테이지 전체를 덮는 큰 존은 꺼서 쓴다 — 파티클이 화면 전체로 늘어나 비싸고 의미 없다. 손으로 만든 존은 `Visual` 자식이 없어 무동작 |
| (collider) | Collider2D, `isTrigger=true` | 아무 모양 OK. Layer 9: GravityField (`GravityField` 와 레이어 공유) |

`gravityScale = 0` 은 완전 무중력: 슬라이드·sag·`gravityHelp` 가 전부 0 이 되고 조각도 안 떨어진다.

## Scene 구조
```
LowGravityZone (GameObject, Layer 9)
├─ BoxCollider2D / CircleCollider2D / PolygonCollider2D (trigger)
└─ LowGravityZone.cs
```

## 테스트 셋업
메뉴 `SlimeJumping/Mechanism/Setup LowGravityZone Test` — 5케이스(발사 높이 비교, 슬로프 슬라이드, 볼록 코너, GravityField 합성, 경계 관통 아치).

## 관련 파일
- [LowGravityZone.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/LowGravityZone.cs)
- [SlimeController.LowGravity.cs](../../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.LowGravity.cs)
- [LowGravityZoneTestSetup.cs](../../../SlimeJumping_v2/Assets/Scripts/Editor/LowGravityZoneTestSetup.cs)

## 관련
- [gravity-field.md](gravity-field.md)
- [../infra/teleport.md](../infra/teleport.md)
