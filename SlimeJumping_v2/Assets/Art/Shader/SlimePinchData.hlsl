#ifndef SLIME_PINCH_DATA_INCLUDED
#define SLIME_PINCH_DATA_INCLUDED

// Pinch contact data for the GENERALIZED squeeze deform (SlimeDeform.hlsl section 3.5).
// One entry per closing wall the slime is pressed against — works for a vertical sandwich
// (2 opposite contacts), an X / scissor press (2 angled contacts), or a 3-side pinch.
//
// Declared OUTSIDE UnityPerMaterial (same reason + same single-renderer cost-is-nil argument as
// SlimeSliceData.hlsl): Quibli's StylizedInput.hlsl owns that CBUFFER. Pushed each frame by
// SlimeVisual.cs via MaterialPropertyBlock.SetFloat / SetVectorArray / SetFloatArray.
//
// Default _PinchCount = 0 => the deform section is a byte-for-byte no-op (slime renders as before).

#define PINCH_MAX 3

float  _PinchCount;                 // active contact count (0 = no-op)
float4 _PinchNormalWS[PINCH_MAX];   // xyz = unit face normal (WORLD), pointing FROM the wall TOWARD the slime
float  _PinchComp[PINCH_MAX];       // per-contact compression 0..1 (= how far that wall has squashed in)
float  _PinchHalfGap[PINCH_MAX];    // per-contact WORLD half-gap (gap-center → that wall's face plane).
                                    // The deform plane-clamps every vertex past it back onto the plane.
float4 _PinchCenterWS;              // WORLD gap-center (midpoint between the plates). Clamp planes sit at
                                    // _PinchCenterWS ± halfGap along each wall normal — NOT the mesh origin,
                                    // which drifts off-center via the visual offset pipeline.
// Pinch's OWN displacement scale, pushed per-frame by SlimeVisual. Decoupled from the material's
// global _VertexDisplaceScale (which is 0 on Mat_SlimeBody3D_Quibli — the whole-mesh deform is
// intentionally off, squash is done via body.localScale). This lets the pinch dent render WITHOUT
// globally re-enabling the surface/charge/velocity mesh deforms.
float  _PinchDisplaceScale;

#endif // SLIME_PINCH_DATA_INCLUDED
