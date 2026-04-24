# 스크립트 구조

## SlimeController (partial classes)
| 스크립트 | 네임스페이스 | 역할 |
|---------|-------------|------|
| `SlimeController.cs` | SlimeJumping.Controller | partial 메인: 필드, 라이프사이클, FixedUpdate 디스패치, 표면 modifier/velocity, ChangeState, RegisterAttachToggle |
| `SlimeController.Attached.cs` | | partial — FixedUpdateAttached (슬라이드, 크롤, 스틱) |
| `SlimeController.Charging.cs` | | partial — FixedUpdateCharging (에임, 드리프트, 코요테) |
| `SlimeController.Airborne.cs` | | partial — FixedUpdateAirborne, 착지 처리, CalculateStickDuration |
| `SlimeController.Input.cs` | | partial — HandleJump/Vertical 입력 콜백 |
| `SlimeController.Launch.cs` | | partial — Launch, Hop, ApplyGroundDash |
| `SlimeController.Stunned.cs` | | partial — FixedUpdateStunned |
| `SlimeController.Teleport.cs` | | partial — TeleportTo |

## 입력 / 설정
| 스크립트 | 역할 |
|---------|------|
| `SlimeInputReader.cs` | New Input System 래퍼 (Move, Jump) |
| `SlimeSettings.cs` | ScriptableObject. 물리/게임플레이 튜닝 |
| `SlimeVisualSettings.cs` | ScriptableObject. 비주얼/변형 튜닝 |

## 인터페이스
| 스크립트 | 역할 |
|---------|------|
| `HazardEffect.cs` | DamageType enum + HazardEffect struct |
| `IHazardSource.cs` | 위험 오브젝트 |
| `IPlatformEffect.cs` | 플랫폼 효과 (OnSlimeAttach/Detach/Stay) |
| `IInteractable.cs` | 상호작용 (E키, Interact 액션) |
| `IExternalController.cs` | 외부 제어 오버라이드 |

## 물리
| 스크립트 | 역할 |
|---------|------|
| `SurfaceProbe.cs` | OverlapCircle + ClosestPoint 기반 active contact, 5-step geometric SelectPrimary, 2-Phase SnapToSurface |
| `IgnoredColliderList.cs` | 2-mode 무시 리스트 (ReattachGuard / Passthrough) |
| `SurfaceMaterial.cs` | ScriptableObject. 표면별 배율 데이터 |
| `SurfaceModifiers.cs` | 구조체. 표면 배율 컨테이너 |
| `SurfaceTag.cs` | MonoBehaviour 마커. 기본값과 다른 표면에만 부착 |

## 플랫폼 (PlatformMotor 기반)
| 스크립트 | 설명 |
|---------|------|
| `PlatformMotor.cs` | 추상 베이스 |
| `PlatformCarrier.cs` | 슬라임에 부착. `[DefaultExecutionOrder(-200)]` |
| `WaypointPlatform.cs` | 웨이포인트 이동 |
| `RotatingPlatform.cs` | 연속/oscillate 회전 |
| `TiltPlatform.cs` | 좌우 기울기 |
| `PendulumPlatform.cs` | 진자 |
| `RollingDiskPlatform.cs` | 360° 회전 원형 |
| `SpringyBranchPlatform.cs` | 탄력 가지 |
| `CrumblingPlatform.cs` | 무너지는 |
| `DeformablePlatform.cs` | 진흙/찰흙 소성 변형 |
| `PushDoor.cs` | 밀어서 여는 문 |
| `Rope.cs` | Verlet 로프 |
| `SpringyBranchVisual.cs` | 가지 비주얼 휨 본 체인 |
| `MegaDeform2DCollider.cs` | MegaFiers deform mesh → EdgeCollider2D 복사 |

## 메커니즘
| 스크립트 | 설명 |
|---------|------|
| `SpikeHazard.cs` | 가시 |
| `TurretHazard.cs` | CCTV 확장 사격 |
| `SawBladeHazard.cs` | 톱날 |
| `KnifeHazard.cs` | 칼 (body passthrough) |
| `CCTVTracker.cs` | 3D 추적 |
| `JumpPad.cs` | 점프대 |
| `ConveyorBelt.cs` | 컨베이어 |
| `EnvironmentalZone.cs` | 환경 존 베이스 |
| `WaterZone.cs` | 물 v1 |
| `WaterZone_v2.cs` (+ `.Shape/.Simulation/.Mesh/.Interaction.cs`) | 동적 폴리곤 물 |
| `WaterZoneAudioManager.cs` | 씬 단일 오디오 브로드캐스트 |
| `WindZone2D.cs` | 바람 |
| `DragZone.cs` | 모래/감속 |
| `GravityField.cs` | 중력 치환 |
| `Portal.cs` | 포탈 쌍 |
| `PortalSwapper.cs` | 포탈 위치 교체 |
| `SignalSource.cs` | 신호 베이스 |
| `WeightDescendPlatform.cs` | 무게 하강 |
| `PlatformRiderSensor.cs` | 탑승 감지 |

## 인터랙션
| 스크립트 | 설명 |
|---------|------|
| `InteractionDetector.cs` | Trigger 기반 후보 감지 |
| `InteractionPromptManager.cs` | 월드 스페이스 "E" 프롬프트 |
| `MemoNote.cs` | Tier 3 검증용 IInteractable |
| `KnockdownController.cs` | Tier 3 검증용 IExternalController |
| `KnockdownTrigger.cs` | KnockdownController 테스트 트리거 |

## 비주얼 / 렌더링
| 스크립트 | 설명 |
|---------|------|
| `SlimeVisual.cs` | 절차적 변형 (LateUpdate) |
| `SlimeStateDebugUI.cs` | 상태 표시 Gizmo UI |
| `SlimeChargeVisual.cs` | 판정원 UI (Screen Space Overlay, 디버그) |
| `SlimeChargeRingVisual.cs` | 월드 공간 링 비주얼 (디버그, 기본 비활성) |
| `SlimeCameraFollow.cs` | SmoothDamp 카메라 |
| `VFXType.cs` / `VFXColorPalette.cs` / `ParticlePaletteBinder.cs` | VFX 팔레트 |
| `LandingDustSpawner.cs` | 착지 파티클 |
| `WorldTrailManager.cs` / `LocalTrailReceiver.cs` / `TrailStamper.cs` / `TrailColliderRegistry.cs` / `TrailPersistence.cs` | 진행 트레일 |
| `UnderwaterVolumeBinder.cs` | InFluid 기반 Global Volume weight lerp |

## UI / 게임 플로우
| 스크립트 | 설명 |
|---------|------|
| `GameFlow.cs` | 메인메뉴 ↔ 게임플레이 씬 로드 |
| `SlimeStartStateManager.cs` | 씬 시작 시 특정 Interaction에 슬라임 투입 |
| `GameOptions.cs` | 데모용 옵션 영속화 |
| `OptionsPanelController.cs` | 옵션 패널 ESC 토글 |
| `UIClickSoundInstaller.cs` | 버튼 클릭 SFX 후킹 |
| `ResolutionScaleController.cs` | FOV 해상도 비례 스케일 |
| `BackgroundCameraOption.cs` | BG 카메라 + TIS 온/오프 + Main 카메라 Overlay↔Base 전환 |

## 에디터
| 스크립트 | 설명 |
|---------|------|
| `SlimeSceneSetup.cs` | 씬 자동 구성 |
| `DeformablePlatformSpawner.cs` | 프리셋 메뉴 |
| `DeformablePlatformTestSetup.cs` | 테스트 스테이지 |
| `TrailEditor.cs` | 트레일 마이그레이션 툴 |

## 아키텍처 상수
- Unity 6000.3.11f1.
- `Rigidbody2D` + `gravityScale = 0` + 수동 velocity 관리.
- `CircleCollider2D` 단일.
- ScriptableObject 기반 튜닝.
