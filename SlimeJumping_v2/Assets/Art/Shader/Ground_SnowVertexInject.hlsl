// Ground_SnowVertexInject.hlsl
// Vertex-stage snow displacement for Ground_Quibli_Snow.shader. Wraps Quibli's
// LitPassVertex so positionOS picks up per-vertex height *before* Quibli derives
// world-space position, normal, view dir, lighting, etc. Also computes the
// per-vertex carve mask (1 - heightCurrent/heightRest) and stuffs it into
// SnowVaryings.snowExtras.x for the fragment stage.
//
// Mesh contract (set by SnowMeshBuilder):
//   - positionOS    : un-displaced patch vertex
//   - tangentOS.xyz : displacement direction (= patch vertex normal for "top" verts,
//                     = 0 for "base" verts that should stay glued to the host surface)
//   - texcoord      : (patch.verts.x, patch.verts.y) — drives surface noise sample so
//                     the noise pattern is fixed in host-local space
//   - TEXCOORD1.x   : raw vertex index (float). Cast to uint to index _SnowHeightBuf.
//   - TEXCOORD1.y   : heightRest for this vertex (m). Read via input.staticLightmapUV
//                     because Quibli's Attributes maps that field to TEXCOORD1;
//                     SnowField never lightmaps so the lightmap path stays inert.
//
// _SnowHeightBuf is a StructuredBuffer<float> bound per-renderer via MPB.SetBuffer.
// Replaces the previous V×1 RFloat Texture2D path so patches whose vertex count
// exceeds GPU max texture width (~16384) still upload cleanly. Requires shader
// model 4.5 (set by #pragma target 4.5 on the ForwardLit pass).
#ifndef GROUND_SNOW_VERTEX_INJECT_INCLUDED
#define GROUND_SNOW_VERTEX_INJECT_INCLUDED

StructuredBuffer<float> _SnowHeightBuf;
TEXTURE2D(_SnowSurfaceNoise); SAMPLER(sampler_SnowSurfaceNoise);   // Bilinear Repeat
float _SnowHeightScale;
float _SnowNoiseAmplitude;
float _SnowNoiseTiling;

SnowVaryings SnowVertex(Attributes input)
{
    UNITY_SETUP_INSTANCE_ID(input);

    // 1. Per-vertex current height. Buffer index lives in TEXCOORD1.x as a raw
    //    float; round-to-nearest then cast to uint to defend against any tiny
    //    interpolator drift introduced by the rasterizer (vertex stage is exact
    //    in practice, but the cast is free).
    uint vid = (uint)(input.staticLightmapUV.x + 0.5);
    float h = _SnowHeightBuf[vid];

    // 2. Procedural noise multiply. Same height value gets a small per-vertex
    //    variation so the carved/uncarved snow surface looks granular instead of a
    //    perfectly smooth shell. Sampling by texcoord (host-local xy) keeps the
    //    pattern locked to the platform — moving the platform slides the noise too.
    if (_SnowNoiseAmplitude > 0.0)
    {
        float n = SAMPLE_TEXTURE2D_LOD(_SnowSurfaceNoise, sampler_SnowSurfaceNoise,
                                       input.texcoord * _SnowNoiseTiling, 0).r;
        h *= 1.0 - _SnowNoiseAmplitude * (1.0 - n);
    }

    // 3. Displace along the per-vertex tangent direction (patch vertex normal). For
    //    "base" verts the mesh writes tangent.xyz = 0 so this term is zero and the
    //    vertex stays anchored on the host surface; for "top" verts the strip rides
    //    heightCurrent up the surface normal.
    input.positionOS.xyz += input.tangentOS.xyz * (h * _SnowHeightScale);

    // 4. Carve mask = 1 - heightCurrent/heightRest. heightRest baked into uv2.y
    //    by SnowMeshBuilder; never mutates after build (Phase 3 contract).
    //    "base" verts have heightRest=0 by construction (boundary taper) — guard
    //    against divide-by-zero so they get mask=0 instead of NaN.
    float heightRest = input.staticLightmapUV.y;
    float mask = saturate(1.0 - h / max(heightRest, 1e-4));

    SnowVaryings o;
    o.v = LitPassVertex(input);
    o.snowExtras = float2(mask, 0.0);
    return o;
}

#endif
