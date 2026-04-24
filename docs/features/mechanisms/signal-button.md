# Signal / Button 시스템

## SignalSource (베이스)
`UnityEvent onActivate` / `onDeactivate`를 가진 베이스 클래스. Inspector 연결만으로 레벨 디자인 가능.

버튼, 압력판, 타이머, 무게 플랫폼 등이 상속. 코드 수정 없이 `onActivate → 원하는 메서드` 연결.

## WeightDescendPlatform (무게 하강 플랫폼 / 버튼)
슬라임이 위에 서면 천천히 내려가는 플랫폼. 바닥 도달 시 `Activate()` 발동. 슬라임 이탈 시 복귀, 복귀 완료 시 `Deactivate()`.

- **Sensor 기반 감지**: Attached 상태 무관 — charging 중, 인접 표면 접촉 중에도 동작.
- **PlatformMotor 미상속** (rider 의존 — time/waypoint 패턴과 다름).
- **Kinematic Rigidbody2D + MovePosition**.

## PlatformRiderSensor
자식 trigger 콜라이더. 슬라임 탑승 감지 (`RiderCount`). WeightDescendPlatform 등이 사용.

## 관련 파일
- [SignalSource.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/SignalSource.cs)
- [WeightDescendPlatform.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/WeightDescendPlatform.cs)
- [PlatformRiderSensor.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/PlatformRiderSensor.cs)
