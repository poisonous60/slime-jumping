# 입력 요약

## 표면 (Attached)
| 입력 | 동작 |
|------|------|
| A / D | 표면 위 크롤링 (접선 투영) |
| W | 홉 (표면 법선 방향 작은 점프, 접선 운동량 보존) |
| AA / DD (더블탭) | 그라운드 대시 (해당 방향 크롤 순간 부스트) |
| Space 홀드 | 차징 시작 (판정원 생성, 조준점 중심). Dash → Space = 하이퍼 대시 |

## 차징 (Charging)
| 입력 | 동작 |
|------|------|
| WASD | 조준점 2D 조작 (바깥=가속, 안=감속, 좌우=각도) |
| Space 릴리즈 | 수동 발사 |
| (자동) | 가장자리 도달 시 자동 발사 |

## 공중 (Airborne)
| 입력 | 동작 |
|------|------|
| A / D | 수평 가감속 (하강 시에만 — apex 게이트) |
| W | 홉 버퍼 (착지 시 즉시 홉 발동) / 중력 조절 float (하강 시) |
| AA / DD (더블탭) | 대시 버퍼 (착지 시 A/D 홀드 방향으로 대시) |
| S | 중력 조절 dive (하강 시) |
| Space 홀드 | 표면 충돌 시 에너지 전환 준비 |

## 착지 시 입력 우선순위
**Space(Charging) > W(Hop) > A/D 더블탭(Dash)**. 상위 입력이 하위 버퍼를 소비.

## 표면 충돌 분기
| 충돌 시 상태 | 결과 |
|-------------|------|
| Space 안 누름 | velocity = 0, Attached (에너지 소멸) |
| Space 누르고 있음 | velocity = 0, Charging 직행 (판정원 확장 가능) |
| 천장 | 부착 안 함. 법선 속도 상쇄, Airborne 유지 |

## 인터랙션
- **E 키** (Interact 액션, Gamepad: buttonWest): `IInteractable` 발동. 침대/TV/신사 등.
- **ESC**: 옵션 패널 토글 (게임 진행은 계속).

## 관련
- [../slime/crawling.md](../slime/crawling.md), [../slime/hop.md](../slime/hop.md), [../slime/ground-dash.md](../slime/ground-dash.md)
- [../slime/charge-launch.md](../slime/charge-launch.md), [../slime/airborne.md](../slime/airborne.md)
- [../mechanisms/tier1-interactables.md](../mechanisms/tier1-interactables.md)
