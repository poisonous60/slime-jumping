using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Settings;

namespace SlimeJumping.Effects
{
    /// <summary>
    /// "Slime loses a piece of itself" effect — a cut slime IS a smaller slime.
    ///
    /// A blade hit (Begin) or a deliberate mid-air self-split (BeginSelfSplit) does two things:
    ///   • Severed piece → the slime's own blob mesh spawned at the LOST volume's size,
    ///     flattened to a sliver then surface-tensioned into a round droplet that falls and
    ///     melts into the ground (SlimeChunk, unchanged).
    ///   • Body → loses real volume: SlimeController.SetBodyScale shrinks the actual
    ///     CircleCollider2D (and every radius-derived length), and SlimeVisual's body-scale
    ///     spring chases the new ratio with a tunable slosh, gathering the body into a smaller
    ///     round blob. A SHORT transition shows the flat cut face closing as the ball rounds
    ///     up (see the cut-face transition block below); after that there is no clip, no cap —
    ///     the persistent state is just a smaller, fully round, fully cute slime.
    ///
    /// Healing: BodyScaleRatio ramps back to 1 over recoveryDuration (recoveryCurve). Growing
    /// while resting on a surface pushes the slime up through ordinary depenetration, so the
    /// regrow needs no special ground handling. Launch power recovers on its own curve
    /// (SplitLaunchMultiplier), independent of the size ramp.
    ///
    /// Re-cut while healing stacks naturally: the new cut shrinks from the CURRENT ratio and
    /// restarts the heal timer. MinBodyScale in SlimeController guards the degenerate case.
    ///
    /// Regression: only reversible state — BodyScaleRatio (→1) and SplitLaunchMultiplier (→1).
    /// When idle, nothing is touched; normal play is byte-for-byte unchanged.
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

        // Single heal timeline. A re-cut restarts it from the (now deeper) current ratio.
        private float healStartRatio = 1f;
        private float healTimer;
        private bool healing;
        private float recutTimer;

        // --- Cut transition (user-approved design, 2026-06-10) ---
        // Explicit phase machine; CLOSE runs THREE CONCURRENT LAYERS, each with its own
        // designer curve (see LateUpdate header for the layer breakdown):
        //   HOLD  (cutHoldDuration): full-size body + volume-true flat face — "just got cut"
        //         beat. Nothing shrinks yet.
        //   CLOSE (closeDuration): L1 wound knits shut rim-inward (closeSealCurve: plane
        //         retreat + lip + tear-fade + heal-goo band), L2 the body migrates toward the
        //         cut and re-centers (gatherCurve/gatherAmount), L3 the physics size ramps
        //         down (closeBodyCurve) with the visual spring sloshing on top.
        // After CLOSE the clip is off forever and HEAL (recoveryDuration) regrows the size
        // quietly, ending in one outward "pop" slosh (regrowPopKick).
        private enum CutPhase { None, Hold, Close }
        private CutPhase phase = CutPhase.None;
        private float phaseTimer;
        private float preScale;            // BodyScaleRatio at the cut (could be < 1 already)
        private float postScale;           // BodyScaleRatio after this cut's volume loss
        private Vector2 clipNormalWorld;   // unit, points to the REMOVED side (world-fixed)
        private float clipDepthStartFrac;  // volume-true plane offset at the cut, fraction of full radius

        private const int SliceMax = 3;    // must match SLICE_MAX in SlimeSlice.hlsl
        private readonly Vector4[] slicePlaneOS = new Vector4[SliceMax];
        private readonly float[] sliceSeal = new float[SliceMax];
        private readonly float[] sliceHeal = new float[SliceMax];

        private static readonly int SliceCountID = Shader.PropertyToID("_SliceCount");
        private static readonly int SlicePlaneOSID = Shader.PropertyToID("_SlicePlaneOS");
        private static readonly int SliceSealID = Shader.PropertyToID("_SliceSeal");
        private static readonly int SliceHealID = Shader.PropertyToID("_SliceHeal");
        private static readonly int CutRoundnessID = Shader.PropertyToID("_CutRoundness");
        private static readonly int TearAmpID = Shader.PropertyToID("_TearAmp");
        private static readonly int TearFreqID = Shader.PropertyToID("_TearFreq");
        private static readonly int LipBandID = Shader.PropertyToID("_LipBand");
        private static readonly int LipMagID = Shader.PropertyToID("_LipMag");
        private static readonly int SliceCapColorID = Shader.PropertyToID("_SliceCapColor");
        private static readonly int SliceCapRimColorID = Shader.PropertyToID("_SliceCapRimColor");
        private static readonly int SliceCapDepthID = Shader.PropertyToID("_SliceCapDepth");
        private static readonly int SliceHealColorID = Shader.PropertyToID("_SliceHealColor");
        private static readonly int SliceHealWidthID = Shader.PropertyToID("_SliceHealWidth");
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

        private void ApplyCut(SlimeController s, Vector2 outDir, Vector2 inheritVel, float volumeFraction)
        {
            if (s == null || settings == null) return;
            if (recutTimer > 0f) return;            // internal re-cut guard (same-frame / double hazard)

            slime = s;
            if (!ResolveBody()) return;

            float f = Mathf.Clamp(volumeFraction, 0.01f, 0.9f);

            // Volume → radius: the piece carries f of the CURRENT volume, the body keeps 1-f.
            // (Cube root: the slime reads as a 3D blob even in 2.5D play.)
            float before = slime.BodyScaleRatio;
            float dropletR = slime.SlimeRadius * Mathf.Pow(f, 1f / 3f);

            SpawnSeveredPiece(outDir, inheritVel, dropletR);

            // Transition state: the chunk flies NOW; the body holds full size (HOLD), then
            // shrinks + heals shut over closeDuration (CLOSE). Jump power drops immediately.
            preScale = before;
            postScale = before * Mathf.Pow(1f - f, 1f / 3f);
            phase = settings.cutHoldDuration > 0f ? CutPhase.Hold : CutPhase.Close;
            phaseTimer = 0f;
            healing = false;
            slime.SplitLaunchMultiplier = settings.woundedJumpMultiplierMin;

            // Cut-face plane: depth is volume-true (spherical cap holding fraction f),
            // clamped off-center so the clip can never erase the body core.
            clipNormalWorld = outDir;
            clipDepthStartFrac = Mathf.Max(0.02f, CapPlaneOffsetFrac(f));

            recutTimer = settings.recutMinInterval;
            IsActive = true;
        }

        // Phase machine: HOLD (flat fresh face) → CLOSE (wound heals shut while the body
        // ramps smaller, both on their own designer curves) → HEAL (size regrows).
        private void Update()
        {
            if (recutTimer > 0f) recutTimer -= Time.deltaTime;
            if (!IsActive || slime == null) return;

            switch (phase)
            {
                case CutPhase.Hold:
                    phaseTimer += Time.deltaTime;
                    if (phaseTimer >= settings.cutHoldDuration)
                    {
                        phase = CutPhase.Close;
                        phaseTimer = 0f;
                    }
                    return;

                case CutPhase.Close:
                {
                    // PHYSICS ramps along closeBodyCurve — the collider shrinks WITH the
                    // visual (the visual spring chases this ramp with goo lag + slosh),
                    // so collision and render never diverge mid-transition.
                    phaseTimer += Time.deltaTime;
                    float u = Mathf.Clamp01(phaseTimer / Mathf.Max(0.05f, settings.closeDuration));
                    float bodyP = RecoveryEval(settings.closeBodyCurve, u);
                    slime.SetBodyScale(Mathf.Lerp(preScale, postScale, bodyP));

                    if (u >= 1f)
                    {
                        phase = CutPhase.None;
                        slime.SetBodyScale(postScale);
                        if (slimeVisual != null) slimeVisual.SetCutGatherOffset(Vector2.zero);
                        healStartRatio = slime.BodyScaleRatio;
                        healTimer = 0f;
                        healing = true;
                    }
                    return;
                }
            }

            if (!healing) return;

            healTimer += Time.deltaTime;
            float dur = Mathf.Max(0.01f, settings.recoveryDuration);
            float tn = Mathf.Clamp01(healTimer / dur);
            float healSize = RecoveryEval(settings.recoveryCurve, tn);
            float healLaunch = RecoveryEval(settings.launchRecoveryCurve, tn);

            slime.SetBodyScale(Mathf.Lerp(healStartRatio, 1f, healSize));
            slime.SplitLaunchMultiplier = Mathf.Lerp(settings.woundedJumpMultiplierMin, 1f, healLaunch);

            // Done only when BOTH tracks have fully recovered (a back-loaded curve can still
            // be < 1 at t≈dur, so the timer alone is not enough). Natural completion gets
            // the regrow "pop" — one outward slosh announcing full size.
            if (healTimer >= dur && healSize >= 0.999f && healLaunch >= 0.999f)
            {
                if (slimeVisual != null && settings.regrowPopKick > 0f)
                    slimeVisual.AddBodyScaleImpulse(settings.regrowPopKick);
                RestoreAll();
            }
        }

        // Push the wound + gather after SlimeVisual's writes (execution order 110). The
        // transition is THREE CONCURRENT LAYERS over CLOSE, each on its own tunable:
        //   L1 상처 덮임 (sealP = closeSealCurve): the flat face's rim closes inward (plane
        //      retreats → face area shrinks to a point), a surface-tension lip puckers at the
        //      rim (lipBand/lipMag), the raw torn edge smooths (tearAmp fades), and fresh goo
        //      wells along the closing edge (sliceHealColor band) — flesh flowing over the cut.
        //   L2 비대칭 정착 (gatherCurve/gatherAmount): the whole body migrates toward the cut
        //      face (visual offset; the collider stays centered) so the far side reads as
        //      flowing in to cover the wound, then re-centers as it seals.
        //   L3 작은 구 (closeBodyCurve, in Update + the visual spring): the physics size ramps
        //      down and the spring slosh settles the result into a smaller round slime.
        private void LateUpdate()
        {
            if (slime == null || bodyRenderer == null) return;

            // The wound is RETAINED state on the renderer (an MPB push, not an immediate-mode
            // draw): it shows whatever was pushed last until someone pushes count=0. So the
            // wound layer must terminate ITSELF — whoever flips the phase off, whenever, the
            // next LateUpdate sees a shown wound with no phase and clears it. Without this the
            // off-push depended on Update/LateUpdate ordering and could be skipped on the final
            // frame, freezing the last wound on the renderer (the "pimple").
            if (phase == CutPhase.None)
            {
                if (woundShown) PushWound(0, Vector4.zero, 0f);
                return;
            }

            float u = phase == CutPhase.Hold
                ? 0f
                : Mathf.Clamp01(phaseTimer / Mathf.Max(0.05f, settings.closeDuration));
            float sealP = phase == CutPhase.Hold ? 0f : Mathf.Clamp01(RecoveryEval(settings.closeSealCurve, u));

            float s = slimeVisual != null ? slimeVisual.DisplayedBodyScale : slime.BodyScaleRatio;
            float baseR = slime.SlimeRadius / Mathf.Max(0.01f, slime.BodyScaleRatio);   // full-size world radius

            // The wound may seal EARLIER than the shrink (designer curve) but must never
            // outlive it: once the displayed size has finished settling, a leftover clip
            // sliver reads as a blemish stuck on the small slime. The visible-shrink
            // progress therefore caps the seal from below.
            if (preScale - postScale > 1e-4f)
                sealP = Mathf.Max(sealP, Mathf.Clamp01(Mathf.InverseLerp(preScale, postScale, s)));

            // L2 — asymmetric gather: peak magnitude = the lost radius (so at gatherAmount 1
            // the cut face holds still in the world while the far side flows toward it).
            if (slimeVisual != null)
            {
                float gather = settings.gatherAmount * (preScale - postScale) * baseR
                             * Mathf.Max(0f, RecoveryEval(settings.gatherCurve, u));
                slimeVisual.SetCutGatherOffset(clipNormalWorld * gather);
            }

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

            // L1 — plane offset, interpolated IN REST OS:
            //   start = volume-true world depth (converted through the live reference matrix);
            //   end   = the mesh's own support extent along the plane normal + margin — i.e.
            //           mathematically past EVERY vertex, so at sealP=1 the face area is
            //           exactly zero and turning the clip off is invisible. (A world-estimate
            //           end point popped here before: the mesh origin sits above rb and its
            //           OS extent exceeds R, so the wound was force-killed while still showing.)
            float offOSStart = clipDepthStartFrac * baseR * osPerWorld;
            Bounds mb = origMesh.bounds;
            float offOSEnd = (Vector3.Dot(mb.center, normalOS)
                              + mb.extents.x * Mathf.Abs(normalOS.x)
                              + mb.extents.y * Mathf.Abs(normalOS.y)
                              + mb.extents.z * Mathf.Abs(normalOS.z)) * 1.02f;
            float offOS = Mathf.Lerp(offOSStart, Mathf.Max(offOSStart, offOSEnd), sealP);

            if (sealP >= 1f)
            {
                PushWound(0, Vector4.zero, 0f);   // face area is already zero — no pop
                return;
            }
            PushWound(1, new Vector4(normalOS.x, normalOS.y, normalOS.z, offOS), sealP);
        }

        // True while the renderer is showing a wound (last push had count > 0).
        private bool woundShown;

        private void PushWound(int count, Vector4 plane, float sealP)
        {
            if (bodyRenderer == null) return;
            woundShown = count > 0;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            slicePlaneOS[0] = plane;
            sliceSeal[0] = sealP;
            sliceHeal[0] = sealP * settings.sliceHealIntensity;   // goo wells up as it knits
            bodyRenderer.GetPropertyBlock(mpb);
            mpb.SetFloat(SliceCountID, count);
            mpb.SetVectorArray(SlicePlaneOSID, slicePlaneOS);
            mpb.SetFloatArray(SliceSealID, sliceSeal);
            mpb.SetFloatArray(SliceHealID, sliceHeal);
            mpb.SetFloat(CutRoundnessID, settings.cutRoundnessMax);
            mpb.SetFloat(TearAmpID, settings.tearAmp);
            mpb.SetFloat(TearFreqID, settings.tearFreq);
            mpb.SetFloat(LipBandID, settings.lipBand);
            mpb.SetFloat(LipMagID, settings.lipMag);
            mpb.SetColor(SliceCapColorID, settings.sliceCapColor);
            mpb.SetColor(SliceCapRimColorID, settings.sliceCapRimColor);
            mpb.SetFloat(SliceCapDepthID, settings.sliceCapDepth);
            mpb.SetColor(SliceHealColorID, settings.sliceHealColor);
            mpb.SetFloat(SliceHealWidthID, settings.sliceHealWidth);
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

        private bool ResolveBody()
        {
            bodyFilter = slime.GetComponentInChildren<MeshFilter>(true);   // SlimeMesh (body)
            if (bodyFilter == null) return false;
            bodyRenderer = bodyFilter.GetComponent<MeshRenderer>();
            origMesh = bodyFilter.sharedMesh;
            if (slimeVisual == null) slimeVisual = slime.GetComponentInChildren<SlimeVisual>(true);
            return bodyRenderer != null && origMesh != null;
        }

        // The severed piece is the slime's OWN round blob mesh, spawned at the lost volume's
        // size. SlimeChunk flattens it to a sliver along the fling direction at spawn, then
        // surface-tensions it into a round droplet — a genuine piece of the slime, not a particle.
        private void SpawnSeveredPiece(Vector2 outDir, Vector2 inheritVel, float dropletR)
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

            var chunk = root.AddComponent<SlimeChunk>();
            chunk.Init(settings, inheritVel, outDir, slime.GravityVector);

            // The slime always has a collider (it's the player); ignore collision with the piece
            // for its ENTIRE lifetime so it never blocks/pushes the slime. ignoreDur spans the
            // chunk's max airtime + melt; if it dies sooner, the null collider self-cleans from
            // the slime's ignore list (Passthrough also makes SurfaceProbe skip it, so the slime
            // never tries to attach to the falling droplet).
            float ignoreDur = settings.severedMaxAirTime + settings.meltDuration + 1f;
            slime.RegisterIgnoredCollider(col, ignoreDur);
        }

        private void RestoreAll()
        {
            if (slime != null)
            {
                slime.SetBodyScale(1f);
                slime.SplitLaunchMultiplier = 1f;
            }
            if (phase != CutPhase.None) PushWound(0, Vector4.zero, 0f);
            if (slimeVisual != null) slimeVisual.SetCutGatherOffset(Vector2.zero);
            phase = CutPhase.None;
            phaseTimer = 0f;
            healing = false;
            healTimer = 0f;
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
