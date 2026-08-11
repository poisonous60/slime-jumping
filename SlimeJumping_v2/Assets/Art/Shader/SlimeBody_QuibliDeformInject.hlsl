#ifndef SLIME_BODY_QUIBLI_DEFORM_INJECT_INCLUDED
#define SLIME_BODY_QUIBLI_DEFORM_INJECT_INCLUDED

// Injects the SlimeBody3D 5-stage vertex deformation (SlimeDeform.hlsl) into
// any Quibli StylizedLit pass whose Attributes struct exposes positionOS and
// normalOS. SlimeVisual.cs pushes all input parameters per-frame via
// MaterialPropertyBlock — see SlimeVisual.cs:75-360.

// Slice DATA first (sole owner of _Slice* decls + SliceSignedDist). Hoisted ABOVE SlimeDeform
// so the slice vertex stages (lip/conform) resolve their symbols, and so EVERY pass that calls
// SLIME_APPLY_DEFORM — including ShadowCaster, which never includes SlimeSlice.hlsl — still
// compiles. Guarded, so the second include from SlimeSlice.hlsl is a no-op.
#include "SlimeSliceData.hlsl"

// Pinch contact DATA (sole owner of _Pinch* decls). Hoisted ABOVE SlimeDeform so its squeeze
// deform section resolves the symbols. Guarded; default _PinchCount = 0 => no-op.
#include "SlimePinchData.hlsl"

#include "SlimeDeform.hlsl"

// Declared outside UnityPerMaterial because Quibli's StylizedInput.hlsl owns
// that CBUFFER and we must not modify third-party includes. Slime is a single
// renderer so the SRP-batcher hit is zero in practice.
float  _Asymmetry;
float  _ChargeDip;
float4 _SurfaceNormalWS;
float4 _DipDirWS;
float4 _VelocityDirWS;
float  _VertexDisplaceScale;
float  _VolumePreservation;
float  _VelocityStretchAmount;
float  _VelocityFrontCompress;

#define SLIME_APPLY_DEFORM(input)                                              \
    {                                                                           \
        float3 _slimeDeformedPos;                                               \
        float3 _slimeDeformedNrm;                                               \
        SlimeDeform_float(                                                      \
            (input).positionOS.xyz,                                             \
            (input).normalOS,                                                   \
            _SurfaceNormalWS.xyz,                                               \
            _DipDirWS.xyz,                                                      \
            _VelocityDirWS,                                                     \
            _Asymmetry,                                                         \
            _ChargeDip,                                                         \
            _VertexDisplaceScale,                                               \
            _VolumePreservation,                                                \
            _VelocityStretchAmount,                                             \
            _VelocityFrontCompress,                                             \
            _slimeDeformedPos,                                                  \
            _slimeDeformedNrm);                                                 \
        (input).positionOS.xyz = _slimeDeformedPos;                             \
        (input).normalOS       = _slimeDeformedNrm;                             \
    }

#endif // SLIME_BODY_QUIBLI_DEFORM_INJECT_INCLUDED
