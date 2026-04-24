# 메모 / 내레이션 (Memo)

트리거 영역 진입 시 자동으로 월드 스페이스 종이쪽지 UI가 페이드인, 이탈 시 페이드아웃. **입력 없이** "지나가면서 본다"는 UX — 플레이 리듬을 끊지 않음.

## 동작 불변식
- **시간 독립성**: 모든 페이드·패럴럭스 보간은 `Time.unscaledDeltaTime`. `Time.timeScale` 변경(일시정지/슬로우모)에도 메모 연출 타이밍 고정.
- **데이터 주도**: `MemoProfile` ScriptableObject가 콘텐츠, `layoutPrefab`(프리팹 배리언트)이 크기·폰트·슬롯 배치 책임. 같은 레이아웃으로 콘텐츠만 바꾸거나 반대도 에셋 재생성 없이 가능.
- **살짝 따라오기 (Parallax)**: 패널 기준 위치는 트리거의 anchor(씬 고정). 매 프레임 `offset = clamp((slime − anchor) × followStrength, maxFollowOffset)`를 더해 슬라임 방향으로 약간 쏠림. 카메라를 쫓는 게 아니라 씬에 박힌 채 미세 반응 — "종이를 자석처럼 살짝 당기는" 느낌.
- **기존 Interaction과 분리**: 버튼 인터랙션(`IInteractable` + `InteractionDetector`)과 별개 파이프라인. 메모는 `Collider2D(isTrigger)`만 사용, `OnTriggerEnter2D`에서 `GetComponentInParent<SlimeController>()`로 슬라임 판정. 같은 Collider idiom이지만 자동 연출이라 버튼 큐/narrow-phase 불필요.

## 스크립트 구조
- **`MemoProfile`** (ScriptableObject, `[CreateAssetMenu("SlimeJumping/Memo Profile")]`) — 콘텐츠 캐리어.
  - `layoutPrefab` (프리팹 배리언트 포인터)
  - `backgroundSprite`/`backgroundTint` (배경 오버라이드. tint는 프리팹 기본색과 **곱해짐** — white=유지)
  - `titleText`/`bodyText`/`illustration`
  - 페이드 시간, 패럴럭스 파라미터 (`followStrength`, `maxFollowOffset`, `followSmoothing`)
- **`MemoTrigger`** — `Collider2D(isTrigger)` + `OnTriggerEnter2D/Exit2D`. `IInteractable` 구현 안 함 — 입력 없이 자동 연출.
- **`MemoDisplay`** — 실제 패널 인스턴스 소유자. 첫 `Show(profile, anchor)`에서 `profile.layoutPrefab` 인스턴스화하고 `MemoSlot` 태그로 자식(Background/Title/Body/Illustration) 찾아 바인딩. `CanvasGroup.alpha`를 `Time.unscaledDeltaTime` 보간 단일 코루틴 (재호출 시 StopCoroutine 후 현재 alpha에서 재개 → 급 enter/exit에도 부드럽게 반전). LateUpdate에서 `panelRoot.position = anchor + Vector2.ClampMagnitude((slime − anchor) × followStrength, maxFollowOffset)`를 `1 − exp(−k·Δt)` 프레임레이트 무관 lerp.
- **`MemoSlot`** — `MemoSlotType { PanelRoot, Background, Title, Body, Illustration }` 태그. 프리팹 하이어라키 자유 재배열해도 `MemoDisplay`가 슬롯 타입으로 참조 찾음.

## 레이아웃 배리언트
`Assets/Prefabs/Memo/MemoPanel_Base.prefab` 기반 프리팹 배리언트로 크기/폰트/슬롯 위치 WYSIWYG 편집. 기본 3종:
- `MemoPanel_TextOnly` — 일러스트 비활성, Body 영역 확장.
- `MemoPanel_ImageOnly` — Title/Body 비활성, Illustration 풀 사이즈.
- `MemoPanel_ImageTop` — 상단 일러스트 + 하단 Title/Body.

시각 스타일은 "Stardew / A Short Hike" 종이쪽지 — 크림색(#F5E6C8) 9-slice 배경 + 드롭 섀도 + 루트 `−2°` 틸트.

## 메모 에셋 생성
`Assets/Memos/`에서 Create → SlimeJumping → Memo Profile로 SO 생성 후 `layoutPrefab`/텍스트/이미지 할당. 씬에서는 `MemoZone` GameObject에 `BoxCollider2D(isTrigger) + MemoTrigger + MemoDisplay`를 붙이고 Profile 연결.

## 테스트 씬
`Assets/Scenes/MemoTest.unity` (3존: Text/Image+Text/ImageOnly + 슬라임 프리팹). **한글 텍스트는 TMP 기본 폰트에 글리프가 없어 `□`로 나오므로** 실제 운용 시 프리팹 배리언트의 Title/Body 슬롯에 한글 TMP 폰트 에셋 할당 필요.

## 에디터 유틸
`Assets/Editor/MemoSetup/MemoSetup.cs`의 `BuildAll / BuildProfiles / BuildTestScene` 정적 메서드 (초기 세팅 재현용; 한 번 실행 후 수동 튜닝).

## 관련 파일
- [MemoNote.cs](../../../SlimeJumping_v2/Assets/Scripts/Interaction/MemoNote.cs)
