// Ground_SnowCapInject.hlsl
// Snow-cap wrapper around Quibli's StylizedPassFragment: surfaces facing world
// up blend toward _SnowCapColor by normal Y (smoothstep from _SnowCapThreshold
// over _SnowCapSoftness). The trailing #define redirects every later call to
// StylizedPassFragment through this wrapper, so Ground_TrailReceiverInject.hlsl
// composites its goo on top of the snowed base without any code duplication.
// Include order: after LitForwardPass_DR.hlsl, before Ground_TrailReceiverInject.hlsl.
#ifndef GROUND_SNOW_CAP_INJECT_INCLUDED
#define GROUND_SNOW_CAP_INJECT_INCLUDED

half4 _SnowCapColor;
float _SnowCapAmount;
float _SnowCapThreshold;
float _SnowCapSoftness;

half4 SnowCapStylizedFragment(Varyings input)
{
    half4 color = StylizedPassFragment(input);
#if defined(_SNOW_CAP)
    float3 n = normalize(input.normalWS);
    half cover = smoothstep(_SnowCapThreshold, saturate(_SnowCapThreshold + _SnowCapSoftness), n.y);
    color.rgb = lerp(color.rgb, _SnowCapColor.rgb, cover * _SnowCapAmount * _SnowCapColor.a);
#endif
    return color;
}

#define StylizedPassFragment SnowCapStylizedFragment

#endif
