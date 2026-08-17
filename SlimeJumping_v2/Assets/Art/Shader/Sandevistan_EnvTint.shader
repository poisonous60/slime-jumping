// Sandevistan_EnvTint — full-screen colour grade for the implant effect.
//
// Samples the custom global _SpaceWarpSourceTex (a per-camera colour copy written by
// SpaceWarpSourceFeature), desaturates it and pushes it toward a tint, then writes the
// result back over the screen. _CameraOpaqueTexture is unusable here for the same reason
// as Rain_Refraction: the project runs a Base+Overlay stack, so the Base copy holds only
// the background layer and an Overlay-rendered quad would sample near-black.
//
// URP draws only the FIRST pass of a shader, so grading and compositing share one pass.
//
// Author on a quad parented to the camera and scaled to the frustum; SandevistanManager
// does that every LateUpdate. _Effect 0 is a true no-op (output == source), which is what
// lets the manager disable the look without disabling the renderer mid-frame.
//
// Queue MUST stay below the other SpaceWarpDraw shaders (they sit at Transparent/3000).
// They all share one pass sorted by queue then back-to-front, and this quad hugs the near
// plane, so at an equal queue it would draw LAST and its opaque output would erase the rain
// and bell distortion already composited into the target. Drawing first instead means those
// meshes still sample the ungraded snapshot — their refracted pixels stay ungraded, which is
// a small mismatch rather than a disappearing effect.

Shader "SlimeJumping/Sandevistan_EnvTint"
{
    Properties
    {
        [HDR] _TintColor    ("Tint",                Color)        = (0.35, 0.75, 1.0, 1.0)
        _TintStrength       ("Tint Strength",       Range(0, 1))  = 0.45
        _Desaturation       ("Desaturation",        Range(0, 1))  = 0.6
        _Contrast           ("Contrast",            Range(0.5, 2))= 1.15
        _Effect             ("Effect Amount (MPB)", Range(0, 1))  = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent-400"
            "RenderType"      = "Transparent"
            "IgnoreProjector" = "True"
        }

        // Custom LightMode keeps URP's default DrawObjectsPass from rendering this material.
        Pass
        {
            Name "SandevistanEnvTint"
            Tags { "LightMode" = "SpaceWarpDraw" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X(_SpaceWarpSourceTex);
            SAMPLER(sampler_SpaceWarpSourceTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _TintColor;
                float  _TintStrength;
                float  _Desaturation;
                float  _Contrast;
                float  _Effect;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos  : TEXCOORD0;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.screenPos  = ComputeScreenPos(p.positionCS);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                float3 src = SAMPLE_TEXTURE2D_X(_SpaceWarpSourceTex,
                                                sampler_SpaceWarpSourceTex, screenUV).rgb;

                float  luma  = dot(src, float3(0.299, 0.587, 0.114));
                float3 graded = lerp(src, luma.xxx, _Desaturation);
                graded = lerp(graded, graded * _TintColor.rgb, _TintStrength);
                graded = saturate((graded - 0.5) * _Contrast + 0.5);

                return half4(lerp(src, graded, saturate(_Effect)), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
