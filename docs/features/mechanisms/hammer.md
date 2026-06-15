# 스윙 해머 (Hammer)

폴대(`hammer_lod`) 꼭대기를 피벗으로 좌우로 진자 스윙하는 머리(`hammer_head`). 슬라임이 머리에 닿으면 그 순간의 머리 속도로 날린다. 야구 배트로 공을 치는 느낌 — 부착/차징 중이든 상관없이 즉시 발사 (`ForceAirborneWithVelocity`, JumpPad와 동일 코드 경로).

## 구조
- `SlimeJumping.Mechanisms.HammerPlatform` (단일 컴포넌트) — 기존 `hammer` **루트**에 부착. 재부모/노드 삽입 없음.
- 루트에 `Rigidbody2D`(Kinematic, Interpolate) + `BoxCollider2D`(trigger) 추가.
- 자식 `hammer_head`(머리 비주얼), `hammer_lod`(폴대 비주얼)는 그대로 — 루트가 통째로 피벗 주위를 공전하면 따라 움직인다.
- **비주얼/콜라이더 분리**: 머리 비주얼은 자식 `hammer_head`, 타격 콜라이더는 루트의 `BoxCollider2D`. `headSizeMultiplier`가 둘을 같이 키워 게임플레이가 비주얼과 일치하되, 머리 비주얼의 변형은 콜라이더에 새지 않는다.

## 스윙 모델
- 피벗 = `pivotLocalOffset` (루트 기준 로컬, 기본 = 폴대 꼭대기 `(0, 5.24)`). Awake에서 월드 좌표로 고정.
- 매 FixedUpdate에 스윙각 `φ`를 계산해 루트를 피벗 주위로 공전:
  - `pos = pivotWorld + Rot(φ)·armRest`, `rotation = baseRot + φ` (`MovePosition`/`MoveRotation`).
- `φ`는 `centerAngle − maxAngle` ↔ `centerAngle + maxAngle` 사이를 왕복. 진폭 = `maxAngle`, 중심 = `centerAngle`(0 = 정수직 아래 = 휴식 포즈 = 최저점).

## 속도 형상
- `usePendulumMotion` (기본 ON) → **진짜 진자(SHM)**: `φ = centerAngle − maxAngle·cos(2π·t/period)`. 최저점 최속 + **양끝 가속도 최대**라 중력처럼 위에서 휙 떨어짐. 곡선 무시. 망치 기본 느낌.
  - `pendulumDwell` (0~8, 기본 0) → 끝 dwell 조절. `φ = center − max·tanh(k·cos(wt))/tanh(k)`. 0=순수 진자, ↑=끝에서 더 오래 머물고 바닥 더 빨리 통과. 1~3 미묘, 4+ 강함.
  - ⚠️ 구 기본값 `EaseInOut`(SmoothStep)은 **양끝 가속도 0** → 꼭대기서 떠 있어(머무름) 로봇틱/어색. 진자 느낌엔 이 토글을 켜거나 코사인형 곡선을 그릴 것.
- `usePendulumMotion` OFF → `speedProfile` (AnimationCurve) 사용:
  - X = 반-스윙 위상 0..1, Y = 정규화 각도 0..1(0 = 한쪽 끝, 1 = 반대쪽 끝).
  - 양 끝 평탄 → 각도 끝에서 정지. 중앙 가파름 → 최저점 최속. 디자이너가 직접 그려 임의 형상.
- `period` = 1회 완전 왕복(끝→반대끝→복귀) 초. 전체 속도 스케일. (두 모드 공통)

## 발사
- 타격점 머리 선속도 = `ω × r` (`ω` = 피벗 기준 각속도 rad/s, `r` = `슬라임위치 − pivotWorld`). 2D에서 `ω·(−r.y, r.x)`.
  - → **스윙 속도(ω)** 와 **타격 반경(|r|, 끝쪽일수록 큼)** 이 자동으로 동시 반영. 요구사항 "hammer 속도랑 타격점에 맞춰서".
- 방향 = 접선(머리 진행방향). `outwardBias`(0..1)로 피벗에서 방사-바깥 방향으로 블렌드.
- 세기 = `headSpeed × launchMultiplier`, `maxLaunchSpeed`로 클램프.
- `hitCooldown` 초 동안 같은 슬라임 재타격 방지. `slime.HasOverride`면 스킵(컷신/외부 제어 중 안 끌어당김).
- `minLaunchSpeed` 미만(스윙 양끝 ω≈0 근처)의 접촉은 무시 — 발사 안 함 + 쿨다운 안 씀. 양끝에서 멈춘 머리가 슬라임을 살짝 밀고 쿨다운을 소모해 정작 빠른 복귀 타격이 씹히는 것을 방지.

## 인스펙터 노브 (요구사항 매핑)
1. **각도** — `maxAngle`(진폭), `centerAngle`(중심), `pivotLocalOffset`(피벗 위치).
2. **움직임(그래프)** — `speedProfile` 곡선 + `period`.
3. **발사 정도** — `launchMultiplier`, `maxLaunchSpeed`, `outwardBias`.
4. **크기** — `headSizeMultiplier`(머리 비주얼+콜라이더), `poleSizeMultiplier`(폴대 비주얼).

## 셋업
unity-cli로 기존 씬 `hammer` 루트에 1샷 와이어링: RB2D + BoxCollider2D(머리 bounds 크기·trigger) + `HammerPlatform`, head/pole 참조 + base scale + 콜라이더 size/offset + pivot offset(폴대 꼭대기) 캡처. 재부모 없음.

## 알려진 한계
- 타격 판정은 트리거 `OnTriggerEnter2D`/`Stay2D` (SawBlade 패턴). 매우 빠른 스윙(높은 `ω`)에서 얇은 머리가 슬라임을 한 FixedUpdate에 지나치면 놓칠 수 있음 — 콜라이더를 너무 작게 잡지 말 것. 필요 시 sweep/overlap 판정으로 보강 가능.
