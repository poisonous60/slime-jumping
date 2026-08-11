#ifndef SLIME_SLICE_INCLUDED
#define SLIME_SLICE_INCLUDED

// Slice / wound rendering for the slime body (FRAGMENT side).
//
// A blade slice removes a wedge from the slime. We render this WITHOUT cutting geometry: clip
// the removed side in REST-POSE OBJECT SPACE (the same osPosRest the burn/frost systems capture
// before SlimeDeform runs), so the wound rides the body through squash/stretch/rotation exactly
// like burn marks. The cut BOUNDARY is no longer a straight machined plane — SliceSignedDist
// (SlimeSliceData.hlsl) bows it into a rounded face (scaled by sealRatio) plus optional frozen
// low-freq noise, so the opening reads as gooey, not sawn.
//
// The opening is filled with a WET, depth-shaded cross-section (rim = lighter/wet, deep = dark),
// drawn on the body's BACK faces (the ForwardLit pass renders Cull Off while slicing), cel-
// quantized to keep the toon look — instead of the old flat dark slab.
//
// GROUNDING: the cut is a real SHAPE change — there is no foot guard, the removed band is fully
// clipped. Float / catch / phantom-stick are prevented by the COLLISION side: SlimeSplitSequence
// feeds the surface probe the cut shape's inscribed circle, so the slime physically rests on its
// cut edge and the removed region carries no collider. The visual just renders the cut.
//
// All data + SliceSignedDist live in SlimeSliceData.hlsl (the sole owner). Default _SliceCount = 0
// => every function below is a no-op, so the slime renders byte-for-byte as before when not slicing.

#include "SlimeSliceData.hlsl"

// Discards removed-side fragments for ANY active wound. Pass rest-pose OS position.
void SliceClip(float3 osPosRest)
{
    int n = (int)(_SliceCount + 0.5);
    if (n <= 0) return;
    [unroll]
    for (int i = 0; i < SLICE_MAX; i++)
    {
        if (i >= n) break;
        clip(-SliceSignedDist(osPosRest, i));    // d > 0 (removed side) -> discard
    }
}

// Replaces surface color with a WET, depth-shaded cross-section on BACK faces while slicing
// (front-face shading is untouched). The cross-section shades from a lighter/wet RIM color at
// the opening edge to a dark DEEP color into the wound, cel-quantized — reads as gooey interior,
// not a flat dark wall. isFrontFace: 1 = front, 0 = back.
half3 SliceCap(half3 col, float isFrontFace, float3 osPosRest)
{
    if (_SliceCount < 0.5 || isFrontFace > 0.5) return col;

    // Distance to the nearest cut surface => how deep into the wound this back-face sits.
    float dmin = 1e9;
    int n = (int)(_SliceCount + 0.5);
    [unroll]
    for (int i = 0; i < SLICE_MAX; i++)
    {
        if (i >= n) break;
        dmin = min(dmin, abs(SliceSignedDist(osPosRest, i)));
    }

    float t = saturate(dmin / max(1e-4, _SliceCapDepth));

    // Frozen marbling: bend the rim->deep depth bands so the interior reads as swirled
    // goo rather than machined contour lines. No time term — perfectly still unless the
    // boundary itself moves (wobble shifts dmin, which re-swirls the bands organically).
    if (_CapMarbleAmp > 1e-4)
    {
        float3 q = osPosRest * _CapMarbleFreq;
        t += _CapMarbleAmp * sin(q.x * 1.3 + q.y * 2.1) * cos(q.y * 1.7 + q.z * 1.1);
        t = saturate(t);
    }
    t = floor(t * 3.0 + 0.5) / 3.0;                          // cel: 3 bands
    half3 capCol = lerp(_SliceCapRimColor.rgb, _SliceCapColor.rgb, t);   // edge = wet rim, deep = dark

    // Wet meniscus line: a thin bright highlight hugging the opening edge — the strongest
    // "wet living goo, not sawn flesh" cue. dmin comes from the same SliceSignedDist as the
    // clip and the lip, so the highlight is welded to the (possibly wobbling) boundary.
    if (_CapWetEdgeWidth > 1e-4)
    {
        float wet = 1.0 - smoothstep(_CapWetEdgeWidth * 0.6, _CapWetEdgeWidth, dmin);
        capCol = lerp(capCol, _CapWetEdgeColor.rgb, wet);
    }
    return capCol;
}

// Fresh-goo fill: tints the kept-side band at each active wound's closing edge toward a lighter
// slime-goo color, settling to the body color behind. The clip edge recedes as the wound fills
// (Axis-B healVisual drives the offset), so the band rises across the opening — slime goo welling
// up. Lerp (not additive, no glow). _SliceHeal[i] ramps 0->1 over recovery.
half3 SliceHealFill(half3 col, float3 osPosRest)
{
    int n = (int)(_SliceCount + 0.5);
    if (n <= 0) return col;
    float w = max(1e-4, _SliceHealWidth);
    float fill = 0.0;
    [unroll]
    for (int i = 0; i < SLICE_MAX; i++)
    {
        if (i >= n) break;
        float d = SliceSignedDist(osPosRest, i);   // < 0 kept side
        float b = saturate(1.0 + d / w);           // 0 at d=-w, 1 at the closing edge (d=0)
        b = b * b;                                  // concentrate the wet front at the edge
        fill = max(fill, _SliceHeal[i] * b);
    }
    fill = saturate(fill);
    return lerp(col, _SliceHealColor.rgb, fill);
}

#endif // SLIME_SLICE_INCLUDED
