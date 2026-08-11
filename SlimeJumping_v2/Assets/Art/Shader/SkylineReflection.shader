Shader "SlimeJumping/Skyline Reflection"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color (alpha = strength)", Color) = (0.275, 0.290, 0.522, 0.55)
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        _FadeStartY("Fade Start Height (full strength)", Float) = 132.6
        _FadeEndY("Fade End Height (invisible)", Float) = 124.0
        _WaterY("Water Surface Height", Float) = 132.6
        _StencilRef("Stencil Ref", Range(0, 255)) = 40
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            // Ice Lake stamps its visible pixels with the same ref. Equal keeps the
            // reflection inside the ice surface, IncrSat bumps the value so overlapping
            // skyline rings can't blend the same pixel twice.
            Stencil
            {
                Ref [_StencilRef]
                Comp Equal
                Pass IncrSat
            }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _Cutoff;
                float _FadeStartY;
                float _FadeEndY;
                float _WaterY;
                float _StencilRef;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float heightWS : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.heightWS = TransformObjectToWorld(IN.positionOS.xyz).y;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Texture supplies the silhouette only; flat color keeps every building
                // at one tone so the union reads as a single reflection.
                // Reflection lives on top of the ice, so it is gone once the eye drops below it.
                clip(_WorldSpaceCameraPos.y - _WaterY);
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a - _Cutoff);
                float fade = saturate((IN.heightWS - _FadeEndY) / max(0.001, _FadeStartY - _FadeEndY));
                return half4(_BaseColor.rgb, _BaseColor.a * fade);
            }
            ENDHLSL
        }
    }
}
