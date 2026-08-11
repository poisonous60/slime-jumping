#ifndef SLIME_FROST_INCLUDED
#define SLIME_FROST_INCLUDED

// Frost coating — driven by SlimeFrostController.cs via MaterialPropertyBlock
// when the slime is inside a ColdZone. Surface crystal overlay only — there
// is no body color shift in the freeze pipeline (an earlier two-palette
// design was removed because Quibli's lit colors couldn't be cleanly mirrored
// via MPB overrides; the frozen visual is "same slime, with frost on top").
//
// Declared OUTSIDE UnityPerMaterial so it pairs with the same SRP-batcher
// trade-off SlimeBurn.hlsl already accepts on this material — slime is one
// instance, lost batch is irrelevant.

float  _FrostStrength;
float  _FrostNoiseScale;
float4 _FrostBaseColor;
float  _FrostMaxCoverage;

float HashFrost(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

// Value-noise-style triliear interpolation over a hashed lattice. Domain is
// rest-pose OS so the pattern is welded to the surface and survives the
// vertex deform (same anchoring rule SlimeBurn relies on).
float SampleFrostMask(float3 osPosRest)
{
    float3 q = osPosRest * _FrostNoiseScale;
    float3 i = floor(q);
    float3 f = frac(q);
    f = f * f * (3.0 - 2.0 * f);

    float n000 = HashFrost(i + float3(0, 0, 0));
    float n100 = HashFrost(i + float3(1, 0, 0));
    float n010 = HashFrost(i + float3(0, 1, 0));
    float n110 = HashFrost(i + float3(1, 1, 0));
    float n001 = HashFrost(i + float3(0, 0, 1));
    float n101 = HashFrost(i + float3(1, 0, 1));
    float n011 = HashFrost(i + float3(0, 1, 1));
    float n111 = HashFrost(i + float3(1, 1, 1));

    float n00 = lerp(n000, n100, f.x);
    float n10 = lerp(n010, n110, f.x);
    float n01 = lerp(n001, n101, f.x);
    float n11 = lerp(n011, n111, f.x);
    float n0  = lerp(n00, n10, f.y);
    float n1  = lerp(n01, n11, f.y);
    float n   = lerp(n0, n1, f.z);

    // Patchy coverage: noise picks which surface areas accumulate frost
    // first, strength scales how much shows up. _FrostMaxCoverage caps the
    // peak lerp so the slime base is still readable through the crystals
    // even at strength=1.
    //   strength=0          → coverage=0 everywhere
    //   strength=1, n=1     → coverage=_FrostMaxCoverage (peak patch)
    //   strength=1, n=0     → coverage=0 (still bare)
    return saturate(_FrostStrength + n - 1.0) * _FrostMaxCoverage;
}

// Frost crystal overlay on top of whatever Quibli already rendered.
half3 ApplyFrost(half3 albedo, float3 osPosRest)
{
    float frost = SampleFrostMask(osPosRest);
    return lerp(albedo, _FrostBaseColor.rgb, frost);
}

#endif
