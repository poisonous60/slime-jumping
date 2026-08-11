// Ground_TrailReceiverInject.hlsl
// World-space trail receiver: samples the currently bound trail region's RT keyed on positionWS.xy
// (WorldTrailManager binds the region holding the slime), then runs the result through (A) an
// intensity-driven gradient and optionally (B) a world-position gradient, before tinting on top
// of Quibli's lit color. Included by Ground_Quibli_Trail.shader after Quibli's LitForwardPass_DR.hlsl.
#ifndef GROUND_TRAIL_RECEIVER_INJECT_INCLUDED
#define GROUND_TRAIL_RECEIVER_INJECT_INCLUDED

TEXTURE2D(_TrailMask);          SAMPLER(sampler_TrailMask);
TEXTURE2D(_TrailGradient);      SAMPLER(sampler_TrailGradient);      // (A) by mask intensity
TEXTURE2D(_TrailPosGradient);   SAMPLER(sampler_TrailPosGradient);   // (B) by world position
TEXTURE2D(_TrailAngleGradient); SAMPLER(sampler_TrailAngleGradient); // (C) by surface slope
TEXTURE2D(_TrailMaskCold);      SAMPLER(sampler_TrailMaskCold);      // (D) cold-route RT
TEXTURE2D(_TrailGradientCold);  SAMPLER(sampler_TrailGradientCold);  // (D) cold-route gradient
float4 _TrailWorldMin;          // xy = bound region origin
float4 _TrailWorldSize;         // xy = bound region extent (0 = no region bound)
float4 _TrailPosGradientParams; // xy = center, z = size, w = angle (degrees)
float  _TrailPosGradientEnabled;
float  _TrailAngleGradientEnabled;
float  _TrailMaxMask;           // visual saturation cap (0..1) on blend strength only
float  _TrailZCenter;           // gameplay Z plane (usually 0)
float  _TrailZNear;             // |z - center| <= near → full trail visible
float  _TrailZFar;              // |z - center| >= far  → trail fully faded (no tint on background)

// Hard-rejects outside the rect instead of clamping: regions are adjacent, so a clamped sample
// would smear one region's edge row across every neighbouring region's ground.
half SampleTrailRegion(TEXTURE2D_PARAM(maskTex, maskSampler), float2 uv)
{
    if (any(uv < 0.0) || any(uv > 1.0)) return 0.0h;
    return SAMPLE_TEXTURE2D(maskTex, maskSampler, uv).r;
}

half4 TrailReceiverFragment(Varyings input) : SV_Target
{
    half4 color = StylizedPassFragment(input);

    // Guard: WorldTrailManager publishes the bound region's size > 0. Zero means no manager in
    // scene, no region configured, or Edit Mode after Play exit with stale globals.
    if (_TrailWorldSize.x < 0.0001 || _TrailWorldSize.y < 0.0001) return color;

    // Z-plane fade: trail only marks geometry near the gameplay plane.
    float zDist = abs(input.positionWS.z - _TrailZCenter);
    float zFade = 1.0 - saturate((zDist - _TrailZNear) / max(_TrailZFar - _TrailZNear, 0.0001));
    if (zFade < 0.0001) return color;

    float2 uv = (input.positionWS.xy - _TrailWorldMin.xy) / _TrailWorldSize.xy;
    half maskRaw    = SampleTrailRegion(TEXTURE2D_ARGS(_TrailMask, sampler_TrailMask), uv);
    half maskCapped = min(maskRaw, _TrailMaxMask) * zFade;

    // Cold mask is read up front: a fully frozen slime writes cold only, so an early-out on the
    // warm mask alone would make its whole trail invisible.
    half maskRawCold    = SampleTrailRegion(TEXTURE2D_ARGS(_TrailMaskCold, sampler_TrailMaskCold), uv);
    half maskCappedCold = min(maskRawCold, _TrailMaxMask) * zFade;

    if (maskCapped < 0.0001 && maskCappedCold < 0.0001) return color;

    if (maskCapped >= 0.0001)
    {
        // (A) Intensity gradient. Use the *raw* (uncapped) mask for ramp position so all stops
        // remain reachable — _TrailMaxMask only limits how strongly we blend, not which color.
        half4 grad      = SAMPLE_TEXTURE2D(_TrailGradient, sampler_TrailGradient, float2(maskRaw, 0.5));
        half3 trailRgb  = grad.rgb;
        half  trailAlpha = grad.a;

        // (B) Optional position gradient — same math as Quibli's DR_GRADIENT_ON: project worldXY
        // onto a rotated axis, normalize by size, recenter at 0.5. Multiplies the (A) tint.
        if (_TrailPosGradientEnabled > 0.5)
        {
            float angleRad = _TrailPosGradientParams.w * (3.14159265 / 180.0);
            float2 dir     = float2(sin(angleRad), cos(angleRad));
            float2 rel     = input.positionWS.xy - _TrailPosGradientParams.xy;
            float  t       = saturate(dot(rel, dir) / _TrailPosGradientParams.z + 0.5);
            half4  posGrad = SAMPLE_TEXTURE2D(_TrailPosGradient, sampler_TrailPosGradient, float2(t, 0.5));
            trailRgb   *= posGrad.rgb;
            trailAlpha *= posGrad.a;
        }

        // (C) Optional angle gradient — samples by the surface's slope relative to world up.
        // t=0 at a perfectly horizontal floor (normal = +Y), t=0.5 at a vertical wall, t=1 at
        // an overhang/ceiling (normal = -Y). Multiplies the current tint, layering with (A)+(B).
        if (_TrailAngleGradientEnabled > 0.5)
        {
            float3 n     = normalize(input.normalWS);
            float  tAng  = saturate((1.0 - n.y) * 0.5);
            half4  aGrad = SAMPLE_TEXTURE2D(_TrailAngleGradient, sampler_TrailAngleGradient, float2(tAng, 0.5));
            trailRgb   *= aGrad.rgb;
            trailAlpha *= aGrad.a;
        }

        // Trail color is the literal gradient sample — what the author picks
        // in the inspector is what shows on the ground (alpha-blended over base).
        // The previous luma-preserve path multiplied by ground luminance which
        // made authored colors read as "tint × ground" rather than "trail color".
        color.rgb = lerp(color.rgb, trailRgb, maskCapped * trailAlpha);
    }

    // (D) Cold layer — alpha-over composition. Stamps split between warm
    // and cold by SlimeFrostController.FreezeT (single-slime stage), so a
    // freezing slime's trail draws on top of any warm trail it walks back
    // through. PosGrad/AngleGrad reused with the same multipliers as warm
    // for visual consistency; luma-preserve sampled from the post-warm
    // color so cold tint sits in the same shading frame.
    if (maskCappedCold >= 0.0001)
    {
        half4 gradCold      = SAMPLE_TEXTURE2D(_TrailGradientCold, sampler_TrailGradientCold, float2(maskRawCold, 0.5));
        half3 trailRgbCold  = gradCold.rgb;
        half  trailAlphaCold = gradCold.a;

        if (_TrailPosGradientEnabled > 0.5)
        {
            float angleRad = _TrailPosGradientParams.w * (3.14159265 / 180.0);
            float2 dir     = float2(sin(angleRad), cos(angleRad));
            float2 rel     = input.positionWS.xy - _TrailPosGradientParams.xy;
            float  t       = saturate(dot(rel, dir) / _TrailPosGradientParams.z + 0.5);
            half4  posGrad = SAMPLE_TEXTURE2D(_TrailPosGradient, sampler_TrailPosGradient, float2(t, 0.5));
            trailRgbCold   *= posGrad.rgb;
            trailAlphaCold *= posGrad.a;
        }
        if (_TrailAngleGradientEnabled > 0.5)
        {
            float3 n     = normalize(input.normalWS);
            float  tAng  = saturate((1.0 - n.y) * 0.5);
            half4  aGrad = SAMPLE_TEXTURE2D(_TrailAngleGradient, sampler_TrailAngleGradient, float2(tAng, 0.5));
            trailRgbCold   *= aGrad.rgb;
            trailAlphaCold *= aGrad.a;
        }

        // Cold layer also uses literal gradient color (no luma multiply).
        color.rgb = lerp(color.rgb, trailRgbCold, maskCappedCold * trailAlphaCold);
    }

    return color;
}
#endif
