#ifndef SLIME_DEFORM_INCLUDED
#define SLIME_DEFORM_INCLUDED

// SlimeDeform_float
//
// Vertex displacement for the 3D slime body. Designed to be invoked by a
// Shader Graph Custom Function node (File mode), Type = float, Name =
// SlimeDeform. Shader Graph + URP automatically expose the
// TransformObjectToWorldDir / TransformWorldToObjectDir helpers used below.
//
// Inputs:
//   positionOS         object-space vertex position (from SG Position node, Object)
//   normalOS           object-space vertex normal   (from SG Normal node,   Object)
//   surfaceNormalWS    world-space surface normal of the platform the slime is
//                      attached to (or GravityUp when Airborne). Pushed by
//                      SlimeVisual.cs every frame as _SurfaceNormalWS.
//   dipDirWS           world-space direction of the charge-dip indent. Pushed
//                      by SlimeVisual.cs every frame as _DipDirWS.
//   asymmetry          MPB float _Asymmetry. C# sends a non-positive value
//                      ([-surfaceSquashAsymmetry, 0]) when grounded; 0 in air.
//                      Negative => the side facing into the surface compresses
//                      toward the surface.
//   chargeDip          MPB float _ChargeDip. Non-negative; bigger = deeper indent.
//   displaceScale      Per-material multiplier (SlimeVisualSettings.vertexDisplaceScale,
//                      default 0.3). Lets us dial the vertex-space magnitude
//                      down from the original 2D-UV-tuned values without
//                      retuning every other field.
//   volumePreserve     0..1 toggle (SlimeVisualSettings.volumePreservation).
//                      1 = full incompressible-blob feel; 0 = pure squash with
//                      no orthogonal bulge.
//
// Outputs:
//   positionOS_out     deformed vertex position
//   normalOS_out       passthrough of the input normal. We don't recompute the
//                      analytic normal here — URP recalculates the lighting
//                      normal per-pixel from interpolated tangent-space data,
//                      and the displacement is gentle enough that the visible
//                      shading error is negligible. If at step 4 the lighting
//                      looks faceted, swap in a finite-difference normal
//                      (sample positionOS at +eps on two tangent axes, cross).
void SlimeDeform_float(
    float3 positionOS,
    float3 normalOS,
    float3 surfaceNormalWS,
    float3 dipDirWS,
    float4 velocityDirWS,            // xyz = normalized world velocity dir, w = speed norm (0..1)
    float  asymmetry,
    float  chargeDip,
    float  displaceScale,
    float  volumePreserve,
    float  velocityStretchAmount,    // pre-evaluated stretch s from the AnimationCurve (C# samples curve.Evaluate every frame)
    float  velocityFrontCompress,    // material max intensity for front-hemisphere compression
    out float3 positionOS_out,
    out float3 normalOS_out)
{
    // --- World-space vertex normal for both masks ---
    // Using world-space dots sidesteps two hazards:
    //   1) body.localScale (set every frame by SlimeVisual) is non-uniform, so
    //      doing the dots in object space would warp the masks as the slime
    //      squashes/stretches.
    //   2) The mesh prefab is rotated -90 deg X; the wrapper / MeshDepthFlatten
    //      add more rotation+scale. We don't want the displacement direction
    //      to track those bookkeeping transforms.
    float3 normalWS = TransformObjectToWorldDir(normalOS);

    // Defensive normalize — SlimeVisual.cs is supposed to send normalized
    // vectors, but the cost is one rsqrt and it stops a single garbage frame
    // from producing visible glitches.
    float3 surfNormWS = normalize(surfaceNormalWS);
    float3 dipDirWSn  = normalize(dipDirWS);

    // ============================================================
    // 1. Asymmetric squash — bottom flattens and bulges (egg / puddle shape)
    // ============================================================
    // Mirrors the 2D shader's V-axis remap: "출력의 아래 절반을 텍스처
    // 적도 근처에서 샘플링 → 아래는 평평하고 넓음, 위는 둥금". The
    // surface-touching hemisphere compresses up toward the equator AND
    // bulges out horizontally, giving a "puddle pressed against the
    // surface" silhouette. Top hemisphere is untouched (still round).
    //
    // Mask = 1 on the surface-touching hemisphere. On a floor that's the
    // actual bottom; on a wall, the wall-touching side; on a ceiling, the
    // top-of-screen side that's stuck to the ceiling.
    float bottomMask = saturate(dot(normalWS, -surfNormWS));

    // Object-space surface normal so we can author the displacement on
    // positionOS. TransformWorldToObjectDir with normalize=true compensates
    // for non-uniform parent scale.
    float3 surfNormOS = normalize(TransformWorldToObjectDir(surfNormWS, true));

    // asymmetry is non-positive when active ([-0.3, 0]); convert to a
    // positive "compression magnitude". Push the masked hemisphere along
    // +surfNormOS (away from the surface, toward the sphere center on that
    // side). For the bottom vertex of a sphere on the floor:
    //   positionOS=(0,-1,0), surfNormOS=(0,+1,0), bottomMask=1
    //   compress = -(-0.3)*1*1 = 0.3
    //   squashVec = +(0,1,0) * 0.3 = (0,0.3,0)
    //   newPos = (0, -0.7, 0)  // bottom moved UP toward equator — flattens.
    float  compress  = -asymmetry * bottomMask * displaceScale;
    float3 squashVec = surfNormOS * compress;

    float3 deformedOS = positionOS + squashVec;

    // ============================================================
    // 2. Volume preservation (orthogonal bulge)
    // ============================================================
    // When the bottom compresses up by `compress`, the orthogonal axes
    // (along the surface tangent + bitangent) bulge by 1/sqrt(1 - compress)
    // to preserve volume. This is what makes the bottom not just lift, but
    // SPREAD — the "wide" part of "납작하고 넓음". Per-vertex because the
    // top hemisphere has compress=0 and shouldn't bulge.
    //
    // The max(0.1, ...) clamp is a NaN guard against extreme tuning where
    // compress * volumePreserve > 1 would put denom <= 0.
    float denom    = max(0.1, 1.0 - compress * volumePreserve);
    float perpGain = rsqrt(denom);

    // Decompose deformedOS into (parallel to surface normal) +
    // (perpendicular). Scale only the perpendicular part.
    float3 parPart  = dot(deformedOS, surfNormOS) * surfNormOS;
    float3 perpPart = deformedOS - parPart;
    deformedOS      = parPart + perpPart * perpGain;

    // ============================================================
    // 3. Ground compensation — keep the slime planted on the surface
    // ============================================================
    // The asymmetric squash moved the bottom-most vertex up by maxCompress
    // along +surfNormOS. Without compensation, the visible bottom of the
    // mesh lifts off the floor (the slime appears to float). Sink the whole
    // mesh by maxCompress along -surfNormOS so:
    //   - bottom vertex returns to its original world-space position (planted)
    //   - everything else sinks by maxCompress (the "settling" feel the user
    //     described as 몸이 조금 내려가는)
    // This mirrors the 2D shader's behavior where texture-clamp keeps the
    // sprite quad's bottom edge filled with the texture's v=0 row, anchoring
    // the visible content to the quad's floor edge.
    float maxCompress = -asymmetry * displaceScale;
    deformedOS -= surfNormOS * maxCompress;

    // ============================================================
    // 3.5 Pinch squash — flatten the FULL-SIZE body to the TRUE gap against each closing wall
    // ============================================================
    // The slime visual is NOT scaled down during a squeeze (the collider shrinks instead — decoupled),
    // so the entire "pressed flat" look is produced here: every vertex that pokes past a wall's face
    // plane is CLAMPED back onto that plane → a genuine flat face EXACTLY at the measured gap, with no
    // clipping through the plate. Two opposite contacts = a vertical sandwich's two flats; angled /
    // 3-side fall out of the same per-contact clamp. Done in WORLD space so the mesh's bookkeeping
    // rotation (-90°X) + flatten scale can't warp the plane. After clamping, ONE volume-preserving
    // lateral bulge about the dominant pinch axis spreads the lost volume out the open sides (two
    // opposite contacts share that axis, so bulging once avoids double-counting). _PinchCount == 0
    // => skipped (byte-for-byte no-op).
    if (_PinchCount > 0.5)
    {
        int pinchN = (int)(_PinchCount + 0.5);
        // Clamp from the TRUE gap-center (midpoint between the plates), not the mesh origin — the body3d
        // mesh drifts off-center (velocity lean / sag / rest offset) and clamping from it would slide the
        // carved slab off the real plate faces (slime floats below one plate, pokes through the other).
        float3 centerWS = _PinchCenterWS.xyz;
        float  maxComp     = 0.0;
        float3 bulgeAxisWS = float3(0, 0, 0);

        [unroll]
        for (int pidx = 0; pidx < PINCH_MAX; pidx++)
        {
            if (pidx >= pinchN) break;
            float3 cNWS = _PinchNormalWS[pidx].xyz;
            if (dot(cNWS, cNWS) < 1e-6) continue;
            cNWS = normalize(cNWS);

            // outwardWS = slime center → this wall's face. The face plane sits halfGap out along it;
            // any vertex beyond it is projected straight back onto the plane (flat face, no clip).
            float3 outwardWS = -cNWS;
            float  halfGap   = max(0.0, _PinchHalfGap[pidx]);

            float3 pWS  = TransformObjectToWorld(deformedOS);
            float  dOut = dot(pWS - centerWS, outwardWS);
            float  over = dOut - halfGap;
            if (over > 0.0)
                deformedOS = TransformWorldToObject(pWS - outwardWS * over);

            if (_PinchComp[pidx] > maxComp) { maxComp = _PinchComp[pidx]; bulgeAxisWS = outwardWS; }
        }

        // Volume-preserving spread perpendicular to the dominant pinch axis. thickness t = gap/2R =
        // 1 − comp; the two orthogonal axes grow by 1/sqrt(t) so the flattened blob conserves volume —
        // the "납작하고 넓음" spread. volumePreserve lerps from pure flatten (no spread) to full.
        if (maxComp > 1e-4 && dot(bulgeAxisWS, bulgeAxisWS) > 1e-6)
        {
            float  t      = max(0.05, 1.0 - saturate(maxComp));
            float  bulge  = lerp(1.0, rsqrt(t), volumePreserve);
            float3 axisOS = normalize(TransformWorldToObjectDir(bulgeAxisWS, true));
            float3 par    = dot(deformedOS, axisOS) * axisOS;
            float3 perp   = deformedOS - par;
            deformedOS    = par + perp * bulge;
        }
    }

    // ============================================================
    // 4. Charge-dip indent
    // ============================================================
    // smoothstep gives a soft circular spot on the side of the sphere that
    // points toward dipDirWS (computed in C# from -surfaceNormal + lateral
    // aim offset). Threshold (0.7, 1.0) gives roughly a 45-deg cone of
    // influence — change to (0.85, 1.0) for a tighter pinch, or (0.5, 1.0)
    // for a broader basin.
    float dipSpot = smoothstep(0.7, 1.0, dot(normalWS, dipDirWSn));

    // Push the affected vertices INWARD along their own normal. Using normalOS
    // (not dipDirOS) means the dip stays perpendicular to the surface
    // everywhere inside the spot, which reads as a clean indent rather than
    // a sideways shove.
    deformedOS -= normalOS * (chargeDip * dipSpot * displaceScale);

    // ============================================================
    // 5a. Velocity direction setup — shared by 5b (stretch) and 5c (front-compress)
    // ============================================================
    // NaN trap: TransformWorldToObjectDir(v, true) normalizes internally,
    // and normalize(float3(0,0,0)) = NaN. A step()-based validity multiplier
    // doesn't clean it up because 0 * NaN = NaN in IEEE. Instead, substitute
    // a safe non-zero fallback direction BEFORE any normalize runs so no
    // NaN is ever born. When velocity is zero, speedNorm = 0, so the final
    // contributions (stretch s=0, frontPush=0) vanish regardless of the
    // fallback direction.
    float3 velDirRaw  = velocityDirWS.xyz;
    float  velDirSqr  = dot(velDirRaw, velDirRaw);
    float3 velDirSafe = lerp(float3(0, 1, 0), velDirRaw, step(1e-6, velDirSqr));
    float3 velDirWSn  = normalize(velDirSafe);
    // Normalize AFTER WS→OS — the combined scale chain (body3d + MeshDepthFlatten
    // + baseline) is non-uniform, which warps direction magnitudes; TransformWorldToObjectDir's
    // internal normalize handles the unit-length restoration.
    float3 velDirOS   = normalize(TransformWorldToObjectDir(velDirWSn, true));
    float  speedNorm  = saturate(velocityDirWS.w);

    // ============================================================
    // 5b. Velocity stretch — volume-preserving directional elongation
    // ============================================================
    // body.localScale can only do axis-aligned scale, which produces an
    // enlarged ball for diagonal motion (X and Y both stretch equally, no
    // rotated ellipse possible). Fix here with true vertex displacement
    // along velDirOS: elongate parallel by (1+s), compress the two
    // orthogonal axes by rsqrt(1+s) for 3D volume preservation:
    //   (1+s) * (1/sqrt(1+s))² = (1+s)/(1+s) = 1  ✓
    //
    // `velocityStretchAmount` is the stretch s pre-evaluated in C# from the
    // SlimeVisualSettings.velocityStretch3DCurve AnimationCurve, sampled at
    // the current normalized speed. Any curve shape the designer draws in the
    // Inspector graph editor maps 1:1 here.
    //
    // volumePreserve lerps between pure elongation (perp = 1, slime inflates
    // along velocity without thinning) and full 3D volume preservation
    // (perp = 1/sqrt(1+s), slime gets thinner perpendicular to match).
    float stretch    = velocityStretchAmount;
    float alongScale = 1.0 + stretch;
    float perpScale  = lerp(1.0, rsqrt(alongScale), volumePreserve);
    float3 stretchPar  = dot(deformedOS, velDirOS) * velDirOS;
    float3 stretchPerp = deformedOS - stretchPar;
    deformedOS = stretchPar * alongScale + stretchPerp * perpScale;

    // ============================================================
    // 5c. Velocity front-compress — teardrop tip
    // ============================================================
    // After the rugby-ball stretch in 5b, flatten the LEADING edge so the
    // silhouette reads as a teardrop trailing behind the motion (the back
    // stays elongated, front is blunted). Pushes front-hemisphere vertices
    // inward along -velDirOS.
    //
    // Tied to `stretch` (not speedNorm) so the compression scales with the
    // actual elongation. At velocityFrontCompress = 1.0, the inward push
    // exactly cancels the outward displacement from 5b at front vertices —
    // front stays at rest position while back hemisphere keeps full
    // elongation = clean teardrop. > 1.0 creates a dimple at the front,
    // < 1.0 partial cancel (elongated on both sides but front less so).
    float  frontMask = saturate(dot(normalWS, velDirWSn));
    float  frontPush = stretch * velocityFrontCompress * frontMask;
    deformedOS -= velDirOS * frontPush;

    // ============================================================
    // 6. Slice RIM band — surface-tension shaping of the cut edge (gated on an active wound)
    // ============================================================
    // The band just inside the cut plane is displaced along the surface normal by the wound's own
    // signed magnitude: a cut wound pulls INWARD (_SliceLip < 0), rounding the sharp 90° rim into
    // a fillet — high curvature relaxes first, and material can only be removed, so the silhouette
    // can never exceed the pre-cut body. An absorption merge pushes OUTWARD (_SliceLip > 0) to
    // squeeze the contact patch. Keyed on SlicePlaneDist, whose zero set is the FIXED rim circle:
    // the full SDF collapses to ~0 across the whole cap once the membrane reaches the body sphere,
    // which would displace the entire cap instead of a ring. sealRatio scales it. Normal tilt is
    // magnitude-signed-independent so the toon ramp shades a rounded roll either way.
    // _SliceCount == 0 => skipped (byte-for-byte no-op).
    if (_SliceCount > 0.5 && _LipBand > 1e-5)
    {
        int sliceN = (int)(_SliceCount + 0.5);
        float bestBump = 0.0;
        float bestMag = 0.0;
        float3 bestCutN = float3(0, 0, 0);
        [unroll]
        for (int si = 0; si < SLICE_MAX; si++)
        {
            if (si >= sliceN) break;
            float sd = SlicePlaneDist(positionOS, si);    // rest OS; < 0 = kept side
            float mag = _SliceLip[si];
            float t = saturate(-sd / _LipBand);           // 0 at the rim, 1 at the band's inner edge
            // The clip boundary is evaluated on the REST position, so it does not move with this
            // displacement: anything that shifts the rim itself tears the surface away from the
            // boundary and opens a gap the far shell shows through. So an INWARD fillet uses a hump
            // that vanishes at both ends — the rim stays put and only the flesh behind it sinks,
            // which is what reads as a rounded edge. An OUTWARD bulge peaks at the rim (it grows
            // past the boundary, where there is nothing to tear away from).
            // Fillet fades as the wound seals — the sharp rim is what there is to round, and it
            // opens up on its own as the membrane fills. The merge bulge does the opposite.
            float seal = saturate(_SliceSeal[si]);
            float prof = mag < 0.0 ? 4.0 * t * (1.0 - t) * (1.0 - seal)
                                   : (1.0 - t) * (1.0 - t) * (3.0 - 2.0 * (1.0 - t)) * seal;
            float bump = prof * step(sd, 0.0);
            if (abs(bump * mag) > abs(bestBump * bestMag))
            {
                bestBump = bump; bestMag = mag; bestCutN = _SlicePlaneOS[si].xyz;
            }
        }
        deformedOS += normalOS * (bestBump * bestMag);
        normalOS = normalize(normalOS + bestCutN * (bestBump * abs(bestMag) * 4.0));
    }

    positionOS_out = deformedOS;
    normalOS_out   = normalOS;
}

#endif // SLIME_DEFORM_INCLUDED
