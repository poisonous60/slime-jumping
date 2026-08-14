using UnityEngine;

namespace SlimeJumping.Background
{
    /// 현재 RenderSettings.skybox의 그라디언트/스칼라를 매 프레임 복사해 렌더러가 스카이박스 크로스페이드를 그대로 따라가게 한다.
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public class SkyboxColorMatch : MonoBehaviour
    {
        static readonly int GradientId = Shader.PropertyToID("_Gradient");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int ExponentId = Shader.PropertyToID("_Exponent");
        static readonly int YawId = Shader.PropertyToID("_DirectionYaw");
        static readonly int PitchId = Shader.PropertyToID("_DirectionPitch");

        private Renderer targetRenderer;
        private MaterialPropertyBlock block;

        void OnEnable()
        {
            targetRenderer = GetComponent<Renderer>();
            block = new MaterialPropertyBlock();
        }

        void LateUpdate()
        {
            var sky = RenderSettings.skybox;
            if (sky == null || !sky.HasProperty(GradientId)) return;

            targetRenderer.GetPropertyBlock(block);
            var ramp = sky.GetTexture(GradientId);
            if (ramp != null) block.SetTexture(GradientId, ramp);
            block.SetFloat(IntensityId, sky.GetFloat(IntensityId));
            block.SetFloat(ExponentId, sky.GetFloat(ExponentId));
            block.SetFloat(YawId, sky.GetFloat(YawId));
            block.SetFloat(PitchId, sky.GetFloat(PitchId));
            targetRenderer.SetPropertyBlock(block);
        }
    }
}
