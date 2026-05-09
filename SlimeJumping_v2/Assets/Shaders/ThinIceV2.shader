// URP thin-ice shader: Quibli toon lighting + procedural Voronoi crack overlay.
//
// Lighting layer mirrors Quibli's StylizedLit math — main light direction + shadow
// attenuation feed an NdotL ramp lookup against `_GradientRamp` (the same texture
// convention used by Quibli/Stylized Lit). `StylizedLighting.hlsl` from the Quibli
// package is included directly so any change Quibli ships to that math (e.g. self-
// shading offset behavior) flows through here too. We deliberately avoid the
// heavier `Lighting_DR.hlsl` / `LitForwardPass_DR.hlsl` machinery — those are
// Shader-Graph-bound and pull in the full DR_* feature flag matrix.
//
// _CrackProgress drives both the cell-edge thickness and edge emissive intensity:
//   0 → cells invisible (clean toon-shaded ice)
//   1 → thick, glowing cyan boundaries (about-to-shatter)
//
// Cracks are layered ON TOP of the lit color and contribute purely additive
// emissive — they look the same whether the surface is in toon-shadow or in light.
//
// _Alpha is reserved for ThinIceShardRuntime's per-instance fade (set via
// MaterialPropertyBlock). Multiplied with _BaseColor.a so the platform body's
// alpha and the shard's despawn fade compose without stomping each other.
Shader "SlimeJumping/ThinIceV2"
{
    Properties
    {
        // Quibli-style toon lighting.
        // [Gradient] swaps the default 2D-texture slot for an inline Unity Gradient
        // picker (handled by Quibli's MaterialGradientDrawer — the texture is baked
        // as a sub-asset of the material). Convention name from Quibli/Stylized Lit.
        [Gradient] _GradientRamp    ("Toon Gradient", 2D) = "white" {}
                   _SelfShadingSize ("Shading Offset", Range(0, 1)) = 0.0
                   _LightContribution ("Light Color Impact", Range(0, 1)) = 1.0

        // Ice base color (multiplied with ramp)
        [HDR] _BaseColor    ("Base Color",      Color) = (0.55, 0.78, 1.0, 0.85)

        // Crack overlay (additive emissive)
        [HDR] _CrackColor   ("Crack Color",     Color) = (0.45, 0.95, 1.0, 1.0)
              _CrackProgress("Crack Progress",  Range(0,1)) = 0.0
              _CellScale    ("Cell Scale",      Float) = 6.0
              _BorderEdge   ("Border Width",    Range(0.005, 0.4)) = 0.05
              _BorderGrow   ("Border Grow",     Range(0.0, 0.4)) = 0.15
              _AlphaCutAt   ("Alpha Loss At Edge", Range(0, 1)) = 0.5
              _Alpha        ("Alpha", Range(0, 1)) = 1.0
        [Toggle(_)] _WorldSpaceSampling ("Sample in World Space", Float) = 1.0

        // Surface controls — exposed so the material inspector can switch between the
        // default see-through look (back/side faces visible through the front of cube
        // primitives) and an opaque-like look (Z Write on, Cull Back) for solid slabs.
        [Toggle] _ZWrite ("Z Write (opaque-like)", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector"= "True"
        }
        LOD 100

        Pass
        {
            Name "ThinIceForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // URP shadow keywords — the Quibli helper samples the main light's
            // shadow attenuation, so we have to enable the multi-compile so the
            // shadow path actually compiles.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            // Transparent surface: skip the screen-space shadow branch in
            // StylizedLighting.hlsl which doesn't work for non-opaque queue.
            #define _SURFACE_TYPE_TRANSPARENT 1

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _CrackColor;
                float  _CrackProgress;
                float  _CellScale;
                float  _BorderEdge;
                float  _BorderGrow;
                float  _AlphaCutAt;
                float  _Alpha;
                float  _WorldSpaceSampling;
                float  _SelfShadingSize;
                float  _LightContribution;
            CBUFFER_END

            TEXTURE2D(_GradientRamp);
            SAMPLER(sampler_GradientRamp);

            // Pull in Quibli's MainLight_half so this shader uses literally the
            // same main-light + shadow sampling Quibli/Stylized Lit uses.
            #include "../ThirdParty/Quibli/Shaders/StylizedLighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionOS  : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
                float  fogCoord    : TEXCOORD3;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS  = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionOS  = IN.positionOS.xyz;
                OUT.fogCoord    = ComputeFogFactor(OUT.positionHCS.z);
                return OUT;
            }

            // Cheap 2D pseudo-random hash. Returns site offset in [0, 1).
            float2 hash22(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)),
                           dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            // Returns (d2 - d1): 0 along a Voronoi cell border, larger toward cell interior.
            // Same idea as the F2-F1 cell-border trick used by Inigo Quilez's article.
            float voronoiBorderDist(float2 p)
            {
                float2 g = floor(p);
                float2 f = frac(p);
                float d1 = 8.0; // nearest
                float d2 = 8.0; // 2nd nearest
                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 cell = float2(x, y);
                        float2 site = cell + hash22(g + cell);
                        float  d    = length(f - site);
                        if (d < d1) { d2 = d1; d1 = d; }
                        else if (d < d2) { d2 = d; }
                    }
                }
                return d2 - d1;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                // ---- Quibli-style toon lighting ---------------------------------
                half3 lightDir; half3 lightColor;
                half  distAtten; half  shadowAtten;
                MainLight_half(IN.positionWS, lightDir, lightColor, distAtten, shadowAtten);

                half3 N = normalize(IN.normalWS);

                // Mirrors Quibli's NdotLTransitionPrimary: (NdotL*0.5+0.5) - selfShadingSize.
                half  NdotL  = saturate((dot(N, lightDir) * 0.5 + 0.5) - _SelfShadingSize);
                half2 rampUV = half2(NdotL, 0.5);
                half3 ramp   = SAMPLE_TEXTURE2D(_GradientRamp, sampler_GradientRamp, rampUV).rgb;

                // _LightContribution mirrors Quibli's "Light Color Impact" — 0 = ramp
                // owns the look, 1 = ramp tinted by main light color & shadow.
                half3 lit = ramp * lerp(half3(1,1,1), lightColor * shadowAtten, _LightContribution);

                float3 baseRGB = _BaseColor.rgb * lit;

                // ---- Voronoi crack overlay (additive emissive) ------------------
                // World-space sampling lets non-uniformly-scaled platforms (e.g. a 3:0.6
                // wide tile) display square cells — the platform is a "window" into a
                // uniform world Voronoi field. Object-space sampling is preserved as a
                // toggle for shards: small flying chunks should keep cells locked to
                // their own mesh rather than scrolling as they translate / rotate.
                float2 p = lerp(IN.positionOS.xy, IN.positionWS.xy, _WorldSpaceSampling) * _CellScale;

                float borderDist = voronoiBorderDist(p);

                // Edge mask: 1 right at the boundary, 0 in cell interior. The threshold
                // (== "border thickness in cell units") grows with _CrackProgress so cells
                // start invisible and cracks visibly widen as breakage approaches.
                float threshold = _BorderEdge + _CrackProgress * _BorderGrow;
                float edgeMask  = 1.0 - smoothstep(0.0, threshold, borderDist);
                float crackStr  = edgeMask * _CrackProgress;

                // Cracks add emissive cyan independent of toon ramp so they read in
                // shadow as well as in light.
                float3 finalRGB = baseRGB + crackStr * _CrackColor.rgb;

                // Crack edges punch a small alpha hole so the platform feels brittle without
                // turning fully transparent. _AlphaCutAt 0 disables this; default 0.5 leaves
                // half-transparent fissures at full progress.
                float alpha = _BaseColor.a * (1.0 - crackStr * _AlphaCutAt) * _Alpha;

                finalRGB = MixFog(finalRGB, IN.fogCoord);
                return float4(finalRGB, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
