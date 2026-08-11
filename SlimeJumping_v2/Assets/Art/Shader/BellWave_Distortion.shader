// BellWave_Distortion — a single expanding circular refraction ring for the bell sound wave.
//
// Purpose-built replacement for SpaceWarp_Distortion on the bell's wave quads: that shader
// composites the whole quad interior (tint + scene re-sample) and adds swirl/noise, which at
// screen scale reads as the camera itself moving. This one draws NOTHING outside a thin
// annulus band — pixels the ring has passed (inside) and not yet reached (outside) are left
// untouched, and the band's crisp edges make the circular outline unmistakable.
//
// Driven by SpaceWarpEffect via MPB exactly like the original: _PulsePhase ramps 0→1 over
// pulseDuration, _PulseStrength from the component. Extra properties it writes (_PopPhase,
// _Taper, ...) don't exist here and are ignored.
//
// Samples the same _SpaceWarpSourceTex global (SpaceWarpSourceFeature) — the pass must be
// tagged "SpaceWarpDraw" or URP never draws it. Author the quad with uniform XY scale.
Shader "SlimeJumping/BellWave_Distortion"
{
    Properties
    {
        [Header(Shockwave Ring driven via MPB)]
        _PulsePhase       ("Pulse Phase 0..1",           Range(0, 1))    = 0
        _PulseStrength    ("Refraction Strength",        Range(0, 0.2))  = 0.02
        _PulseRippleWidth ("Ring Band Width (uv)",       Range(0.01, 0.6)) = 0.08
        _PulseMaxRadius   ("Ring Max Radius (uv)",       Range(0.1, 1.5)) = 1.0
        _EdgeSharpness    ("Edge Sharpness (0=soft)",    Range(0.02, 0.5)) = 0.15
        _EndFadeStart     ("End Fade Start (phase)",     Range(0, 1))    = 0.85
        _BlurSize         ("Band Blur Radius (screen uv)", Range(0, 0.02)) = 0.004

        [Space(8)][Header(Draw Order)]
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Z Test", Float) = 8
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

            TEXTURE2D_X(_SpaceWarpSourceTex);
            SAMPLER(sampler_SpaceWarpSourceTex);

            CBUFFER_START(UnityPerMaterial)
                float _PulsePhase;
                float _PulseStrength;
                float _PulseRippleWidth;
                float _PulseMaxRadius;
                float _EdgeSharpness;
                float _EndFadeStart;
                float _BlurSize;
                float _ZTest;
            CBUFFER_END

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
                OUT.uv         = IN.uv;
                OUT.screenPos  = ComputeScreenPos(p.positionCS);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 centered = IN.uv * 2.0 - 1.0;
                float  dist     = length(centered);
                float2 radial   = dist > 1e-5 ? centered / dist : float2(0.0, 0.0);

                // Annulus band whose OUTER edge is the wavefront. The ring's trailing side sits
                // behind it, so passed pixels (dist < bandPos - width) are already clean.
                float bandPos = _PulsePhase * _PulseMaxRadius;
                float t = (dist - (bandPos - _PulseRippleWidth)) / max(_PulseRippleWidth, 1e-4);
                if (t <= 0.0 || t >= 1.0) discard;

                // Crisp edges on both sides — the refraction discontinuity at the outer edge is
                // what makes the outline read as a definite circle.
                float band = smoothstep(0.0, _EdgeSharpness, t) *
                             (1.0 - smoothstep(1.0 - _EdgeSharpness, 1.0, t));

                // Fade only near the very end of the travel so the ring dies at max radius
                // instead of weakening the whole way out.
                float endFade = 1.0 - smoothstep(_EndFadeStart, 1.0, _PulsePhase);

                float weight = band * endFade;
                if (weight < 0.005) discard;

                float2 offset   = -radial * (weight * _PulseStrength);
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                float2 uvBase   = screenUV + offset;

                // Small disc blur inside the band — softens the ring into frosted glass instead
                // of a hard displacement, and feathers out toward the band edges with `weight`.
                float  blurR = _BlurSize * weight;
                float3 c = SAMPLE_TEXTURE2D_X(_SpaceWarpSourceTex, sampler_SpaceWarpSourceTex, uvBase).rgb;
                const float2 taps[8] = {
                    float2( 1.0,  0.0), float2( 0.7071,  0.7071),
                    float2( 0.0,  1.0), float2(-0.7071,  0.7071),
                    float2(-1.0,  0.0), float2(-0.7071, -0.7071),
                    float2( 0.0, -1.0), float2( 0.7071, -0.7071)
                };
                [unroll]
                for (int i = 0; i < 8; i++)
                    c += SAMPLE_TEXTURE2D_X(_SpaceWarpSourceTex, sampler_SpaceWarpSourceTex,
                                            uvBase + taps[i] * blurR).rgb;
                c /= 9.0;

                return half4(c, weight);
            }
        ENDHLSL

        // Custom LightMode — SpaceWarpSourceFeature captures the camera color first, then draws
        // meshes tagged "SpaceWarpDraw" so they sample the fully-populated copy.
        Pass
        {
            Name "SpaceWarpDraw"
            Tags { "LightMode" = "SpaceWarpDraw" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
