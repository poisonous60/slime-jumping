using UnityEngine;
using SlimeJumping.Settings;

namespace SlimeJumping.Thumbnail
{
    /// 썸네일 촬영용 슬라임 포즈 프리뷰. 인스펙터의 speed/direction 을 SlimeVisual 과 같은 식으로
    /// 풀어서(속도곡선 → _VelocityStretchAmount, 접지 → localScale 눌림) 몸체 메시에 그대로 먹인다.
    /// Play 모드에선 SlimeVisual 이 같은 프로퍼티를 매 프레임 덮으므로 편집 모드에서만 돈다.
    [ExecuteAlways]
    public class ThumbnailSlimePose : MonoBehaviour
    {
        [SerializeField] private MeshRenderer bodyMesh;
        [SerializeField] private Transform body3d;
        [SerializeField] private SlimeVisualSettings settings;
        [Tooltip("body3d 의 변형 전 localScale. 눌림/늘어남은 여기에 곱해진다.")]
        [SerializeField] private Vector3 body3dRestScale = new Vector3(0.26f, 0.26f, 0.26f);

        [Header("Motion")]
        [Min(0f)] [SerializeField] private float speed = 20f;
        [Tooltip("0 = 오른쪽, 90 = 위. 사선 비행은 45 근처.")]
        [Range(-180f, 180f)] [SerializeField] private float directionDegrees = 45f;

        [Header("Ground")]
        [Tooltip("켜면 평지 접지 눌림(1+I, 1-I)을 겹친다. 사선 비행 썸네일은 끈 상태.")]
        [SerializeField] private bool grounded;

        private MaterialPropertyBlock mpb;

        private void Update()
        {
            if (Application.isPlaying) return;
            Apply();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying) Apply();
        }

        private void Apply()
        {
            if (bodyMesh == null || settings == null) return;

            float rad = directionDegrees * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            float speedNorm = Mathf.Clamp01(speed / Mathf.Max(0.01f, settings.velocityStretchSpeedRef));
            float stretchT = (speed - settings.velocityStretch3DSpeedMin)
                             / Mathf.Max(0.01f, settings.velocityStretch3DSpeedMax - settings.velocityStretch3DSpeedMin);
            float stretch = settings.velocityStretch3DCurve != null
                ? settings.velocityStretch3DCurve.Evaluate(stretchT)
                : 0f;
            float squash = grounded ? settings.surfaceSquashIntensity : 0f;

            if (mpb == null) mpb = new MaterialPropertyBlock();
            bodyMesh.GetPropertyBlock(mpb);
            mpb.SetVector("_VelocityDirWS", new Vector4(dir.x, dir.y, 0f, speedNorm));
            mpb.SetFloat("_VelocityStretchAmount", stretch);
            mpb.SetFloat("_Asymmetry", grounded ? -settings.surfaceSquashAsymmetry : 0f);
            mpb.SetVector("_SurfaceNormalWS", new Vector4(0f, 1f, 0f, 0f));
            bodyMesh.SetPropertyBlock(mpb);

            if (body3d != null)
                body3d.localScale = Vector3.Scale(body3dRestScale, new Vector3(1f + squash, 1f - squash, 1f));
        }
    }
}
