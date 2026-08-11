// Water_FlowingRefraction — directional-flow variant of WaterStream_Refraction.
//
// Fork of WaterStream_Refraction.shader for the WaterCurrentZone mesh. Same
// translucent / refraction-through-wall feel as WaterfallImpactVFX uses on
// its LineRenderer stream — this is the "calm, you-can-see-through-it" look
// the WaterCurrentZone wants instead of the saturated rim glow of
// Water_FlowingV2.
//
// Differences from WaterStream_Refraction:
//   - Adds `_FlowSpeed` (uv-V scroll). The mesh built by WaterCurrentZone
//     puts UV V-axis along flowDirection, so this term shifts the noise
//     downstream over time, giving "water keeps coming down" motion.
//   - Otherwise identical: _CameraOpaqueTexture sample with noise-driven
//     screen-UV wobble, lerp toward _BaseColor by _TintStrength.
//
// Requires URP Opaque Texture enabled in the active pipeline asset (already
// the case in this project).

Shader "SlimeJumping/Water_FlowingRefraction"
{
    Properties
    {
        [HDR] _BaseColor       ("Tint (RGB) + Alpha",   Color) = (0.45, 0.85, 1.0, 0.4)
        _NoiseTex              ("Noise (seamless)",     2D)    = "gray" {}
        _NoiseScale            ("Noise Tiling (xy)",    Vector) = (2, 2, 0, 0)
        _ScrollA               ("Scroll A (uv/s)",      Vector) = ( 0.05,  0.03, 0, 0)
        _ScrollB               ("Scroll B (uv/s)",      Vector) = (-0.04,  0.06, 0, 0)
        _FlowSpeed             ("Flow Speed (uv/s, V)", Float)  = 1.5
        _DistortionStrength    ("Distortion Strength",  Range(0, 0.2)) = 0.03
        _TintStrength          ("Tint Strength",        Range(0, 1))   = 0.18
        _EdgeFadePower         ("Edge Fade Power",      Range(0.1, 8)) = 1.5
        _EdgeNoise             ("Edge Noise Wobble",    Range(0, 1))   = 0.4
        _Turbulence            ("Turbulence (UV warp)", Range(0, 1.5)) = 0.4
        _LayerSpeedRatio       ("Layer B speed / A",    Range(0.1, 4)) = 1.7
        // Vertex-stage crests. Keep _CrestAmplitude < tubeDepth on the WaterCurrentZone
        // (default 0.2) so peaks never push past the GameObject's Z plane and clip into the
        // wall behind. Crests are masked by vertex color alpha so silhouette edges stay flat.
        _CrestAmplitude        ("Crest Amplitude (Z disp)",     Range(0, 0.4)) = 0.08
        _CrestSpeed            ("Crest Scroll Speed",           Range(0, 4))   = 1.2
        [IntRange] _CrestBands ("Crest Bands (0=smooth)",       Range(0, 12))  = 0
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
                float  _FlowSpeed;
                float  _DistortionStrength;
                float  _TintStrength;
                float  _EdgeFadePower;
                float  _EdgeNoise;
                float  _Turbulence;
                float  _LayerSpeedRatio;
                float  _CrestAmplitude;
                float  _CrestSpeed;
                float  _CrestBands;
            CBUFFER_END

            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

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
                float4 screenPos  : TEXCOORD1;
                float  edgeT      : TEXCOORD2;   // vertex.color.a, passed through (0=centroid, 1=outline)
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;

                // Vertex Z-displacement crests. Sample noise at the vertex's UV (with a slight
                // re-tile for crest scale) and offset positionOS.z toward the camera. Masked by
                // vertex color alpha so silhouette edges (alpha=1) stay anchored at z=0 — only
                // the inner cross-section rows (alpha < 1) actually rise.
                float2 crestUV = IN.uv * _NoiseScale.xy * 0.7
                               + float2(0, -_CrestSpeed * _Time.y);
                float crestN = SAMPLE_TEXTURE2D_LOD(_NoiseTex, sampler_NoiseTex, crestUV, 0).r - 0.5;

                // Optional banding for the BotW-style stylized look (round to N levels).
                if (_CrestBands > 0.5)
                {
                    crestN = round(crestN * _CrestBands) / _CrestBands;
                }

                float centerWeight = saturate(1.0 - IN.color.a);
                float3 posOS = IN.positionOS.xyz;
                posOS.z -= crestN * _CrestAmplitude * centerWeight;  // -Z = toward camera

                VertexPositionInputs p = GetVertexPositionInputs(posOS);
                OUT.positionCS = p.positionCS;
                OUT.uv         = TRANSFORM_TEX(IN.uv, _NoiseTex);
                OUT.screenPos  = ComputeScreenPos(p.positionCS);
                OUT.edgeT      = IN.color.a;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // Perspective-correct screen UV for the opaque-texture sample.
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;

                // V-axis flow scroll. Negative sign so positive _FlowSpeed
                // moves the noise pattern toward larger V (downstream) on
                // the mesh — same convention as Water_FlowingV2.
                float2 baseUV   = IN.uv * _NoiseScale.xy;
                float2 flowOffA = float2(0, -_FlowSpeed * _Time.y);
                float2 flowOffB = flowOffA * _LayerSpeedRatio;

                // Domain warp turbulence — sample a slower, larger-scale
                // noise field and use it to displace the main flow UVs.
                // Without this, every fragment scrolls down at exactly the
                // same rate and the result looks like a uniform sheet
                // sliding rigidly. The warp introduces eddy-like swirls
                // where some regions stretch and others compress as the
                // pattern flows.
                float2 turbUV = IN.uv * _NoiseScale.xy * 0.4
                              + float2(0, -_FlowSpeed * _Time.y * 0.3);
                float  tA = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, turbUV).r - 0.5;
                float  tB = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, turbUV + 0.37).r - 0.5;
                float2 turbOff = float2(tA, tB) * _Turbulence;

                // Two main noise taps — same base UV warped by turbulence,
                // but each layer scrolls at a different speed (LayerSpeedRatio).
                // Different speeds = the two layers shear past each other,
                // breaking the synchronized "rigid slide" appearance.
                float2 nuvA = baseUV + turbOff + _ScrollA.xy * _Time.y + flowOffA;
                float2 nuvB = baseUV + turbOff + _ScrollB.xy * _Time.y + flowOffB;
                float  nA   = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, nuvA).r - 0.5;
                float  nB   = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, nuvB).r - 0.5;
                float2 off  = float2(nA, nB) * _DistortionStrength;

                float3 refracted = SAMPLE_TEXTURE2D_X(_CameraOpaqueTexture,
                                                     sampler_CameraOpaqueTexture,
                                                     screenUV + off).rgb;

                float3 rgb = lerp(refracted, _BaseColor.rgb, _TintStrength);

                // Edge alpha fade — kills the clean polygon outline that
                // makes the water look like a sticker. Vertex color alpha is
                // 0 at centroid and 1 at outline (mesh build encoding), so
                // (1 - edgeT)^power yields 1 in the middle and 0 at perimeter.
                // Higher _EdgeFadePower → narrower visible band.
                float edgeMask = pow(saturate(1.0 - IN.edgeT), _EdgeFadePower);

                // Edge noise wobble — perturbs the alpha cutoff with the
                // same flow noise so the silhouette is irregular instead of
                // a clean rectangle. nA + nB is in [-1, 1]; we weight by
                // edgeT so the wobble only acts near the perimeter (where
                // edgeMask is small) and leaves the center solid.
                float wobble = (nA + nB) * _EdgeNoise * IN.edgeT;
                float alphaMask = saturate(edgeMask + wobble);

                return half4(rgb, _BaseColor.a * alphaMask);
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
