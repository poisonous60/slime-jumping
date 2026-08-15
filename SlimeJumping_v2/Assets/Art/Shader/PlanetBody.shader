// 배경 행성의 고체 본체. 평범한 구 메시에 붙는 평범한 오브젝트 셰이더다 — 씬에서 옮기고
// 스케일 조절하면 그게 곧 행성 위치와 반지름이다.
// 태양은 씬 라이트와 무관한 자체 방향 노브다. 배경 연출이라 게임플레이 조명에 끌려다니면 안 된다.
// 역광으로 두면 본체는 통째로 어두워지고 윤곽만 남는다. 대기 셸이 그 윤곽에 림을 얹는다.
// Edge 노브는 실루엣 안쪽 밴드를 태양과 무관하게 밝혀, 원반이 우주와 같은 색일 때도 형태를 남긴다.
Shader "SlimeJumping/Planet Body"
{
    Properties
    {
        [HDR] _BodyColor ("Body Color", Color) = (0.35, 0.40, 0.55, 1)
        [HDR] _ShadowColor ("Dark Side Color", Color) = (0.02, 0.03, 0.06, 1)
        _LimbDarkening ("Limb Darkening u", Range(0, 1)) = 0.3
        _Terminator ("Terminator Softness", Range(0.001, 1)) = 0.15

        [Header(Edge)]
        [HDR] _EdgeColor ("Edge Colour", Color) = (0.10, 0.16, 0.28, 1)
        _EdgeWidth ("Edge Width (x radius)", Range(0.005, 0.5)) = 0.08
        _EdgeIntensity ("Edge Intensity", Range(0, 4)) = 0

        [Header(Night Limb)]
        [HDR] _NightGlowColor ("Night Edge Colour", Color) = (0.25, 0.55, 1, 1)
        _NightGlowWidth ("Night Edge Width", Range(0.002, 0.3)) = 0.014
        _NightGlowLift ("Night Edge Brightness", Range(0, 0.2)) = 0

        [Header(Sun)]
        [HDR] _SunColor ("Sun Color", Color) = (0.93, 0.96, 1, 1)
        _SunIntensity ("Sun Intensity", Range(0, 200)) = 9.5
        _SunYaw ("Sun Yaw (deg)", Range(-180, 180)) = 0
        _SunPitch ("Sun Pitch (deg)", Range(-90, 90)) = 20
        _DitherAmount ("Dither (LSB)", Range(0, 4)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry"
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Body"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BodyColor;
                half4 _ShadowColor;
                float _LimbDarkening;
                float _Terminator;
                half4 _EdgeColor;
                float _EdgeWidth;
                float _EdgeIntensity;
                half4 _NightGlowColor;
                float _NightGlowWidth;
                float _NightGlowLift;
                half4 _SunColor;
                float _SunIntensity;
                float _SunYaw;
                float _SunPitch;
                float _DitherAmount;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            float3 SunDirection()
            {
                float y = radians(_SunYaw);
                float p = radians(_SunPitch);
                return normalize(float3(cos(p) * sin(y), sin(p), cos(p) * cos(y)));
            }

            // Interleaved Gradient Noise (Jimenez 2014). 어두운 그라디언트의 8bit 밴딩용.
            float3 Dither(float3 col, float2 pixel)
            {
                float n = frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
                return col + (n - 0.5) * _DitherAmount / 255.0;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(_WorldSpaceCameraPos - IN.positionWS);

                // 램버트를 그대로 쓰면 명암경계가 딱딱하다. 대기가 빛을 넘겨주는 폭만큼 감아준다.
                float ndl = dot(N, SunDirection());
                float lit = smoothstep(-_Terminator, _Terminator, ndl);
                float darkening = 1.0 - _LimbDarkening * (1.0 - saturate(dot(N, V)));

                float3 col = lerp(_ShadowColor.rgb,
                                  _BodyColor.rgb * _SunColor.rgb * (_SunIntensity / 3.14159265359) * darkening,
                                  lit);

                // 실루엣에서 반지름의 _EdgeWidth 배만큼 안쪽까지 걸치는 밴드. 명암과 독립 채널이다.
                float ndv = saturate(dot(N, V));
                float radial = sqrt(saturate(1.0 - ndv * ndv));
                col += _EdgeColor.rgb * _EdgeIntensity * smoothstep(1.0 - _EdgeWidth, 1.0, radial);

                // 태양이 지평선 아래로 내려가도 높은 대기는 아직 빛을 본다. sec(δ)−1 ≈ δ²/2 에서 나오는
                // 가우시안 띠를 명암경계에 얹고 밤쪽으로 치우치게 해 낮쪽에서는 저절로 사라지게 한다.
                float nw = max(_NightGlowWidth, 1e-4);
                float night = exp(-0.5 * (ndl * ndl) / (nw * nw)) * smoothstep(nw, -nw, ndl);
                col += _NightGlowColor.rgb * (_NightGlowLift * night);

                return half4(Dither(col, IN.positionHCS.xy), 1);
            }
            ENDHLSL
        }
    }
}
