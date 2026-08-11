// Waterfall_Background — stylized 2D waterfall sheet for the blurred background
// layer. Two vertically-scrolling noise layers drive a 2-tone gradient; soft
// horizontal/vertical masks fade the edges so the quad blends into the painted
// scene. Intended to sit on the BackgroundBlur layer (culled by BackgroundCamera,
// then blurred by TranslucentImageBlurSource), so features are kept chunky
// enough to survive the blur pass.

Shader "SlimeJumping/Waterfall_Background"
{
    Properties
    {
        [Header(Texture)]
        _NoiseTex        ("Noise (seamless vert-streak)", 2D) = "gray" {}
        _Tiling          ("Tiling (x, y)",                 Vector) = (1, 2, 0, 0)

        [Header(Flow)]
        _ScrollA         ("Scroll A (uv/s, negative Y = down)", Vector) = ( 0.02, -0.55, 0, 0)
        _ScrollB         ("Scroll B (uv/s)",                    Vector) = (-0.015, -0.85, 0, 0)
        _LayerBTiling    ("Layer B extra tiling (x, y)",        Vector) = (1.3, 1.6, 0, 0)

        [Header(Color)]
        _ShadowColor     ("Shadow Color",    Color) = (0.20, 0.28, 0.42, 1)
        _MidColor        ("Mid Color",       Color) = (0.45, 0.58, 0.78, 1)
        _HighlightColor  ("Highlight Color", Color) = (0.82, 0.90, 0.98, 1)
        _HighlightThreshold ("Highlight Threshold", Range(0, 1)) = 0.78
        _HighlightSharpness ("Highlight Sharpness", Range(0, 1)) = 0.10

        [Header(Mask)]
        _EdgeFadeX       ("Horizontal Edge Fade", Range(0, 0.5)) = 0.18
        _EdgeFadeTop     ("Top Fade",             Range(0, 0.5)) = 0.12
        _EdgeFadeBottom  ("Bottom Fade",          Range(0, 0.5)) = 0.22
        _Alpha           ("Overall Alpha",        Range(0, 1))   = 0.90
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

            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseTex_ST;
                float4 _Tiling;
                float4 _ScrollA;
                float4 _ScrollB;
                float4 _LayerBTiling;

                float4 _ShadowColor;
                float4 _MidColor;
                float4 _HighlightColor;
                float  _HighlightThreshold;
                float  _HighlightSharpness;

                float  _EdgeFadeX;
                float  _EdgeFadeTop;
                float  _EdgeFadeBottom;
                float  _Alpha;
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
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.uv         = IN.uv;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // Two scrolling samples in UV space (not world — the waterfall
                // needs to scroll with the quad even if it's parented/moved).
                float2 baseUV = IN.uv * _Tiling.xy;
                float2 uvA = baseUV + _ScrollA.xy * _Time.y;
                float2 uvB = baseUV * _LayerBTiling.xy + _ScrollB.xy * _Time.y;

                float nA = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uvA).r;
                float nB = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uvB).r;

                // Combine: average gives body tone, product gives hot spots.
                float body     = (nA + nB) * 0.5;
                float hotspot  = nA * nB;

                // Two-tone gradient: shadow → mid based on body.
                half3 col = lerp(_ShadowColor.rgb, _MidColor.rgb, saturate(body));

                // Highlight pop — smooth threshold on the hotspot product so
                // bright streaks survive the blur as distinct bands.
                float hi = smoothstep(
                    _HighlightThreshold,
                    _HighlightThreshold + _HighlightSharpness + 1e-4,
                    hotspot
                );
                col = lerp(col, _HighlightColor.rgb, hi);

                // Edge masking — soft horizontal taper + top/bottom fades so the
                // quad reads as a sheet rather than a rectangle.
                float u = IN.uv.x;
                float v = IN.uv.y;
                float maskX   = smoothstep(0.0, max(_EdgeFadeX, 1e-4), u) *
                                smoothstep(0.0, max(_EdgeFadeX, 1e-4), 1.0 - u);
                float maskTop = smoothstep(0.0, max(_EdgeFadeTop, 1e-4), 1.0 - v);
                float maskBot = smoothstep(0.0, max(_EdgeFadeBottom, 1e-4), v);
                float mask = maskX * maskTop * maskBot;

                half alpha = saturate(mask * _Alpha);
                return half4(col, alpha);
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
