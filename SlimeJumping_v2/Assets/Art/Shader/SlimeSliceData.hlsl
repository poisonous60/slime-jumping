#ifndef SLIME_SLICE_DATA_INCLUDED
#define SLIME_SLICE_DATA_INCLUDED

// SINGLE owner of all slime-slice shader data. Shared by the VERTEX deform stages
// (SlimeDeform.hlsl, reached via SlimeBody_QuibliDeformInject.hlsl which includes THIS as its
// first line, ABOVE the SlimeDeform include) and the FRAGMENT slice (SlimeSlice.hlsl, which
// includes this — guarded, so it is a no-op the second time).
//
// Declared OUTSIDE UnityPerMaterial: Quibli's StylizedInput.hlsl owns that CBUFFER and we must
// not edit third-party includes. The slime is a single renderer, so the SRP-batcher cost is nil.
//
// Default _SliceCount = 0 => every consumer (clip / cap / heal / lip / conform) is a no-op, so
// the slime renders byte-for-byte as before when nothing is slicing.

#define SLICE_MAX 3

float  _SliceCount;                  // active wound count (0 = no-op)
float4 _SlicePlaneOS[SLICE_MAX];     // xyz = cut-plane normal (rest OS, points to REMOVED side), w = offset
float  _SliceHeal[SLICE_MAX];        // per-wound fresh-goo fill amount (0 = none)
float  _SliceSeal[SLICE_MAX];        // per-wound sealRatio (Axis-A): 0 = open raw tear … 1 = sealed lip

float4 _SliceCapColor;               // wet cross-section DEEP color (rgb) — interior of the cut
float4 _SliceCapRimColor;            // wet cross-section RIM color (rgb) — lighter/wet near the opening
float4 _SliceHealColor;              // fresh-goo fill color (lighter slime tone, LDR)
float  _SliceHealWidth;              // OS band width of the fresh-goo fill front
float  _SliceCapDepth;               // OS depth over which the cap shades rim -> deep
float  _SliceCapFresnel;             // reserved (wet-rim fresnel power, used once N/V are threaded)

// Meniscus membrane per wound. x = apex height b along the plane normal (OS units;
//   < 0 = shallow concave dish — the just-cut state, > 0 = welling convex membrane,
//   b == cap height => membrane coincides with the body sphere = fully sealed).
// y = rim radius² rho0² (OS) — the FIXED circle where the cut plane meets the body
//   surface; the membrane is anchored there like a soap film. z/w unused.
float4 _SliceMeniscus[SLICE_MAX];

float  _TearAmp;                     // boundary noise amplitude (0 = smooth curve, default)
float  _TearFreq;                    // boundary noise frequency (keep LOW)

float  _LipBand;                     // OS band width of the rim band (vertex stage 6)
// Per-slot rim displacement (OS, vertex stage 6). NEGATIVE = inward fillet (a cut wound
// rounds its sharp rim corner off), POSITIVE = outward bulge (an absorption merge squeezes
// the contact patch out). Per slot because the two run in the same frame.
float  _SliceLip[SLICE_MAX];

// Motion-driven wobble of the cut boundary. x = amplitude (OS units, 0 = still),
// y = phase (radians), z = spatial frequency (OS rad/unit), w = unused.
// C# drives x from a damped envelope excited by velocity change (launch/impact/landing),
// so at rest and in pure freefall the boundary is PERFECTLY still — no crawling noise.
float4 _SliceWobble[SLICE_MAX];

float4 _CapWetEdgeColor;             // wet meniscus highlight color at the opening edge
float  _CapWetEdgeWidth;             // OS width of the meniscus highlight band (0 = off)
float  _CapMarbleAmp;                // frozen marbling amount bending the cap depth bands (0 = off)
float  _CapMarbleFreq;               // marbling spatial frequency (OS)

// Frozen tear noise + motion wobble, shared by the full SDF and the plane-only distance so
// the clip, the cap and the rim band all ride the same wiggling boundary.
float SliceBoundaryNoise(float3 osPosRest, int i)
{
    float3 n = _SlicePlaneOS[i].xyz;
    float  add = 0.0;

    float tear = _TearAmp * (1.0 - saturate(_SliceSeal[i]));
    if (tear > 1e-4)
    {
        float3 p = osPosRest * _TearFreq + n * 7.13;       // n offsets the sample per wound orientation
        float nz = sin(p.x * 1.7 + p.y * 2.3) * cos(p.y * 1.9 + p.z * 2.1);   // low-freq, frozen, [-1,1]
        // ONE-SIDED: a tear takes flesh away, so the boundary may only eat inward (d grows).
        // A negative lobe would move the boundary to the removed side and leave flesh sticking
        // out past the cut plane.
        add += tear * max(0.0, nz);
    }

    // MOTION-DRIVEN wobble: a low-freq ripple of the boundary along the cut face's two
    // tangents. Amplitude is the C#-side damped envelope (zero at rest => exactly the
    // shape above). Both modes share ONE phase multiplier so the C# phase wrap at 2*PI
    // stays continuous. Lip / cap / heal all consume this same function, so the whole
    // wound (edge, meniscus, fill front) jiggles as one piece of goo.
    float wAmp = _SliceWobble[i].x;
    if (wAmp > 1e-5)
    {
        float3 axis = abs(n.x) < 0.9 ? float3(1, 0, 0) : float3(0, 1, 0);
        float3 t1 = normalize(cross(n, axis));
        float3 t2 = cross(n, t1);
        float u  = dot(osPosRest, t1) * _SliceWobble[i].z;
        float v  = dot(osPosRest, t2) * _SliceWobble[i].z;
        float ph = _SliceWobble[i].y;
        add += wAmp * (0.7 * sin(u + ph) + 0.3 * sin(1.83 * u + 0.9 * v - ph));
    }
    return add;
}

// Distance to the FLAT cut plane only — the meniscus term is deliberately absent, so its
// zero set stays the fixed rim circle for the whole seal. The vertex rim band keys on this:
// the full SDF below flattens to ~0 across the entire cap once the membrane reaches the body
// sphere, which would turn a rim ring into a whole-cap displacement.
float SlicePlaneDist(float3 osPosRest, int i)
{
    float d = dot(osPosRest, _SlicePlaneOS[i].xyz) - _SlicePlaneOS[i].w;
    return d + SliceBoundaryNoise(osPosRest, i);
}

// Signed distance to wound i's cut surface in REST OS. < 0 = kept side, > 0 = removed side.
//   • base: flat plane dot(p,n) - off  (== the freshly-cut face when the meniscus is 0).
//   • MENISCUS membrane (_SliceMeniscus[i].x = b): a soap-film cap anchored at the FIXED rim
//     circle (rho0² in .y). b < 0 dents the face into a shallow concave dish (just cut);
//     b > 0 wells it outward as a spherical membrane — removed = beyond the plane AND outside
//     the membrane, so the wound closes concentrically (rim → apex), never as a sweeping plane.
//     At b = cap height the membrane IS the body sphere: clip area is exactly zero.
//   • FROZEN low-freq noise: a still torn edge (no time term => no crawling), damped by sealRatio
//     (sealed = smoother). Default _TearAmp = 0 => exactly the smooth curve.
float SliceSignedDist(float3 osPosRest, int i)
{
    float3 n   = _SlicePlaneOS[i].xyz;
    float  off = _SlicePlaneOS[i].w;
    float  d = dot(osPosRest, n) - off;

    float b    = _SliceMeniscus[i].x;
    float rho2 = max(_SliceMeniscus[i].y, 1e-4);
    if (b > 1e-4)
    {
        // Spherical membrane through the rim circle with apex height b: radius
        // Rc = (rho0² + b²)/(2b), center on the cut axis at off - (Rc - b).
        float Rc = (rho2 + b * b) / (2.0 * b);
        float3 q = n * (off - (Rc - b));
        d = min(d, length(osPosRest - q) - Rc);   // removed = beyond plane AND outside membrane
    }
    else if (b < -1e-4)
    {
        // Shallow concave dish (paraboloid, deepest on the axis) — the struck, dented face.
        float3 perp = osPosRest - dot(osPosRest, n) * n;
        d -= b * (1.0 - saturate(dot(perp, perp) / rho2));
    }

    return d + SliceBoundaryNoise(osPosRest, i);
}

#endif // SLIME_SLICE_DATA_INCLUDED
