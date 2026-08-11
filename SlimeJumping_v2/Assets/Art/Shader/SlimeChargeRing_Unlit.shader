// SlimeChargeRing_Unlit — floor-ellipse arc ring shown during the slime's
// Charging state. Two passes: a visible pass (ZTest LEqual) and an occluded
// silhouette (ZTest Greater), mirroring SlimeBody3D's occluded-silhouette
// technique so the ring reads through terrain at reduced alpha.
//
// Shape/progress math lives in SlimeChargeRingCore.hlsl and is shared across
// both passes. The C# driver (SlimeChargeRingVisual.cs) writes _FillRatio,
// _FullAimTick, _Thickness (pre-converted to UV units), _Alpha, etc. via
// MaterialPropertyBlock every frame while charging.

Shader "SlimeJumping/SlimeChargeRing_Unlit"
{
    Properties
    {
        [Header(Shape)]
        _Thickness     ("Thickness (UV)", Range(0, 0.5)) = 0.06
        _EdgeSoftness  ("Edge Softness",  Range(0, 0.05)) = 0.015
        [IntRange] _PosterizeSteps ("Posterize Steps (tint)", Range(2, 8)) = 2

        [Space(10)][Header(Progress)]
        _FillRatio   ("Fill Ratio (MPB)",    Range(0, 1)) = 0
        _FullAimTick ("Full Aim Tick (MPB)", Range(0, 1)) = 0
        _Alpha       ("Alpha (MPB)",         Range(0, 1)) = 1

        [Space(10)][Header(Colors)]
        [HDR] _BaseColor    ("Base Color",     Color) = (0.78, 0.82, 0.9, 0.4)
        [HDR] _FullAimColor ("Full Aim Color", Color) = (1.0, 0.88, 0.7, 0.85)

        [Space(10)][Header(Occlusion Silhouette)]
        _OccludedAlpha ("Occluded Alpha", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 100

        // Pass 1 — visible: draws only where ring is in front of scene geometry.
        Pass
        {
            Name "RingVisible"
            Tags { "LightMode" = "UniversalForward" }

            ZTest LEqual
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex RingVert
            #pragma fragment FragVisible

            #include "SlimeChargeRingCore.hlsl"

            half4 FragVisible(RingVaryings IN) : SV_Target
            {
                return RingFragCore(IN.uv, _Alpha);
            }
            ENDHLSL
        }

        // Pass 2 — occluded silhouette: draws only where scene geometry is in
        // front of the ring (ZTest Greater). Different LightMode from main so
        // URP's DrawObjectsPass iterates both tagIds without batching them.
        Pass
        {
            Name "RingOccludedSilhouette"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            ZTest Greater
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex RingVert
            #pragma fragment FragOccluded

            #include "SlimeChargeRingCore.hlsl"

            half4 FragOccluded(RingVaryings IN) : SV_Target
            {
                return RingFragCore(IN.uv, _Alpha * _OccludedAlpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
