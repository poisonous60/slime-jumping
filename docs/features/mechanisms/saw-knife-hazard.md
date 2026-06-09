# SawBladeHazard / KnifeHazard (베임 해저드)

## 공통
두 해저드 모두 `IHazardSource` 구현. 접촉 시 `SliceEffect` 스폰 + `AddExternalVelocity` + `ApplyHazardEffect`로 Stunned.

## SawBladeHazard
Pull 방향 `HazardEffect` (radial + tangent 블렌드, `speedRetainRatio`, `hitCooldown`).
- 접촉 시 `SliceEffect` 스폰 (파티클/SFX/Impulse).
- `AddExternalVelocity(tangent * sliceKick)` — tangent 부호는 슬라임 속도 사영으로 결정 (정지 시 `rotationRPM` fallback).

## KnifeHazard
Pull + **body passthrough** (관통 가능).
- `Physics2D.IgnoreCollision` + SurfaceProbe ignore list (`IgnoredColliderList.Passthrough` 모드: 타이머 only로 해제, overlap-aware release, re-hit guard).
- 접촉 시 `SliceEffect` 스폰 + `AddExternalVelocity(edge * sliceKick)` — edge 방향은 `bladeEdgeRef.right` (null이면 `transform.right`), 부호는 슬라임 속도 사영.

## SliceEffect (공통 베임 피드백)
`Knife/Saw` 공통. `SliceEffect.Spawn(prefab, contact, signedTangent)` 정적 팩토리.
- `signedTangent`는 local +Y로 cone 회전 정렬.
- `ParticlePaletteBinder`로 `VFXType.Slice` 팔레트 색 자동 주입.
- 수명은 `max(particleLife, audioClip.length) + 0.1s`로 자체 관리 — SFX 잘림 방지.
- `DamageType.Slice`로 해저드 식별.
- 튜닝 가이드: [VFX_튜닝_가이드.md](../../VFX_튜닝_가이드.md).

## Slime Split (베임 = 진짜 모양 변화)
블레이드 히트 시 슬라임은 **잘린 모양 그 자체가 된다 — 렌더·오클루전·충돌·부착·접지 전부.** 제거된 부분은 "없는 것": 안 뜨고, 잘린 데서 안 걸리고, 안 붙고, **잘린 면으로 닿아 부착**. 스턴/넉백/눈은 평소대로 `Stunned`에서 오고 그 위에 얹되 **장악 안 함**.

**발동**: 해저드 `splitSequence` 슬롯이 채워져 있고 `!slime.HasOverride`이면 `ApplyHazardEffect`(스턴) 직전 `Begin(slime, slime.Velocity)`. 미할당 시 byte-for-byte 동일.

**핵심**: 콜라이더는 원 그대로 두되 surface probe가 **방향별 reach(support function)** 로 컷 모양을 충돌. 중심 고정, 잘린 면 방향→컷 평면까지(잘린 면으로 앉음/붙음, 안 뜸), 제거영역→reach 밖(안 걸림/안 붙음), `cutPlaneCount==0`→full circle(회귀 0). 비주얼은 곡선 단면+wet 속+정점 lip, 회복은 2축(모양/파워 별도 곡선, ~20–60s).

→ **전체 설계/수학/통합지점/폐기된 접근/튜닝/회귀안전: [slime-split.md](slime-split.md)**

## 관련
- [../slime/stunned.md](../slime/stunned.md)
- [../infra/external-velocity-two-channel.md](../infra/external-velocity-two-channel.md)
