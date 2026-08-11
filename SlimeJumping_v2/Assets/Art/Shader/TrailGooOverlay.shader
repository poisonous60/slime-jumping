// TrailGooOverlay.shader
// Goo-only display for a LocalTrailReceiver: samples the per-renderer trail RT exactly like
// Ground_TrailLocalReceiverInject.hlsl (same MPB properties, same globals, same composition)
// but outputs ONLY the goo — transparent everywhere the mask is empty. Lives on its own
// renderer so trail display is fully decoupled from whatever shader the platform body uses
// (bell-reveal platforms: body fades with the sound wave, goo on this overlay stays visible).
// Point the LocalTrailReceiver's targetRenderer at THIS renderer.
Shader "SlimeJumping/TrailGooOverlay"
{
    Properties
    {
        // Per-renderer via MaterialPropertyBlock (LocalTrailReceiver binds these).
        [HideInInspector][PerRendererData] _LocalTrailMask ("Local Trail Mask", 2D) = "black" {}
        [HideInInspector][PerRendererData] _LocalTrailMaskCold ("Local Trail Mask Cold", 2D) = "black" {}
        [HideInInspector][PerRendererData] _LocalTrailMin  ("Local Trail Min",  Vector) = (0,0,0,0)
        [HideInInspector][PerRendererData] _LocalTrailSize ("Local Trail Size", Vector) = (1,1,0,0)
        // _TrailGradient / _TrailMaxMask etc. are globals from WorldTrailManager.
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent" "Queue" = "Transparent+10"
            "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "GooOverlay"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Blend SrcAlpha OneMinusSrcAlpha
            // Two-sided so goo reads from below too, but with depth write: on a closed mesh
            // Cull Off alone blends front AND back faces at the same pixel (the mask UV is
            // world-XY, so both faces carry identical goo) — double-applied alpha. Depth keeps
            // exactly one goo layer per pixel from any viewing side.
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Per-renderer (MPB, bound by LocalTrailReceiver)
            TEXTURE2D(_LocalTrailMask);     SAMPLER(sampler_LocalTrailMask);
            TEXTURE2D(_LocalTrailMaskCold); SAMPLER(sampler_LocalTrailMaskCold);
            float4   _LocalTrailMin;
            float4   _LocalTrailSize;
            // Receiver's world→local matrix — the paint frame. Without the MPB this is a zero
            // matrix and every fragment would sample one texel, so Frag guards on _LocalTrailSize.
            float4x4 _LocalTrailW2L;

            // Globals (shared with the ground trail shaders, pushed by WorldTrailManager)
            TEXTURE2D(_TrailGradient);      SAMPLER(sampler_TrailGradient);
            TEXTURE2D(_TrailGradientCold);  SAMPLER(sampler_TrailGradientCold);
            TEXTURE2D(_TrailPosGradient);   SAMPLER(sampler_TrailPosGradient);
            TEXTURE2D(_TrailAngleGradient); SAMPLER(sampler_TrailAngleGradient);
            float4 _TrailPosGradientParams;
            float  _TrailPosGradientEnabled;
            float  _TrailAngleGradientEnabled;
            float  _TrailMaxMask;
            float  _TrailZCenter;
            float  _TrailZNear;
            float  _TrailZFar;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            // Same (B)/(C) gradient modulation as Ground_TrailLocalReceiverInject.hlsl.
            void ApplySharedGradients(float3 positionWS, float3 normalWS,
                                      inout half3 rgb, inout half alpha)
            {
                if (_TrailPosGradientEnabled > 0.5)
                {
                    float angleRad = _TrailPosGradientParams.w * (3.14159265 / 180.0);
                    float2 dir     = float2(sin(angleRad), cos(angleRad));
                    float2 rel     = positionWS.xy - _TrailPosGradientParams.xy;
                    float  t       = saturate(dot(rel, dir) / _TrailPosGradientParams.z + 0.5);
                    half4  posGrad = SAMPLE_TEXTURE2D(_TrailPosGradient, sampler_TrailPosGradient, float2(t, 0.5));
                    rgb   *= posGrad.rgb;
                    alpha *= posGrad.a;
                }
                if (_TrailAngleGradientEnabled > 0.5)
                {
                    float3 n     = normalize(normalWS);
                    float  tAng  = saturate((1.0 - n.y) * 0.5);
                    half4  aGrad = SAMPLE_TEXTURE2D(_TrailAngleGradient, sampler_TrailAngleGradient, float2(tAng, 0.5));
                    rgb   *= aGrad.rgb;
                    alpha *= aGrad.a;
                }
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // MPB-not-bound guards — fail closed, never flood. An unbound float4x4 is the
                // ZERO matrix (m33 = 0; a real worldToLocal always has m33 = 1), and an unbound
                // or zero size means garbage UVs. Draw nothing instead of a uniform tint.
                if (abs(_LocalTrailW2L._m33) < 0.5) discard;
                if (_LocalTrailSize.x < 1e-4 || _LocalTrailSize.y < 1e-4) discard;

                // Z-plane fade — same semantics as the ground shaders.
                float zDist = abs(IN.positionWS.z - _TrailZCenter);
                float zFade = 1.0 - saturate((zDist - _TrailZNear) / max(_TrailZFar - _TrailZNear, 0.0001));
                if (zFade < 0.0001) discard;

                float3 posLS = mul(_LocalTrailW2L, float4(IN.positionWS, 1.0)).xyz;
                float2 uv    = (posLS.xy - _LocalTrailMin.xy) / _LocalTrailSize.xy;
                // Outside the paint rect there is no goo — do NOT clamp-sample the edge texel.
                if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) discard;

                // Warm layer
                half maskRaw    = SAMPLE_TEXTURE2D(_LocalTrailMask, sampler_LocalTrailMask, uv).r;
                half maskCapped = min(maskRaw, _TrailMaxMask) * zFade;
                half3 warmRgb = 0;
                half  warmA   = 0;
                if (maskCapped >= 0.0001)
                {
                    half4 grad = SAMPLE_TEXTURE2D(_TrailGradient, sampler_TrailGradient, float2(maskRaw, 0.5));
                    warmRgb = grad.rgb;
                    half a  = grad.a;
                    ApplySharedGradients(IN.positionWS, IN.normalWS, warmRgb, a);
                    warmA = maskCapped * a;
                }

                // Cold layer — alpha-over over warm, same routing as the ground shaders.
                half maskRawCold    = SAMPLE_TEXTURE2D(_LocalTrailMaskCold, sampler_LocalTrailMaskCold, uv).r;
                half maskCappedCold = min(maskRawCold, _TrailMaxMask) * zFade;
                half3 coldRgb = 0;
                half  coldA   = 0;
                if (maskCappedCold >= 0.0001)
                {
                    half4 grad = SAMPLE_TEXTURE2D(_TrailGradientCold, sampler_TrailGradientCold, float2(maskRawCold, 0.5));
                    coldRgb = grad.rgb;
                    half a  = grad.a;
                    ApplySharedGradients(IN.positionWS, IN.normalWS, coldRgb, a);
                    coldA = maskCappedCold * a;
                }

                half outA = warmA + coldA * (1.0 - warmA);
                if (outA < 0.001) discard;
                half3 outRgb = (coldRgb * coldA + warmRgb * warmA * (1.0 - coldA)) / max(outA, 1e-4);
                return half4(outRgb, outA);
            }
            ENDHLSL
        }
    }
}
