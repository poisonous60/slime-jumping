using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Settings;

namespace SlimeJumping.Effects
{
    /// <summary>
    /// Cut slime = smaller slime: the severed piece is a real pickup (SlimeChunk) that flies
    /// off and is re-absorbed; the wound is a meniscus anchored at a world-fixed rim that
    /// wells outward until it coincides with the body sphere. Healing is absorption-gated —
    /// a chunk lost forever converts to slow regen so the slime can't be soft-locked small.
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

        // HOLD: full size, concave dent. CLOSE: meniscus + body gather + physics shrink.
        // SETTLE: membrane spring's last overshoot until the clip area reaches zero.
        // RELAX: the lip is geometrically pinned to the rim circle, so switching the wound off
        //        here would POP the lip ring off in one frame — it sloshes to zero first.
        private enum CutPhase { None, Hold, Close, Settle, Relax }
        private CutPhase phase = CutPhase.None;
        private float phaseTimer;
        private float relaxTimer;
        private float relaxPhase;          // slow slosh phase for the Relax wobble (Hz = sealRelaxFreq)
        private float lipMagScale = 1f;    // Relax: bump height sinks (with an upward-only bounce)
        private float lipBandScale = 1f;   // Relax: bump foot spreads — viscous flattening
        private float preScale;            // BodyScaleRatio at the cut — ramp START only, never the volume base
        private float postScale;           // BodyScaleRatio this cut's volume loss ramps toward
        private float rampAbsorbedVol;     // swallowed mid-ramp; the ramp adds it so it can't be overwritten

        // Wounds STACK: each keeps its own world-fixed plane and stays drawn, but they all seal on
        // ONE clock (the newest cut's), so overlapping cuts close together instead of racing.
        private const int CutMax = 2;      // + 1 merge bulge = SliceMax
        private readonly Vector2[] cutNormalWorld = new Vector2[CutMax];   // unit, toward the REMOVED side
        private readonly float[] cutDepthFrac = new float[CutMax];         // plane offset, fraction of full radius
        private int cutCount;

        // Meniscus spring: w = the seal progress the membrane DISPLAYS. Target runs
        // -cutConcaveDepthFrac → 1 (membrane == body sphere); underdamped for the welling.
        private float meniscusW;
        private float meniscusWVel;

        // Strike quiver envelope feeding _SliceWobble; also re-kicked by absorption merges.
        private float quiverEnv;
        private float quiverPhase;

        // Absorption-gated healing. Volumes are in FULL-BODY units (ratio³).
        private readonly Queue<PendingMerge> mergeQueue = new();
        private bool merging;
        private float mergeTimer;
        private float mergeVolume;         // this merge's total, handed over across mergeDuration
        private float mergeDelivered;      // how much of it the body already got
        private bool mergeLocal;           // draw the contact-point bulge; false = uniform growth only
        private float woundQuietTime = 999f;   // seconds since the wound stopped performing (999 = none yet)

        private bool WoundBusy => phase == CutPhase.Hold || phase == CutPhase.Close
                               || phase == CutPhase.Settle;
        private Vector2 mergeDirWorld;     // body center → contact point (world)
        private float slowPool;            // volume owed by LOST chunks, regenerated slowly
        private float slowRate;            // volume/s that drains slowPool in slowRecoveryDuration

        private struct PendingMerge
        {
            public Vector2 dirWorld;
            public float volume;
            public bool atContact;   // false = it had to wait out a wound, so the contact point is stale
        }

        private const int SliceMax = 3;    // must match SLICE_MAX in SlimeSlice.hlsl
        private readonly Vector4[] slicePlaneOS = new Vector4[SliceMax];
        private readonly float[] sliceSeal = new float[SliceMax];
        private readonly float[] sliceHeal = new float[SliceMax];
        private readonly Vector4[] sliceMeniscus = new Vector4[SliceMax];
        private readonly Vector4[] sliceWobble = new Vector4[SliceMax];
        // Rim displacement per slot: negative = inward fillet (cut), positive = outward bulge (merge).
        private readonly float[] sliceLip = new float[SliceMax];

        private static readonly int SliceCountID = Shader.PropertyToID("_SliceCount");
        private static readonly int SlicePlaneOSID = Shader.PropertyToID("_SlicePlaneOS");
        private static readonly int SliceSealID = Shader.PropertyToID("_SliceSeal");
        private static readonly int SliceHealID = Shader.PropertyToID("_SliceHeal");
        private static readonly int SliceMeniscusID = Shader.PropertyToID("_SliceMeniscus");
        private static readonly int SliceWobbleID = Shader.PropertyToID("_SliceWobble");
        private static readonly int TearAmpID = Shader.PropertyToID("_TearAmp");
        private static readonly int TearFreqID = Shader.PropertyToID("_TearFreq");
        private static readonly int LipBandID = Shader.PropertyToID("_LipBand");
        private static readonly int SliceLipID = Shader.PropertyToID("_SliceLip");
        private static readonly int SliceCapColorID = Shader.PropertyToID("_SliceCapColor");
        private static readonly int SliceCapRimColorID = Shader.PropertyToID("_SliceCapRimColor");
        private static readonly int SliceCapDepthID = Shader.PropertyToID("_SliceCapDepth");
        private static readonly int CapWetEdgeWidthID = Shader.PropertyToID("_CapWetEdgeWidth");
        private static readonly int CapMarbleAmpID = Shader.PropertyToID("_CapMarbleAmp");

        public bool IsActive { get; private set; }

        /// <summary>Blade cut: piece flies perpendicular to entry, body loses cutVolumeFraction.</summary>
        /// <param name="entryVelocity">Must be read BEFORE the caller applies knockback.</param>
        public void Begin(SlimeController s, Vector2 entryVelocity)
        {
            Vector2 v = entryVelocity.sqrMagnitude > 1e-4f ? entryVelocity.normalized : Vector2.right;
            Vector2 n = new Vector2(-v.y, v.x);
            Vector2 outDir = Rotate(n, Random.Range(-settings.cutNormalJitterDeg, settings.cutNormalJitterDeg));
            ApplyCut(s, outDir, entryVelocity, settings.cutVolumeFraction);
        }

        /// <summary>Mid-air self-split along ejectDir. The recoil impulse is the CALLER's job.</summary>
        public void BeginSelfSplit(SlimeController s, Vector2 ejectDir)
        {
            Vector2 dir = ejectDir.sqrMagnitude > 1e-4f ? ejectDir.normalized : Vector2.down;
            ApplyCut(s, dir, s != null ? s.Velocity : Vector2.zero, settings.selfSplitVolumeFraction);
        }

        /// <summary>A chunk reached the slime; dirWorld = body center → contact point.</summary>
        public void NotifyChunkAbsorbed(Vector2 dirWorld, float volume)
        {
            if (volume <= 0f) return;
            mergeQueue.Enqueue(new PendingMerge
            {
                dirWorld = dirWorld.sqrMagnitude > 1e-6f ? dirWorld.normalized : Vector2.up,
                volume = volume,
                atContact = !WoundBusy && mergeQueue.Count == 0 && !merging,
            });
            IsActive = true;
        }

        /// <summary>Chunk gone forever: volume converts to slow regen so the slime can't lock small.</summary>
        public void NotifyChunkLost(float volume)
        {
            if (volume <= 0f) return;
            slowPool += volume;
            // Rate is fixed at loss time so the pool always drains in slowRecoveryDuration flat,
            // however much was lost. A second loss re-spreads the leftover over a fresh duration.
            slowRate = slowPool / Mathf.Max(0.05f, settings.slowRecoveryDuration);
            IsActive = true;
        }

        private void ApplyCut(SlimeController s, Vector2 outDir, Vector2 inheritVel, float volumeFraction)
        {
            if (s == null || settings == null) return;
            if (recutTimer > 0f) return;            // internal re-cut guard (same-frame / double hazard)

            slime = s;
            if (!ResolveBody()) return;

            // A new cut banks the in-flight merge's undelivered volume, else it is silently lost.
            if (merging)
            {
                AddVolume(mergeVolume - mergeDelivered);
                merging = false;
            }

            float f = Mathf.Clamp(volumeFraction, 0.01f, 0.9f);

            // Cube root volume → radius: the slime reads as a 3D blob even in 2.5D play.
            float before = slime.BodyScaleRatio;
            // pieceSizeScale inflates only how the piece LOOKS; volume bookkeeping stays honest to f.
            float dropletR = slime.SlimeRadius * Mathf.Pow(f, 1f / 3f) * settings.pieceSizeScale;

            // f comes off the volume the running ramp is still headed for, not off the live ratio —
            // the live one is mid-shrink, so cutting it again would forgive all the pending loss.
            bool ramping = phase == CutPhase.Hold || phase == CutPhase.Close;
            float baseVol = ramping ? postScale * postScale * postScale + rampAbsorbedVol
                                    : before * before * before;

            preScale = before;
            postScale = Mathf.Pow(baseVol * (1f - f), 1f / 3f);
            rampAbsorbedVol = 0f;

            // Lost volume rides on the chunk; it returns via NotifyChunkAbsorbed/Lost, never silently.
            float lostVolume = baseVol * f;
            float planeFrac = Mathf.Max(0.02f, CapPlaneOffsetFrac(f) * preScale);
            float fullR = slime.SlimeRadius / Mathf.Max(0.01f, slime.BodyScaleRatio);
            SpawnSeveredPiece(outDir, inheritVel, dropletR, lostVolume, planeFrac * fullR);

            // Each wound keeps its own world-fixed orientation and depth — all healing motion is the
            // membrane's. Depth is the volume-true spherical cap of the body being cut (× preScale,
            // since the cap solve is in units of THAT body's radius), clamped off-center so the clip
            // can't erase the core. Stored as a fraction of the FULL radius.
            if (phase == CutPhase.None) cutCount = 0;
            if (cutCount >= CutMax)
            {
                for (int k = 1; k < CutMax; k++)
                {
                    cutNormalWorld[k - 1] = cutNormalWorld[k];
                    cutDepthFrac[k - 1] = cutDepthFrac[k];
                }
                cutCount = CutMax - 1;
            }
            cutNormalWorld[cutCount] = outDir;
            cutDepthFrac[cutCount] = planeFrac;
            CaptureFaceCut(cutCount, outDir, planeFrac * fullR);
            cutCount++;

            phase = settings.cutHoldDuration > 0f ? CutPhase.Hold : CutPhase.Close;
            phaseTimer = 0f;

            meniscusW = -settings.cutConcaveDepthFrac;
            meniscusWVel = 0f;
            quiverEnv = 1f;

            recutTimer = settings.recutMinInterval;
            IsActive = true;
        }

        private void Update()
        {
            if (recutTimer > 0f) recutTimer -= Time.deltaTime;
            if (!IsActive || slime == null) return;
            float dt = Time.deltaTime;
            float sdt = Mathf.Min(dt, 0.05f * Time.timeScale);   // editor-hitch guard for the springs

            quiverEnv *= Mathf.Exp(-settings.quiverDecay * dt);
            quiverPhase += dt * settings.quiverFreq * 2f * Mathf.PI;
            if (quiverPhase > 2f * Mathf.PI) quiverPhase -= 2f * Mathf.PI;

            UpdateCutPhase(dt, sdt);
            UpdateMerge(dt);
            UpdateSlowRecovery(dt);
            PushCutShape();

            // Launch power derives from the body — small slime = weak jump, healed = full.
            float ratio = slime.BodyScaleRatio;
            slime.SplitLaunchMultiplier = ratio >= 0.999f
                ? 1f
                : Mathf.Lerp(settings.woundedJumpMultiplierMin, 1f,
                             Mathf.InverseLerp(settings.woundedScaleRef, 1f, ratio));

            // Fully healed + nothing in flight → quiet reset to idle.
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
                    // Collider shrinks WITH the visual spring, so collision and render never diverge.
                    phaseTimer += dt;
                    float u = Mathf.Clamp01(phaseTimer / Mathf.Max(0.05f, settings.closeDuration));
                    float bodyP = RecoveryEval(settings.closeBodyCurve, u);
                    float rampFrom = RampRatio(0f), rampTo = RampRatio(1f);
                    slime.SetBodyScale(RampRatio(bodyP));

                    // Seal is locked to the DISPLAYED (spring-lagged) size, never its own
                    // timeline — else "body already small but wound still open" desync. Measured
                    // against the ramp's own ends so a swallow mid-ramp can't stall the seal.
                    float disp = slimeVisual != null ? slimeVisual.DisplayedBodyScale : slime.BodyScaleRatio;
                    float shrinkP = Mathf.Abs(rampFrom - rampTo) > 1e-4f
                        ? Mathf.Clamp01(Mathf.InverseLerp(rampFrom, rampTo, disp))
                        : u;
                    float prevW = meniscusW;
                    meniscusW = Mathf.Lerp(-settings.cutConcaveDepthFrac, 1f, shrinkP);
                    meniscusWVel = (meniscusW - prevW) / Mathf.Max(dt, 1e-4f);   // Settle spring handoff

                    if (u >= 1f)
                    {
                        postScale = rampTo;          // fold the swallowed volume into the settled target
                        rampAbsorbedVol = 0f;
                        slime.SetBodyScale(postScale);
                        if (slimeVisual != null) slimeVisual.SetCutGatherOffset(Vector2.zero);
                        phase = CutPhase.Settle;
                        // phaseTimer keeps running — it doubles as the settle timeout.
                    }
                    break;
                }

                case CutPhase.Settle:
                    // Membrane spring rides the last stretch to 1; zero clip area hands off to Relax.
                    phaseTimer += dt;
                    meniscusWVel += (settings.meniscusSpringK * (1f - meniscusW)
                                     - settings.meniscusSpringDamping * meniscusWVel) * sdt;
                    meniscusW = Mathf.Min(1f, meniscusW + meniscusWVel * sdt);   // never past the body sphere
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
                    meniscusW = Mathf.Min(1f, meniscusW + meniscusWVel * sdt);   // never past the body sphere
                    if (Mathf.Exp(-settings.sealRelaxDecay * relaxTimer) < 0.02f)
                        phase = CutPhase.None;
                    break;
            }
        }

        private void UpdateMerge(float dt)
        {
            // One performance at a time: a piece swallowed while the wound is still welling shut
            // waits in the queue until the seal is done, then one beat, then the body fills.
            if (WoundBusy) woundQuietTime = 0f;
            else if (woundQuietTime < settings.mergeDeferDelay) woundQuietTime += dt;

            if (!merging && mergeQueue.Count > 0 && woundQuietTime >= settings.mergeDeferDelay)
            {
                PendingMerge pm = mergeQueue.Dequeue();
                merging = true;
                mergeTimer = 0f;
                mergeDirWorld = pm.dirWorld;
                mergeVolume = pm.volume;
                mergeDelivered = 0f;
                // A queued merge fires long after the piece touched, so a bulge aimed at the old
                // contact point reads as the body swelling toward nothing. Whole-body growth only.
                mergeLocal = pm.atContact;

                if (slimeVisual != null && settings.absorbPopKick > 0f)
                    slimeVisual.AddBodyScaleImpulse(settings.absorbPopKick);
                quiverEnv = Mathf.Max(quiverEnv, settings.mergeWobbleKick);
            }

            if (!merging) return;

            // The swallow always runs on ITS clock, whatever the cut ramp is doing. Handing the
            // volume to the ramp's destination instead made the body wait out the close curve.
            mergeTimer += dt;
            float m = Mathf.Clamp01(mergeTimer / Mathf.Max(0.05f, settings.mergeDuration));
            float eased = m * m * (3f - 2f * m);   // smoothstep — quick gulp, soft finish
            float step = mergeVolume * eased - mergeDelivered;
            mergeDelivered += step;
            AddVolume(step);
            if (m >= 1f) merging = false;
        }

        // Grows the body by a volume increment. Mid-ramp it also joins the ramp's accumulator,
        // or the next ramp frame would write the growth straight back off.
        private void AddVolume(float v)
        {
            if (v <= 1e-9f) return;
            if (phase == CutPhase.Hold || phase == CutPhase.Close) rampAbsorbedVol += v;
            float r = slime.BodyScaleRatio;
            slime.SetBodyScale(Mathf.Min(1f, Mathf.Pow(r * r * r + v, 1f / 3f)));
        }

        // CLOSE ramp position, with whatever was swallowed since the cut riding on top.
        private float RampRatio(float bodyP)
        {
            float r = Mathf.Lerp(preScale, postScale, bodyP);
            if (rampAbsorbedVol <= 0f) return r;
            return Mathf.Min(1f, Mathf.Pow(r * r * r + rampAbsorbedVol, 1f / 3f));
        }

        private void UpdateSlowRecovery(float dt)
        {
            // Lost-chunk fallback: ambient regen, the lost volume back over slowRecoveryDuration.
            if (slowPool <= 0f || merging || phase != CutPhase.None) return;
            float ratio = slime.BodyScaleRatio;
            if (ratio >= 0.999f) { slowPool = 0f; return; }

            float dv = Mathf.Min(slowPool, slowRate * dt);
            slowPool -= dv;
            float vol = Mathf.Min(1f, ratio * ratio * ratio + dv);
            slime.SetBodyScale(Mathf.Pow(vol, 1f / 3f));
        }

        // Must run after SlimeVisual's writes (execution order 110).
        // Slot 0 = cut wound, slot 1 = absorption bulge (membrane past the surface, clips nothing).
        private void LateUpdate()
        {
            if (slime == null || bodyRenderer == null) return;

            // Relax = viscous flattening: foot widens, height sinks, upward-only bounce
            // (a sign flip reads as an inverted ring artifact, not goo). Scales the CUT slot only;
            // the merge bulge carries its own magnitude in the same array.
            lipMagScale = 1f;
            lipBandScale = 1f;
            if (phase == CutPhase.Relax)
            {
                float env = Mathf.Exp(-settings.sealRelaxDecay * relaxTimer);
                float bounce = 1f + settings.sealRelaxBounce * env
                                  * Mathf.Sin(2f * Mathf.PI * settings.sealRelaxFreq * relaxTimer);
                lipMagScale = env * Mathf.Max(0f, bounce);
                lipBandScale = 1f + settings.sealRelaxSpread * (1f - env);
            }

            int count = 0;
            if (phase != CutPhase.None) count = BuildCutSlices();
            cutSliceCount = count;   // the face is clipped by wounds only, never by the merge bulge
            if (merging && mergeLocal && BuildMergeSlice(count)) count++;

            // MPB state is RETAINED: the wound shows until someone pushes count=0, so this
            // layer must clear itself whenever the source disappears.
            if (count == 0)
            {
                if (woundShown) PushSlices(0);
                return;
            }
            PushSlices(count);
        }

        // One slice per open wound, written from slot 0. Returns how many; 0 once every wound has
        // vanished naturally (body shrunk past its fixed plane).
        private int BuildCutSlices()
        {
            float s = slimeVisual != null ? slimeVisual.DisplayedBodyScale : slime.BodyScaleRatio;
            float baseR = slime.SlimeRadius / Mathf.Max(0.01f, slime.BodyScaleRatio);   // full-size world radius

            // World plane → rest-OS through (live rotation × rest scale × displayed scale):
            // rotation and displayed shrink compensated, squash/stretch deliberately NOT —
            // the face stays world-oriented and fixed but still deforms with the body.
            // DIRECTION ONLY: no world POSITION enters here. rb.position is physics space while this
            // matrix is the interpolated render chain; mixing the two makes the plane lag the body by
            // the interpolation error every frame (= the wound stutters).
            Matrix4x4 mRefT = Matrix4x4.identity;
            bool haveRef = false;
            Transform body3dT = slimeVisual != null ? slimeVisual.Body3d : null;
            if (body3dT != null && bodyFilter != null)
            {
                Matrix4x4 child = body3dT.worldToLocalMatrix * bodyFilter.transform.localToWorldMatrix;
                Matrix4x4 lref = Matrix4x4.TRS(body3dT.localPosition, body3dT.localRotation,
                                               slimeVisual.Body3dRestScale * s);
                Matrix4x4 parent = body3dT.parent != null ? body3dT.parent.localToWorldMatrix : Matrix4x4.identity;
                mRefT = (parent * lref * child).transpose;   // plane normals pull back through Mᵀ
                haveRef = true;
            }

            // Shared across every wound — one seal clock, one ripple. Relax reuses the same boundary
            // ripple but slow and long-waved: heavy goo, not a buzz.
            float seal = Mathf.Clamp01(meniscusW);
            Vector4 wobble = phase == CutPhase.Relax
                ? new Vector4(settings.quiverAmp * meshRadiusOS * quiverEnv,
                              relaxPhase, settings.quiverSpatialFreq * 0.5f, 0f)
                : new Vector4(settings.quiverAmp * meshRadiusOS * quiverEnv,
                              quiverPhase, settings.quiverSpatialFreq, 0f);

            int n = 0;
            for (int k = 0; k < cutCount; k++)
            {
                Vector3 nWorld = new Vector3(cutNormalWorld[k].x, cutNormalWorld[k].y, 0f);
                Vector3 normalOS = nWorld;
                float osPerWorld = 1f;
                if (haveRef)
                {
                    Vector3 v = mRefT.MultiplyVector(nWorld);
                    float vLen = v.magnitude;
                    if (vLen > 1e-6f) { normalOS = v / vLen; osPerWorld = 1f / vLen; }
                }

                // As the displayed body shrinks, off grows toward the mesh extent — the wound
                // vanishes on its own once the ball contracts past the plane.
                float off = cutDepthFrac[k] * baseR * osPerWorld;
                float support = SupportAlongOS(normalOS);
                float capH = support - off;
                if (capH <= 0.02f * support) continue;

                // Membrane anchored at the FIXED rim circle. meniscusSealMargin pushes w=1 slightly
                // past the blob (not a perfect sphere) → zero clip area before the wound switches off.
                float rho2 = Mathf.Max(support * support - off * off, 0.0025f * support * support);

                int i = n++;
                slicePlaneOS[i] = new Vector4(normalOS.x, normalOS.y, normalOS.z, off);
                sliceMeniscus[i] = new Vector4(meniscusW * capH * settings.meniscusSealMargin,
                                               rho2, 0f, 0f);
                sliceSeal[i] = seal;
                sliceHeal[i] = 0f;   // glow band off by design
                sliceLip[i] = -settings.filletMag * lipMagScale;   // inward: the rim corner rounds off
                sliceWobble[i] = wobble;
            }

            // Gather peak = lost radius: at gatherAmount 1 the cut face holds still in world. Pulls
            // toward the NEWEST wound. Max(0,..): a mid-transition merge can push postScale >
            // preScale — gather must not flip.
            if (n > 0 && slimeVisual != null && phase == CutPhase.Close)
            {
                float u = Mathf.Clamp01(phaseTimer / Mathf.Max(0.05f, settings.closeDuration));
                float gather = settings.gatherAmount * Mathf.Max(0f, preScale - postScale) * baseR
                             * Mathf.Max(0f, RecoveryEval(settings.gatherCurve, u));
                slimeVisual.SetCutGatherOffset(cutNormalWorld[cutCount - 1] * gather);
            }

            if (n == 0 && (phase == CutPhase.Settle || phase == CutPhase.Relax)) phase = CutPhase.None;
            return n;
        }

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

            // Membrane 1.2× past the surface: nothing clips, but the lip swells the contact
            // patch outward — the chunk visibly squeezes in.
            float m = Mathf.Clamp01(mergeTimer / Mathf.Max(0.05f, settings.mergeDuration));
            float env = 1f - m * m;
            slicePlaneOS[i] = new Vector4(normalOS.x, normalOS.y, normalOS.z, off);
            sliceMeniscus[i] = new Vector4(capH * 1.2f, rho2, 0f, 0f);
            sliceSeal[i] = env;                                   // gates the vertex rim band
            sliceHeal[i] = 0f;
            sliceLip[i] = settings.mergeBulgeMag;                 // outward: the chunk squeezes in
            sliceWobble[i] = new Vector4(settings.quiverAmp * meshRadiusOS * quiverEnv,
                                         quiverPhase, settings.quiverSpatialFreq, 0f);
            return env > 1e-3f;
        }

        // Wounds in physics units for the collider builder. No render transform — interpolation lag
        // would jitter every contact.
        private void PushCutShape()
        {
            if (phase == CutPhase.None || cutCount == 0) { slime.ClearCutWounds(); return; }

            float R = slime.SlimeRadius;
            float fullR = R / Mathf.Max(0.01f, slime.BodyScaleRatio);
            float w = Mathf.Min(meniscusW, 1f);
            int n = 0;
            for (int k = 0; k < cutCount; k++)
            {
                float off = cutDepthFrac[k] * fullR;
                if (off >= R) continue;              // body shrank past this plane
                float capH = R - off;
                cutShapeScratch[n].normal = cutNormalWorld[k];
                cutShapeScratch[n].off = off;
                cutShapeScratch[n].b = w * capH;     // seal margin left out: physics must not exceed the flesh
                cutShapeScratch[n].rho0 = Mathf.Sqrt(Mathf.Max(1e-6f, R * R - off * off));
                n++;
            }
            if (n == 0) slime.ClearCutWounds();
            else slime.SetCutWounds(cutShapeScratch, n);
        }
        private readonly SlimeController.CutWound[] cutShapeScratch =
            new SlimeController.CutWound[CutMax];

        // AABB support along n — the local "body radius" of the rest mesh.
        private float SupportAlongOS(Vector3 nOS)
        {
            Bounds mb = origMesh.bounds;
            return Vector3.Dot(mb.center, nOS)
                 + mb.extents.x * Mathf.Abs(nOS.x)
                 + mb.extents.y * Mathf.Abs(nOS.y)
                 + mb.extents.z * Mathf.Abs(nOS.z);
        }

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
            mpb.SetFloatArray(SliceLipID, sliceLip);
            mpb.SetColor(SliceCapColorID, settings.sliceCapColor);
            mpb.SetColor(SliceCapRimColorID, settings.sliceCapRimColor);
            mpb.SetFloat(SliceCapDepthID, settings.sliceCapDepth);
            // Explicit zeros so a material-asset default can't resurrect these.
            mpb.SetFloat(CapWetEdgeWidthID, 0f);
            mpb.SetFloat(CapMarbleAmpID, 0f);
            bodyRenderer.SetPropertyBlock(mpb);
            PushFaceSlices(count > 0 ? cutSliceCount : 0);
        }

        private int cutSliceCount;
        // The severed face region, frozen in the FACE's own local space at the moment of the cut.
        // A world plane rebuilt from the body each frame slides across the face as the slime moves
        // and its own drift offset shifts, which reads as the cut line crawling over the eyes.
        private readonly Vector2[] faceCutNormal = new Vector2[CutMax];
        private readonly float[] faceCutOffset = new float[CutMax];
        private readonly float[] faceCutSpan = new float[CutMax];
        private SpriteRenderer[] faceRenderers;
        private MaterialPropertyBlock faceMpb;
        private static readonly int SliceCountWSID = Shader.PropertyToID("_SliceCountWS");
        private static readonly int SlicePlaneWSID = Shader.PropertyToID("_SlicePlaneWS");
        private static readonly int SliceBottomGuardWSID = Shader.PropertyToID("_SliceBottomGuardWS");
        private readonly Vector4[] facePlaneWS = new Vector4[SliceMax];
        private bool faceShown;

        // Freezes which part of the face the blade took, in face-local space, plus how far the plane
        // has to travel to clear the face again — that distance is the heal sweep.
        private void CaptureFaceCut(int i, Vector2 normalWorld, float planeOffsetWorld)
        {
            faceCutNormal[i] = Vector2.up;
            faceCutOffset[i] = 1e9f;
            faceCutSpan[i] = 0f;
            Transform ft = slimeVisual != null ? slimeVisual.FaceTransform : null;
            if (ft == null || bodyFilter == null || faceRenderers == null) return;

            Vector3 nWorld = new Vector3(normalWorld.x, normalWorld.y, 0f);
            Vector3 nLocal = ft.InverseTransformDirection(nWorld);
            nLocal.z = 0f;
            float l = nLocal.magnitude;
            if (l < 1e-6f) return;
            nLocal /= l;

            Vector3 planeWorld = bodyFilter.transform.position + nWorld * planeOffsetWorld;
            float off = Vector3.Dot(nLocal, ft.InverseTransformPoint(planeWorld));

            // Far edge of the face along the cut normal: once the plane reaches it nothing is clipped.
            float far = off;
            for (int r = 0; r < faceRenderers.Length; r++)
            {
                var sr = faceRenderers[r];
                if (sr == null) continue;
                Bounds b = sr.bounds;
                for (int c = 0; c < 4; c++)
                {
                    Vector3 corner = new Vector3(
                        (c & 1) == 0 ? b.min.x : b.max.x,
                        (c & 2) == 0 ? b.min.y : b.max.y,
                        b.center.z);
                    float p = Vector3.Dot(nLocal, ft.InverseTransformPoint(corner));
                    if (p > far) far = p;
                }
            }
            faceCutNormal[i] = new Vector2(nLocal.x, nLocal.y);
            faceCutOffset[i] = off;
            faceCutSpan[i] = Mathf.Max(0f, far - off);
        }

        // The frozen face region, pushed back out through the face's CURRENT transform so the cut
        // line stays glued to the eyes however the slime moves. Healing sweeps the plane off the
        // face over its own span, so the missing part grows back instead of popping into existence.
        private void PushFaceSlices(int n)
        {
            if (faceRenderers == null) return;
            Transform ft = slimeVisual != null ? slimeVisual.FaceTransform : null;
            if (ft == null) return;

            float w = Mathf.Clamp01(meniscusW);
            if (w >= 0.999f) n = 0;
            if (n <= 0 && !faceShown) return;
            faceShown = n > 0;

            if (faceMpb == null) faceMpb = new MaterialPropertyBlock();

            for (int i = 0; i < n; i++)
            {
                Vector3 nLocal = new Vector3(faceCutNormal[i].x, faceCutNormal[i].y, 0f);
                float off = faceCutOffset[i] + w * faceCutSpan[i];
                Vector3 nWS = ft.TransformDirection(nLocal);
                nWS.z = 0f;
                float l = nWS.magnitude;
                if (l < 1e-6f) { facePlaneWS[i] = new Vector4(0f, 1f, 0f, 1e9f); continue; }
                nWS /= l;
                Vector3 pWS = ft.TransformPoint(nLocal * off);
                facePlaneWS[i] = new Vector4(nWS.x, nWS.y, 0f, Vector3.Dot(nWS, pWS));
            }
            for (int i = n; i < SliceMax; i++) facePlaneWS[i] = new Vector4(0f, 1f, 0f, 1e9f);

            for (int r = 0; r < faceRenderers.Length; r++)
            {
                var sr = faceRenderers[r];
                if (sr == null) continue;
                sr.GetPropertyBlock(faceMpb);
                faceMpb.SetFloat(SliceCountWSID, n);
                faceMpb.SetVectorArray(SlicePlaneWSID, facePlaneWS);
                // Guard off: a zero normal makes the shader keep everything once d > 0.
                faceMpb.SetVector(SliceBottomGuardWSID, new Vector4(0f, 1f, 0f, -1e9f));
                sr.SetPropertyBlock(faceMpb);
            }
        }

        // Plane offset of the spherical cap holding volume fraction f: Newton-solves
        // f = h²(3-h)/4 for cap height h (R=1 units), offset = 1-h.
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

        // Null / cleared curve falls back to linear, else the slime stalls small instead of regrowing.
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
            if (faceRenderers == null && slimeVisual != null && slimeVisual.FaceTransform != null)
                faceRenderers = slimeVisual.FaceTransform.GetComponentsInChildren<SpriteRenderer>(true);
            return bodyRenderer != null && origMesh != null;
        }

        // The piece is the slime's OWN blob mesh, not a particle — SlimeChunk owns its lifecycle.
        private void SpawnSeveredPiece(Vector2 outDir, Vector2 inheritVel, float dropletR, float lostVolume,
                                       float planeOffsetWorld)
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

            // Engine-level exclusion: the chunk lives indefinitely, so a timer-based ignore
            // list can't cover it — "chunk under the slime = forever airborne".
            col.excludeLayers = 1 << slime.gameObject.layer;

            // Parented to the ROOT: Mesh takes non-uniform round-up scale and would smear sprites.
            SlimeFaceShard shard = null;
            if (slimeVisual != null && slimeVisual.FaceTransform != null)
            {
                var src = slimeVisual.FaceTransform;
                var clone = Instantiate(src.gameObject, src.position, src.rotation, root.transform);
                clone.name = "FaceShard";
                // Chunk root is unparented at unit scale, so world scale == local scale here.
                clone.transform.localScale = src.lossyScale;
                foreach (var extra in clone.GetComponents<MonoBehaviour>()) Destroy(extra);
                shard = clone.AddComponent<SlimeFaceShard>();
                shard.Init(outDir, worldPos + (Vector3)(outDir * planeOffsetWorld));
            }

            var chunk = root.AddComponent<SlimeChunk>();
            chunk.Init(settings, this, slime, inheritVel, outDir, slime.GravityVector, lostVolume, shard);
        }

        private void RestoreAll()
        {
            if (slime != null)
            {
                slime.SetBodyScale(1f);
                slime.SplitLaunchMultiplier = 1f;
                slime.ClearCutWounds();
            }
            if (woundShown) PushSlices(0);
            if (slimeVisual != null) slimeVisual.SetCutGatherOffset(Vector2.zero);
            phase = CutPhase.None;
            phaseTimer = 0f;
            cutCount = 0;
            rampAbsorbedVol = 0f;
            merging = false;
            mergeVolume = 0f;
            mergeDelivered = 0f;
            woundQuietTime = 999f;
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
