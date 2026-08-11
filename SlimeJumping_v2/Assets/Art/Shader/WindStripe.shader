// Wind stripe particles. Alpha stays exactly as authored no matter how many stripes
// pile onto a pixel: a stencil bit is claimed by the first stripe over that pixel and
// every later stripe fails the test, so translucent stripes never stack into a denser
// blob the way plain alpha blending (or the old additive material) did.
Shader "SlimeJumping/WindStripe"
{
    Properties
    {
        _BaseColor("Tint", Color) = (1,1,1,1)
        _StencilBit("Stencil Bit", Range(1, 128)) = 16
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "WindStripe"

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            // Masked to one bit so the ice lake's stencil marks survive untouched.
            Stencil
            {
                Ref [_StencilBit]
                ReadMask [_StencilBit]
                WriteMask [_StencilBit]
                Comp NotEqual
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _StencilBit;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return IN.color * _BaseColor;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
