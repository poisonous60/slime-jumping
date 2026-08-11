// Snowball_FragmentInject.hlsl
// Fragment-stage polish for Snowball_Quibli.shader. Wraps Quibli's
// StylizedPassFragment so the base toon-ramp lighting / shadow path stays intact;
// snow-look (fresnel rim, glitter sparkle) layers on top. The blue shadow tone
// is delivered via Quibli's stock _GradientRamp (assign a blue→white ramp on the
// material) — same convention as Ground_Quibli_Snow.shader so SnowField & snowball
// match without extra wiring.
//
// Difference from Ground_SnowFragmentInject.hlsl:
//   - No carve mask / footprint detail (snowball doesn't carve).
//   - No SnowVaryings wrapper — uses stock Quibli Varyings directly.
//   - Property names prefixed with _Snowball* to avoid collision when a scene
//     uses both snow stack materials.
//
// Include order in Snowball_Quibli.shader (violations yield a silent pink material
// or compile errors):
//   1. StylizedInput.hlsl
//   2. LitForwardPass_DR.hlsl         (defines Varyings + StylizedPassFragment)
//   3. QuibliVertex.hlsl              (defines LitPassVertex — stock, no wrapper)
//   4. Snowball_FragmentInject.hlsl   (this file — SnowballFragment)
#ifndef SNOWBALL_FRAGMENT_INJECT_INCLUDED
#define SNOWBALL_FRAGMENT_INJECT_INCLUDED

// ---- Fresnel rim --------------------------------------------------------
half4  _SnowballFresnelColor;
float  _SnowballFresnelPower;
float  _SnowballFresnelIntensity;

// ---- Glitter ------------------------------------------------------------
TEXTURE2D(_SnowballGlitterNoise);  SAMPLER(sampler_SnowballGlitterNoise);
half4  _SnowballGlitterColor;
float  _SnowballGlitterTiling;
float  _SnowballGlitterThreshold;
float  _SnowballGlitterIntensity;
float  _SnowballGlitterViewSharpness;

half4 SnowballFragment(Varyings i) : SV_Target
{
    // 1. Base Quibli stylized pass — toon-ramp lighting (blue tone via designer-
    //    assigned _GradientRamp), specular, shadow, fog, etc.
    half4 base = StylizedPassFragment(i);

    float3 N = normalize(i.normalWS);
    float3 V = normalize(i.viewDirWS);
    float NdotV = saturate(dot(N, V));

    // 2. Fresnel rim: graze-angle tint. Lerp toward rim color (additive white
    //    on white snow clamps). Pick a contrasting tint (cool blue) for a
    //    visible rim against the body — matches Ground_SnowFragmentInject.
#if defined(_SNOWBALL_FRESNEL)
    {
        half f = pow(1.0 - NdotV, _SnowballFresnelPower);
        base.rgb = lerp(base.rgb, _SnowballFresnelColor.rgb, saturate(f * _SnowballFresnelIntensity));
    }
#endif

    // 3. Glitter: noise-stepped sparkle modulated by view direction. UV tiling
    //    keeps the noise pattern in mesh-local space so tumbling rotation
    //    sweeps sparkle across the surface naturally.
#if defined(_SNOWBALL_GLITTER)
    {
        half n = SAMPLE_TEXTURE2D(_SnowballGlitterNoise, sampler_SnowballGlitterNoise,
                                  i.uv * _SnowballGlitterTiling).r;
        half s = step(_SnowballGlitterThreshold, n) * pow(NdotV, _SnowballGlitterViewSharpness);
        base.rgb = lerp(base.rgb, _SnowballGlitterColor.rgb, saturate(s * _SnowballGlitterIntensity));
    }
#endif

    return base;
}

#endif
