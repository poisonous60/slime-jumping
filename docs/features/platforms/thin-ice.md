# 얇은 얼음 플랫폼 (Thin Ice Platform)

`CrumblingPlatform`의 확장형. 깨짐 사운드·파티클, 부드러운 재생성 애니메이션을 추가.

## 트리거 조건
- 슬라임이 **Attached 상태**로 이 플랫폼 표면에 붙은 순간 트리거.
- 한번 트리거되면 카운트다운은 **슬라임이 떨어져도 계속 진행**.
- 선택적 각도 필터 (`useAngleLimit`)는 CrumblingPlatform과 동일.

## 상태 머신
```
Idle → Triggered → Crumbling → Gone → Regenerating → Idle
```

| 상태 | 동작 | 시청각 피드백 |
|------|------|----------------|
| Idle | 대기 | 없음 |
| Triggered | `triggerDelay` 카운트다운 | `crackSound` 1회, 점진적 셰이크·틴트 (→ crackingTint) |
| Crumbling | `crumbleDuration` 카운트다운 | 강한 셰이크 + 알파 감소 |
| Gone | 콜라이더·렌더러 비활성 | `shatterSound` 1회 + `shatterParticles.Play()` |
| Regenerating | `regenDuration` 동안 scale/alpha 0 → 1 (ease-out) | `regenSound` 1회. **콜라이더는 OFF 유지** |
| Idle (재진입) | 겹침 없는 상태에서 콜라이더 복귀 | — |

## Crumbling과의 차이
| 항목 | CrumblingPlatform | ThinIcePlatform |
|------|------|------|
| 사운드 | 없음 | crack / shatter / regen 3종 슬롯 |
| 파티클 | 없음 | `shatterParticles` — Gone 진입 시 `Play(true)` |
| 재생성 | 즉시 스냅 | `regenDuration` 동안 scale + alpha 부드럽게 복귀 |
| Overlap 체크 타이밍 | 콜라이더 먼저 켜고 체크 | 콜라이더 끈 채로 체크 → 통과하면 regen 애니메이션 시작 |
| 애니메이션 중 통과 | — | Regenerating 중 콜라이더 OFF — 슬라임이 형성 중 얼음을 통과 가능 |

## 구현 방식
- **폴링 방식**: 매 FixedUpdate에서 `slime.CurrentState`, `slime.SurfaceCollider` 확인. SlimeController 수정 없음.
- **사운드**: `SoundService.PlayAt(clip, transform.position, SoundCategory.SFX_Object, soundVolume)` — 풀드 AudioSource 재생.
- **파티클**: 원샷 트리거. 루핑·지속 파티클은 지원하지 않음.
- **스케일 애니메이션**: `visualRoot.localScale = originalScale * eased` — Visual 자식만 스케일되므로 콜라이더에 영향 없음.

## 오디오 매니저 (ThinIcePlatformAudioManager)
`WaterZoneAudioManager` 와 동일한 패턴. 씬에 하나 배치하면 Awake 시 `FindObjectsByType<ThinIcePlatform>` 으로 모든 타일에 `ApplyAudioSettings(crack, shatter, regen, volume)` 을 브로드캐스트 — 타일별 클립 개별 할당 불필요.

- 실행 순서: `[DefaultExecutionOrder(-50)]` 로 일반 MonoBehaviour 보다 앞서 Awake. ThinIcePlatform 의 PlaySound 는 항상 상태 전이(FixedUpdate) 시점에만 호출되므로 Awake 순서 뒤집혀도 안전.
- `includeInactive = true` 면 비활성 타일도 대상.
- 컨텍스트 메뉴 `Apply To All Thin Ice Platforms` 로 에디터에서 즉시 반영 가능.
- 타일 인스펙터의 Audio 슬롯은 fallback 용. 매니저가 있으면 런타임에 덮어씀.

## 구조 (비주얼 분리)
```
ThinIcePlatform (부모)
├─ BoxCollider2D, ThinIcePlatform.cs, (선택) PlatformMotor
└─ Visual (자식) — MeshFilter + MeshRenderer
    └─ ShatterParticles (자식) — ParticleSystem (선택)
```
셰이크·스케일은 모두 자식 `localPosition`/`localScale`에 적용되어 콜라이더와 독립.

## 파라미터
| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| triggerDelay | 1.0 | 트리거 후 깨지기 시작까지 (초) |
| crumbleDuration | 0.15 | 깨지는 연출 시간 (초) |
| respawnTime | 3.0 | 숨겨진 상태 유지. 음수면 영구 소멸 |
| regenDuration | 0.6 | 재생성 페이드인 애니메이션 시간 (초) |
| crackingTint | light blue | Triggered / Crumbling 단계 틴트 |
| maxShakeOffset | 0.05 | 셰이크 최대 변위 |
| crackSound / shatterSound / regenSound | — | 오디오 클립 슬롯 |
| soundVolume | 1.0 | 세 사운드 공통 볼륨 |
| shatterParticles | — | Gone 진입 시 한 번 `Play()` |
| blockingLayers | ~0 | 재생성 차단 레이어 |
| useAngleLimit / min / maxSurfaceAngle | false / 0 / 180 | CrumblingPlatform과 동일 |
