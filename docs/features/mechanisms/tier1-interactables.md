# Tier 1 인터랙터블 (E키 상호작용)

`InteractableBase` 상속, **E 입력** (Interact 액션, Gamepad: buttonWest) 으로 발동.

## 감지 & 프롬프트 인프라
- `InteractionDetector`: Trigger 기반 후보 감지 (Slime child, Kinematic RB2D, InteractionSensor layer — Interactable layer만 충돌). broad-phase + narrow-phase(slime body overlap) 조합.
- `CurrentTarget` / `OnTargetChanged` / `OnInteracted` 이벤트.
- `InteractionPromptManager`: 현재 타깃 상단에 반투명 "E" TMP 월드 스페이스 표시. E를 누르거나 범위 벗어나면 숨김. 같은 타깃은 범위 밖으로 나갔다 재진입해야 다시 표시.
- `ReleaseOverride.Rescan`: override 해제 후 동일 trigger 재등록.

## 개별 인터랙터블

### SitPoseInteractable (IExternalController)
침대/벤치/신사/화톳불 옆자리. 물리 동결 + anchor 텔레포트만 담당.

**앉기 애니메이션**: 슬라임에 정적 부착된 `SitPoseAnimator` 컴포넌트가 구동 (외부 튠 라이브러리 없음):
- 4단계 상태머신: `SittingDown` OutBack squash → `Idle` Spring-Damper breathing → `GettingUp` anticipation + OutElastic settle → `Released`.
- 가구별 튜닝은 `SitPoseProfile` ScriptableObject.
- 비주얼 쓰기는 `SlimeVisual.SetExternalDeform(scale, asymmetry, chargeDip)` — `IsExternallyAnimated` 게이트 준수, MPB 경유로 SRP Batcher 호환.
- 일어나기 완료는 `SitPoseAnimator.OnFullyReleased` 이벤트 → Interactable이 그 시점에 `ReleaseOverride` 호출 (get-up 중 물리 동결 유지).

**3D 메쉬 호환**:
- `SlimeVisual.ApplySitDepthShift/RestoreSitDepthShift`가 face 스프라이트와 sibling `body3d` 서브트리 Z 동시 이동.
- `SetExternalSurfaceNormal(Vector2.up)`으로 sit 중 셰이더 법선을 world-up 고정 (진입 표면 각도 무관).
- `BodyNativeHeight`는 mesh `sharedMesh.bounds × rest lossyScale` 기반 rest-only 캐시 (피드백 루프 방지) — SlimeVisual-local 단위 반환.
- `body3d.localRotation`은 sit 중 `body3dRestRotation`만 적용 (벽/경사 틸트 무시). 일반 플레이에선 `body.localRotation * body3dRestRotation` 합성.

### MediaInteractable_TV
머티리얼 / 오디오 토글 (WebView는 추후).

### MediaInteractable_Phonograph
트랙 순환, 디스크 축 선택 회전, 클립 프리로드.

### MediaInteractable_PhonographV2
축음기 v2. E 입력 시 공유 `GramophonePanel` (modal, UILayer.Modal) 을 `OpenFor(this)` 로 띄우고, 플레이어가 트랙을 고르면 패널 페이드-아웃 후 `PlayTrack(index)` 콜백에서 `SoundService.PlayBgm(clip, crossfade, volume)` 호출 + 니들 Z-회전 틸트 + 디스크 스핀업 코루틴. "Stop music" 선택 시 `StopBgm(fade)` + 니들 복귀 + 스핀다운.

- 데이터 소스: `PhonographPlaylist` ScriptableObject (asset 재사용 가능). 각 축음기 인스턴스에 Playlist 참조만 꽂으면 됨.
- 오디오: v1 과 달리 임베디드 AudioSource 를 쓰지 않고 `SoundService` 의 BGM 버스 경유 → 옵션 패널 BGM 슬라이더에 반응, 크로스페이드 자동.
- 비주얼 분리: 콜라이더/프롬프트는 루트, 회전은 `discTransform`(자식 Pivot) 과 `needleArm` 만. 바디/트리거는 미이동.
- 다중 축음기: `SoundService` BGM 채널이 글로벌 싱글이라 두 번째 축음기에서 트랙을 고르면 첫 번째 축음기 BGM 은 크로스페이드로 대체되며, 첫 축음기의 디스크/니들은 그대로 남음 (각자 로컬 `IsPlaying` 플래그). 의도된 동작.
- v1 (`MediaInteractable_Phonograph`) 과 병존 — 기존 씬 호환.

### ApplianceInteractable_Washing
토글 + 시각 루트 리사주 진동 (콜라이더 분리 원칙 준수).

### FireworkInteractable
`ForceAirborneWithVelocity` 발사. `launchDelay`, `pushOriginOffset`, `launchAwayFromCenter`.

### FireworkInteractable_V2
오브젝트 자체가 폭죽 로켓. E 입력 시 `rocketVisual` 자식이 `flightDirection × flightDistance` 만큼 `flightDuration` 동안 `flightCurve` 로 비행 → 정점에서 `explosionVFX` 를 현재 위치로 이동 후 재생, `ForceAirborneWithVelocity` 로 슬라임 발사, `impulseEmitter` 카메라 쉐이크, `explosionSfx` 재생 → `rocketVisual` SetActive(false) → `respawnDelay` 초 대기 → 원위치 복귀 + `respawnScaleCurve` 팝인 (+선택적 `respawnAnimatorTrigger`). 비행/폭발/리스폰 전 구간 `ExtraCanInteract` 로 재입력 차단. 콜라이더/프롬프트는 루트에, 비주얼만 rocketVisual 에 두어 분리 원칙 준수.

### FireworkInteractable_V3
Stage 3 용 다발 폭죽. E 한 번에 `shells` 배열의 모든 셸이 각자 `igniteDelay` 에 점화 → 각자 `targetAnchor` 에서 `flightDuration` 후 폭발 (그림의 시간차 3방향). 셸마다 독립 코루틴이라 배열 순서 = 발사 순서가 아니며 정렬도 하지 않는다.
- **슬라임은 폭발 지점에서 방사형으로 밀린다** (도착 지점 지정 아님): `pushSpeed` 가 폭발 지점 정중앙 값이고 `pushRadius` 에서 0 으로 선형 감쇠. 폭발마다 독립으로 적용되므로 시간차 3발 = 밀림 3회. 공중/스턴 상태면 `AddExternalVelocity` 로 현재 궤적에 더하고(`ApplyHazardPush` 는 `OnHazardPushed` 로 화상 비주얼까지 물려 있어 안 씀), Attached/Charging 이면 외부 채널이 접선 성분만 먹으므로 `ForceAirborneWithVelocity` 로 떼어내야 실제로 날아간다.
- **비행 = V2 VFX-sync 계약 그대로**: `shellVfxPrefab`(SF_1) 인스턴스에서 `startLifetime = flightDuration`, `velocity = Δ/flightDuration` 으로 1발만 Emit 하고, **그 파티클이 권위**다 — 매 프레임 `GetParticles` 로 읽어 로켓 메시를 `파티클 − visualPivotOffset` 위치로 옮기고, 파티클이 죽는 순간이 폭발(Death 서브에미터)이자 밀림·SFX 시점. 메시를 따로 lerp 하면 시계가 둘이 되어 이펙트와 어긋난다(실제로 어긋났음). 인스턴스에서 `gravityModifier`·`limitVelocityOverLifetime`(dampen) 을 끄고(경로가 휘어 앵커 앞에서 죽음) 루트 렌더러도 끈다(로켓이 둘로 보임). 파티클이 안 나오면 폭발·밀림 없이 리스폰만 한다.
- 셸 겉모습·라이프사이클 = **stage 1 그대로 ×3**: 로켓(`firework_rocket.glb` + SparkleTrail)은 **씬에 미리 땅에 서 있는 오브젝트**(`shells[i].visual`)다. 런타임 Instantiate 아님 — 점화 순간 허공에서 생겨나면 안 된다. 폭발 시 트레일 `StopEmittingAndClear` + `SetActive(false)`, `respawnDelay` 후 rest pose 복귀 + `respawnScaleCurve` 팝인.
- **트레일 포즈는 MainScene 실측값을 복제**: SparkleTrail 은 로켓 루트가 아니라 glb 안쪽 `root` 노드의 자식이고 `localPos (-0.58,0,0)` / `localRot 90°X` / `scale 1.667` 이다(유저가 씬에서 손튜닝한 값 — V2 *셋업 스크립트* 의 local 0 과 다르다). 루트에 local 0 으로 붙이면 스파크가 꼬리가 아니라 피벗에서 나와 로켓과 떨어져 보인다.
- 기울기는 **런타임 계산, 하드코딩 아님**: 대기 중엔 똑바로 서 있고, 점화 순간 `FromToRotation(up, anchor − visual.position) × rest회전` 으로 경로 방향으로 눕는다. 앵커를 어디로 옮기든 자세와 궤적이 따라간다. 전제는 rest 자세가 "똑바로 선" 상태 — glb 축이 Y-up 이 아니라 셋업이 -90°X 보정(`RocketUpright`)을 rest 회전으로 넣어 둔다. `visualPivotOffset`(stage1 -0.3/0.29)도 같은 회전으로 기울여 적용한다.
- 재입력 차단은 타이머가 아니라 **살아 있는 셸 카운트**: 마지막 셸 코루틴의 `finally` 에서 `busy` 를 푼다(타임아웃·중단으로 시간이 늘어도 두 번째 볼리가 같은 셸을 덮치지 않게). `OnDisable` 에서는 숨은 로켓을 rest pose 로 되돌린다 — 코루틴이 죽으면 로켓이 영영 안 돌아온다(OffscreenSleeper 등으로 비활성화될 수 있음). 호스트 선택 시 기즈모로 셸 라인 + 앵커별 `pushRadius` 원 표시.
- 셋업: `SlimeJumping/Interaction/Setup Firework V3 Test` (앵커 마커 구체 + 로켓 템플릿 + stage1 SFX/임펄스 배선). 검증 기준: 폭발이 앵커 마커에 얹히는지, 발판 위에서 첫 폭발에 떨어져 나가고 이후 2발이 공중에서 밀어주는지.

### ShrineInteractable (IExternalController)
일본 신사 앞에서 E로 데모 엔딩 진입 (2026-08-15 행성 리빌 개편 — 딸기가 연출 캐리어, 루트 = 저작 경로 하나).
- 시퀀스: 슬라임 동결 → `spawnAnchor`(신사 옆 구 WispAnchor)에 딸기 스폰(자식 Light 전부 off, 등장음 `Play2D`) → `spawnHold` 정지 → **점멸 2회**(`departBlinkSeconds`, 켜짐/꺼짐 각각 `blinkOnSound`/`blinkOffSound`) → `postBlinkHold` 정지 → **`flightPath`(`ShrineFlightPath` WaypointSnakePath) 주행** — 신사→행성 선회→림 정착까지 경로 하나가 전부, 경로 끝점 = 최종 위치 = 아이리스 중심 → `appearHold` 강조 → `DeleteSave` → reverse iris.
- 루트 진행도(이동 거리 비율)가 연출 타임라인의 축: `PlanetPhaseTransition.ApplyProgress()` 는 `[transitionStart, transitionEnd]` 창으로 재매핑(창 진입 순간 `planetTransitionSound` 1회), 카메라는 `cameraDepartProgress` 에서 행성 샷으로(priority 200 + Brain DefaultBlend push, 벨 메커니즘), `cameraReturnProgress` 에서 게임플레이 카메라로 복귀(1 = 경로 끝에서).
- 속도 = `pathSpeed` × `speedCurve`(x = 진행도, y = 배율) — 그래프로 루트 전체 완급 조형. 경로 곡률은 `WaypointSnakePath` 웨이포인트별 **`tension`**(-1 = 접선 2배 둥글게, 0 = Catmull-Rom, 1 = 직선 꺾임; Hermite 변환, 기존 소비자는 기본 0 이라 무영향).
- 딸기 크기 = `wispScale` + **`sizeCompensation`**(0 = 순수 원근 축소, 1 = 스폰 화면 크기 유지, 중간 = 지수 부분 보정 `(d/스폰거리)^k`). 상하 보빙 = `bobAmplitude`/`bobFrequency`(루트 양끝 10% 자동 페이드). WispFlicker 는 자식 `Fruit` 로컬만 만지므로 루트 구동과 안 싸운다.
- 구도는 벨과 같은 존 방식: `ShrinePlanetRegion`(BoxCollider2D) 박스를 끌면 `FitVCamToRegion()`(인터랙트 시 자동 + 컨텍스트 메뉴)이 VCam 을 맞춘다. 박스 z = 초점 깊이. 뒤편 통과 가림은 불투명 본체 z-테스트가 공짜로 처리.
- 사운드 4종 = `Assets/Settings/Sounds/Sound_Strawberry{Appear,BlinkOn,BlinkOff,PlanetTransition}.asset` (전부 `Play2D` — 리스너가 카메라를 타고 떠나므로; 클립은 유저 배정).
- 연출 동안 슬라임은 `SetOverride(self)`로 동결 (no-op ExternalController) — 씬 전환과 함께 자연 소멸. `flightPath` 미배선 시 딸기는 신사에 머물고 카메라 블렌드 + 즉시 planet2 + 아이리스로 폴백.
- 픽업 프리팹: `SpriteRenderer` + `WispFlicker` + 3D `Point Light` 자식. `WispFlicker`가 스폰 연출(scale 0 → overshoot → settle, alpha fade-in, `spawnDuration` 0.9s)을 구동하면서 idle bob/drift/scale-wobble/alpha-flicker blend. 같은 ramp로 링크된 `Light.intensity` 펄스 (URP 2D Renderer 아닌 3D Point Light — 신사 3D 메쉬 직접 조명).

## 인터페이스
- [IInteractable.cs](../../../SlimeJumping_v2/Assets/Scripts/Interfaces/IInteractable.cs) — E키, Interact 액션
- [IExternalController.cs](../../../SlimeJumping_v2/Assets/Scripts/Interfaces/IExternalController.cs) — 외부 제어 오버라이드

## 관련
- [../slime/state-machine.md](../slime/state-machine.md) — IExternalController override
- [memo.md](memo.md) — 버튼 없는 트리거 연출 (별도 파이프라인)
