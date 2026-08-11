// WaterStream_Refraction — Transparent refraction shader for waterfall LineRenderer.
//
// Samples _CameraOpaqueTexture at each fragment's screen-space position with a
// noise-driven UV wobble, so whatever is behind the water stream (slime body,
// walls, etc.) appears to ripple through the water. Tinted by _BaseColor so the
// WaterfallImpactVFX's streamColorProperty="_BaseColor" + MPB pipeline works
// unchanged.
//
// Only pixels covered by the LineRenderer mesh are processed — this is NOT a
// full-screen post-process.
//
// Requires URP "Opaque Texture" to be enabled in the active pipeline asset.

Shader "SlimeJumping/WaterStream_Refraction"
{
    Properties
    {
        [HDR] _BaseColor       ("Tint (RGB) + Alpha",   Color) = (0.45, 0.85, 1.0, 0.4)
        _NoiseTex              ("Noise (seamless)",     2D)    = "gray" {}
        _NoiseScale            ("Noise Tiling (xy)",    Vector) = (2, 2, 0, 0)
        _ScrollA               ("Scroll A (uv/s)",      Vector) = ( 0.05,  0.15, 0, 0)
        _ScrollB               ("Scroll B (uv/s)",      Vector) = (-0.04,  0.10, 0, 0)
        _DistortionStrength    ("Distortion Strength",  Range(0, 0.2)) = 0.03
        _TintStrength          ("Tint Strength",        Range(0, 1))   = 0.35
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

        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _NoiseTex_ST;
                float4 _NoiseScale;
                float4 _ScrollA;
                float4 _ScrollB;
                float  _DistortionStrength;
                float  _TintStrength;
            CBUFFER_END

            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.uv         = TRANSFORM_TEX(IN.uv, _NoiseTex);
                OUT.screenPos  = ComputeScreenPos(p.positionCS);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // Perspective-correct screen UV for the opaque-texture sample.
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;

                // Two scrolling noise taps → signed 2D offset.
                float2 nuvA = IN.uv * _NoiseScale.xy + _ScrollA.xy * _Time.y;
                float2 nuvB = IN.uv * _NoiseScale.xy + _ScrollB.xy * _Time.y;
                float  nA   = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, nuvA).r - 0.5;
                float  nB   = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, nuvB).r - 0.5;
                float2 off  = float2(nA, nB) * _DistortionStrength;

                float3 refracted = SAMPLE_TEXTURE2D_X(_CameraOpaqueTexture,
                                                     sampler_CameraOpaqueTexture,
                                                     screenUV + off).rgb;

                float3 rgb = lerp(refracted, _BaseColor.rgb, _TintStrength);
                return half4(rgb, _BaseColor.a);
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
    }

    FallBack Off
}
