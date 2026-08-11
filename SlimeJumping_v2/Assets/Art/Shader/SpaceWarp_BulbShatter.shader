// SpaceWarp_BulbShatter — v2 of the JumpPad stretch warp.
// Two configurable axes to iterate on:
//   _ShapeMode  0=Bulb-on-Neck, 1=Inverted Teardrop, 2=Dumbbell
//   _CrackMode  0=Voronoi shards, 1=Radial wedges
//
// Driver: SpaceWarpEffect.cs writes _PopPhase, _PulsePhase, _PulseStrength,
// _BulbCenter (live = (anchor→slime length) / totalLen). The slime sits at
// u=_BulbCenter; the tip extends past it by tipLead. Constant-width quad
// (set baseWidth = flareScale*tipWidth in the driver so localScale.y stays
// fixed) — shape carving happens entirely in the fragment.
//
// Pop pipeline (driven by _PopPhase 0..1):
//   0.00 .. 0.10  —  white edge flash on the membrane outline
//   0.05 .. 0.40  —  crack lines glow brightly along cell/wedge boundaries
//   0.10 .. 1.00  —  per-cell shard refraction throw + per-cell alpha cut
//                    (each cell shrinks toward its center on its own offset
//                     timer, so cells don't disappear in lockstep)

Shader "SlimeJumping/SpaceWarp_BulbShatter"
{
    Properties
    {
        [Header(Tint)]
        [HDR] _BaseColor       ("Tint (RGB) + Alpha",        Color) = (0.7, 0.85, 1.0, 1.0)
        _TintStrength          ("Tint Strength",             Range(0, 1))   = 0.04

        [Space(8)][Header(Shape Mode)]
        [Enum(BulbOnNeck,0,InvertedTeardrop,1,Dumbbell,2,Horn,3)] _ShapeMode ("Shape Mode", Float) = 3
        _NeckWidth             ("Neck Width (v half-units, full-stretch)", Range(0.02, 0.95)) = 0.45
        _NeckEnd               ("Neck End (u)",                   Range(0.1, 0.95)) = 0.55
        _BulbCenter            ("Bulb Center (u, driver-driven)", Range(0.5, 1.0))  = 0.85
        _BulbRadius            ("Bulb Radius (v half-units)",     Range(0.1, 0.95)) = 0.65
        _BulbAspect            ("Bulb Aspect (x/y)",              Range(0.5, 2.0))  = 1.0
        _AnchorBulbRadius      ("Anchor Bulb Radius (mode 2)",    Range(0.05, 0.6)) = 0.18
        _AnchorBulbAspect      ("Anchor Bulb Aspect (mode 2)",    Range(0.5, 2.0))  = 1.0
        // Driver-driven (MPB). _AnchorScale animates 0.3..1.0 across stretch
        // so the flat back widens with stretch progress; _QuadAspect = quadLen
        // / (quadHeight*0.5) lets the bulb cap render as a true WORLD circle
        // even when the quad is much longer than tall.
        _AnchorScale           ("Anchor Scale (MPB)",             Range(0.0, 1.0)) = 1.0
        _QuadAspect            ("Quad Aspect (MPB)",              Range(0.5, 12.0)) = 1.0

        [Space(8)][Header(Persistent Warp)]
        _PersistStrength       ("Noise Strength",            Range(0, 0.3))  = 0.0
        _PersistScale          ("Noise Scale",               Range(0.1, 16)) = 3.0
        _PersistScroll         ("Noise Scroll (uv/s)",       Vector) = (0.08, 0.05, 0, 0)
        _DomainWarp            ("Domain Warp Iteration",     Range(0, 4))   = 1.6
        _SwirlAmount           ("Swirl Strength (radians)",  Range(-3.14, 3.14)) = 0.6
        _SwirlAnim             ("Swirl Anim Speed",          Range(0, 4))   = 0.4

        [Space(8)][Header(Shockwave Pulse driven via MPB)]
        _PulsePhase            ("Pulse Phase 0..1",          Range(0, 1))   = 0
        _PulseStrength         ("Pulse Strength",            Range(0, 0.4)) = 0.15
        _PulseRippleWidth      ("Ripple Band Width",         Range(0.01, 0.6)) = 0.18
        _PulseMaxRadius        ("Ripple Max Radius",         Range(0.1, 1.5)) = 1.0

        [Space(8)][Header(Pop Crack Mode)]
        [Enum(Voronoi,0,RadialWedges,1)] _CrackMode ("Crack Mode", Float) = 0
        _PopPhase              ("Pop Phase 0..1 (MPB)",      Range(0, 1)) = 0
        _ShardScale            ("Voronoi Cell Density",      Range(2, 32))  = 12
        _ShardThrow            ("Shard UV Throw",            Range(0, 0.4)) = 0.18
        _ShardStagger          ("Per-cell Time Stagger",     Range(0, 0.7)) = 0.35
        _CrackEdgeWidth        ("Crack Edge Width",          Range(0.0, 0.1)) = 0.025
        _CrackBoost            ("Crack Glow Boost",          Range(0, 6))   = 2.5
        _PopFlashStrength      ("Pop Edge Flash",            Range(0, 4))   = 1.5
        _WedgeCount            ("Wedge Count (Mode 1)",      Range(4, 32)) = 14

        [Space(8)][Header(Draw Order)]
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Z Test", Float) = 8
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent"
            "RenderType"      = "Transparent"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X(_SpaceWarpSourceTex);
            SAMPLER(sampler_SpaceWarpSourceTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _TintStrength;
                float  _ShapeMode;
                float  _NeckWidth;
                float  _NeckEnd;
                float  _BulbCenter;
                float  _BulbRadius;
                float  _BulbAspect;
                float  _AnchorBulbRadius;
                float  _AnchorBulbAspect;
                float  _AnchorScale;
                float  _QuadAspect;
                float  _PersistStrength;
                float  _PersistScale;
                float4 _PersistScroll;
                float  _DomainWarp;
                float  _SwirlAmount;
                float  _SwirlAnim;
                float  _PulsePhase;
                float  _PulseStrength;
                float  _PulseRippleWidth;
                float  _PulseMaxRadius;
                float  _CrackMode;
                float  _PopPhase;
                float  _ShardScale;
                float  _ShardThrow;
                float  _ShardStagger;
                float  _CrackEdgeWidth;
                float  _CrackBoost;
                float  _PopFlashStrength;
                float  _WedgeCount;
            CBUFFER_END

            #define PI 3.14159265

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
            };

            float Hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float2 Hash22(float2 p)
            {
                float a = Hash21(p);
                float b = Hash21(p + 17.71);
                return float2(a, b);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i + float2(0.0, 0.0));
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // Smooth-min: rounds the seam where two SDFs meet so the bulb fairs
            // into the neck instead of a hard transition. k controls smoothness.
            float SmoothMaxMask(float a, float b, float k)
            {
                float h = saturate(0.5 + 0.5 * (b - a) / max(k, 1e-4));
                return lerp(a, b, h) + k * h * (1.0 - h);
            }

            // ---- Shape mask: returns 0..1 coverage of the membrane silhouette ----
            float ComputeShapeMask(float u, float v)
            {
                float mask = 0.0;
                if (_ShapeMode < 0.5)
                {
                    // Mode 0: Bulb on Neck
                    float neckHalf  = _NeckWidth;
                    float neckRange = (1.0 - smoothstep(0.0, 0.05, -u))
                                    * (1.0 - smoothstep(_NeckEnd, _NeckEnd + 0.08, u));
                    float neckMask  = (1.0 - smoothstep(neckHalf * 0.7, neckHalf, abs(v))) * neckRange;

                    float du = (u - _BulbCenter) / max(_BulbAspect, 1e-3);
                    float dv = v;
                    float bulbDist = sqrt(du*du + dv*dv);
                    float bulbMask = 1.0 - smoothstep(_BulbRadius * 0.85, _BulbRadius, bulbDist);

                    mask = SmoothMaxMask(neckMask, bulbMask, 0.18);
                }
                else if (_ShapeMode < 1.5)
                {
                    // Mode 1: Inverted Teardrop (smooth widen anchor → tip)
                    float w = lerp(_NeckWidth, _BulbRadius, smoothstep(0.0, 1.0, u));
                    float taper = (1.0 - smoothstep(0.95, 1.05, u));
                    mask = (1.0 - smoothstep(w * 0.85, w, abs(v))) * taper;
                }
                else if (_ShapeMode < 2.5)
                {
                    // Mode 2: Dumbbell — small bulb at u≈0.05 + main bulb at _BulbCenter
                    float duA = (u - 0.05) / max(_AnchorBulbAspect, 1e-3);
                    float dvA = v;
                    float anchorDist = sqrt(duA*duA + dvA*dvA);
                    float anchorMask = 1.0 - smoothstep(_AnchorBulbRadius * 0.85, _AnchorBulbRadius, anchorDist);

                    float neckHalf  = _NeckWidth;
                    float neckRange = (1.0 - smoothstep(_BulbCenter - 0.08, _BulbCenter, u))
                                    * smoothstep(0.0, 0.1, u);
                    float neckMask  = (1.0 - smoothstep(neckHalf * 0.7, neckHalf, abs(v))) * neckRange;

                    float du = (u - _BulbCenter) / max(_BulbAspect, 1e-3);
                    float bulbDist = sqrt(du*du + v*v);
                    float bulbMask = 1.0 - smoothstep(_BulbRadius * 0.85, _BulbRadius, bulbDist);

                    mask = SmoothMaxMask(SmoothMaxMask(anchorMask, neckMask, 0.15), bulbMask, 0.18);
                }
                else
                {
                    // Mode 3: Horn — flat anchor edge at u=0 (width = _NeckWidth,
                    // driver-rewritten live as a v-fraction lerped from world-unit
                    // anchorHalfWidthMin..Max via stretch progress), smooth flare
                    // to _BulbRadius at u=_BulbCenter, then a hemispherical cap
                    // from u=_BulbCenter through u=1. The cap's long-axis distance
                    // is multiplied by _QuadAspect so the hemisphere stays a true
                    // WORLD circle even when the quad is much longer than tall.
                    if (u <= _BulbCenter)
                    {
                        float t = u / max(_BulbCenter, 1e-3);
                        float w = lerp(_NeckWidth, _BulbRadius, smoothstep(0.0, 1.0, t));
                        mask = 1.0 - smoothstep(w * 0.85, w, abs(v));
                    }
                    else
                    {
                        float duEquiv = (u - _BulbCenter) * max(_QuadAspect, 1e-3);
                        float dist = sqrt(duEquiv * duEquiv + v * v);
                        mask = 1.0 - smoothstep(_BulbRadius * 0.85, _BulbRadius, dist);
                    }
                }
                return saturate(mask);
            }

            // ---- Cell partition: outputs cell id, cell center (uv-space), and
            // edgeProximity (~0 on cell boundary, larger inside the cell). ----
            void ComputeCells(float2 uv, out float cellId, out float2 cellCenter, out float edgeProximity)
            {
                if (_CrackMode < 0.5)
                {
                    // Voronoi (3x3 neighborhood)
                    float scale = max(_ShardScale, 1.0);
                    float2 g    = uv * scale;
                    float2 gi   = floor(g);
                    float2 gf   = frac(g);
                    float minD     = 9.0;
                    float secondD  = 9.0;
                    float2 winner  = gi;
                    float2 winnerJ = float2(0.5, 0.5);
                    [unroll] for (int x = -1; x <= 1; x++)
                    {
                        [unroll] for (int y = -1; y <= 1; y++)
                        {
                            float2 ngi = gi + float2(x, y);
                            float2 jit = Hash22(ngi);
                            float2 c   = float2(x, y) + jit;
                            float d    = length(c - gf);
                            if (d < minD)
                            {
                                secondD = minD;
                                minD = d;
                                winner = ngi;
                                winnerJ = jit;
                            }
                            else if (d < secondD)
                            {
                                secondD = d;
                            }
                        }
                    }
                    cellId        = Hash21(winner);
                    cellCenter    = (winner + winnerJ) / scale;
                    edgeProximity = (secondD - minD) / scale; // in uv units
                }
                else
                {
                    // Radial wedges around _BulbCenter
                    float wCount = max(_WedgeCount, 3.0);
                    float2 from = uv - float2(_BulbCenter, 0.5);
                    float r     = length(from);
                    float a     = atan2(from.y, from.x); // -PI..PI
                    float aN    = a / (2.0 * PI) + 0.5;  // 0..1
                    float wIdx  = floor(aN * wCount);
                    float wAng  = (wIdx + 0.5) / wCount * (2.0 * PI) - PI;
                    cellId      = Hash11(wIdx + 0.5);
                    cellCenter  = float2(_BulbCenter, 0.5) + float2(cos(wAng), sin(wAng)) * (r * 0.6);
                    // distance to nearest wedge boundary, in arc length at radius r
                    float fAng     = aN * wCount - wIdx;          // 0..1 within wedge
                    float dToEdge  = min(fAng, 1.0 - fAng);       // 0..0.5
                    edgeProximity  = dToEdge * (2.0 * PI / wCount) * max(r, 0.05);
                }
            }

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.uv         = IN.uv;
                OUT.screenPos  = ComputeScreenPos(p.positionCS);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;
                float u   = uv.x;
                float v   = uv.y * 2.0 - 1.0;

                // Pop expansion lets the silhouette puff out slightly so shards
                // visibly fly past the original outline before they cull.
                float popExpand = 1.0 + _PopPhase * 0.25;

                // Shape mask (use expanded radii during pop so the outline grows).
                float bulbR  = _BulbRadius      * popExpand;
                float neckW  = _NeckWidth       * popExpand;
                float anchR  = _AnchorBulbRadius * popExpand;
                // Temporary swap with locals via static branch — cheaper than
                // duplicating the whole shape function.
                float origBulb  = _BulbRadius;
                float origNeck  = _NeckWidth;
                float origAnch  = _AnchorBulbRadius;
                // (Note: _BulbRadius etc. are CBUFFER constants; we can't write
                // them. Instead recompute inline using the expanded scalars.)

                // Inline expanded shape (mirrors ComputeShapeMask but with locals).
                float baseMask;
                {
                    if (_ShapeMode < 0.5)
                    {
                        float neckRange = (1.0 - smoothstep(0.0, 0.05, -u))
                                        * (1.0 - smoothstep(_NeckEnd, _NeckEnd + 0.08, u));
                        float neckMask  = (1.0 - smoothstep(neckW * 0.7, neckW, abs(v))) * neckRange;
                        float du = (u - _BulbCenter) / max(_BulbAspect, 1e-3);
                        float bulbDist = sqrt(du*du + v*v);
                        float bulbMask = 1.0 - smoothstep(bulbR * 0.85, bulbR, bulbDist);
                        baseMask = SmoothMaxMask(neckMask, bulbMask, 0.18);
                    }
                    else if (_ShapeMode < 1.5)
                    {
                        float w     = lerp(neckW, bulbR, smoothstep(0.0, 1.0, u));
                        float taper = (1.0 - smoothstep(0.95, 1.05, u));
                        baseMask    = (1.0 - smoothstep(w * 0.85, w, abs(v))) * taper;
                    }
                    else if (_ShapeMode < 2.5)
                    {
                        float duA = (u - 0.05) / max(_AnchorBulbAspect, 1e-3);
                        float anchorDist = sqrt(duA*duA + v*v);
                        float anchorMask = 1.0 - smoothstep(anchR * 0.85, anchR, anchorDist);
                        float neckRange  = (1.0 - smoothstep(_BulbCenter - 0.08, _BulbCenter, u))
                                         * smoothstep(0.0, 0.1, u);
                        float neckMask   = (1.0 - smoothstep(neckW * 0.7, neckW, abs(v))) * neckRange;
                        float du = (u - _BulbCenter) / max(_BulbAspect, 1e-3);
                        float bulbDist = sqrt(du*du + v*v);
                        float bulbMask = 1.0 - smoothstep(bulbR * 0.85, bulbR, bulbDist);
                        baseMask = SmoothMaxMask(SmoothMaxMask(anchorMask, neckMask, 0.15), bulbMask, 0.18);
                    }
                    else
                    {
                        // Mode 3: Horn — flare body up to _BulbCenter, then a
                        // world-space hemispherical cap. _NeckWidth and _BulbRadius
                        // are both driver-rewritten live (v-fractions converted
                        // from world-unit inspector fields) every stretch frame.
                        // duEquiv multiplies by _QuadAspect so the cap is a true
                        // round in world space rather than the squished ellipse
                        // a long-thin quad would otherwise produce.
                        if (u <= _BulbCenter)
                        {
                            float t = u / max(_BulbCenter, 1e-3);
                            float w = lerp(neckW, bulbR, smoothstep(0.0, 1.0, t));
                            baseMask = 1.0 - smoothstep(w * 0.85, w, abs(v));
                        }
                        else
                        {
                            float duEquiv = (u - _BulbCenter) * max(_QuadAspect, 1e-3);
                            float dist = sqrt(duEquiv * duEquiv + v * v);
                            baseMask = 1.0 - smoothstep(bulbR * 0.85, bulbR, dist);
                        }
                    }
                    baseMask = saturate(baseMask);
                }

                if (baseMask <= 0.001) discard;

                // ---- Cell partition for crack glow + shard throw ----
                float cellId;
                float2 cellCenter;
                float edgeProximity;
                ComputeCells(uv, cellId, cellCenter, edgeProximity);

                // Per-cell pop progress, staggered by cellId so cells crack at
                // different moments → fragmented feel instead of synchronized fade.
                float stag      = saturate(_ShardStagger);
                float cellDelay = cellId * stag;
                float cellPopT  = saturate((_PopPhase - cellDelay) / max(1.0 - stag, 0.001));

                // Crack glow: peaks in 5..40% pop, brightest along cell edges.
                float crackPhase = smoothstep(0.0, 0.15, _PopPhase) * (1.0 - smoothstep(0.4, 1.0, _PopPhase));
                float crackEdge  = 1.0 - smoothstep(0.0, _CrackEdgeWidth, edgeProximity);
                float crackGlow  = crackEdge * crackPhase;

                // Shard refraction throw — direction = cellCenter relative to
                // membrane center (so cells fly outward from the bulb origin).
                float2 membCenter = float2(_BulbCenter, 0.5);
                float2 dir        = cellCenter - membCenter;
                float dlen        = length(dir);
                dir = dlen > 1e-4 ? dir / dlen : float2(0.0, 1.0);
                float2 jitterDir  = (Hash22(float2(cellId * 13.7, cellId * 27.3)) - 0.5) * 2.0;
                dir = normalize(dir + jitterDir * 0.35);
                float2 shardOffset = dir * cellPopT * _ShardThrow;

                // ---- Persistent warp (v1-style swirl + domain noise) ----
                // Local half-width at this u — drives the same vc/width stretch
                // trick v1 used so swirl in the narrow neck reads as turbulent
                // axial flow instead of a uniform fisheye rotation. Without this,
                // the warp looks like a single-direction concave mirror.
                float localHalfW;
                if (_ShapeMode < 0.5)
                {
                    float bulbInfl = smoothstep(_NeckEnd - 0.1, _NeckEnd + 0.1, u);
                    localHalfW = lerp(_NeckWidth, _BulbRadius, bulbInfl);
                }
                else if (_ShapeMode < 1.5)
                {
                    localHalfW = lerp(_NeckWidth, _BulbRadius, smoothstep(0.0, 1.0, u));
                }
                else if (_ShapeMode < 2.5)
                {
                    float bulbInfl   = smoothstep(_BulbCenter - 0.15, _BulbCenter, u);
                    float anchorInfl = 1.0 - smoothstep(0.0, 0.15, u);
                    localHalfW = lerp(lerp(_NeckWidth, _AnchorBulbRadius, anchorInfl), _BulbRadius, bulbInfl);
                }
                else
                {
                    // Mode 3: Horn — flare body grows neckW→bulbR up to _BulbCenter,
                    // then bulb radius for the hemispherical cap.
                    if (u <= _BulbCenter)
                    {
                        float t = u / max(_BulbCenter, 1e-3);
                        localHalfW = lerp(_NeckWidth, _BulbRadius, smoothstep(0.0, 1.0, t));
                    }
                    else
                    {
                        localHalfW = _BulbRadius;
                    }
                }
                localHalfW = max(localHalfW, 0.05);

                float du        = u - _BulbCenter;
                float dv        = v / localHalfW;
                float2 centered = float2(du, dv);
                float swirlR    = sqrt(du*du + dv*dv);
                float swirlMask = 1.0 - smoothstep(0.0, 1.5, swirlR);
                float angle     = (_SwirlAmount + sin(_Time.y * _SwirlAnim) * 0.25) * swirlMask;
                float cs        = cos(angle);
                float sn        = sin(angle);
                float2 swirled  = float2(centered.x * cs - centered.y * sn,
                                         centered.x * sn + centered.y * cs);
                // Project the rotation back into screen-uv-space scale by
                // multiplying the y-component by localHalfW so the offset isn't
                // exaggerated to absurd lengths in the narrow neck.
                float2 swirlOffset = float2(swirled.x - centered.x,
                                            (swirled.y - centered.y) * localHalfW) * 0.5;

                float2 flowUv = uv * _PersistScale + _PersistScroll.xy * _Time.y;
                float  n0 = ValueNoise(flowUv);
                float  n1 = ValueNoise(flowUv + n0 * _DomainWarp + 7.7);
                float  n2 = ValueNoise(flowUv + n0 * _DomainWarp + 19.3);
                float2 noiseOffset = float2(n1 - 0.5, n2 - 0.5) * (_PersistStrength * 2.0);
                float2 persistOffset = swirlOffset + noiseOffset;

                // Pulse band (legacy radial shockwave in case the JumpPad pulse
                // is also triggered alongside a stretch).
                float2 toBulb     = float2((u - _BulbCenter) / max(_BulbAspect, 1e-3), v);
                float bulbDist    = length(toBulb);
                float bandPos     = _PulsePhase * _PulseMaxRadius;
                float bandDist    = abs(bulbDist - bandPos) / max(_PulseRippleWidth, 1e-4);
                float bandIntens  = saturate(1.0 - bandDist * bandDist);
                float pulseDecay  = 1.0 - _PulsePhase;
                float2 pulseRad   = bulbDist > 1e-4 ? toBulb / bulbDist : float2(0.0, 0.0);
                float2 pulseOff   = pulseRad * (bandIntens * pulseDecay * _PulseStrength);

                float2 totalOffset = (persistOffset + pulseOff + shardOffset) * baseMask;

                float2 screenUV   = IN.screenPos.xy / IN.screenPos.w;
                float3 refracted  = SAMPLE_TEXTURE2D_X(_SpaceWarpSourceTex,
                                                       sampler_SpaceWarpSourceTex,
                                                       screenUV + totalOffset).rgb;
                float3 rgb = lerp(refracted, _BaseColor.rgb, _TintStrength);

                // Crack glint — bright additive on cell boundaries.
                rgb += _BaseColor.rgb * (crackGlow * _CrackBoost);

                // Pop edge flash — bright outline on the silhouette during the
                // first 8% of pop only. Gated by step(0.001, _PopPhase) so the
                // membrane never glows white during the long stretch phase
                // (where _PopPhase = 0 would otherwise drive flashT = 1).
                float popActive = step(0.001, _PopPhase);
                float flashT = saturate((0.08 - _PopPhase) / 0.08) * popActive;
                float silEdge = saturate((1.0 - baseMask) * 6.0) * baseMask;
                rgb += float3(1.0, 1.0, 1.0) * (silEdge * flashT * _PopFlashStrength);

                // Per-cell alpha cut. Each cell shrinks toward its center as it
                // dies on its own staggered timer. Cells outside their own kill
                // window go fully transparent → visible "missing fragment" gaps
                // before the whole membrane disappears.
                float cellLife = 1.0 - smoothstep(0.55, 1.0, cellPopT);

                float alpha = _BaseColor.a * baseMask * cellLife;
                return half4(rgb, saturate(alpha));
            }
        ENDHLSL

        Pass
        {
            Name "SpaceWarpDraw"
            Tags { "LightMode" = "SpaceWarpDraw" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }

    FallBack Off
}
