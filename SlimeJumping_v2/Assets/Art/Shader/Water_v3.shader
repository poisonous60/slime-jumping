// Water_v3 — WaterZone_v2 volume shader with caustics overlay.
//
// Fork of Water_v2.shader adapted for Unity 6 Forward+ (adds UniversalForward pass)
// plus a scrolling caustics layer sampled from a grayscale sheet texture.
//
// Mesh data coming in:
//   - WaterZone_v2.Mesh.cs builds a fan mesh (center + perimeter ring)
//   - Vertex colors supply the center→edge gradient:
//       center vertex: rgb=centerColor, a=0
//       edge vertices: rgb=edgeColor,   a=1
//   - Triangle interpolation gives a smooth radial gradient "for free"
//
// The original Water_v2.shader is kept untouched for rollback / 2D-Renderer fallback.

Shader "SlimeJumping/Water_v3"
{
    Properties
    {
        _NoiseTex           ("Noise (seamless)",    2D) = "gray" {}
        _NoiseScale         ("Noise Tiling (xy)",   Vector) = (2, 2, 0, 0)
        _ScrollA            ("Scroll A (uv/s)",     Vector) = ( 0.05,  0.03, 0, 0)
        _ScrollB            ("Scroll B (uv/s)",     Vector) = (-0.04,  0.06, 0, 0)
        _DistortionStrength ("Distortion Strength", Range(0, 0.5)) = 0.08
        _SparkleThreshold   ("Sparkle Threshold",   Range(0, 1))   = 0.85
        _SparkleColor       ("Sparkle Color",       Color) = (1, 1, 1, 1)
        _RimColor           ("Rim Color",           Color) = (0.6, 1, 1, 1)
        _RimPower           ("Rim Power",           Range(0.1, 8)) = 2
        _Alpha              ("Overall Alpha",       Range(0, 1))   = 0.8

        _CausticsTex        ("Caustics Sheet",      2D) = "black" {}
        _CausticsScale      ("Caustics Tiling (xy)",    Vector) = (1.5, 1.5, 0, 0)
        _CausticsScroll     ("Caustics Scroll (uv/s)",  Vector) = (0.03, 0.02, 0, 0)
        _CausticsTint       ("Caustics Tint",           Color)  = (0.7, 1.0, 1.0, 1)
        _CausticsIntensity  ("Caustics Intensity",      Range(0, 3)) = 0.6
        _CausticsCenterBias ("Caustics Center Bias",    Range(0, 1)) = 0.7
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent"
            "RenderType"      = "Transparent"
            "IgnoreProjector" = "True"
        }

        // Shared HLSL — vertex/fragment/CBUFFER/samplers reused across both passes.
        // ShaderLab directives (Blend/ZWrite/Cull/Tags) MUST stay in each Pass block.
        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseTex_ST;
                float4 _NoiseScale;
                float4 _ScrollA;
                float4 _ScrollB;
                float  _DistortionStrength;
                float  _SparkleThreshold;
                float4 _SparkleColor;
                float4 _RimColor;
                float  _RimPower;
                float  _Alpha;

                float4 _CausticsTex_ST;
                float4 _CausticsScale;
                float4 _CausticsScroll;
                float4 _CausticsTint;
                float  _CausticsIntensity;
                float  _CausticsCenterBias;
            CBUFFER_END

            TEXTURE2D(_NoiseTex);     SAMPLER(sampler_NoiseTex);
            TEXTURE2D(_CausticsTex);  SAMPLER(sampler_CausticsTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                float3 positionWS : TEXCOORD1;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.uv         = IN.uv;
                OUT.color      = IN.color;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 wsUV = IN.positionWS.xy * _NoiseScale.xy;
                float2 uvA  = wsUV + _ScrollA.xy * _Time.y;
                float2 uvB  = wsUV + _ScrollB.xy * _Time.y;

                float nA = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uvA).r;
                float nB = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uvB).r;

                float noise  = (nA - 0.5) + (nB - 0.5);
                float bright = nA * nB;

                half3 baseCol   = IN.color.rgb;
                half3 distorted = baseCol + noise * _DistortionStrength;

                float sparkle    = step(_SparkleThreshold, bright);
                half3 sparkleCol = _SparkleColor.rgb * sparkle;

                float rim    = pow(saturate(IN.color.a), _RimPower);
                half3 rimCol = _RimColor.rgb * rim;

                half3 finalRGB = distorted + sparkleCol + rimCol;

                // Caustics overlay — sheet scrolls independently over worldspace XY.
                // Center bias: stronger at α=0 (center), fades toward α=1 (rim-lit edge).
                float2 cUV = IN.positionWS.xy * _CausticsScale.xy + _CausticsScroll.xy * _Time.y;
                float  caustic = SAMPLE_TEXTURE2D(_CausticsTex, sampler_CausticsTex, cUV).r;
                float  centerWeight = lerp(1.0, 1.0 - _CausticsCenterBias, IN.color.a);
                finalRGB += _CausticsTint.rgb * caustic * _CausticsIntensity * centerWeight;

                half finalA = saturate(IN.color.a * 0.3 + 0.5) * _Alpha;
                return half4(finalRGB, finalA);
            }
        ENDHLSL

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "Universal2D"
            Tags { "LightMode" = "Universal2D" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }

    FallBack Off
}
