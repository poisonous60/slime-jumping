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

## 관련
- [../slime/stunned.md](../slime/stunned.md)
- [../infra/external-velocity-two-channel.md](../infra/external-velocity-two-channel.md)
