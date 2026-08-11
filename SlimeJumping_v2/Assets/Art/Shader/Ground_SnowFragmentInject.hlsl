// Ground_SnowFragmentInject.hlsl
// Fragment-stage snow polish for Ground_Quibli_Snow.shader. Wraps Quibli's
// StylizedPassFragment so the base lighting/specular/shadow path stays intact;
// snow look (glitter, fresnel rim, footprint detail) layers on top. The blue
// shadow tone is delivered via Quibli's existing _GradientRamp (assign a
// blue-to-white ramp to the material) — no shader work needed for it.
//
// Wrapper-struct pattern: SnowVaryings nests Quibli's Varyings + adds extras on
// TEXCOORD9. The vertex shader (Ground_SnowVertexInject) writes
// snowExtras.x = saturate(1 - heightCurrent/heightRest), the per-vertex carve
// mask. Quibli's Varyings struct uses TEXCOORD0–TEXCOORD8 conditionally;
// TEXCOORD9 is unclaimed so the slot is safe.
//
// Include order in the .shader (violations yield silent pink material or
// compile errors):
//   1. StylizedInput.hlsl
//   2. LitForwardPass_DR.hlsl         (defines Varyings + StylizedPassFragment)
//   3. QuibliVertex.hlsl              (defines LitPassVertex)
//   4. Ground_SnowFragmentInject.hlsl (this file — SnowVaryings + SnowFragment)
//   5. Ground_SnowVertexInject.hlsl   (SnowVertex returns SnowVaryings)
#ifndef GROUND_SNOW_FRAGMENT_INJECT_INCLUDED
#define GROUND_SNOW_FRAGMENT_INJECT_INCLUDED

struct SnowVaryings
{
    Varyings v;
    float2 snowExtras : TEXCOORD9; // x = footprint mask, y = reserved
};

// ---- Footprint detail (carved area) -------------------------------------
TEXTURE2D(_FootprintAlbedo);  SAMPLER(sampler_FootprintAlbedo);
half4  _FootprintTint;
float  _FootprintTiling;
float  _FootprintBlend;

// ---- Fresnel rim --------------------------------------------------------
half4  _SnowFresnelColor;
float  _SnowFresnelPower;
float  _SnowFresnelIntensity;

// ---- Glitter ------------------------------------------------------------
TEXTURE2D(_GlitterNoise);     SAMPLER(sampler_GlitterNoise);
half4  _GlitterColor;
float  _GlitterTiling;
float  _GlitterThreshold;
float  _GlitterIntensity;
float  _GlitterViewSharpness;

half4 SnowFragment(SnowVaryings i) : SV_Target
{
    // 1. Base Quibli stylized pass — gives toon-ramp lighting (blue tone via
    //    designer-assigned _GradientRamp), specular, shadow, fog, etc.
    half4 base = StylizedPassFragment(i.v);

    float mask = saturate(i.snowExtras.x);
    float3 N = normalize(i.v.normalWS);
    float3 V = normalize(i.v.viewDirWS);
    float NdotV = saturate(dot(N, V));

    // 2. Footprint detail: texture provides the SHAPE only (grayscale .r = where
    //    inner snow is exposed, 0 = original surface, 1 = full inner snow). The
    //    inner snow COLOR is _FootprintTint (designer-authored, e.g. cool blue).
    //    base.rgb already carries toon-ramp shading; multiplying tint by base
    //    keeps the same shading on the tinted inner snow.
#if defined(_SNOW_FOOTPRINT)
    if (mask > 1e-3)
    {
        half pattern = SAMPLE_TEXTURE2D(_FootprintAlbedo, sampler_FootprintAlbedo,
                                        i.v.uv * _FootprintTiling).r;
        half3 inner = base.rgb * _FootprintTint.rgb;
        base.rgb = lerp(base.rgb, inner, mask * _FootprintBlend * pattern);
    }
#endif

    // 3. Fresnel rim: graze-angle tint. Lerp toward the rim color (not
    //    additive) — adding white over already-white snow clamps and gives
    //    no visible toggle difference. Matches Quibli's stock _FlatRimColor
    //    pattern: pick a contrasting tint (cool blue or warm gold) so the
    //    silhouette edge reads as snow-rim, not just brighter snow.
#if defined(_SNOW_FRESNEL)
    {
        half f = pow(1.0 - NdotV, _SnowFresnelPower);
        base.rgb = lerp(base.rgb, _SnowFresnelColor.rgb, saturate(f * _SnowFresnelIntensity));
    }
#endif

    // 4. Glitter: noise-stepped sparkle modulated by view direction. Lerp
    //    toward the glitter color at sparkle pixels (same reasoning as fresnel
    //    — additive white is invisible on white snow). Host-local uv keeps the
    //    pattern locked to the platform; rotating the camera changes which
    //    pixels qualify but their host position stays put.
#if defined(_SNOW_GLITTER)
    {
        half n = SAMPLE_TEXTURE2D(_GlitterNoise, sampler_GlitterNoise,
                                  i.v.uv * _GlitterTiling).r;
        half s = step(_GlitterThreshold, n) * pow(NdotV, _GlitterViewSharpness);
        base.rgb = lerp(base.rgb, _GlitterColor.rgb, saturate(s * _GlitterIntensity));
    }
#endif

    return base;
}

#endif
