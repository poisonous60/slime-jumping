# JumpBall (점프볼)

트리거 기반 자동 점프 볼. 슬라임이 닿으면 위쪽 방향으로 즉시 발사. JumpPad와 달리 착지/부착 없이 통과하며, 비주얼은 시간에 따라 위아래로 천천히 흔들린다. 옵션으로 먹힌 뒤 사라지고 일정 시간 뒤 재생성할 수 있다 (연출 + SFX).

## 파라미터

### Jump
| 필드 | 설명 |
|------|------|
| `fixedJumpSpeed` | 최저 보장 점프 속도. 접근 속도가 낮을 때 이 값으로 고정 (JumpPad 방식) |
| `incomingSpeedPercent` | 기존 속도 중 유지할 비율 (0~2) |
| `bonusSpeed` | 스케일된 속도 위에 더할 추가 속도 |
| `cooldown` | 재발동 방지 쿨다운 (`consumable=false`일 때만 사용) |
| `preserveHorizontal` | 발사 방향에 수직인 속도 성분 유지 여부 |

### Visual Bob
| 필드 | 설명 |
|------|------|
| `visual` | 흔들릴 자식 Transform (콜라이더 포함 금지) |
| `bobAmplitude` | 흔들림 피크-투-피크 거리 |
| `bobFrequency` | 초당 흔들림 사이클 수 |
| `randomizePhase` | Awake 시 위상 랜덤화 (여러 볼 동기화 방지) |

### Consume / Respawn
| 필드 | 설명 |
|------|------|
| `consumable` | true면 먹힌 뒤 사라지고 재생성. false면 쿨다운으로 무한 반복 |
| `consumeDuration` | 사라지는 연출(축소) 시간 |
| `respawnDelay` | 안 보이는 상태로 대기하는 시간 |
| `respawnDuration` | 재생성 연출(확대) 시간 — 이 동안에도 콜라이더는 꺼져 있음 |
| `consumeScaleCurve` | 사라짐 스케일 커브 (기본: 1 → 0, ease-out) |
| `respawnScaleCurve` | 재생성 스케일 커브 (기본: 0 → 1, ease-in-out overshoot 가능) |

### SFX
| 필드 | 설명 |
|------|------|
| `consumeSfx` | 먹힐 때 `SoundService.PlayAt`로 재생 |
| `respawnSfx` | 재생성 시작 시 재생 |
| `sfxVolume` | 공통 볼륨 |

인스펙터에 직접 지정해도 되지만, 씬에 [`JumpBallAudioManager`](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/JumpBallAudioManager.cs) 하나를 두면 모든 JumpBall에 동일한 클립 세트를 일괄 적용 — WaterZone / ThinIce 와 같은 패턴. Awake 또는 컨텍스트 메뉴 `Apply To All Jump Balls` 로 방송, `JumpBall.ApplyAudioSettings(consume, respawn, volume)` 를 호출해 덮어씀.

## 동작

발사 속도 공식:

```
finalSpeed = max(fixedJumpSpeed, incomingSpeed * incomingSpeedPercent + bonusSpeed)
```

- **저속 접근** → `fixedJumpSpeed` 지배 (JumpPad 스타일)
- **고속 접근** → `% + bonus` 지배 (모멘텀 부스트)

발사 방향은 `transform.up` (GameObject 회전으로 각도 조절 가능). `OnTriggerEnter2D`/`OnTriggerStay2D`에서 `SlimeController.ForceAirborneWithVelocity`를 호출하므로 Charging/Attached 상태도 정상 해제된다.

## 상태 머신 (`consumable=true`)

```
Idle → Consuming → Gone → Respawning → Idle
```

- **Idle**: 비주얼 보빙, 트리거 감지 활성
- **Consuming**: 콜라이더 OFF, `consumeScaleCurve`로 비주얼 축소, `consumeSfx` 재생
- **Gone**: 비주얼 GameObject SetActive(false), `respawnDelay` 대기
- **Respawning**: 비주얼 SetActive(true), `respawnScaleCurve`로 확대, `respawnSfx` 재생 (콜라이더는 여전히 OFF)
- → **Idle** 복귀 (콜라이더 재활성)

`consumable=false`는 기존 쿨다운 모델 유지: 바운스 → `cooldown`초 대기 → 다시 바운스 가능. 상태 머신은 항상 Idle.

## 비주얼/콜라이더 분리

콜라이더는 루트에, 비주얼은 자식 Transform(`visual`)에. 보빙/축소/확대 모두 `visual.localPosition` 과 `visual.localScale` 만 수정 — 콜라이더 위치·판정에 영향 없음. Consume/Respawn 전환 시 콜라이더 `enabled`를 토글해 트리거 감지를 제어.

## 관련 파일

- [JumpBall.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/JumpBall.cs)
- [JumpBallAudioManager.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/JumpBallAudioManager.cs) — 씬 공용 SFX 브로드캐스트
- [JumpBallTestSetup.cs](../../../SlimeJumping_v2/Assets/Scripts/Editor/JumpBallTestSetup.cs) — 테스트 씬 생성 (`SlimeJumping/Mechanism/Setup Jump Ball Test`)
- [JumpPad.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/JumpPad.cs) — 접촉 부착형 동급 메커니즘
- [SoundService.cs](../../../SlimeJumping_v2/Assets/Scripts/Audio/SoundService.cs) — SFX 재생
