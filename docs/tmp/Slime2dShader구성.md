SlimeBody Shader 구성 문서

개요
슬라임 본체의 시각적 변형을 담당하는 Shader Graph. Unity URP의 Sprite Lit sub-target 기반. 원형 텍스처(body.png)를 입력받아 fragment 단계에서 UV를 재매핑해 비선형 변형을 만든다.
Transform의 localScale은 본질적으로 원을 타원으로만 변형할 수 있는 한계가 있다. 달걀 모양이나 한쪽이 움푹 들어간 모양 같은 비affine 변형은 불가능하다. 이 셰이더는 그 한계를 UV 재매핑으로 우회한다.
파일 위치

Shader Graph: Assets/Art/Shader/SlimeBody.shadergraph
Material: Assets/Art/Slime/SlimeBody.mat
적용 위치: Body GameObject의 SpriteRenderer.material

노출 프로퍼티
이름타입범위기본값목적_MainTexTexture2D—body.png슬라임 원본 원형 텍스처. SpriteRenderer가 런타임에 자동 주입_AsymmetryFloat-1 ~ 10세로 방향 비대칭 (달걀 모양 형성)_ChargeDipFloat0 ~ 0.50차징 시 슬라임 한 쪽이 움푹 들어가는 효과 강도_DipCenterUFloat-0.5 ~ 0.50Dip이 슬라임 표면 좌우 어디에 생길지 (aim 접선 성분)
좌표계 규칙
슬라임 body는 body.up = surfaceNormal로 회전한다. 따라서 셰이더 내부 좌표는:

UV의 v축 (세로): body-local, 표면 법선 방향. v=0은 표면 쪽(바닥), v=1은 표면 반대쪽(머리).
UV의 u축 (가로): body-local, 표면 접선 방향. 평지에선 world x, 벽에선 world y.

이 회전 덕에 셰이더는 어떤 표면 각도에서도 동일한 논리로 작동한다.
UV 재매핑 파이프라인
Fragment 단계에서 원본 UV를 다음 순서로 변형한 뒤 텍스처를 샘플링한다.
원본 UV
  ↓
[1] v축 asymmetry 재매핑    ← _Asymmetry
  ↓
[2] u축 기준 dip 계산         ← _DipCenterU, _ChargeDip
  ↓
[3] v축에 dip 오프셋 추가
  ↓
재매핑된 UV → Sample Texture 2D → Base Color + Alpha
[1] Asymmetry — v축 재매핑 (달걀 모양)
원본 원이 대칭이라 단순 scale로는 달걀이 안 나온다. 출력의 아래 절반을 텍스처의 적도(넓은 부분) 근처에서 샘플링하게 만들어 "아래는 평평하고 넓음, 위는 둥금" 모양을 만든다.
식:
bottomSampleStart = _Asymmetry * 0.5
if (v < 0.5):
    v_remapped = lerp(bottomSampleStart, 0.5, v * 2)
else:
    v_remapped = v  (그대로)

_Asymmetry = 0: 변형 없음. 완전한 원
_Asymmetry > 0: 아래 절반이 텍스처 적도에서 샘플링됨 → 평평하고 넓은 바닥
_Asymmetry < 0: 반대 방향 효과 (프로젝트에선 음수 사용. C#에서 -smoothedSquashStrength * surfaceSquashAsymmetry로 전달)

노드 구성:
UV → Split → G(v)
Comparison: G < 0.5 → bool
_Asymmetry × 0.5 → bottomSampleStart
v × 2 → Multiply
Lerp(bottomSampleStart, 0.5, v*2) → 아래 절반 결과
Branch(bool, Lerp 결과, v 원본) → v_remapped
[2~3] Charge Dip — 한 쪽이 움푹 들어감
uv.u 위치에 따라 v를 추가로 오프셋한다. 가운데 지점(u = 0.5 + DipCenterU)에서 가장 많이 오프셋되고 가장자리에선 0이다.
식:
dipShape = 1 - 4 * (u - 0.5 - _DipCenterU)²
dipShape = max(dipShape, 0)         // 음수 클램프 (필수)
dipAmount = dipShape * _ChargeDip
v_final = v_remapped + dipAmount

_ChargeDip = 0: dip 없음
_ChargeDip > 0: _DipCenterU 위치에서 가장 깊게 움푹 들어감
_DipCenterU = 0: dip이 body 정가운데
_DipCenterU = 0.2: dip이 body 오른쪽 어깨 쪽으로 이동 (접선 양의 방향)
_DipCenterU = -0.2: dip이 왼쪽 어깨 쪽

max 클램프가 중요한 이유: DipCenterU가 shift되면 반대쪽에서 (u - 0.5 - DipCenterU)²가 0.25를 넘어 dipShape가 음수가 된다. 클램프 없이는 shader가 반대쪽을 밀어 올려 이상한 형태가 생긴다.
노드 구성:
Split의 R(u) → Subtract(- 0.5) → Subtract(- _DipCenterU) → shifted_u
shifted_u² → Multiply(× 4) → One Minus → dipShape
Max(dipShape, 0) → dipShape_clamped
dipShape_clamped × _ChargeDip → dipAmount
v_remapped + dipAmount → v_final
최종 샘플링
Combine(R = 원본 u, G = v_final) → 재매핑된 UV
Sample Texture 2D(MainTex, 재매핑된 UV) → RGBA
RGBA의 RGB → Base Color
RGBA의 A → Alpha
효과 합산 원리
Asymmetry와 Dip은 모두 v축을 조작하지만 작동 방식이 다르다:

Asymmetry: v 전체를 재매핑 (v=0.3을 v=0.4에서 샘플링)
Dip: v에 위치 의존적 오프셋 추가 (u=0.5에서만 v가 오프셋)

둘은 독립적으로 작동한다. 동시에 활성화되면:

달걀 모양 (asymmetry) 위에
aim 방향 쪽 한 어깨가 움푹 들어감 (dip)

각 효과가 다른 u/v 영역에 영향을 주기 때문에 서로 간섭 없이 중첩된다.
C#과의 연동
SlimeVisual.cs의 LateUpdate()에서 매 프레임 MaterialPropertyBlock으로 파라미터를 전달한다.
csharpprivate static readonly int AsymmetryID  = Shader.PropertyToID("_Asymmetry");
private static readonly int ChargeDipID  = Shader.PropertyToID("_ChargeDip");
private static readonly int DipCenterUID = Shader.PropertyToID("_DipCenterU");

// Asymmetry: 표면 압착 강도에서 계산
float asymmetry = -smoothedSquashStrength * settings.surfaceSquashAsymmetry;

// ChargeDip: 차징 power ratio에서 계산
float chargeDip = 0f;
if (Charging)
    chargeDip = chargingPowerRatio * settings.chargingDipIntensity;

// DipCenterU: aim의 접선 성분 × lateral shift
float dipCenterU = 0f;
if (Charging) {
    Vector2 tangent = new Vector2(-lastSurfaceNormal.y, lastSurfaceNormal.x);
    float aimTangent = Vector2.Dot(slimeController.AimDirection, tangent);
    dipCenterU = aimTangent * settings.chargingDipLateralShift;
}

bodyRenderer.GetPropertyBlock(mpb);
mpb.SetFloat(AsymmetryID, asymmetry);
mpb.SetFloat(ChargeDipID, chargeDip);
mpb.SetFloat(DipCenterUID, dipCenterU);
bodyRenderer.SetPropertyBlock(mpb);
smoothedSquashStrength는 SmoothDamp로 부드러워진 값을 쓴다. 이유: body의 Transform scale도 같은 SmoothDamp를 쓰는데 asymmetry만 snap하면 표면 전환 순간 "scale은 부드럽고 텍스처 모양은 튀는" 불일치가 생긴다.
관련 Settings
SlimeVisualSettings ScriptableObject의 관련 필드:

surfaceSquashAsymmetry — asymmetry의 최대 강도 (기본 0.3)
chargingDipIntensity — ChargeDip의 최대 강도 (기본 0.2)
chargingDipLateralShift — DipCenterU의 최대 shift량 (기본 0.2)
deformSmoothTime — smoothedSquashStrength의 SmoothDamp 시간 (기본 0.1)

주의사항

Transform rotation과 scale은 셰이더와 독립적으로 동작. 셰이더는 texture space 변형만 담당. Transform scale(타원 변형), 위치 offset, 회전(surfaceNormal 정렬)은 C#이 별도로 적용.
SpriteRenderer의 Material 슬롯이 비어있으면 기본 Sprite-Default로 되돌아가 모든 dip/asymmetry 효과가 사라진다. Play 모드 진입 시 확인.
MaterialPropertyBlock은 GC-free하게 설계. new MaterialPropertyBlock()은 Start()에서 한 번만. LateUpdate()에서는 재사용.
Sub-target 선택 영향: Sprite Lit은 2D 라이팅 패스를 거친다. 라이팅을 안 쓸 거면 Sprite Unlit으로 바꾸는 게 성능상 유리하다.
DipCenterU 값 범위: 절댓값이 0.3을 넘으면 dip이 body 가장자리를 벗어나 어색하다. C#에서 chargingDipLateralShift를 0.2 이하로 유지 권장.


이 문서는 셰이더 자체만 다룬다. Transform 기반 변형(C-1~C-6 squash/stretch 파이프라인)은 별도 문서 참조.