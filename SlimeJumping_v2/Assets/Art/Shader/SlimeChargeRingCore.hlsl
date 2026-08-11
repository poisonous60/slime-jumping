#ifndef SLIME_CHARGE_RING_CORE_INCLUDED
#define SLIME_CHARGE_RING_CORE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

struct RingAttributes
{
    float4 positionOS : POSITION;
    float2 uv         : TEXCOORD0;
};

struct RingVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv         : TEXCOORD0;
};

CBUFFER_START(UnityPerMaterial)
    float  _Thickness;
    float  _FillRatio;
    float  _FullAimTick;
    float  _Alpha;
    float  _OccludedAlpha;
    float  _EdgeSoftness;
    float  _PosterizeSteps;
    float4 _BaseColor;
    float4 _FullAimColor;
CBUFFER_END

RingVaryings RingVert(RingAttributes IN)
{
    RingVaryings OUT;
    OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
    OUT.uv = IN.uv;
    return OUT;
}

// Shared fragment core. alphaScalar lets the main pass pass _Alpha and the
// silhouette pass pass _Alpha * _OccludedAlpha.
half4 RingFragCore(float2 uv, float alphaScalar)
{
    float2 p = uv - 0.5;
    float d = length(p) * 2.0;

    float th = max(_Thickness, 1e-4);
    float e = _EdgeSoftness;

    // Soft-edged annulus. Inner edge fades in, outer edge fades out.
    float inner = smoothstep(0.5 - th - e, 0.5 - th + e, d);
    float outer = smoothstep(0.5 - e, 0.5 + e, d);
    float ring = saturate(inner - outer);

    // Posterize the tick term (NOT the fill ratio — avoids WASD pull-in flicker).
    float steps = max(_PosterizeSteps, 2.0);
    float tick = floor(saturate(_FullAimTick) * steps) / steps;

    float3 rgb = lerp(_BaseColor.rgb, _FullAimColor.rgb, tick);
    float colA = lerp(_BaseColor.a, _FullAimColor.a, tick);

    // Brightness = ChargingProgress. Whole ring fades in gradually; no angular
    // sweep. tick holds ring visible through the grace window even if _FillRatio
    // dips momentarily at the edge.
    float brightness = max(saturate(_FillRatio), tick);

    float finalA = ring * colA * alphaScalar * brightness;
    return half4(rgb, finalA);
}

#endif
