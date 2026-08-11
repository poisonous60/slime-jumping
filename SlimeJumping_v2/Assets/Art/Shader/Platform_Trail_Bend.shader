// Platform_Trail_Bend.shader
// Minimal URP Unlit trail receiver that samples _LocalTrailMask by vertex UV0
// (rest-pose normalized in [0,1]). Survives MegaFiers bend because UVs travel
// with vertices while positions are overwritten. The mesh must have UV0 laid
// out so rest-pose XY = UV0 * localSize + localMin (see BendTestSetup).
Shader "SlimeJumping/Platform Trail Bend"
{
    Properties
    {
        [MainColor] _BaseColor ("Base Color", Color) = (0.7, 0.7, 0.7, 1)
        [MainTexture] _BaseMap ("Base Map (sampled by UV0)", 2D) = "white" {}
        [HDR] _TrailTint ("Trail Tint", Color) = (0.2, 0.1, 0.05, 1)
        _TrailStrengthMul ("Trail Strength Multiplier", Range(0, 4)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                float4 _TrailTint;
                float  _TrailStrengthMul;
            CBUFFER_END

            TEXTURE2D(_BaseMap);        SAMPLER(sampler_BaseMap);

            // Per-renderer (pushed by LocalTrailReceiver via MaterialPropertyBlock).
            // UV path only uses _LocalTrailMask; the other three are set too but
            // are unused here — kept so the same receiver component works on both
            // world-XY and UV-rest-pose variants without reconfiguration.
            TEXTURE2D(_LocalTrailMask); SAMPLER(sampler_LocalTrailMask);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0; // rest-pose UV, pre-baked
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float  fogCoord    : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogCoord = ComputeFogFactor(OUT.positionHCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                half4 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                // Trail sampling: IN.uv == rest-pose normalized to [0,1] by bake.
                // Since UVs travel with vertices through bend deformation, sampling
                // here recovers the mark painted at the original rest-pose point.
                half mask = SAMPLE_TEXTURE2D(_LocalTrailMask, sampler_LocalTrailMask, saturate(IN.uv)).r;
                mask = saturate(mask * _TrailStrengthMul);

                half3 rgb = lerp(base.rgb, _TrailTint.rgb, mask * _TrailTint.a);
                rgb = MixFog(rgb, IN.fogCoord);
                return half4(rgb, base.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
