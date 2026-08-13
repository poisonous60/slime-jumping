// EngraveTextMask.shader
// Bakes a TMP mesh into the engrave mask RT: font-atlas SDF alpha is thresholded
// into soft coverage. Max-blended so overlapping glyph quads never double up.
// Drawn by RockEngraving.cs through a CommandBuffer with explicit ortho matrices.
Shader "Hidden/SlimeJumping/EngraveTextMask"
{
    Properties
    {
        _MainTex ("Font Atlas", 2D) = "black" {}
        _EdgeSoftness ("Edge Softness", Range(0.005, 0.3)) = 0.08
    }
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            BlendOp Max
            Blend One One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float _EdgeSoftness;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct V2F
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            V2F Vert(Attributes v)
            {
                V2F o;
                o.positionCS = mul(UNITY_MATRIX_VP, mul(UNITY_MATRIX_M, float4(v.positionOS.xyz, 1)));
                o.uv = v.uv;
                return o;
            }

            half4 Frag(V2F i) : SV_Target
            {
                half sdf = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                half coverage = smoothstep(0.5 - _EdgeSoftness, 0.5 + _EdgeSoftness, sdf);
                return half4(coverage, coverage, coverage, coverage);
            }
            ENDHLSL
        }
    }
}
