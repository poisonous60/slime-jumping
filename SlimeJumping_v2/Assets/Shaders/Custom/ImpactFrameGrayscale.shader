Shader "Hidden/SlimeJumping/ImpactFrameGrayscale"
{
    Properties
    {
        [Header(Master)]
        _Intensity ("Intensity (0=scene, 1=full effect)", Range(0,1)) = 0

        [Header(Posterize)]
        _Threshold ("Luma Threshold", Range(0,1)) = 0.5
        _Softness  ("Threshold Softness", Range(0,0.2)) = 0.02
        _Invert    ("Invert", Range(0,1)) = 0

        [Header(Edge Detection)]
        _EdgeStrength  ("Edge Strength", Range(0,4)) = 2.0
        _EdgeThreshold ("Edge Threshold", Range(0,0.5)) = 0.08

        [Header(Radial Speed Lines)]
        _RadialStrength    ("Radial Strength", Range(0,1)) = 0.9
        _RadialCenter      ("Radial Center (UV xy)", Vector) = (0.5, 0.5, 0, 0)
        _RadialLineCount   ("Line Count", Range(4,128)) = 40
        _RadialSharpness   ("Line Sharpness", Range(0,1)) = 0.5
        _RadialInnerRadius ("Inner Fade Radius (UV)", Range(0,0.6)) = 0.1
        _RadialFadeWidth   ("Inner Fade Width (UV)", Range(0.001,0.6)) = 0.15
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "ImpactFrameGrayscale"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float _Threshold;
            float _Softness;
            float _Invert;
            float _EdgeStrength;
            float _EdgeThreshold;
            float _RadialStrength;
            float4 _RadialCenter;
            float _RadialLineCount;
            float _RadialSharpness;
            float _RadialInnerRadius;
            float _RadialFadeWidth;

            half Luma(half3 c) { return dot(c, half3(0.299, 0.587, 0.114)); }

            half SampleLuma(float2 uv)
            {
                return Luma(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                half luma = Luma(col.rgb);

                // Silhouette mask: by default (invert=0) bright scene regions become
                // black ink on white paper — slime silhouettes out of a dark background.
                // Flip with _Invert=1 when the subject is darker than the background.
                half soft = max(_Softness, 1e-4);
                half silhouette = smoothstep(_Threshold - soft, _Threshold + soft, luma);
                silhouette = lerp(silhouette, 1.0 - silhouette, _Invert);

                // Sobel edge detection on luminance. Gives bold black outlines around
                // silhouettes/colors — the "manga ink" component.
                float2 ts = _BlitTexture_TexelSize.xy;
                half c00 = SampleLuma(uv + ts * float2(-1,-1));
                half c01 = SampleLuma(uv + ts * float2(-1, 0));
                half c02 = SampleLuma(uv + ts * float2(-1, 1));
                half c10 = SampleLuma(uv + ts * float2( 0,-1));
                half c12 = SampleLuma(uv + ts * float2( 0, 1));
                half c20 = SampleLuma(uv + ts * float2( 1,-1));
                half c21 = SampleLuma(uv + ts * float2( 1, 0));
                half c22 = SampleLuma(uv + ts * float2( 1, 1));
                half gx = (c02 + 2.0 * c12 + c22) - (c00 + 2.0 * c10 + c20);
                half gy = (c20 + 2.0 * c21 + c22) - (c00 + 2.0 * c01 + c02);
                half edgeMag = sqrt(gx * gx + gy * gy) * _EdgeStrength;
                half edge = smoothstep(_EdgeThreshold, _EdgeThreshold + 0.05, edgeMag);

                // Radial speed lines emanating from _RadialCenter. Aspect-correct so they
                // look like evenly distributed rays, not ellipses.
                float2 toC = uv - _RadialCenter.xy;
                toC.x *= _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float angle = atan2(toC.y, toC.x);
                float dist = length(toC);

                // abs(sin(angle * N)) dips to 0 at rays and peaks at 1 between them.
                // Map _RadialSharpness so 0=wide sunbeams, 1=needle-thin spikes.
                half wave = abs(sin(angle * _RadialLineCount));
                half widthCutoff = lerp(0.9, 0.05, _RadialSharpness);
                half lineAmt = 1.0 - smoothstep(0.0, widthCutoff, wave);

                // Fade rays out near the center so the impact point is clear.
                half distFade = smoothstep(_RadialInnerRadius, _RadialInnerRadius + _RadialFadeWidth, dist);
                half radial = lineAmt * distFade * _RadialStrength;

                // Manga composition: white paper + accumulated black ink from three sources
                // (silhouette, edges, radial rays). Additive (not multiplicative) so each
                // source shows up independently even when others are zero — the old mul
                // composition let a dark background suppress edge/radial visibility.
                half ink = saturate(silhouette + edge + radial);
                half3 manga = (1.0 - ink).xxx;

                half3 outRgb = lerp(col.rgb, manga, saturate(_Intensity));
                return half4(outRgb, col.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
