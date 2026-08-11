// Ground_TrailLocalReceiverInject.hlsl
// Object-space trail receiver: samples a per-renderer RT (set via MaterialPropertyBlock)
// keyed on positionOS.xy — so the trail follows the platform's transform (translation,
// rotation, tilt) automatically. The (A) intensity gradient and (B) optional world-position
// gradient are shared globals with the world variant.
// Included by Ground_Quibli_Trail_Local.shader after Quibli's LitForwardPass_DR.hlsl.
#ifndef GROUND_TRAIL_LOCAL_RECEIVER_INJECT_INCLUDED
#define GROUND_TRAIL_LOCAL_RECEIVER_INJECT_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

// Per-renderer (set via MaterialPropertyBlock on each platform)
TEXTURE2D(_LocalTrailMask);     SAMPLER(sampler_LocalTrailMask);
TEXTURE2D(_LocalTrailMaskCold); SAMPLER(sampler_LocalTrailMaskCold);
float4   _LocalTrailMin;
float4   _LocalTrailSize;
// Receiver's world→local matrix. Decoupled from the renderer's unity_WorldToObject so the
// paint frame can live on a separate (e.g. unrotated) GameObject — lets a 3D-tilted mesh
// still receive a world-XY-aligned trail. Identity-safe default via MPB.
float4x4 _LocalTrailW2L;

// Globals (shared with world variant)
TEXTURE2D(_TrailGradient);      SAMPLER(sampler_TrailGradient);
TEXTURE2D(_TrailGradientCold);  SAMPLER(sampler_TrailGradientCold);
TEXTURE2D(_TrailPosGradient);   SAMPLER(sampler_TrailPosGradient);
TEXTURE2D(_TrailAngleGradient); SAMPLER(sampler_TrailAngleGradient);
float4 _TrailPosGradientParams;
float  _TrailPosGradientEnabled;
float  _TrailAngleGradientEnabled;
float  _TrailMaxMask;
float  _TrailZCenter;
float  _TrailZNear;
float  _TrailZFar;

half4 TrailLocalReceiverFragment(Varyings input) : SV_Target
{
    half4 color = StylizedPassFragment(input);

    // Z-plane fade — same semantics as the world variant. For a platform whose body extends
    // in Z, this keeps the trail on the visible face near the gameplay plane.
    float zDist = abs(input.positionWS.z - _TrailZCenter);
    float zFade = 1.0 - saturate((zDist - _TrailZNear) / max(_TrailZFar - _TrailZNear, 0.0001));
    if (zFade < 0.0001) return color;

    // Transform world → receiver-local via the MPB matrix. One mat-vec per fragment.
    // Using the receiver's matrix instead of unity_WorldToObject lets a rotated mesh keep
    // a flat, world-XY paint plane when the receiver lives on an unrotated parent.
    float3 posLS    = mul(_LocalTrailW2L, float4(input.positionWS, 1.0)).xyz;
    float2 uv       = (posLS.xy - _LocalTrailMin.xy) / _LocalTrailSize.xy;
    half maskRaw    = SAMPLE_TEXTURE2D(_LocalTrailMask, sampler_LocalTrailMask, saturate(uv)).r;
    half maskCapped = min(maskRaw, _TrailMaxMask) * zFade;
    if (maskCapped < 0.0001) return color;

    // (A) Intensity gradient — same as world variant.
    half4 grad      = SAMPLE_TEXTURE2D(_TrailGradient, sampler_TrailGradient, float2(maskRaw, 0.5));
    half3 trailRgb  = grad.rgb;
    half  trailAlpha = grad.a;

    // (B) Position gradient is sampled by world XY (not object space) so stripes stay
    // world-aligned even as platforms rotate/move — matches Quibli's height-gradient feel.
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

    // (C) Angle gradient by world-space surface slope. Normal is world-space so this reacts
    // to the platform's actual current orientation (rotating/tilting platforms recolor live).
    if (_TrailAngleGradientEnabled > 0.5)
    {
        float3 n     = normalize(input.normalWS);
        float  tAng  = saturate((1.0 - n.y) * 0.5);
        half4  aGrad = SAMPLE_TEXTURE2D(_TrailAngleGradient, sampler_TrailAngleGradient, float2(tAng, 0.5));
        trailRgb   *= aGrad.rgb;
        trailAlpha *= aGrad.a;
    }

    // Trail color is the literal gradient sample — see Ground_TrailReceiverInject.hlsl.
    color.rgb = lerp(color.rgb, trailRgb, maskCapped * trailAlpha);

    // (D) Cold layer — alpha-over over warm. Same split routing as the world
    // variant: stamps split between _LocalTrailMask and _LocalTrailMaskCold
    // by SlimeFrostController.FreezeT, so a frozen slime's trail draws on top
    // of warm trail it walks back through. Luma preserved against the
    // post-warm color so cold tint stays in the same shading frame.
    half maskRawCold    = SAMPLE_TEXTURE2D(_LocalTrailMaskCold, sampler_LocalTrailMaskCold, saturate(uv)).r;
    half maskCappedCold = min(maskRawCold, _TrailMaxMask) * zFade;
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

        color.rgb = lerp(color.rgb, trailRgbCold, maskCappedCold * trailAlphaCold);
    }

    return color;
}
#endif
