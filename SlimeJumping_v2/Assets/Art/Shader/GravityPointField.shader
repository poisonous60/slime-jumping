// GravityPoint2D 영향 반경 비주얼. 시각화 연구 Case B 규칙 준수: 경계가 부드러운 장(커버리지
// 블렌드로 힘이 반경에서 C1 수렴)에는 림을 그리지 않는다 — 림은 물리 불연속의 표시라 여기선 거짓말.
// 장의 존재는 실제 forceProfile 곡선(베이크 텍스처)에 비례하는 글로우 + 안쪽으로 흐르는 펄스가 말한다:
// 힘이 강한 곳만 보이고, 경계로 갈수록 물리와 똑같이 사라진다. 쿼드 스케일 = 지름 2R.
Shader "SlimeJumping/GravityPointField"
{
    Properties
    {
        _FieldGlowColor ("Field Glow Color", Color) = (0.80, 0.86, 1.00, 0.045)
        [NoScaleOffset] _ProfileTex ("Force Profile (R = q 0..1)", 2D) = "black" {}
        _PulseStrength ("Pulse Strength", Range(0, 0.2)) = 0.05
        _PulseSpeed ("Pulse Speed", Float) = 0.25
        _PulseCount ("Pulse Ring Count", Float) = 3
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _FieldGlowColor;
                float _PulseStrength;
                float _PulseSpeed;
                float _PulseCount;
            CBUFFER_END

            TEXTURE2D(_ProfileTex);
            SAMPLER(sampler_ProfileTex);

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 p = IN.uv * 2.0 - 1.0;
                float r = length(p);
                float aa = max(fwidth(r), 1e-4);

                // 힘 크기 = 점의 forceProfile 곡선 베이크 텍스처 (비주얼 = 물리)
                float fieldMag = saturate(SAMPLE_TEXTURE2D(_ProfileTex, sampler_ProfileTex, float2(r, 0.5)).r);

                // 안쪽으로 흐르는 펄스 링 (힘 크기로 마스킹 → 중심·림 근처에서 자연 소멸)
                float s = frac(r * _PulseCount + _Time.y * _PulseSpeed);
                float pulse = pow(s, 10.0) * _PulseStrength * fieldMag;

                float interiorMask = 1.0 - smoothstep(1.0 - aa, 1.0 + aa, r);
                float alpha = (_FieldGlowColor.a * fieldMag + pulse) * interiorMask;

                return half4(_FieldGlowColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
