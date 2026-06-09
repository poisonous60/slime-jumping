using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Settings;

namespace SlimeJumping.Effects
{
    /// <summary>
    /// VISUAL "slime sliced by a blade" effect, layered on top of the NORMAL stun.
    ///
    /// A blade hit calls Begin() AND slime.ApplyHazardEffect() — so it is an ordinary stun
    /// (eyes change, knockback, control suspended for the stun duration) PLUS this slice
    /// visual. No control override here.
    ///
    /// Per cut (one asymmetric plane along the slime's entry direction):
    ///   • Small severed piece → the slime's own blob mesh spawned small, flattened to a
    ///     sliver along the cut normal then surface-tensioned into a round goo droplet in the
    ///     air, then melted into the ground (SlimeChunk).
    ///   • Body → keeps its full mesh; the cut shows as a SHADER CLIP wound (SlimeSlice.hlsl)
    ///     that heals on a BACK-LOADED timeline: HOLD the clean cut → GATHER (cap domes +
    ///     body tightens) → FILL ("뿅" snap, clip recedes off the mesh + a squash pop).
    ///   • Face (eyes + mouth) → clipped by the SAME wound planes in world space, so eyes on
    ///     the cut-away side vanish and return as the wound heals.
    ///
    /// Up to <see cref="SlimeSplitSettings.maxWounds"/> (≤3, the shader cap) wounds stack:
    /// re-cutting mid-regen adds a new wound; a 4th evicts the most-healed one. Each wound
    /// runs its own timeline. Jump power = the freshest (least-healed) wound, snapping back
    /// to full as it fills; a new cut drops it to min again.
    ///
    /// Non-lethal by construction: every clip plane removes only dot(osPosRest, n) > offset
    /// with offset > 0, so the body center (dot = 0) is never clipped — no number of
    /// overlapping cuts can erase the blob.
    ///
    /// Regression: only reversible / default-off state — SplitLaunchMultiplier (→1), the
    /// per-renderer _Slice* MPB (→ _SliceCount 0) on body + face, RegisterIgnoredCollider,
    /// and a single-frame transient squash. Shared material/mesh untouched; shader slice
    /// path is a no-op when _SliceCount == 0.
    ///
    /// [DefaultExecutionOrder(110)] → Update runs after SlimeVisual/Burn so the transient
    /// squash it requests is consumed by SlimeVisual.LateUpdate the SAME frame (Update runs
    /// before all LateUpdates); LateUpdate pushes the slice MPB after their writes.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public class SlimeSplitSequence : MonoBehaviour
    {
        [SerializeField] private SlimeSplitSettings settings;
        [Tooltip("Material for the SEVERED falling piece. Null → reuse the slime body material.")]
        [SerializeField] private Material chunkMaterial;

        private const int SliceMax = 3;   // MUST match SLICE_MAX in SlimeSlice.hlsl

        // One live cut. Heals on its own timeline measured by `timer`.
        private class Wound
        {
            public bool active;
            public Vector3 normalOS;     // unit, rest-pose OS, points to the REMOVED side
            public float offsetStart;    // OS plane offset at the fresh cut
            public float offsetEnd;      // OS plane offset once fully healed (past the mesh)
            public float timer;          // seconds since this cut landed
            public Collider2D severedCol;
            // Per-frame display values computed in Update, consumed in LateUpdate.
            public float dispOffset;
            public float dispHeal;
        }

        private readonly Wound[] wounds = new Wound[SliceMax];

        private SlimeController slime;
        private SlimeVisual slimeVisual;

        private MeshFilter bodyFilter;
        private MeshRenderer bodyRenderer;
        private Mesh origMesh;
        private MaterialPropertyBlock mpb;

        private SpriteRenderer[] faceRenderers;
        private MaterialPropertyBlock faceMpb;

        // Pre-allocated MPB arrays. Unity locks the array length on the first Set*Array call,
        // so allocate once at length SliceMax and reuse every frame.
        private readonly Vector4[] slicePlaneOS = new Vector4[SliceMax];
        private readonly float[] sliceHeal = new float[SliceMax];
        private readonly Vector4[] slicePlaneWS = new Vector4[SliceMax];

        private float recutTimer;

        public bool IsActive { get; private set; }

        private static readonly int SliceCountID = Shader.PropertyToID("_SliceCount");
        private static readonly int SlicePlaneOSID = Shader.PropertyToID("_SlicePlaneOS");
        private static readonly int SliceHealID = Shader.PropertyToID("_SliceHeal");
        private static readonly int SliceCapColorID = Shader.PropertyToID("_SliceCapColor");
        private static readonly int SliceHealColorID = Shader.PropertyToID("_SliceHealColor");
        private static readonly int SliceHealWidthID = Shader.PropertyToID("_SliceHealWidth");
        private static readonly int SliceBottomGuardID = Shader.PropertyToID("_SliceBottomGuard");
        private static readonly int SliceBottomGuardWSID = Shader.PropertyToID("_SliceBottomGuardWS");
        private static readonly int SliceCountWSID = Shader.PropertyToID("_SliceCountWS");
        private static readonly int SlicePlaneWSID = Shader.PropertyToID("_SlicePlaneWS");

        /// <param name="entryVelocity">Slime velocity entering the blade — sets the cut angle.
        /// Read BEFORE the caller applies knockback.</param>
        public void Begin(SlimeController s, Vector2 entryVelocity)
        {
            if (s == null || settings == null) return;
            if (recutTimer > 0f) return;            // internal re-cut guard (same-frame / double hazard)

            slime = s;
            if (!ResolveBody()) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();

            int slot = PickSlot();
            Wound w = wounds[slot];
            if (w == null) { w = new Wound(); wounds[slot] = w; }
            else if (w.active) RetireWound(w);      // evicting the most-healed wound: free its collider

            // Cut plane: perpendicular to entry velocity, jittered.
            Vector2 v = entryVelocity.sqrMagnitude > 1e-4f ? entryVelocity.normalized : Vector2.right;
            Vector2 n = new Vector2(-v.y, v.x);
            Vector2 cutNormalWS = Rotate(n, Random.Range(-settings.cutNormalJitterDeg, settings.cutNormalJitterDeg));

            Vector3 cutN3 = new Vector3(cutNormalWS.x, cutNormalWS.y, 0f);
            Vector3 normalOS = bodyFilter.transform.InverseTransformDirection(cutN3).normalized;

            Vector3 e = origMesh.bounds.extents;
            float extentOS = Mathf.Abs(normalOS.x) * e.x
                           + Mathf.Abs(normalOS.y) * e.y
                           + Mathf.Abs(normalOS.z) * e.z;

            w.active = true;
            w.normalOS = normalOS;
            w.offsetStart = settings.cutOffsetFraction * extentOS;   // > 0 → core never clipped
            w.offsetEnd = extentOS * 1.1f;                           // plane past the mesh → healed
            w.timer = 0f;
            w.dispOffset = w.offsetStart;
            w.dispHeal = 0f;

            SpawnSeveredPiece(cutNormalWS, entryVelocity, out w.severedCol);

            recutTimer = settings.recutMinInterval;
            IsActive = true;
        }

        // Update advances every wound's timeline, sets jump power (freshest wound governs) and
        // requests the transient body squash (gather tighten + fill pop). Runs in the Update
        // phase (before all LateUpdates) so SlimeVisual.LateUpdate consumes the squash THIS frame.
        private void Update()
        {
            if (!IsActive) return;
            if (recutTimer > 0f) recutTimer -= Time.deltaTime;

            float dt = Time.deltaTime;
            float holdEnd = settings.regenHoldDuration;
            float gatherEnd = holdEnd + settings.regenGatherDuration;
            float fillEnd = gatherEnd + settings.regenFillDuration;
            float fillDur = Mathf.Max(0.001f, settings.regenFillDuration);
            float gatherDur = Mathf.Max(0.001f, settings.regenGatherDuration);

            int activeCount = 0;
            float minJump = 1f;

            for (int i = 0; i < wounds.Length; i++)
            {
                Wound w = wounds[i];
                if (w == null || !w.active) continue;

                w.timer += dt;
                if (w.timer >= fillEnd) { RetireWound(w); continue; }
                activeCount++;

                // --- offset (FILL: clip recedes off the mesh) ---
                if (w.timer < gatherEnd)
                {
                    w.dispOffset = w.offsetStart;
                }
                else
                {
                    float fp = Mathf.Clamp01((w.timer - gatherEnd) / fillDur);
                    w.dispOffset = Mathf.LerpUnclamped(w.offsetStart, w.offsetEnd, FillEase(fp));
                }

                // --- regeneration glow (0..1): ramps up over GATHER, stays on through FILL so
                //     the seam glows while the clip edge sweeps the wound closed ---
                if (w.timer < holdEnd)
                {
                    w.dispHeal = 0f;
                }
                else if (w.timer < gatherEnd)
                {
                    float gp = Mathf.Clamp01((w.timer - holdEnd) / gatherDur);
                    w.dispHeal = Mathf.SmoothStep(0f, 1f, gp);
                }
                else
                {
                    w.dispHeal = 1f;
                }

                // --- jump power: min through hold+gather, ramps to full over fill ---
                float jf;
                if (w.timer < gatherEnd)
                {
                    jf = settings.woundedJumpMultiplierMin;
                }
                else
                {
                    float fp = Mathf.Clamp01((w.timer - gatherEnd) / fillDur);
                    jf = Mathf.Lerp(settings.woundedJumpMultiplierMin, 1f, FillEase(fp));
                }
                if (jf < minJump) minJump = jf;
            }

            if (slime != null) slime.SplitLaunchMultiplier = activeCount > 0 ? minJump : 1f;

            if (activeCount == 0) RestoreAll();
        }

        // Push the slice MPB after SlimeVisual / SlimeBurnVisual so their writes survive.
        private void LateUpdate()
        {
            if (!IsActive || bodyRenderer == null || mpb == null) return;

            int count = 0;
            for (int i = 0; i < wounds.Length && count < SliceMax; i++)
            {
                Wound w = wounds[i];
                if (w == null || !w.active) continue;

                slicePlaneOS[count] = new Vector4(w.normalOS.x, w.normalOS.y, w.normalOS.z, w.dispOffset);
                sliceHeal[count] = w.dispHeal * settings.sliceHealIntensity;

                // World-space plane for the face sprites. The body wound clips in rest-pose OS
                // and rides the in-shader SlimeDeform automatically; the face sprites are separate
                // and do NOT, so we map the rest-OS plane through the body's CURRENT transform each
                // frame. TransformPoint/Direction already account for the body's localPosition, so
                // a non-centered pivot is fine. This is an APPROXIMATION of the deformed wound: the
                // in-shader vertex squash and (during squash) the body's non-uniform scale are not
                // modelled here — acceptable since the face is small and centered while wounds sit
                // at the body edge, so the face-clip error stays sub-pixel in practice.
                Vector3 nWs = bodyFilter.transform.TransformDirection(w.normalOS).normalized;
                Vector3 pWs = bodyFilter.transform.TransformPoint(w.normalOS * w.dispOffset);
                slicePlaneWS[count] = new Vector4(nWs.x, nWs.y, nWs.z, Vector3.Dot(pWs, nWs));

                count++;
            }

            // Bottom guard: OS y below which nothing is clipped (the ground-contact band, in
            // rest-pose OS where +y = the body's surface-normal/up axis). bottomProtectFraction
            // of the height, measured from the bottom (y = -extentY).
            float extentY = origMesh.bounds.extents.y;
            float guardY = -(1f - 2f * Mathf.Clamp01(settings.bottomProtectFraction)) * extentY;

            bodyRenderer.GetPropertyBlock(mpb);
            mpb.SetFloat(SliceCountID, count);
            mpb.SetVectorArray(SlicePlaneOSID, slicePlaneOS);
            mpb.SetFloatArray(SliceHealID, sliceHeal);
            mpb.SetColor(SliceCapColorID, settings.sliceCapColor);
            mpb.SetColor(SliceHealColorID, settings.sliceHealColor);
            mpb.SetFloat(SliceHealWidthID, settings.sliceHealWidth);
            mpb.SetFloat(SliceBottomGuardID, guardY);
            bodyRenderer.SetPropertyBlock(mpb);

            // Same guard for the face, as a world plane (keep where dot(posWS,n) < d).
            Vector3 guardN = bodyFilter.transform.TransformDirection(Vector3.up).normalized;
            Vector3 guardP = bodyFilter.transform.TransformPoint(new Vector3(0f, guardY, 0f));
            Vector4 guardWS = new Vector4(guardN.x, guardN.y, guardN.z, Vector3.Dot(guardP, guardN));

            PushFace(count, guardWS);
        }

        // --- internals ---

        // Fill ease, robust to a null / cleared (empty) curve → falls back to linear so the
        // wound always heals instead of stalling at offset 0.
        private float FillEase(float t)
        {
            var c = settings.regenFillCurve;
            return (c != null && c.length > 1) ? c.Evaluate(t) : t;
        }

        private int PickSlot()
        {
            int cap = Mathf.Clamp(settings.maxWounds, 1, SliceMax);
            for (int i = 0; i < cap; i++)
                if (wounds[i] == null || !wounds[i].active) return i;

            // No free slot → evict the MOST-HEALED wound (highest timer): it is closest to
            // vanishing anyway, so least gameplay/visual loss.
            int best = 0;
            float bestTimer = -1f;
            for (int i = 0; i < cap; i++)
            {
                if (wounds[i].timer > bestTimer) { bestTimer = wounds[i].timer; best = i; }
            }
            return best;
        }

        private void RetireWound(Wound w)
        {
            if (w.severedCol != null && slime != null) slime.UnregisterIgnoredCollider(w.severedCol);
            w.severedCol = null;
            w.active = false;
        }

        private bool ResolveBody()
        {
            bodyFilter = slime.GetComponentInChildren<MeshFilter>(true);   // SlimeMesh (body)
            if (bodyFilter == null) return false;
            bodyRenderer = bodyFilter.GetComponent<MeshRenderer>();
            origMesh = bodyFilter.sharedMesh;
            if (bodyRenderer == null || origMesh == null) return false;

            if (slimeVisual == null) slimeVisual = slime.GetComponentInChildren<SlimeVisual>(true);
            if (faceRenderers == null && slimeVisual != null && slimeVisual.FaceTransform != null)
                faceRenderers = slimeVisual.FaceTransform.GetComponentsInChildren<SpriteRenderer>(true);
            if (faceMpb == null) faceMpb = new MaterialPropertyBlock();
            return true;
        }

        private void PushFace(int count, Vector4 guardWS)
        {
            if (faceRenderers == null || faceMpb == null) return;
            for (int r = 0; r < faceRenderers.Length; r++)
            {
                SpriteRenderer fr = faceRenderers[r];
                if (fr == null) continue;
                fr.GetPropertyBlock(faceMpb);
                faceMpb.SetFloat(SliceCountWSID, count);
                faceMpb.SetVectorArray(SlicePlaneWSID, slicePlaneWS);
                faceMpb.SetVector(SliceBottomGuardWSID, guardWS);
                fr.SetPropertyBlock(faceMpb);
            }
        }

        // The severed piece is the slime's OWN round blob mesh, spawned small. SlimeChunk
        // flattens it to a sliver along the cut normal at spawn, then surface-tensions it into
        // a round droplet — a genuine shape change with clean blob topology (no DMC, no crescent).
        private void SpawnSeveredPiece(Vector2 cutNormalWS2, Vector2 inheritVel, out Collider2D severedCol)
        {
            severedCol = null;

            // Spawn at the slime's PHYSICS position (rb), per project convention.
            Vector3 worldPos = slime.Position;

            // Droplet size = a fraction of the slime's UNDEFORMED physical radius (its
            // CircleCollider2D never deforms). Using the live body lossyScale here would bake
            // the current squash/stretch into the piece, so it would round up to a squashed
            // oval instead of a true ball — the exact thing this redesign must avoid.
            float slimeR = 0.5f;
            var slimeCirc = slime.GetComponent<CircleCollider2D>();
            if (slimeCirc != null)
            {
                Vector3 csl = slimeCirc.transform.lossyScale;
                slimeR = slimeCirc.radius * Mathf.Max(Mathf.Abs(csl.x), Mathf.Abs(csl.y));
            }
            float meshOS = Mathf.Max(origMesh.bounds.extents.x, Mathf.Max(origMesh.bounds.extents.y, origMesh.bounds.extents.z));
            if (meshOS < 1e-4f) meshOS = 0.5f;
            float dropletR = slimeR * Mathf.Clamp(settings.pieceFinalScale, 0.05f, 0.95f);

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
            chunk.Init(settings, inheritVel, cutNormalWS2, slime.GravityVector);

            // The slime always has a collider (it's the player); register the piece so it
            // doesn't collide with the slime on spawn.
            float ignoreDur = settings.severedMaxAirTime + settings.meltDuration + 1f;
            slime.RegisterIgnoredCollider(col, ignoreDur);
            severedCol = col;
        }

        private void RestoreAll()
        {
            if (bodyRenderer != null && mpb != null)
            {
                bodyRenderer.GetPropertyBlock(mpb);
                mpb.SetFloat(SliceCountID, 0f);
                bodyRenderer.SetPropertyBlock(mpb);
            }
            if (faceRenderers != null && faceMpb != null)
            {
                for (int r = 0; r < faceRenderers.Length; r++)
                {
                    SpriteRenderer fr = faceRenderers[r];
                    if (fr == null) continue;
                    fr.GetPropertyBlock(faceMpb);
                    faceMpb.SetFloat(SliceCountWSID, 0f);
                    fr.SetPropertyBlock(faceMpb);
                }
            }
            if (slime != null) slime.SplitLaunchMultiplier = 1f;
            for (int i = 0; i < wounds.Length; i++)
                if (wounds[i] != null && wounds[i].active) RetireWound(wounds[i]);
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
