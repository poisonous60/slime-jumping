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
        _SunwardFocus ("Sunward Focus", Range(0, 1)) = 0.4
        _ViewFlow ("View Angle Flow", Range(0, 1)) = 0

        [Header(Colour Flow)]
        [HDR] _ThinColor ("Thin Side Colour", Color) = (0.80, 0.92, 1.12, 1)
        [HDR] _ThickColor ("Thick Side Colour", Color) = (1, 1, 1, 1)
        _ColorTurn ("Colour Turn Thickness", Range(0.02, 4)) = 0.45
        _ColorFlow ("Colour Flow Amount", Range(0, 1)) = 0

        [Header(Inside Disc)]
        _InsideAmount ("Inside Glow Amount", Range(0, 20)) = 0
        _InsideDepth ("Inside Spread Depth (x Halo Width)", Range(0.1, 20)) = 1
        _InsideFade ("Inside Twilight Softness", Range(0.02, 2)) = 0.5

        [Header(Detail)]
        _BandDrift ("Band Thickness Drift", Range(0, 0.3)) = 0
        _HaloShoulder ("Halo Glow Shoulder", Range(0, 1)) = 0

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
                float _SunwardFocus;
                float _ViewFlow;
                half4 _ThinColor;
                half4 _ThickColor;
                float _ColorTurn;
                float _ColorFlow;
                float _InsideAmount;
                float _InsideDepth;
                float _InsideFade;
                float _BandDrift;
                float _HaloShoulder;
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

            // erfcx(z) = exp(z²)erfc(z). z=0 값·기울기와 z→∞ 점근을 맞춘 무리식, 전 구간 오차 1% 미만.
            float Erfcx(float z)
            {
                return rcp(1.1283792 * z + sqrt(0.4149 * z * z + 1.0));
            }

            // 원반 안쪽은 뒤쪽 대기가 고체에 가리고 카메라 쪽 절반만 남는다. 림에서 접선값의 0.5 로 시작하는
            // Chapman 절반 기둥 꼴을 쓰되, 인자를 깊이에 선형으로 둬 실측 감쇠(1/거리)에 맞춘다.
            // cover 는 픽셀 중 원반 안쪽 비율이라 경계 한 픽셀이 저절로 안티에일리어싱된다.
            float InsideColumn(float edge, float H, float w)
            {
                float cover = saturate(0.5 - edge / w);
                if (cover <= 0.0) return 0.0;
                return 0.5 * Erfcx(max(-edge, 0.0) / max(H * _InsideDepth, 1e-6)) * cover;
            }

            // Henyey-Greenstein 을 완전 전방(mu=1) 값으로 나눈 비라 항상 1 이하 — 피크를 못 올린다.
            // 90도 기준으로 재고 상한을 거는 방식은 이 구도(태양이 거의 정후방)에서 전 구간이 상한에 붙어 평평해진다.
            float PhaseRatio(float muViewSun)
            {
                float g = 0.5 * saturate(_SunwardFocus);
                float gg = g * g;
                float den = max(1e-4, 1.0 + gg - 2.0 * g * muViewSun);
                float hg = (1.0 - gg) * rsqrt(den * den * den);
                float denF = max(1e-4, (1.0 - g) * (1.0 - g));
                float hgF = (1.0 - gg) * rsqrt(denF * denF * denF);
                return saturate(hg / max(hgF, 1e-4));
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

                // 림 지점이 태양을 보는지가 아크를 따라 밝기와 두께를 함께 정한다 — 밝은 구간이 더 굵다.
                // 분모 바닥은 안쪽 항이 중심 근처를 볼 때의 발산을 막는다. 바깥(b>=1)에는 영향 없다.
                float3 L = SunDirection();
                float lit = smoothstep(-_RimFade, _RimFade, dot(pc / max(b, 0.95), L) + _RimReach);
                float widthScale = lerp(_ArcWidthMin, 1.0, lit);

                float H = max(HBroad() * widthScale, 1e-6);
                float halo = LimbColumn(edge, H, w);
                float core = LimbColumn(edge, max(HCore() * widthScale, 1e-6), w);
                float inside = _InsideAmount * InsideColumn(edge, H, w);
                if (halo + core + inside <= 0.0) return 0.0;

                // 안쪽 기둥의 밝기는 시선이 닿는 지표점의 태양 노출로 정해진다. 그 위 대기는 지표가 그늘일 때도
                // 빛을 보므로 경계를 _InsideFade 만큼 풀어준다 — 이 폭이 원반 안쪽 감쇠 거리를 정한다.
                float3 ph = ro + rd * (tc - sqrt(max(1.0 - b * b, 0.0)));
                float insideLit = smoothstep(-_InsideFade, _InsideFade, dot(ph, L) + _RimReach);

                float T = exp(-_Extinction * halo);

                // 방위각 저주파 성분으로 유효 스케일높이만 흔든다. 실루엣과 자기흡수는 그대로 둔다.
                float phi = atan2(pc.y, pc.x);
                float drift = max(0.9, 1.0 + _BandDrift * (0.65 * sin(2.0 * phi + 0.7) + 0.35 * sin(5.0 * phi - 1.1)));
                halo *= exp((max(edge, 0.0) / H) * (1.0 - rcp(drift)));

                // 포스트 블룸 없이 발광감만. 코어가 강한 픽셀에서는 꺼서 피크를 안 올린다.
                halo *= 1.0 + _HaloShoulder * (1.0 - saturate(halo)) * (1.0 - saturate(core * 2.0));

                float phase = lerp(1.0, PhaseRatio(dot(rd, L)), _ViewFlow);
                float arc = lerp(_ArcDimMin, 1.0, lit) * phase;
                float3 col = (_RimColor.rgb * (halo * arc + inside * insideLit * phase) * _RimIntensity
                            + _CoreColor.rgb * core * _CoreBoost * arc) * T;

                // 기둥이 두꺼울수록 얇은 쪽 색에서 두꺼운 쪽 색으로. peak guard 가 어느 채널도 못 올리게 막는다.
                float3 tint = lerp(_ThinColor.rgb, _ThickColor.rgb, 1.0 - exp(-(halo + core + inside) / max(_ColorTurn, 1e-4)));
                tint = lerp(1.0, tint, _ColorFlow);
                col *= tint * rcp(max(1.0, max(tint.r, max(tint.g, tint.b))));

                return half4(Dither(col, IN.positionHCS.xy), 1);
            }
            ENDHLSL
        }
    }
}
