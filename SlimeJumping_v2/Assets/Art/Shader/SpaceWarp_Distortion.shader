// SpaceWarp_Distortion — localized screen-space refraction for "warped space".
//
// Renders on a flat quad (UV 0..1, centered at 0.5). Samples _CameraOpaqueTexture
// at each fragment with a UV offset, so whatever is behind the quad appears to
// distort. Two effects combine additively:
//
//   • Persistent warp — animated value-noise displacement, masked by a radial
//     falloff. Use for "anomaly zones" (always-on warped area).
//   • Shockwave pulse — a radial band of outward refraction that travels from
//     the center to the edge as _PulsePhase ramps 0→1. Use for one-shot
//     interactions (jump-pad bounce, impact). Driver script animates _PulsePhase
//     and _PulseStrength via MaterialPropertyBlock.
//
// Both can be active simultaneously: a persistent zone that also pulses on hit.
//
// Samples a custom global texture _SpaceWarpSourceTex populated by
// SpaceWarpSourceFeature (a per-camera color copy). We can't use
// _CameraOpaqueTexture directly because the project uses a Base+Overlay camera
// stack — _CameraOpaqueTexture from the Base only contains the background
// layer, so an Overlay-rendered warp would sample (effectively) black.
//
// Author the quad with uniform XY scale — radial math assumes square UV space.

Shader "SlimeJumping/SpaceWarp_Distortion"
{
    Properties
    {
        [Header(Tint)]
        [HDR] _BaseColor       ("Tint (RGB) + Alpha",        Color) = (0.6, 0.7, 1.0, 1.0)
        _TintStrength          ("Tint Strength",             Range(0, 1))   = 0.05

        [Space(8)][Header(Mask)]
        _FalloffStart          ("Radial Falloff Start",      Range(0, 1))   = 0.55
        _FalloffEnd            ("Radial Falloff End",        Range(0, 1.5)) = 1.0

        [Space(8)][Header(Persistent Warp)]
        _PersistStrength       ("Noise Strength",            Range(0, 0.3))  = 0.04
        _PersistScale          ("Noise Scale",               Range(0.1, 16)) = 3.0
        _PersistScroll         ("Noise Scroll (uv/s)",       Vector) = (0.08, 0.05, 0, 0)
        _DomainWarp            ("Domain Warp Iteration",     Range(0, 4))   = 1.6
        _SwirlAmount           ("Swirl Strength (radians)",  Range(-3.14, 3.14)) = 0.9
        _SwirlAnim             ("Swirl Anim Speed",          Range(0, 4))   = 0.4

        [Space(8)][Header(Shockwave Pulse driven via MPB)]
        _PulsePhase            ("Pulse Phase 0..1",          Range(0, 1))   = 0
        _PulseStrength         ("Pulse Strength",            Range(0, 0.4)) = 0.15
        _PulseRippleWidth      ("Ripple Band Width",         Range(0.01, 0.6)) = 0.18
        _PulseMaxRadius        ("Ripple Max Radius",         Range(0.1, 1.5)) = 1.0

        [Space(8)][Header(Membrane Stretch  Pop)]
        // _Taper > 0 narrows the u=1 end into a teardrop (the stretch driver puts
        // u=1 at the slime side, so the membrane thins as it follows).
        _Taper                 ("Taper (0=ellipse, 1=teardrop)", Range(0, 1)) = 0
        _PopPhase              ("Pop Phase 0..1 (MPB)",      Range(0, 1)) = 0
        _PopFizz               ("Pop Fizz Strength",         Range(0, 0.3)) = 0.06

        [Space(8)][Header(Draw Order)]
        // Foreground (8 / Always) covers gameplay so only the warped scene is visible.
        // Background (4 / LEqual) lets gameplay objects draw on top — use as a backdrop.
        // Pair with material Render Queue: 3500 = on top, 3000 = default transparent.
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
                float4 _BaseColor;
                float  _TintStrength;
                float  _FalloffStart;
                float  _FalloffEnd;
                float  _PersistStrength;
                float  _PersistScale;
                float4 _PersistScroll;     // xy = flow direction
                float  _DomainWarp;
                float  _SwirlAmount;
                float  _SwirlAnim;
                float  _PulsePhase;
                float  _PulseStrength;
                float  _PulseRippleWidth;
                float  _PulseMaxRadius;
                float  _Taper;
                float  _PopPhase;
                float  _PopFizz;
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

            // 2D hash → [0,1). Cheap and good enough for value noise here.
            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Smooth value noise (bilinear-interpolated hash grid).
            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i + float2(0.0, 0.0));
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

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
                // --- Membrane coords ---
                // u along the long axis (0 = anchor end / pad, 1 = stretch target / slime).
                // _Taper narrows the u=1 side: width(u) = 1 - Taper*u, so dist=length(du,vc/width)
                // is circular when Taper=0 and teardrop-shaped otherwise.
                float u  = IN.uv.x;
                float vc = IN.uv.y * 2.0 - 1.0;
                float width = max(1.0 - _Taper * u, 0.05);
                float du = u * 2.0 - 1.0;
                float dv = vc / width;
                float2 centered = float2(du, dv);
                float  dist     = sqrt(du * du + dv * dv);
                float2 radial   = dist > 1e-5 ? centered / dist : float2(0.0, 0.0);

                // Pop expands the mask outward as the membrane bursts.
                float popExpand = 1.0 + _PopPhase * 0.6;
                float fStart    = _FalloffStart * popExpand;
                float fEnd      = max(_FalloffEnd * popExpand, fStart + 1e-4);
                float mask      = 1.0 - smoothstep(fStart, fEnd, dist);
                if (mask <= 0.001) discard;

                // --- Persistent warp: swirl + domain-warped flow noise ---
                // Swirl around center: rotation amount fades with radial distance so the
                // edge stays put while the core twists. Reads as "space dragging" rather
                // than the symmetric fish-eye look two-channel noise produces.
                float swirlMask = 1.0 - smoothstep(0.0, _FalloffEnd, dist);
                float angle     = (_SwirlAmount + sin(_Time.y * _SwirlAnim) * 0.25) * swirlMask;
                float cs        = cos(angle);
                float sn        = sin(angle);
                float2 swirled  = float2(centered.x * cs - centered.y * sn,
                                         centered.x * sn + centered.y * cs);
                float2 swirlOffset = (swirled - centered) * 0.5;

                // Domain-warped noise: noise(p + noise(p)) gives tendril-like flowing
                // offsets that share a directional bias instead of mirroring per-axis.
                float2 flowUv = IN.uv * _PersistScale + _PersistScroll.xy * _Time.y;
                float  n0 = ValueNoise(flowUv);
                float  n1 = ValueNoise(flowUv + n0 * _DomainWarp + 7.7);
                float  n2 = ValueNoise(flowUv + n0 * _DomainWarp + 19.3);
                float2 noiseOffset = float2(n1 - 0.5, n2 - 0.5) * (_PersistStrength * 2.0);

                float2 persistOffset = swirlOffset + noiseOffset;

                // --- Pop fizz: high-frequency UV jitter at the moment the membrane bursts ---
                if (_PopPhase > 0.0)
                {
                    float fA = ValueNoise(IN.uv * 22.0 + _Time.y * 30.0)        - 0.5;
                    float fB = ValueNoise(IN.uv * 22.0 + _Time.y * 30.0 + 5.31) - 0.5;
                    persistOffset += float2(fA, fB) * (_PopPhase * _PopFizz);
                }

                // --- Shockwave: Gaussian-ish band travelling outward along radial ---
                float bandPos       = _PulsePhase * _PulseMaxRadius;
                float bandDist      = abs(dist - bandPos) / max(_PulseRippleWidth, 1e-4);
                float bandIntensity = saturate(1.0 - bandDist * bandDist);   // smooth falloff
                float pulseDecay    = 1.0 - _PulsePhase;                      // taper as ripple expands
                float2 pulseOffset  = radial * (bandIntensity * pulseDecay * _PulseStrength);

                float2 totalOffset = (persistOffset + pulseOffset) * mask;

                // --- Sample camera color (captured by SpaceWarpSourceFeature) with offset ---
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                float3 refracted = SAMPLE_TEXTURE2D_X(_SpaceWarpSourceTex,
                                                      sampler_SpaceWarpSourceTex,
                                                      screenUV + totalOffset).rgb;

                float3 rgb = lerp(refracted, _BaseColor.rgb, _TintStrength);
                // Pop fades alpha to 0 with an ease-in curve so the burst feels sudden.
                float popAlpha = saturate(1.0 - _PopPhase * _PopPhase);
                return half4(rgb, _BaseColor.a * mask * popAlpha);
            }
        ENDHLSL

        // Custom LightMode keeps URP's default DrawObjectsPass from rendering this material.
        // Instead, SpaceWarpSourceFeature first captures the camera color (with all opaques AND
        // transparents — including face, body, etc.) into _SpaceWarpSourceTex at
        // AfterRenderingTransparents, then runs a second pass that draws meshes tagged
        // "SpaceWarpDraw" so they sample that fully-populated texture.
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

    FallBack Off
}
