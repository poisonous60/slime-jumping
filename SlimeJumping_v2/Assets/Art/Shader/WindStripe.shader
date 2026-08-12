// Wind ribbon particles. Requirements:
// - Alpha stays exactly as authored no matter how many ribbons pile onto a pixel: a stencil
//   bit is claimed by the first ribbon over that pixel and every later one fails the test.
// - The core stays fully opaque so the stencil cut lands where the ribbon is already faint;
//   only the outer _EdgeSoftness fraction of the width fades.
// - Both ends taper so ribbons don't pop in at spawn or cut off at death.
// - Shape comes from UV alone, so it fits both the stretched head quad and trail geometry.
Shader "SlimeJumping/WindStripe"
{
    Properties
    {
        _BaseColor("Tint", Color) = (1,1,1,1)
        _EdgeSoftness("Edge Softness", Range(0, 1)) = 0.35
        _EndFade("End Fade", Range(0, 0.5)) = 0.15
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
                float _EdgeSoftness;
                float _EndFade;
                float _StencilBit;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float across = abs(IN.uv.y * 2.0 - 1.0);
                float edge = 1.0 - smoothstep(1.0 - _EdgeSoftness, 1.0, across);

                float fade = max(_EndFade, 1e-4);
                float ends = smoothstep(0.0, fade, IN.uv.x) * smoothstep(0.0, fade, 1.0 - IN.uv.x);

                half4 c = IN.color * _BaseColor;
                c.a *= edge * ends;
                return c;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
