#ifndef SLIME_BURN_INCLUDED
#define SLIME_BURN_INCLUDED

// Burn decals — OS-rest soft discs sampled in body shader passes that want
// scorch marks to render on the slime surface (ForwardLit, OccludedSilhouette).
// Driven by SlimeBurnVisual.cs via MaterialPropertyBlock.
//
// Anchoring rule: the body's vertex stage must capture IN.positionOS into a
// varying BEFORE calling SlimeDeform_float, so the rasterizer interpolates
// rest-pose OS across the deformed triangle — that's what welds each burn
// disc to the surface patch it was placed on, regardless of squash/stretch/
// charge-dip/asymmetric-squash/velocity-stretch.
//
// Declared OUTSIDE UnityPerMaterial in the calling pass — uniform arrays in
// that CBUFFER break the SRP batcher. One slime instance, so the lost batch
// is irrelevant.

float4 _Burns[24];        // xyz = rest-pose OS hit center, w = effective radius (premultiplied by per-burn fade in C#)
int    _BurnCount;
float  _BurnStrength;

float SampleBurn(float3 osPosRest)
{
    float burn = 0;
    [loop] for (int i = 0; i < _BurnCount; i++)
    {
        float r = _Burns[i].w;
        float d = distance(osPosRest, _Burns[i].xyz);
        burn = max(burn, 1.0 - smoothstep(r * 0.65, r, d));
    }
    return saturate(burn * _BurnStrength);
}

// --- Hit flash (independent channel, additive only) -----------------------
// Driven by SlimeBurnVisual.cs MPB push. _HitFlashStrength is decayed in C#
// before each push; shader just consumes it. When the C# driver is disabled
// (enableHitFlash=false) it pushes 0 every frame — and unset uniforms also
// read as 0 — so the flash lerp in calling passes is a strict no-op.
// Reads only its own uniforms — the burn computation above is not affected.
float  _HitFlashStrength;
float4 _HitFlashColor;

float SampleHitFlash()
{
    return saturate(_HitFlashStrength);
}

#endif
