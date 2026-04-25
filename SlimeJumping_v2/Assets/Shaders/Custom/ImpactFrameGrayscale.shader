Shader "Hidden/SlimeJumping/ImpactFrameGrayscale"
{
    Properties
    {
        [Header(Master)]
        _Intensity   ("Intensity (0=scene, 1=full effect)", Range(0,1)) = 0
        _WhiteFlash  ("White Flash (0=off, 1=full white)", Range(0,1)) = 0
        _ChromaActive("Chroma Aberration Active", Range(0,1)) = 0
        _ChromaShift ("Chroma Shift (UV)", Range(0,0.02)) = 0.008

        [Header(Posterize)]
        _Threshold   ("Luma Threshold (fallback silhouette)", Range(0,1)) = 0.5
        _Softness    ("Luma Softness (unused in binary mode)", Range(0,0.2)) = 0.02
        _Invert      ("Invert Luma Silhouette", Range(0,1)) = 0
        _InkThreshold("Ink Threshold (binary step cutoff)", Range(0,1)) = 0.25

        [Header(Subject Silhouette)]
        _UseDepthSilhouette ("Use Depth Silhouette (0=luma, 1=depth)", Range(0,1)) = 1
        _SubjectMaxDepth    ("Subject Max Eye Depth", Float) = 20.0
        _SubjectDepthSoftness("Subject Depth Softness", Float) = 0.5

        [Header(Accent)]
        [HDR]_AccentColor   ("Accent Color (HDR, alpha = fill strength)", Color) = (0.4, 1.0, 0.55, 0.85)
        _AccentThreshold    ("Accent Luma Threshold (unused, reserved)", Range(0,1)) = 0.7
        _AccentStrength     ("Accent Strength", Range(0,1)) = 1.0

        [Header(Edge Detection)]
        _EdgeStrength  ("Edge Strength", Range(0,4)) = 2.0
        _EdgeThreshold ("Edge Threshold", Range(0,0.5)) = 0.08

        [Header(Radial Speed Lines)]
        _RadialStrength    ("Radial Strength", Range(0,1)) = 0.9
        _RadialCenter      ("Radial Center (UV xy)", Vector) = (0.5, 0.5, 0, 0)
        _RadialLineCount   ("Line Count", Range(4,128)) = 40
        _RadialSharpness   ("Line Sharpness (0=wide, 1=thin)", Range(0,1)) = 0.5
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float _Intensity;
            float _WhiteFlash;
            float _ChromaActive;
            float _ChromaShift;

            float _Threshold;
            float _Softness;
            float _Invert;
            float _InkThreshold;

            float _UseDepthSilhouette;
            float _SubjectMaxDepth;
            float _SubjectDepthSoftness;

            float4 _AccentColor;
            float _AccentThreshold;
            float _AccentStrength;

            float _EdgeStrength;
            float _EdgeThreshold;

            float _RadialStrength;
            float4 _RadialCenter;
            float _RadialLineCount;
            float _RadialSharpness;
            float _RadialInnerRadius;
            float _RadialFadeWidth;

            half Luma(half3 c) { return dot(c, half3(0.299, 0.587, 0.114)); }

            half3 SampleColor(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
            }

            half SampleLuma(float2 uv)
            {
                return Luma(SampleColor(uv));
            }

            half SampleEyeDepth(float2 uv)
            {
                float raw = SampleSceneDepth(uv);
                return LinearEyeDepth(raw, _ZBufferParams);
            }

            // 0 = far (background), 1 = near (subject). Smooth cutoff around _SubjectMaxDepth.
            half DepthSilhouette(half eye)
            {
                return 1.0 - smoothstep(_SubjectMaxDepth - _SubjectDepthSoftness, _SubjectMaxDepth, eye);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                // -- Color sample (with optional chroma aberration on pre-flash frame)
                float2 toC = uv - _RadialCenter.xy;
                float2 chromaDir = toC / max(length(toC), 1e-4) * _ChromaShift;
                half3 baseRgb;
                UNITY_BRANCH
                if (_ChromaActive > 0.5)
                {
                    baseRgb.r = SampleColor(uv + chromaDir).r;
                    baseRgb.g = SampleColor(uv).g;
                    baseRgb.b = SampleColor(uv - chromaDir).b;
                }
                else
                {
                    baseRgb = SampleColor(uv);
                }
                half4 col = half4(baseRgb, 1.0);

                // -- Subject silhouette (depth-based or luma-fallback)
                half eye = SampleEyeDepth(uv);
                half depthSil = DepthSilhouette(eye);

                half luma = Luma(col.rgb);
                half lumaSil = step(_Threshold, luma);
                lumaSil = lerp(lumaSil, 1.0 - lumaSil, _Invert);

                half silhouette = lerp(lumaSil, depthSil, saturate(_UseDepthSilhouette));

                // -- 8-tap Sobel, source toggled by _UseDepthSilhouette
                float2 ts = _BlitTexture_TexelSize.xy;
                half s00, s01, s02, s10, s12, s20, s21, s22;
                UNITY_BRANCH
                if (_UseDepthSilhouette > 0.5)
                {
                    half invMax = 1.0 / max(_SubjectMaxDepth, 1e-3);
                    s00 = saturate(SampleEyeDepth(uv + ts * float2(-1,-1)) * invMax);
                    s01 = saturate(SampleEyeDepth(uv + ts * float2(-1, 0)) * invMax);
                    s02 = saturate(SampleEyeDepth(uv + ts * float2(-1, 1)) * invMax);
                    s10 = saturate(SampleEyeDepth(uv + ts * float2( 0,-1)) * invMax);
                    s12 = saturate(SampleEyeDepth(uv + ts * float2( 0, 1)) * invMax);
                    s20 = saturate(SampleEyeDepth(uv + ts * float2( 1,-1)) * invMax);
                    s21 = saturate(SampleEyeDepth(uv + ts * float2( 1, 0)) * invMax);
                    s22 = saturate(SampleEyeDepth(uv + ts * float2( 1, 1)) * invMax);
                }
                else
                {
                    s00 = SampleLuma(uv + ts * float2(-1,-1));
                    s01 = SampleLuma(uv + ts * float2(-1, 0));
                    s02 = SampleLuma(uv + ts * float2(-1, 1));
                    s10 = SampleLuma(uv + ts * float2( 0,-1));
                    s12 = SampleLuma(uv + ts * float2( 0, 1));
                    s20 = SampleLuma(uv + ts * float2( 1,-1));
                    s21 = SampleLuma(uv + ts * float2( 1, 0));
                    s22 = SampleLuma(uv + ts * float2( 1, 1));
                }
                half gx = (s02 + 2.0 * s12 + s22) - (s00 + 2.0 * s10 + s20);
                half gy = (s20 + 2.0 * s21 + s22) - (s00 + 2.0 * s01 + s02);
                half edgeMag = sqrt(gx * gx + gy * gy) * _EdgeStrength;
                half edge = step(_EdgeThreshold, edgeMag);

                // -- Radial speed lines (aspect-corrected, from _RadialCenter)
                float2 radialToC = uv - _RadialCenter.xy;
                radialToC.x *= _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float angle = atan2(radialToC.y, radialToC.x);
                float dist = length(radialToC);
                half wave = abs(sin(angle * _RadialLineCount));
                half widthCutoff = lerp(0.9, 0.05, _RadialSharpness);
                half lineAmt = 1.0 - smoothstep(0.0, widthCutoff, wave);
                half distFade = smoothstep(_RadialInnerRadius, _RadialInnerRadius + _RadialFadeWidth, dist);
                half radial = lineAmt * distFade * _RadialStrength;

                // -- Binary ink composition (no gray mid-tones)
                half ink = saturate(silhouette + edge + radial);
                half inkBin = step(_InkThreshold, ink);
                half3 manga = (1.0 - inkBin).xxx;

                // -- Accent interior: tint subject silhouette where it is not under black ink
                half interiorMask = silhouette * _AccentColor.a * _AccentStrength * (1.0 - step(_InkThreshold, edge));
                manga = lerp(manga, _AccentColor.rgb, saturate(interiorMask));

                // -- Accent rim: thin tint along detected edges
                manga = lerp(manga, _AccentColor.rgb, saturate(edge * _AccentStrength * 0.6));

                // -- White flash override (pre-flash frame)
                manga = lerp(manga, half3(1.0, 1.0, 1.0), saturate(_WhiteFlash));

                half3 outRgb = lerp(col.rgb, manga, saturate(_Intensity));
                return half4(outRgb, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
