# 속도 필드 규약 (Velocity Field Contract)

## 세 가지 계층
| 필드 | 타입 | 역할 |
|------|------|------|
| `velocity` | `Vector2` (내부) | 컨트롤러가 관리하는 의도된 운동. 상태별 의미가 다름 |
| `rb.linearVelocity` | `Vector2` (Unity) | 매 FixedUpdate 끝에 `velocity + platformVel * carrierFriction`으로 덮어씀. 이동 플랫폼 동기화. **직접 읽지 말 것** — 항상 `velocity`를 소스로 삼음 |
| `slideSpeed`, `crawlVelocity` | `float` (스칼라) | 접선 방향 스칼라. 각각 독립 계산 (slide = 중력 접선 분해, crawl = 입력 가속 모델) |

## 상태별 `velocity` 의미
- **Attached / Charging**: `surfaceTangent * (slideSpeed + crawlVelocity)`. 매 FixedUpdate 끝에 재구성. "접선 방향 현재 속도"를 담음.
- **Airborne**: 키네마틱 중력 적분 결과. 공중에서의 실제 궤적 속도.

## 파생 규칙
- **접선 속도 추출**: `Vector2.Dot(velocity, surfaceTangent)` 또는 `slideSpeed + crawlVelocity` 둘 다 유효 (Attached/Charging 한정).
- **상태 전환 시 리셋**: Airborne → Attached 전환에서 `velocity = Vector2.zero`. 이유: "점성 슬라임은 운동량 흡수" 원칙 + 다음 `FixedUpdateAttached`가 slideSpeed/crawlVelocity로 재구성.
- **Hop 예외**: `velocity += surfaceNormal * hopPower`로 법선 성분 주입 후 Airborne 전환. 이 프레임만 velocity가 접선 규약을 벗어남.
- **Charge 진입 시 관성 처리**: `impactTangentSpeed`에 현재 접선 속도 스칼라 저장 후 `stickDeccelTimer`로 접선 공간 lerp 감속. `chargeEntryCrawl`은 별도 캡처 (하이퍼 대시용).

## 관련
- [../slime/crawling.md](../slime/crawling.md)
- [../slime/stick-timer.md](../slime/stick-timer.md)
- [../slime/hop.md](../slime/hop.md)
