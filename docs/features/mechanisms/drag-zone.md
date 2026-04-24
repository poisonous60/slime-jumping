# DragZone (감속 / 모래 볼륨)

Trigger volume. 슬라임이 어떤 방향으로든 진입/탈출할 수 있는 환경 기믹. 점프킹 스타일 모래. **고체 콜라이더 없음** — 순수 trigger.

## 핵심 필드
| 필드 | 기본 | 시점 | 설명 |
|------|------|------|------|
| `entryMultiplier` | 0.3 | 진입 1회 | 외부 운동량 → 내부 운동량 변환 |
| `exitMultiplier` | 3.33 | 탈출 1회 | 내부 운동량 → 외부 운동량 변환 (하향 Y 제외) |
| `launchMultiplier` | 0.3 | 모래 안 발사 1회 | 발사 속도 보존 비율. exit boost와 조합되어 최종 속도 결정 |
| `dragFactor` | 0.3 | 매 프레임 | 체류 시간 비례 지수 감쇠 (`velocity *= Pow(factor, dt)`) |
| `maxFallSpeed` | 5 | 매 프레임 | Y 음수 cap, 무한 중력 가속 방지 |
| `allowCharging` | false | — | Airborne에서 볼륨 내 차징 진입 허용 |

## 동작 (WindZone2D 패턴)
- `OnTriggerEnter2D`: `slime.OnDragZoneEnter(this)` → `velocity × entryMultiplier`.
- `OnTriggerExit2D`: `slime.OnDragZoneExit(this)` → `velocity × exitMultiplier` (하향 Y 보존).
- `FixedUpdate`: per-frame drag (`ApplyDragFactor`) + fall cap (`ClampFallSpeed`).

## 볼륨 차징 (allowCharging = true)
**3번째 차징 진입 경로**:
```
기존: Attached + jump press → Charging (표면)
기존: Airborne + 표면 + space 홀드 → Charging (에너지 전환)
신규: Airborne + DragZone(allowCharging) + jump press → Charging (볼륨)
```

- `isVolumeCharging` 플래그로 표면 차징과 구분.
- 판정원 aim 로직은 동일 (drift, WASD, angular clamp, auto-launch).
- 표면 추적/스냅/슬라이드/코요테 없음.
- 중력만 적용, fall cap은 DragZone.FixedUpdate가 처리.
- 볼륨 이탈 시 차징 취소 → Airborne.
- 발사: 순수 aim 방향 × power × launchMultiplier. 하이퍼 대시/캐리어 없음.

## SlimeController 연동
- `CurrentDragZone` (DragZone 참조): DragZone이 Enter/Exit에서 설정/해제.
- `IsInChargeVolume` (property): `CurrentDragZone != null && CurrentDragZone.allowCharging`.
- `isVolumeCharging` (bool): 볼륨 차징 중 표면 물리 비활성화 플래그.
- `EnterVolumeCharging()` — Airborne + jump press + IsInChargeVolume.
- `CancelChargingToAirborne()` — 볼륨 이탈 시 또는 코요테 만료.

## 발사 후 속도 복원
```
모래 안 charge → Launch: velocity = dir × power × launchMultiplier
모래 밖으로 Exit: velocity × exitMultiplier
최종:             dir × power × launchMultiplier × exitMultiplier
```

## Scene 구조
```
SandVolume (GameObject)
├─ BoxCollider2D (trigger, Layer 8: DragVolume)
└─ DragZone.cs (entry/exit/launchMultiplier, dragFactor, maxFallSpeed, allowCharging)
```

## 한계
- DragZone끼리 겹침은 미지원 (`CurrentDragZone` 단일 참조).

## 관련 파일
- [DragZone.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/DragZone.cs)
