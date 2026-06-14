using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Settings;

namespace SlimeJumping.Effects
{
    /// <summary>
    /// "Slime loses a piece of itself" effect — a cut slime IS a smaller slime, and the lost
    /// piece is a REAL pickup (Zac-style):
    ///   • Severed piece → the slime's own blob mesh spawned at the LOST volume's size. It
    ///     flies off, rests on the ground breathing softly, and when the slime comes near it
    ///     is magnetically pulled in and absorbed (SlimeChunk owns that lifecycle).
    ///   • Body → loses real volume: SlimeController.SetBodyScale shrinks the actual
    ///     CircleCollider2D (and every radius-derived length), and SlimeVisual's body-scale
    ///     spring chases the new ratio with a tunable slosh.
    ///   • Wound → NOT a sweeping plane. The cut face is a surface-tension MENISCUS anchored
    ///     at the fixed wound rim: it starts as a slightly concave, quivering dent (HOLD),
    ///     then an underdamped spring wells it outward until the membrane coincides with the
    ///     body sphere — the wound closes concentrically rim→apex, with lip/tear/heal-goo and
    ///     the whole boundary jiggling from the strike (SlimeSliceData.hlsl membrane SDF).
    ///
    /// Healing is ABSORPTION-GATED: each absorbed chunk restores exactly the volume it
    /// carried (quick merge ramp + local bulge at the contact point + one swallow slosh).
    /// A chunk lost forever (pit fall) converts to a SLOW ambient regen instead
    /// (slowRecoveryDuration), so the slime can never be soft-locked small.
    /// Launch power is DERIVED from the current body scale (woundedScaleRef mapping) — no
    /// independent recovery timer.
    ///
    /// Re-cut while small stacks naturally: the new cut shrinks from the CURRENT ratio.
    /// MinBodyScale in SlimeController guards the degenerate case.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public class SlimeSplitSequence : MonoBehaviour
    {
        [SerializeField] private SlimeSplitSettings settings;
        [Tooltip("Material for the SEVERED falling piece. Null → reuse the slime body material.")]
        [SerializeField] private Material chunkMaterial;

        private SlimeController slime;
        private SlimeVisual slimeVisual;
        private MeshFilter bodyFilter;
        private MeshRenderer bodyRenderer;
        private Mesh origMesh;
        private MaterialPropertyBlock mpb;

        private float recutTimer;

        // --- Cut transition (meniscus redesign, 2026-06-11) ---
        //   HOLD   (cutHoldDuration): full size; the cut face shows as a slightly CONCAVE,
        //          quivering dent — struck goo, not a frozen frame.
        //   CLOSE  (closeDuration): L1 the meniscus spring chases closeSealCurve and wells
        //          the membrane outward (rim closes toward the apex), L2 the body migrates
        //          toward the cut and re-centers (gatherCurve/gatherAmount), L3 the physics
        //          size ramps down (closeBodyCurve) with the visual spring sloshing on top.
        //   SETTLE: body is at its post-cut size; the membrane spring finishes its last
        //          overshoot until the clip area reaches zero.
        //   RELAX : the rim lip can't fade on its own (the membrane is geometrically pinned
        //          to the rim circle, so the lip bump there stays at full strength) — switching
        //          the wound off here would POP the lip ring off in one frame. Instead the lip
        //          magnitude sloshes down to zero (damped oscillation, out ↔ in) and only then
        //          does the wound switch off — sealed shape blends into the small round slime.
        private enum CutPhase { None, Hold, Close, Settle, Relax }
        private CutPhase phase = CutPhase.None;
        private float phaseTimer;
        private float relaxTimer;
        private float relaxPhase;          // slow slosh phase for the Relax wobble (Hz = sealRelaxFreq)
        private float lipMagScale = 1f;    // Relax: bump height sinks (with an upward-only bounce)
        private float lipBandScale = 1f;   // Relax: bump foot spreads — viscous flattening
        private float preScale;            // BodyScaleRatio at the cut (could be < 1 already)
        private float postScale;           // BodyScaleRatio after this cut's volume loss
        private Vector2 clipNormalWorld;   // unit, points to the REMOVED side (world-fixed)
        private float clipDepthStartFrac;  // volume-true plane offset at the cut, fraction of full radius

        // Meniscus spring: w is the seal progress the membrane DISPLAYS. The target runs
        // -cutConcaveDepthFrac (dented) → 1 (membrane == body sphere) along closeSealCurve;
        // the underdamped spring adds the welling overshoot + settle slosh.
        private float meniscusW;
        private float meniscusWVel;

        // Strike quiver: a decaying high-frequency envelope feeding _SliceWobble, so the
        // freshly cut face + rim visibly tremble and calm down — one continuous physical
        // narrative from strike to seal. Also re-kicked by absorption merges.
        private float quiverEnv;
        private float quiverPhase;

        // --- Absorption-gated healing ---
        // Volumes are in FULL-BODY units (ratio³). Chunks report back here.
        private readonly Queue<PendingMerge> mergeQueue = new();
        private bool merging;
        private bool mergeOwnsRamp;        // false = volume folded into the CLOSE ramp instead
        private float mergeTimer;
        private float mergeStartRatio;
        private float mergeTargetRatio;
        private Vector2 mergeDirWorld;     // body center → contact point (world)
        private float slowPool;            // volume owed by LOST chunks, regenerated slowly

        private struct PendingMerge
        {
            public Vector2 dirWorld;
            public float volume;
        }

        private const int SliceMax = 3;    // must match SLICE_MAX in SlimeSlice.hlsl
        private readonly Vector4[] slicePlaneOS = new Vector4[SliceMax];
        private readonly float[] sliceSeal = new float[SliceMax];
        private readonly float[] sliceHeal = new float[SliceMax];
        private readonly Vector4[] sliceMeniscus = new Vector4[SliceMax];
        private readonly Vector4[] sliceWobble = new Vector4[SliceMax];

        private static readonly int SliceCountID = Shader.PropertyToID("_SliceCount");
        private static readonly int SlicePlaneOSID = Shader.PropertyToID("_SlicePlaneOS");
        private static readonly int SliceSealID = Shader.PropertyToID("_SliceSeal");
        private static readonly int SliceHealID = Shader.PropertyToID("_SliceHeal");
        private static readonly int SliceMeniscusID = Shader.PropertyToID("_SliceMeniscus");
        private static readonly int SliceWobbleID = Shader.PropertyToID("_SliceWobble");
        private static readonly int TearAmpID = Shader.PropertyToID("_TearAmp");
        private static readonly int TearFreqID = Shader.PropertyToID("_TearFreq");
        private static readonly int LipBandID = Shader.PropertyToID("_LipBand");
        private static readonly int LipMagID = Shader.PropertyToID("_LipMag");
        private static readonly int SliceCapColorID = Shader.PropertyToID("_SliceCapColor");
        private static readonly int SliceCapRimColorID = Shader.PropertyToID("_SliceCapRimColor");
        private static readonly int SliceCapDepthID = Shader.PropertyToID("_SliceCapDepth");
        private static readonly int CapWetEdgeWidthID = Shader.PropertyToID("_CapWetEdgeWidth");
        private static readonly int CapMarbleAmpID = Shader.PropertyToID("_CapMarbleAmp");

        public bool IsActive { get; private set; }

        /// <summary>
        /// Blade cut. The severed piece flies off perpendicular to the entry direction
        /// (jittered), the body shrinks by cutVolumeFraction of its current volume.
        /// </summary>
        /// <param name="entryVelocity">Slime velocity entering the blade — sets the fling
        /// direction. Read BEFORE the caller applies knockback.</param>
        public void Begin(SlimeController s, Vector2 entryVelocity)
        {
            Vector2 v = entryVelocity.sqrMagnitude > 1e-4f ? entryVelocity.normalized : Vector2.right;
            Vector2 n = new Vector2(-v.y, v.x);
            Vector2 outDir = Rotate(n, Random.Range(-settings.cutNormalJitterDeg, settings.cutNormalJitterDeg));
            ApplyCut(s, outDir, entryVelocity, settings.cutVolumeFraction);
        }

        /// <summary>
        /// Deliberate mid-air self-split (double-jump fuel): the slime ejects a piece of its
        /// own body along ejectDir and becomes smaller. The recoil impulse on the slime is the
        /// CALLER's job (gameplay owns jump strength); this handles volume, piece, and heal.
        /// </summary>
        public void BeginSelfSplit(SlimeController s, Vector2 ejectDir)
        {
            Vector2 dir = ejectDir.sqrMagnitude > 1e-4f ? ejectDir.normalized : Vector2.down;
            ApplyCut(s, dir, s != null ? s.Velocity : Vector2.zero, settings.selfSplitVolumeFraction);
        }

        /// <summary>A chunk reached the slime: restore its volume with a merge ramp + local
        /// bulge. dirWorld = body center → contact point.</summary>
        public void NotifyChunkAbsorbed(Vector2 dirWorld, float volume)
        {
            if (volume <= 0f) return;
            mergeQueue.Enqueue(new PendingMerge
            {
                dirWorld = dirWorld.sqrMagnitude > 1e-6f ? dirWorld.normalized : Vector2.up,
                volume = volume,
            });
            IsActive = true;
        }

        /// <summary>A chunk is gone forever (pit fall / timeout): its volume converts to the
        /// slow ambient regen so the slime can never be locked small.</summary>
        public void NotifyChunkLost(float volume)
        {
            if (volume <= 0f) return;
            slowPool += volume;
            IsActive = true;
        }

        private void ApplyCut(SlimeController s, Vector2 outDir, Vector2 inheritVel, float volumeFraction)
        {
            if (s == null || settings == null) return;
            if (recutTimer > 0f) return;            // internal re-cut guard (same-frame / double hazard)

            slime = s;
            if (!ResolveBody()) return;

            // A new cut interrupts an in-flight merge: bank the merge's remaining volume
            // first (so it isn't silently lost), then cut from the result.
            if (merging)
            {
                if (mergeOwnsRamp) slime.SetBodyScale(mergeTargetRatio);
                merging = false;
            }

            float f = Mathf.Clamp(volumeFraction, 0.01f, 0.9f);

            // Volume → radius: the piece carries f of the CURRENT volume, the body keeps 1-f.
            // (Cube root: the slime reads as a 3D blob even in 2.5D play.)
            float before = slime.BodyScaleRatio;
            // pieceSizeScale inflates only how the piece LOOKS (mesh + collider follow);
            // the volume bookkeeping below stays honest to f.
            float dropletR = slime.SlimeRadius * Mathf.Pow(f, 1f / 3f) * settings.pieceSizeScale;

            preScale = before;
            postScale = before * Mathf.Pow(1f - f, 1f / 3f);

            // The lost volume (full-body units) rides on the chunk; it comes back through
            // NotifyChunkAbsorbed or NotifyChunkLost — never silently.
            float lostVolume = preScale * preScale * preScale - postScale * postScale * postScale;
            SpawnSeveredPiece(outDir, inheritVel, dropletR, lostVolume);

            phase = settings.cutHoldDuration > 0f ? CutPhase.Hold : CutPhase.Close;
            phaseTimer = 0f;

            // Wound starts as a quivering, slightly concave dent.
            meniscusW = -settings.cutConcaveDepthFrac;
            meniscusWVel = 0f;
            quiverEnv = 1f;

            // Cut-face plane: depth is volume-true (spherical cap holding fraction f),
            // clamped off-center so the clip can never erase the body core. WORLD-FIXED:
            // the plane never moves again — all healing motion is the membrane's.
            clipNormalWorld = outDir;
            clipDepthStartFrac = Mathf.Max(0.02f, CapPlaneOffsetFrac(f));

            recutTimer = settings.recutMinInterval;
            IsActive = true;
        }

        private void Update()
        {
            if (recutTimer > 0f) recutTimer -= Time.deltaTime;
            if (!IsActive || slime == null) return;
            float dt = Time.deltaTime;
            float sdt = Mathf.Min(dt, 0.05f);   // editor-hitch guard for the springs

            // Strike quiver decays freely; merges re-kick it.
            quiverEnv *= Mathf.Exp(-settings.quiverDecay * dt);
            quiverPhase += dt * settings.quiverFreq * 2f * Mathf.PI;
            if (quiverPhase > 2f * Mathf.PI) quiverPhase -= 2f * Mathf.PI;

            UpdateCutPhase(dt, sdt);
            UpdateMerge(dt);
            UpdateSlowRecovery(dt);

            // Launch power derives from the body — small slime = weak jump, healed = full.
            float ratio = slime.BodyScaleRatio;
            slime.SplitLaunchMultiplier = ratio >= 0.999f
                ? 1f
                : Mathf.Lerp(settings.woundedJumpMultiplierMin, 1f,
                             Mathf.InverseLerp(settings.woundedScaleRef, 1f, ratio));

            // Fully healed + nothing in flight → quiet reset to the byte-for-byte idle state.
            if (phase == CutPhase.None && !merging && mergeQueue.Count == 0
                && slowPool <= 1e-6f && ratio >= 0.999f)
            {
                RestoreAll();
            }
        }

        private void UpdateCutPhase(float dt, float sdt)
        {
            switch (phase)
            {
                case CutPhase.Hold:
                    phaseTimer += dt;
                    meniscusW = -settings.cutConcaveDepthFrac;   // dented; the quiver supplies the motion
                    meniscusWVel = 0f;
                    if (phaseTimer >= settings.cutHoldDuration)
                    {
                        phase = CutPhase.Close;
                        phaseTimer = 0f;
                    }
                    break;

                case CutPhase.Close:
                {
                    // PHYSICS ramps along closeBodyCurve — the collider shrinks WITH the
                    // visual (the visual spring chases this ramp with goo lag + slosh),
                    // so collision and render never diverge mid-transition.
                    phaseTimer += dt;
                    float u = Mathf.Clamp01(phaseTimer / Mathf.Max(0.05f, settings.closeDuration));
                    float bodyP = RecoveryEval(settings.closeBodyCurve, u);
                    slime.SetBodyScale(Mathf.Lerp(preScale, postScale, bodyP));

                    // SEAL = VISIBLE SHRINK, locked. The wound closes in lockstep with the
                    // DISPLAYED (spring-lagged) size — no independent seal timeline, so the
                    // "body already small but wound still open" desync can't exist.
                    float disp = slimeVisual != null ? slimeVisual.DisplayedBodyScale : slime.BodyScaleRatio;
                    float shrinkP = preScale - postScale > 1e-4f
                        ? Mathf.Clamp01(Mathf.InverseLerp(preScale, postScale, disp))
                        : u;
                    float prevW = meniscusW;
                    meniscusW = Mathf.Lerp(-settings.cutConcaveDepthFrac, 1f, shrinkP);
                    meniscusWVel = (meniscusW - prevW) / Mathf.Max(dt, 1e-4f);   // Settle spring handoff

                    if (u >= 1f)
                    {
                        slime.SetBodyScale(postScale);
                        if (slimeVisual != null) slimeVisual.SetCutGatherOffset(Vector2.zero);
                        phase = CutPhase.Settle;
                        // phaseTimer keeps running — it doubles as the settle timeout.
                    }
                    break;
                }

                case CutPhase.Settle:
                    // The displayed size finishes its slosh; the membrane spring (seeded with
                    // the Close-end closing speed) rides the last stretch to 1 with a small
                    // welling overshoot. Zero clip area → hand off to the lip relax.
                    phaseTimer += dt;
                    meniscusWVel += (settings.meniscusSpringK * (1f - meniscusW)
                                     - settings.meniscusSpringDamping * meniscusWVel) * sdt;
                    meniscusW += meniscusWVel * sdt;
                    if (meniscusW >= 0.997f || phaseTimer >= settings.closeDuration + 1f)
                    {
                        phase = CutPhase.Relax;
                        relaxTimer = 0f;
                        relaxPhase = quiverPhase;   // continue the ripple, don't restart it
                        // The membranes-just-merged "뭉클" — one ripple through the patch.
                        quiverEnv = Mathf.Max(quiverEnv, settings.sealRelaxWobbleKick);
                    }
                    break;

                case CutPhase.Relax:
                    // Membrane stays merged with the surface; the rim bump melts down.
                    relaxTimer += dt;
                    relaxPhase += dt * settings.sealRelaxFreq * 2f * Mathf.PI;
                    meniscusWVel += (settings.meniscusSpringK * (1f - meniscusW)
                                     - settings.meniscusSpringDamping * meniscusWVel) * sdt;
                    meniscusW += meniscusWVel * sdt;
                    if (Mathf.Exp(-settings.sealRelaxDecay * relaxTimer) < 0.02f)
                        phase = CutPhase.None;
                    break;
            }
        }

        private void UpdateMerge(float dt)
        {
            if (!merging && mergeQueue.Count > 0)
            {
                PendingMerge pm = mergeQueue.Dequeue();
                merging = true;
                mergeTimer = 0f;
                mergeDirWorld = pm.dirWorld;

                if (phase == CutPhase.None)
                {
                    // Idle swallow: the merge ramp owns SetBodyScale.
                    mergeOwnsRamp = true;
                    mergeStartRatio = slime.BodyScaleRatio;
                    float vol = mergeStartRatio * mergeStartRatio * mergeStartRatio + pm.volume;
                    mergeTargetRatio = Mathf.Min(1f, Mathf.Pow(vol, 1f / 3f));
                }
                else
                {
                    // Mid-transition swallow: fold the volume into the CLOSE ramp's
                    // DESTINATION — the shrink blends straight into the regrow as one
                    // motion. (Never "finish shrinking, then grow back".)
                    mergeOwnsRamp = false;
                    float vol = postScale * postScale * postScale + pm.volume;
                    postScale = Mathf.Min(1f, Mathf.Pow(vol, 1f / 3f));
                }

                // Swallow feedback: one outward slosh + the contact patch trembles.
                if (slimeVisual != null && settings.absorbPopKick > 0f)
                    slimeVisual.AddBodyScaleImpulse(settings.absorbPopKick);
                quiverEnv = Mathf.Max(quiverEnv, settings.mergeWobbleKick);
            }

            if (!merging) return;

            mergeTimer += dt;
            float m = Mathf.Clamp01(mergeTimer / Mathf.Max(0.05f, settings.mergeDuration));
            if (mergeOwnsRamp)
            {
                float eased = m * m * (3f - 2f * m);   // smoothstep — quick gulp, soft finish
                slime.SetBodyScale(Mathf.Lerp(mergeStartRatio, mergeTargetRatio, eased));
            }
            if (m >= 1f) merging = false;
        }

        private void UpdateSlowRecovery(float dt)
        {
            // Lost-chunk fallback: ambient regen, full body volume per slowRecoveryDuration.
            if (slowPool <= 0f || merging || phase != CutPhase.None) return;
            float ratio = slime.BodyScaleRatio;
            if (ratio >= 0.999f) { slowPool = 0f; return; }

            float dv = Mathf.Min(slowPool, dt / Mathf.Max(1f, settings.slowRecoveryDuration));
            slowPool -= dv;
            float vol = Mathf.Min(1f, ratio * ratio * ratio + dv);
            slime.SetBodyScale(Mathf.Pow(vol, 1f / 3f));
        }

        // Push wound + merge bulge after SlimeVisual's writes (execution order 110).
        // Slot 0 = the cut wound (clip + membrane), slot 1 = the absorption bulge (a
        // membrane pushed PAST the surface: clips nothing, but the lip/wobble/heal-goo
        // machinery lights up the contact patch — the chunk visibly squeezes in).
        private void LateUpdate()
        {
            if (slime == null || bodyRenderer == null) return;

            // Relax = viscous flattening, not an in-place pulse: the bump's FOOT widens
            // (band spread) while its HEIGHT sinks, an upward-only bounce riding the decay
            // (no sign flip — the inverted ring dent read as an artifact, not goo). Held at
            // rest values while a merge bulge is active (shared scalars; the bulge needs
            // its full lip).
            lipMagScale = 1f;
            lipBandScale = 1f;
            if (phase == CutPhase.Relax && !merging)
            {
                float env = Mathf.Exp(-settings.sealRelaxDecay * relaxTimer);
                float bounce = 1f + settings.sealRelaxBounce * env
                                  * Mathf.Sin(2f * Mathf.PI * settings.sealRelaxFreq * relaxTimer);
                lipMagScale = env * Mathf.Max(0f, bounce);
                lipBandScale = 1f + settings.sealRelaxSpread * (1f - env);
            }

            int count = 0;
            if (phase != CutPhase.None && BuildCutSlice(count)) count++;
            if (merging && BuildMergeSlice(count)) count++;

            // The wound is RETAINED state on the renderer (an MPB push, not an immediate-mode
            // draw): it shows whatever was pushed last until someone pushes count=0. So this
            // layer must terminate ITSELF — whoever flips the state off, whenever, the next
            // LateUpdate sees a shown wound with no source and clears it.
            if (count == 0)
            {
                if (woundShown) PushSlices(0);
                return;
            }
            PushSlices(count);
        }

        // Fills slice slot [i] with the cut wound. Returns false when the wound has
        // naturally vanished (body shrunk past the world-fixed plane).
        private bool BuildCutSlice(int i)
        {
            float s = slimeVisual != null ? slimeVisual.DisplayedBodyScale : slime.BodyScaleRatio;
            float baseR = slime.SlimeRadius / Mathf.Max(0.01f, slime.BodyScaleRatio);   // full-size world radius

            // World plane → rest-OS plane through a reference matrix of (live rotation ×
            // rest scale × displayed body scale): rotation compensated → the face stays
            // world-oriented; squash/stretch NOT compensated → the face deforms with the
            // body; displayed shrink compensated → the plane stays put while the ball
            // shrinks around it.
            Vector3 nWorld = new Vector3(clipNormalWorld.x, clipNormalWorld.y, 0f);
            Vector3 normalOS = nWorld;
            float osPerWorld = 1f;
            Transform body3dT = slimeVisual != null ? slimeVisual.Body3d : null;
            if (body3dT != null && bodyFilter != null)
            {
                Matrix4x4 child = body3dT.worldToLocalMatrix * bodyFilter.transform.localToWorldMatrix;
                Matrix4x4 lref = Matrix4x4.TRS(body3dT.localPosition, body3dT.localRotation,
                                               slimeVisual.Body3dRestScale * s);
                Matrix4x4 parent = body3dT.parent != null ? body3dT.parent.localToWorldMatrix : Matrix4x4.identity;
                Matrix4x4 mRefT = (parent * lref * child).transpose;   // plane normals pull back through Mᵀ
                Vector3 v = mRefT.MultiplyVector(nWorld);
                float vLen = v.magnitude;
                if (vLen > 1e-6f) { normalOS = v / vLen; osPerWorld = 1f / vLen; }
            }

            // World-fixed plane in rest OS. As the displayed body shrinks, off grows toward
            // the mesh extent — the wound can vanish naturally when the ball contracts past
            // the plane; the membrane handles every other closing motion.
            float off = clipDepthStartFrac * baseR * osPerWorld;
            float support = SupportAlongOS(normalOS);
            float capH = support - off;
            if (capH <= 0.02f * support)
            {
                if (phase == CutPhase.Settle || phase == CutPhase.Relax) phase = CutPhase.None;
                return false;
            }

            // Membrane anchored at the FIXED rim circle (plane ∩ body surface). The 1.06
            // margin makes w=1 push the membrane slightly past the blob (it isn't a perfect
            // sphere), guaranteeing zero clip area before the wound switches off.
            float rho2 = Mathf.Max(support * support - off * off, 0.0025f * support * support);
            float b = meniscusW * capH * 1.06f;

            // L2 — asymmetric gather: peak magnitude = the lost radius (so at gatherAmount 1
            // the cut face holds still in the world while the far side flows toward it).
            // Max(0,...) on the loss: a mid-transition merge can fold enough volume in that
            // postScale exceeds preScale — gather must not flip away from the cut.
            if (slimeVisual != null && phase == CutPhase.Close)
            {
                float u = Mathf.Clamp01(phaseTimer / Mathf.Max(0.05f, settings.closeDuration));
                float gather = settings.gatherAmount * Mathf.Max(0f, preScale - postScale) * baseR
                             * Mathf.Max(0f, RecoveryEval(settings.gatherCurve, u));
                slimeVisual.SetCutGatherOffset(clipNormalWorld * gather);
            }

            float seal = Mathf.Clamp01(meniscusW);
            slicePlaneOS[i] = new Vector4(normalOS.x, normalOS.y, normalOS.z, off);
            sliceMeniscus[i] = new Vector4(b, rho2, 0f, 0f);
            sliceSeal[i] = seal;
            sliceHeal[i] = 0f;   // fresh-goo glow band removed (user: 회복 진행이 빛으로 티남)

            // Strike phase: high-frequency quiver (struck jelly). Relax phase: the same
            // boundary ripple but SLOW and long-waved — heavy goo sloshing as it settles,
            // not a buzz.
            sliceWobble[i] = phase == CutPhase.Relax
                ? new Vector4(settings.quiverAmp * meshRadiusOS * quiverEnv,
                              relaxPhase, settings.quiverSpatialFreq * 0.5f, 0f)
                : new Vector4(settings.quiverAmp * meshRadiusOS * quiverEnv,
                              quiverPhase, settings.quiverSpatialFreq, 0f);
            return true;
        }

        // Fills slice slot [i] with the absorption bulge at the contact patch.
        private bool BuildMergeSlice(int i)
        {
            Vector3 nWorld = new Vector3(mergeDirWorld.x, mergeDirWorld.y, 0f);
            Vector3 normalOS = nWorld;
            Transform body3dT = slimeVisual != null ? slimeVisual.Body3d : null;
            float s = slimeVisual != null ? slimeVisual.DisplayedBodyScale : slime.BodyScaleRatio;
            if (body3dT != null && bodyFilter != null)
            {
                Matrix4x4 child = body3dT.worldToLocalMatrix * bodyFilter.transform.localToWorldMatrix;
                Matrix4x4 lref = Matrix4x4.TRS(body3dT.localPosition, body3dT.localRotation,
                                               slimeVisual.Body3dRestScale * s);
                Matrix4x4 parent = body3dT.parent != null ? body3dT.parent.localToWorldMatrix : Matrix4x4.identity;
                Vector3 v = (parent * lref * child).transpose.MultiplyVector(nWorld);
                if (v.sqrMagnitude > 1e-12f) normalOS = v.normalized;
            }

            float support = SupportAlongOS(normalOS);
            float off = settings.mergeFaceFrac * support;
            float capH = support - off;
            float rho2 = Mathf.Max(support * support - off * off, 0.0025f * support * support);

            // Membrane 1.2× past the surface: nothing clips, but fragments at the contact
            // patch sit just inside the boundary → the lip swells them outward — the chunk
            // visibly squeezes in. Envelope 1→0 over merge. No tint (glow removed).
            float m = Mathf.Clamp01(mergeTimer / Mathf.Max(0.05f, settings.mergeDuration));
            float env = 1f - m * m;
            slicePlaneOS[i] = new Vector4(normalOS.x, normalOS.y, normalOS.z, off);
            sliceMeniscus[i] = new Vector4(capH * 1.2f, rho2, 0f, 0f);
            sliceSeal[i] = env;                                   // gates the vertex lip
            sliceHeal[i] = 0f;
            sliceWobble[i] = new Vector4(settings.quiverAmp * meshRadiusOS * quiverEnv,
                                         quiverPhase, settings.quiverSpatialFreq, 0f);
            return env > 1e-3f;
        }

        // Support extent of the rest mesh along an OS direction (AABB support function) —
        // the farthest any vertex can sit along n, used as the local "body radius".
        private float SupportAlongOS(Vector3 nOS)
        {
            Bounds mb = origMesh.bounds;
            return Vector3.Dot(mb.center, nOS)
                 + mb.extents.x * Mathf.Abs(nOS.x)
                 + mb.extents.y * Mathf.Abs(nOS.y)
                 + mb.extents.z * Mathf.Abs(nOS.z);
        }

        // True while the renderer is showing a wound (last push had count > 0).
        private bool woundShown;

        private void PushSlices(int count)
        {
            if (bodyRenderer == null) return;
            woundShown = count > 0;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            bodyRenderer.GetPropertyBlock(mpb);
            mpb.SetFloat(SliceCountID, count);
            mpb.SetVectorArray(SlicePlaneOSID, slicePlaneOS);
            mpb.SetFloatArray(SliceSealID, sliceSeal);
            mpb.SetFloatArray(SliceHealID, sliceHeal);
            mpb.SetVectorArray(SliceMeniscusID, sliceMeniscus);
            mpb.SetVectorArray(SliceWobbleID, sliceWobble);
            mpb.SetFloat(TearAmpID, settings.tearAmp);
            mpb.SetFloat(TearFreqID, settings.tearFreq);
            mpb.SetFloat(LipBandID, settings.lipBand * lipBandScale);
            mpb.SetFloat(LipMagID, settings.lipMag * lipMagScale);
            mpb.SetColor(SliceCapColorID, settings.sliceCapColor);
            mpb.SetColor(SliceCapRimColorID, settings.sliceCapRimColor);
            mpb.SetFloat(SliceCapDepthID, settings.sliceCapDepth);
            // Bright white-ish extras from the old slice look — forced OFF (user: 꼴보기 싫음).
            // Explicit zeros so a material-asset default can't resurrect them.
            mpb.SetFloat(CapWetEdgeWidthID, 0f);
            mpb.SetFloat(CapMarbleAmpID, 0f);
            bodyRenderer.SetPropertyBlock(mpb);
        }

        // Plane offset (fraction of R, from center toward the removed side) of the spherical
        // cap holding volume fraction f. Solves f = h²(3-h)/4 for cap height h (R=1 units)
        // by Newton iteration, then offset = 1-h. f=0.3 → ≈0.34, f=0.5 → 0 (hemisphere).
        private static float CapPlaneOffsetFrac(float f)
        {
            float h = 1f;
            for (int i = 0; i < 12; i++)
            {
                float g = h * h * (3f - h) * 0.25f - f;
                float dg = (6f * h - 3f * h * h) * 0.25f;
                if (Mathf.Abs(dg) < 1e-5f) break;
                h -= g / dg;
                h = Mathf.Clamp(h, 0.001f, 2f);
            }
            return 1f - h;
        }

        // --- internals ---

        // Recovery curve eval, robust to a null / cleared (empty) curve → falls back to linear so
        // the slime always regrows instead of stalling small.
        private static float RecoveryEval(AnimationCurve c, float t)
        {
            return (c != null && c.length > 1) ? c.Evaluate(t) : t;
        }

        private float meshRadiusOS = 0.5f;   // max mesh extent — OS scale for quiver amplitude

        private bool ResolveBody()
        {
            bodyFilter = slime.GetComponentInChildren<MeshFilter>(true);   // SlimeMesh (body)
            if (bodyFilter == null) return false;
            bodyRenderer = bodyFilter.GetComponent<MeshRenderer>();
            origMesh = bodyFilter.sharedMesh;
            if (origMesh != null)
                meshRadiusOS = Mathf.Max(origMesh.bounds.extents.x,
                                         Mathf.Max(origMesh.bounds.extents.y, origMesh.bounds.extents.z));
            if (slimeVisual == null) slimeVisual = slime.GetComponentInChildren<SlimeVisual>(true);
            return bodyRenderer != null && origMesh != null;
        }

        // The severed piece is the slime's OWN round blob mesh, spawned at the lost volume's
        // size. SlimeChunk flattens it to a sliver along the fling direction at spawn, shows
        // the same meniscus-healing cut face as the body, then rests on the ground waiting to
        // be re-absorbed — a genuine piece of the slime, not a particle.
        private void SpawnSeveredPiece(Vector2 outDir, Vector2 inheritVel, float dropletR, float lostVolume)
        {
            // Spawn at the slime's PHYSICS position (rb), per project convention.
            Vector3 worldPos = slime.Position;

            float meshOS = Mathf.Max(origMesh.bounds.extents.x, Mathf.Max(origMesh.bounds.extents.y, origMesh.bounds.extents.z));
            if (meshOS < 1e-4f) meshOS = 0.5f;

            Material mat = chunkMaterial != null ? chunkMaterial
                         : (bodyRenderer.sharedMaterials.Length > 0 ? bodyRenderer.sharedMaterials[0] : null);

            var root = new GameObject("SlimeDroplet");
            root.layer = slime.gameObject.layer;
            root.transform.position = worldPos;

            var meshGo = new GameObject("Mesh");
            meshGo.transform.SetParent(root.transform, false);
            var mf = meshGo.AddComponent<MeshFilter>();
            mf.sharedMesh = origMesh;
            var mr = meshGo.AddComponent<MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;
            meshGo.transform.localScale = Vector3.one * (dropletR / meshOS);   // uniform → round ball

            root.AddComponent<Rigidbody2D>();
            var col = root.AddComponent<CircleCollider2D>();

            // Fit the collider to the round droplet so it rests planted on the ground.
            Bounds wb = mr.bounds;
            col.offset = (Vector2)root.transform.InverseTransformPoint(wb.center);
            float ls = Mathf.Abs(root.transform.lossyScale.x);
            if (ls < 1e-4f) ls = 1f;
            float worldR = (wb.extents.x + wb.extents.y) * 0.5f * settings.colliderRadiusFactor;
            col.radius = Mathf.Max(0.02f, worldR / ls);

            // ENGINE-LEVEL exclusion against the player: the chunk lives indefinitely now, so
            // a timer-based ignore list can't cover it (and the launch-clearance sweep used to
            // wipe such entries — the "chunk under the slime = forever airborne" bug). The
            // probe never sees it either: the slime's own layer is not in groundLayer.
            col.excludeLayers = 1 << slime.gameObject.layer;

            var chunk = root.AddComponent<SlimeChunk>();
            chunk.Init(settings, this, slime, inheritVel, outDir, slime.GravityVector, lostVolume);
        }

        private void RestoreAll()
        {
            if (slime != null)
            {
                slime.SetBodyScale(1f);
                slime.SplitLaunchMultiplier = 1f;
            }
            if (woundShown) PushSlices(0);
            if (slimeVisual != null) slimeVisual.SetCutGatherOffset(Vector2.zero);
            phase = CutPhase.None;
            phaseTimer = 0f;
            merging = false;
            mergeQueue.Clear();
            slowPool = 0f;
            quiverEnv = 0f;
            IsActive = false;
        }

        private void OnDestroy()
        {
            if (IsActive) RestoreAll();
        }

        private static Vector2 Rotate(Vector2 v, float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }
    }
}
