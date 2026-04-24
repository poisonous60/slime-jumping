# 튜닝 파라미터 (SlimeSettings 전체)

ScriptableObject. 물리/게임플레이 파라미터 한 곳에 모음. 기능별 세부 설명은 각 feature 문서 참조.

## Surface Movement
| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| crawlSpeed | 5 | A/D 최대 이동 속도 |
| crawlAcceleration | 50 | 입력 시 속도 올라가는 빠르기 |
| crawlDeceleration | 30 | 입력 놓았을 때 멈추는 빠르기 |
| crawlOverspeedDeceleration | 15 | 최대 속도 초과 시 감속 |
| slideGravityScale | 1 | 중력 접선 → 미끄러짐 배율 |
| slideThreshold | 1 | 이 이하 접선 중력은 무시 |
| slideResistance | 2 | slideSpeed에서 빼는 저항값 |
| stickSlideTransitionRampTime | 0.2 | 착지 후 슬라이드 램프업 시간 |

→ [../slime/crawling.md](../slime/crawling.md)

## Ground Dash
| 파라미터 | 기본값 |
|---------|--------|
| groundDashImpulse | 8 |
| groundDashCooldown | 0.3 |
| groundDashInputBuffer | 0.1 |
| groundDashAngleMin | 30 |
| groundDashAngleMax | 60 |
| dashFalloffStart | 5 |
| dashFalloffEnd | 13 |

→ [../slime/ground-dash.md](../slime/ground-dash.md)

## Hop
| 파라미터 | 기본값 |
|---------|--------|
| hopPower | 2.25 |
| hopPowerMin | 1.125 |
| hopMinAngle | 70 |
| hopMaxAngle | 90 |
| hopCooldown | 0.1 |
| hopBufferDuration | 0.1 |
| hopLandingRetention | 1 |
| hopMomentumBonus | 1.5 |
| hopMomentumMaxSpeed | 20 |

→ [../slime/hop.md](../slime/hop.md)

## Hyper Dash
| 파라미터 | 기본값 |
|---------|--------|
| tangentInheritanceFactor | 0.7 |
| hyperDashDeduction | 5 |
| hyperDashConversionDampingMin | 1.2 |
| hyperDashConversionDampingMax | 2.0 |

→ [../slime/hyper-dash.md](../slime/hyper-dash.md)

## Charge Circle
| 파라미터 | 기본값 |
|---------|--------|
| chargeRadius | 1.5 |
| autoDriftSpeed | 1.5 |
| inputAcceleration | 3.0 |
| chargeFullAimTime | 0.1 |
| baseLaunchPower | 15 |
| minLaunchPower | 3 |
| chargeCurveExponent | 1 |
| maxAimAngularSpeed | 180 |
| chargeNormalFollowSpeed | 5 |
| chargingCoyoteTime | 0.2 |
| coyoteGraceTime | 0.1 |
| chargingSurfaceProbeGraceFrames | 3 |

→ [../slime/charge-launch.md](../slime/charge-launch.md)

## Attach Debounce
| 파라미터 | 기본값 |
|---------|--------|
| attachToggleWindow | 0.1 |
| attachToggleLockoutThreshold | 10 |
| attachLockoutDuration | 0.1 |

→ [../slime/attach-debounce.md](../slime/attach-debounce.md)

## Energy Conversion
| 파라미터 | 기본값 |
|---------|--------|
| initialConversionRate | 2.25 |
| subsequentConversionRate | 0.9 |
| notJumpConversionBonus | 1.3 |
| hopConversionBonus | 1.0 |
| hopConversionEnergyBonus | 0.0 |
| aimDriftRadiusScale | 0.7 |
| energyConversionCoyoteTime | 0.12 |

→ [../slime/energy-conversion.md](../slime/energy-conversion.md)

## Stick
| 파라미터 | 기본값 |
|---------|--------|
| minStickDuration | 0.3 |
| maxStickDuration | 0.6 |
| stickSpeedScale | 0.1 |
| stickDecelerationTime | 0.05 |
| stickSlopeThreshold | 0.7 |
| stickCrawlMultiplier | 0.2 |
| ceilingAngle | 107 |
| ceilingReattachAngle | 101 |
| chargeEntryStickBonus | 0.2 |
| chargingSlideGravityScale | 0.5 |

→ [../slime/stick-timer.md](../slime/stick-timer.md)

## Air Control
| 파라미터 | 기본값 |
|---------|--------|
| airMaxHorizontalSpeed | 5 |
| airAcceleration | 20 |
| airDeceleration | 15 |
| floatGravityScaleRising | 1.0 |
| floatGravityScaleFalling | 0.5 |
| diveGravityScaleRising | 1.0 |
| diveGravityScaleFalling | 2.0 |
| floatGravityScaleInFluid | 0.5 |
| diveGravityScaleInFluid | 2.0 |
| risingGravityScale | 1 |
| fallingGravityScale | 1 |
| apexThreshold | 0 |
| postLaunchAirControlLockTime | 0.15 |

→ [../slime/airborne.md](../slime/airborne.md)

## Physics
| 파라미터 | 기본값 |
|---------|--------|
| gravity | -20 |
| slimeRadius | 0.4 |
| surfaceCheckDistance | 0.05 |
| contactSkin | 0.02 |
| depenetrationSkin | 0.01 |
| attachedCheckMultiplier | 3 |
| airborneCheckMultiplier | 1 |
| postLaunchIgnoreTime | 0.05 |
| launchClearanceThreshold | 0.05 |
| maxIgnoreTime | 0.15 |

→ [../slime/surface-attach.md](../slime/surface-attach.md)

## Stunned
| 파라미터 | 기본값 |
|---------|--------|
| defaultStunDuration | 0.5 |
| defaultKnockbackForce | 8 |

→ [../slime/stunned.md](../slime/stunned.md)

## 검증
- `SlimeSettings`: `chargeRadius`, `baseLaunchPower`에 `[Min(0.01f)]`, `initialConversionRate`에 `[Min(0f)]`.
- `SurfaceMaterial.OnValidate()`: 음수 방지, friction/retention 0~1 클램핑.

## 관련
- [../slime/surface-material.md](../slime/surface-material.md) — 표면별 배율 (SurfaceMaterial)
- [SlimeSettings.cs](../../../SlimeJumping_v2/Assets/Scripts/Settings/SlimeSettings.cs)
- [SlimeVisualSettings.cs](../../../SlimeJumping_v2/Assets/Scripts/Settings/SlimeVisualSettings.cs)
