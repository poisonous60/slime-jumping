// Rock_EngraveInject.hlsl
// Projected text engraving for Quibli Stylized Lit. RockEngraving.cs bakes a TMP label
// into a mask RT and binds it here via MaterialPropertyBlock together with the label's
// rect frame. The mask gradient tilts normalWS before Quibli shading so recess walls
// catch the light ramp (top edge shaded, bottom edge lit), then the covered area is
// darkened as recess-floor AO — the rock's own texture continues into the recess.
// Params are deliberately NOT in the Properties block: material-level values would
// override the property block. Include after LitForwardPass_DR.hlsl; the pass must
// define REQUIRES_WORLD_SPACE_POS_INTERPOLATOR.
#ifndef ROCK_ENGRAVE_INJECT_INCLUDED
#define ROCK_ENGRAVE_INJECT_INCLUDED

TEXTURE2D(_EngraveMask);
SAMPLER(sampler_EngraveMask);
float4x4 _EngraveWorldToRect; // world -> rect frame; xy spans [-0.5, 0.5] over the label rect
float4 _EngraveRightWS;       // rect +x in world
float4 _EngraveUpWS;          // rect +y in world
float4 _EngraveOutwardWS;     // out of the engraved face, toward the viewer
float4 _EngraveMaskTexel;     // xy = 1 / mask resolution
float4 _EngraveParams;        // x carve slope, y floor darken, z projection half-depth
float4 _EngraveShadow;        // x inner-shadow strength, y shadow length in uv

half SampleEngraveMask(float2 uv)
{
    return SAMPLE_TEXTURE2D_LOD(_EngraveMask, sampler_EngraveMask, uv, 0).r;
}

half4 EngraveStylizedFragment(Varyings input) : SV_Target
{
    float3 rectPos = mul(_EngraveWorldToRect, float4(input.positionWS, 1)).xyz;
    // Back/side faces fade out instead of receiving a smeared projection.
    half facing = smoothstep(0.15, 0.5, dot(normalize(input.normalWS), _EngraveOutwardWS.xyz));
    half window = facing
        * step(abs(rectPos.x), 0.5) * step(abs(rectPos.y), 0.5)
        * step(abs(rectPos.z), _EngraveParams.z);

    half h = 0;
    half shadow = 0;
    if (window > 0)
    {
        float2 t = _EngraveMaskTexel.xy;
        float2 uv = clamp(rectPos.xy + 0.5, t, 1 - t);
        h = SampleEngraveMask(uv) * window;
        half gx = SampleEngraveMask(uv + float2(t.x, 0)) - SampleEngraveMask(uv - float2(t.x, 0));
        half gy = SampleEngraveMask(uv + float2(0, t.y)) - SampleEngraveMask(uv - float2(0, t.y));
        float3 tilt = (_EngraveRightWS.xyz * gx + _EngraveUpWS.xyz * gy) * _EngraveParams.x * window;
        input.normalWS = normalize(input.normalWS + tilt);
        // 위 광원 기준 내부 그림자: 위쪽 벽 근처 바닥만 어두워져 깊이가 읽힌다.
        shadow = h * (1 - SampleEngraveMask(uv + float2(0, _EngraveShadow.y))) * _EngraveShadow.x;
    }

    half4 color = StylizedPassFragment(input);
    color.rgb *= 1 - saturate(_EngraveParams.y * h + shadow);
    return color;
}

#endif
