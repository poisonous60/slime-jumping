// 배경 행성의 대기 림. 본체와 똑같은 반지름 1 구 메시를 쓰고 정점만 셸 두께로 부풀린다 —
// 씬에서 두 오브젝트가 같은 크기로 보여야 하기 때문이다. 메시 바운드는 에셋에 크게 구워
// 부풀린 뒤에도 컬링되지 않게 한다.
// 레이마치가 아니라 접선 기둥밀도의 해석해를 쓴다: 지수 대기에서 시선이 고도 h 를 스치며 지나는
// 기둥은 ρ(h)·√(2πH) 다. 마치로는 1~2px 짜리 얇은 코어 층을 스텝이 통째로 건너뛰어 아예 안 나왔고,
// 잡으려면 스텝을 수백 개로 올려야 했다. 해석해는 루프가 없어 밴딩도 앨리어싱도 없다.
// 원반 안쪽은 고체가 가리므로 적분을 림에서 끊는다 — 안쪽 항은 중심 근처에서 수치가 터진다.
// 넓고 희미한 헤일로 + 얇고 밝은 코어 두 층으로 분리돼 있고, 각 층은 자기 폭으로 정규화돼
// 폭 노브를 돌려도 밝기가 따라 변하지 않는다.
// 아크를 따라 어디가 밝은지는 태양 방향이 정한다 — 림 지점의 법선과 태양의 내적.
Shader "SlimeJumping/Planet Atmosphere"
{
    Properties
    {
        [Header(Shape)]
        _RimWidth ("Halo Width (planet radii)", Range(0.0005, 0.2)) = 0.02
        _CoreWidth ("Core Width (x Halo Width)", Range(0.005, 1)) = 0.06
        _RimSoftness ("Mesh Reach (x Halo Width)", Range(2, 24)) = 8
        _Extinction ("Self Extinction", Range(0, 8)) = 1

        [Header(Colour)]
        [HDR] _RimColor ("Halo Colour (broad)", Color) = (0.30, 0.52, 1, 1)
        [HDR] _CoreColor ("Core Colour (thin)", Color) = (0.88, 0.94, 1, 1)
        _RimIntensity ("Halo Intensity", Range(0, 20)) = 0.14
        _CoreBoost ("Core Intensity", Range(0, 20)) = 1.2

        [Header(Sun)]
        _SunYaw ("Sun Yaw (deg)", Range(-180, 180)) = -20
        _SunPitch ("Sun Pitch (deg)", Range(-90, 90)) = 25
        _RimFade ("Arc Fade Softness", Range(0.01, 2)) = 0.5
        _RimReach ("Arc Reach Past Terminator", Range(-1, 1)) = 0
        _ArcWidthMin ("Arc Thinning at Faint End", Range(0.05, 1)) = 0.35
        _ArcDimMin ("Arc Floor Brightness", Range(0, 1)) = 0.25

        _DitherAmount ("Dither (LSB)", Range(0, 4)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Atmosphere"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define TWO_PI 6.28318530718

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float _RimWidth;
                float _CoreWidth;
                float _RimSoftness;
                float _Extinction;
                half4 _RimColor;
                half4 _CoreColor;
                float _RimIntensity;
                float _CoreBoost;
                float _SunYaw;
                float _SunPitch;
                float _RimFade;
                float _RimReach;
                float _ArcWidthMin;
                float _ArcDimMin;
                float _DitherAmount;
            CBUFFER_END

            float HBroad() { return max(_RimWidth, 1e-5); }
            float HCore() { return max(_RimWidth * _CoreWidth, 1e-6); }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionOS = IN.positionOS.xyz * (1.0 + HBroad() * _RimSoftness);
                OUT.positionHCS = TransformObjectToHClip(OUT.positionOS);
                return OUT;
            }

            float3 SunDirection()
            {
                float y = radians(_SunYaw);
                float p = radians(_SunPitch);
                float3 w = normalize(float3(cos(p) * sin(y), sin(p), cos(p) * cos(y)));
                return normalize(TransformWorldToObjectDir(w));
            }

            // 림 프로파일 exp(-edge/H) 를 픽셀 폭 w 로 박스 적분한 닫힌 형태.
            // 점 샘플링하면 H 가 한 픽셀 수준일 때 픽셀 중심이 림에서 얼마나 떨어졌냐에 따라 밝기가
            // 행마다 최대 2.7배 출렁여 구슬처럼 끊겨 보인다. 적분하면 서브픽셀 폭이어도 에너지가
            // 보존돼 밝기가 일정하고, 안쪽 경계(edge<0)도 같은 식에서 저절로 안티에일리어싱된다.
            // 원반 안쪽은 고체가 가리므로 적분 하한을 0 에서 자른다.
            float LimbColumn(float edge, float H, float w)
            {
                float lo = max(edge - 0.5 * w, 0.0);
                float hi = max(edge + 0.5 * w, 0.0);
                if (hi <= lo) return 0.0;
                return (H / w) * (exp(-lo / H) - exp(-hi / H));
            }

            // Interleaved Gradient Noise (Jimenez 2014). 어두운 그라디언트의 8bit 밴딩용.
            float3 Dither(float3 col, float2 pixel)
            {
                float n = frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
                return col + (n - 0.5) * _DitherAmount / 255.0;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 ro = TransformWorldToObject(_WorldSpaceCameraPos);
                float3 rd = normalize(IN.positionOS - ro);

                float tc = -dot(ro, rd);
                if (tc <= 0.0) return 0.0;

                float3 pc = ro + rd * tc;
                float b = length(pc);

                float edge = b - 1.0;
                float w = max(fwidth(edge), 1e-6);
                float outside = smoothstep(-w, w, edge);
                if (outside <= 0.0) return 0.0;

                // 림 지점이 태양을 보는지가 아크를 따라 밝기와 두께를 함께 정한다 — 밝은 구간이 더 굵다.
                float3 L = SunDirection();
                float lit = smoothstep(-_RimFade, _RimFade, dot(pc / max(b, 1e-6), L) + _RimReach);
                float widthScale = lerp(_ArcWidthMin, 1.0, lit);

                float broad = LimbColumn(edge, max(HBroad() * widthScale, 1e-6), w);
                float core = LimbColumn(edge, max(HCore() * widthScale, 1e-6), w);

                float T = exp(-_Extinction * broad);
                float3 col = (_RimColor.rgb * broad * _RimIntensity
                            + _CoreColor.rgb * core * _CoreBoost) * T * lerp(_ArcDimMin, 1.0, lit);

                return half4(Dither(col, IN.positionHCS.xy), 1);
            }
            ENDHLSL
        }
    }
}
