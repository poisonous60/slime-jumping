// Ground_WetFragmentInject.hlsl
// Wet-ground layer for the Stage3 rain, wrapped around TrailReceiverFragment so both the Quibli toon
// lighting and the trail overlay stay untouched — this only post-processes their result.
//
// The two terms are one phenomenon, so neither is applied without the other (Lagarde, Water drop 3a/3b):
// a wet surface darkens because water's IOR 1.33 sits closer to the substrate's ~1.5 than air does, so
// light refracts less, scatters longer inside and is absorbed more; it shines because water fills the
// air gaps of the rough surface and replaces diffuse reflection with a mirror one. Darkening alone reads
// as dirt, sheen alone reads as plastic.
//
// There are no puddles: the platforms are flat slabs with nowhere for water to pool, so rain leaves an
// even film instead. A noise-driven puddle mask would be inventing depressions the geometry does not
// have, which reads as grime rather than water.
//
// Ripples are planar-mapped on world XZ, so their tangent frame is the world X/Z axes, not the mesh
// tangent — the perturbation goes straight into the world normal and no TBN is needed. This only holds
// because the effect is masked to up-facing fragments.
//
// The attenuation multiplies an already-fogged colour. The scene has fog off (MainScene m_Fog: 0) so
// MixFog is a no-op; turning scene fog on makes this darken the fog too.
//
// Every knob below is a global set by WetGroundBand, not a material property: a material property
// declared outside Quibli's UnityPerMaterial CBUFFER would drop this shader off the SRP Batcher path,
// and that CBUFFER lives in third-party code we do not edit.
//
// Include AFTER Ground_TrailReceiverInject.hlsl — TrailReceiverFragment must already be defined.
#ifndef GROUND_WET_FRAGMENT_INJECT_INCLUDED
#define GROUND_WET_FRAGMENT_INJECT_INCLUDED

TEXTURE2D(_WetRippleNormal);  SAMPLER(sampler_WetRippleNormal);

float _WetUpFacingThreshold;
float _WetUpFacingSoftness;
float _WetDarkenFloor;
float _WetRippleTiling;
float _WetRippleSpeed;
float _WetRippleStrength;
float _WetReflectionStrength;
float _WetRoughness;
float _WetFresnelPower;

// Driven by WetGroundBand from the rain's altitude band. Only this shader declares it.
float _Stage3WetAmount;

half4 WetFragment(Varyings input) : SV_Target
{
    half4 color = TrailReceiverFragment(input);

    float wet = saturate(_Stage3WetAmount);
    if (wet <= 0.0) return color;

    float3 n = normalize(input.normalWS);
    // Guarded divide: with no WetGroundBand in the scene every global reads 0, and 0/0 would be NaN.
    wet *= saturate((n.y - _WetUpFacingThreshold) / max(_WetUpFacingSoftness, 1e-4));
    if (wet <= 0.0) return color;

    float3 posWS = input.positionWS;

    float2 rippleUV = posWS.xz * _WetRippleTiling;
    float t = _Time.y * _WetRippleSpeed;
    half3 r1 = UnpackNormal(SAMPLE_TEXTURE2D(_WetRippleNormal, sampler_WetRippleNormal, rippleUV + float2(t, 0.0)));
    half3 r2 = UnpackNormal(SAMPLE_TEXTURE2D(_WetRippleNormal, sampler_WetRippleNormal, rippleUV * 0.73 - float2(0.0, t * 0.8)));
    float2 rippleXZ = (r1.xy + r2.xy) * (_WetRippleStrength * wet);
    float3 nW = normalize(n + float3(rippleXZ.x, 0.0, rippleXZ.y));

    float3 V = normalize(input.viewDirWS);
    // Five-argument overload: the four-argument one is compiled out under USE_CLUSTER_LIGHT_LOOP.
    half3 refl = GlossyEnvironmentReflection(reflect(-V, nW), posWS, _WetRoughness, 1.0h,
                                             GetNormalizedScreenSpaceUV(input.positionCS));
    half fresnel = pow(1.0 - saturate(dot(nW, V)), _WetFresnelPower);

    color.rgb *= lerp(1.0, _WetDarkenFloor, wet);
    color.rgb += refl * (fresnel * _WetReflectionStrength * wet);
    return color;
}

#endif
