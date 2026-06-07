using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Settings;

namespace SlimeJumping.Effects
{
    /// <summary>
    /// VISUAL "slime sliced by a blade" effect, layered on top of the NORMAL stun.
    ///
    /// The blade hazard calls Begin() AND slime.ApplyHazardEffect() — so a blade hit is
    /// an ordinary stun (eyes change, knockback, control suspended for the stun duration)
    /// PLUS this slice visual. No control override here: control loss / eyes come from the
    /// Stunned state, so behaviour is consistent with every other hazard.
    ///
    /// On Begin (one asymmetric cut along the slime's entry direction):
    ///   • Small severed piece → a DynamicMeshCutter chunk that falls under the slime's
    ///     own gravity and melts into the ground (SlimeChunk).
    ///   • Body → keeps its full mesh; the cut shows as a SHADER CLIP wound (SlimeSlice.hlsl)
    ///     that heals SMOOTHLY by receding the clip plane over regenDuration. Jump power
    ///     ramps from woundedJumpMultiplierMin back to 1 over the same time.
    ///
    /// Regression: only reversible / default-off state — SplitLaunchMultiplier (→1), the
    /// per-renderer _Slice* MPB (→ _SliceActive 0), RegisterIgnoredCollider. Shared
    /// material/mesh untouched; shader slice path is a no-op when _SliceActive == 0.
    ///
    /// [DefaultExecutionOrder(110)] → LateUpdate runs after SlimeVisual (0) and
    /// SlimeBurnVisual (100) so the slice MPB layers on top of their writes.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public class SlimeSplitSequence : MonoBehaviour
    {
        [SerializeField] private SlimeSplitSettings settings;
        [Tooltip("Material for the SEVERED falling piece. Null → reuse the slime body material.")]
        [SerializeField] private Material chunkMaterial;

        private enum State { Idle, Regen }
        private State state = State.Idle;
        public bool IsActive => state != State.Idle;

        private SlimeController slime;
        private Vector2 pendingCutNormal;
        private Vector2 pendingInheritVel;

        private MeshFilter bodyFilter;
        private MeshRenderer bodyRenderer;
        private MaterialPropertyBlock mpb;
        private Mesh origMesh;

        private Vector3 sliceNormalOS;
        private float sliceOffsetStart;
        private float sliceOffsetEnd;

        private Collider2D severedCol;
        private float phaseTimer;

        private static readonly int SliceActiveID = Shader.PropertyToID("_SliceActive");
        private static readonly int SliceNormalOSID = Shader.PropertyToID("_SliceNormalOS");
        private static readonly int SliceOffsetID = Shader.PropertyToID("_SliceOffset");
        private static readonly int SliceCapColorID = Shader.PropertyToID("_SliceCapColor");

        /// <param name="entryVelocity">Slime velocity entering the blade — sets the cut angle.
        /// Read BEFORE the caller applies knockback.</param>
        public void Begin(SlimeController s, Vector2 entryVelocity)
        {
            if (s == null || IsActive || settings == null) return;

            slime = s;
            Vector2 v = entryVelocity.sqrMagnitude > 1e-4f ? entryVelocity.normalized : Vector2.right;
            Vector2 n = new Vector2(-v.y, v.x);
            pendingCutNormal = Rotate(n, Random.Range(-settings.cutNormalJitterDeg, settings.cutNormalJitterDeg));
            pendingInheritVel = entryVelocity;

            if (!ResolveBody()) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();

            ComputeSlicePlane();
            SpawnSeveredPiece();

            slime.SplitLaunchMultiplier = settings.woundedJumpMultiplierMin;
            state = State.Regen;
            phaseTimer = 0f;
        }

        private void Update()
        {
            if (state != State.Regen) return;
            phaseTimer += Time.deltaTime;
            float progress = settings.regenDuration > 0f
                ? Mathf.Clamp01(phaseTimer / settings.regenDuration)
                : 1f;

            if (slime != null)
                slime.SplitLaunchMultiplier = Mathf.Lerp(settings.woundedJumpMultiplierMin, 1f, progress);

            if (progress >= 1f) RestoreFull();
        }

        // Push the slice MPB after SlimeVisual / SlimeBurnVisual so their writes survive.
        private void LateUpdate()
        {
            if (state == State.Idle || bodyRenderer == null || mpb == null) return;

            float progress = settings.regenDuration > 0f
                ? Mathf.Clamp01(phaseTimer / settings.regenDuration)
                : 0f;
            float offset = Mathf.Lerp(sliceOffsetStart, sliceOffsetEnd, progress);

            bodyRenderer.GetPropertyBlock(mpb);
            mpb.SetFloat(SliceActiveID, 1f);
            mpb.SetVector(SliceNormalOSID, sliceNormalOS);
            mpb.SetFloat(SliceOffsetID, offset);
            mpb.SetColor(SliceCapColorID, settings.sliceCapColor);
            bodyRenderer.SetPropertyBlock(mpb);
        }

        // --- internals ---

        private bool ResolveBody()
        {
            bodyFilter = slime.GetComponentInChildren<MeshFilter>(true);   // SlimeMesh (body)
            if (bodyFilter == null) return false;
            bodyRenderer = bodyFilter.GetComponent<MeshRenderer>();
            origMesh = bodyFilter.sharedMesh;
            return bodyRenderer != null && origMesh != null;
        }

        private void ComputeSlicePlane()
        {
            Vector3 cutN3 = new Vector3(pendingCutNormal.x, pendingCutNormal.y, 0f);
            sliceNormalOS = bodyFilter.transform.InverseTransformDirection(cutN3).normalized;

            Vector3 e = origMesh.bounds.extents;
            float extentOS = Mathf.Abs(sliceNormalOS.x) * e.x
                           + Mathf.Abs(sliceNormalOS.y) * e.y
                           + Mathf.Abs(sliceNormalOS.z) * e.z;
            sliceOffsetStart = settings.cutOffsetFraction * extentOS;
            sliceOffsetEnd = extentOS * 1.1f;   // plane past the mesh → nothing clipped → healed
        }

        private void SpawnSeveredPiece()
        {
            Vector3 worldPos = bodyFilter.transform.position;
            Quaternion worldRot = bodyFilter.transform.rotation;
            Vector3 worldScale = bodyFilter.transform.lossyScale;
            Vector3 cutNormalWS = new Vector3(pendingCutNormal.x, pendingCutNormal.y, 0f);

            float avgScale = (Mathf.Abs(worldScale.x) + Mathf.Abs(worldScale.y)) * 0.5f;
            Vector3 ext = origMesh.bounds.extents;
            float extentOS = Mathf.Abs(sliceNormalOS.x) * ext.x + Mathf.Abs(sliceNormalOS.y) * ext.y + Mathf.Abs(sliceNormalOS.z) * ext.z;
            float worldOffset = settings.cutOffsetFraction * extentOS * avgScale;

            Material mat = chunkMaterial != null ? chunkMaterial
                         : (bodyRenderer.sharedMaterials.Length > 0 ? bodyRenderer.sharedMaterials[0] : null);

            var chunks = SlimeChunkCutter.Cut(origMesh, mat, worldPos, worldRot, worldScale, cutNormalWS, worldOffset);
            if (chunks.Count < 2)
            {
                for (int i = 0; i < chunks.Count; i++) if (chunks[i] != null) Destroy(chunks[i]);
                return;   // no physical piece; the shader wound still shows + heals
            }

            GameObject small = chunks[0], big = chunks[1];
            if (VertCount(small) > VertCount(big)) { var t = small; small = big; big = t; }
            Destroy(big);

            small.layer = slime.gameObject.layer;
            small.AddComponent<Rigidbody2D>();
            var col = small.AddComponent<CircleCollider2D>();

            // Fit the collider to the VISIBLE mesh (offset + radius from world AABB) so the
            // piece rests planted on the ground instead of floating on an oversized circle.
            var smr = small.GetComponentInChildren<MeshRenderer>();
            if (smr != null)
            {
                Bounds wb = smr.bounds;
                col.offset = (Vector2)small.transform.InverseTransformPoint(wb.center);
                float ls = Mathf.Abs(small.transform.lossyScale.x);
                if (ls < 1e-4f) ls = 1f;
                float worldR = (wb.extents.x + wb.extents.y) * 0.5f * settings.colliderRadiusFactor;
                col.radius = Mathf.Max(0.02f, worldR / ls);
            }

            var chunk = small.AddComponent<SlimeChunk>();
            chunk.Init(settings, pendingInheritVel, pendingCutNormal, slime.GravityVector);

            var slimeCol = slime.GetComponent<Collider2D>();
            if (slimeCol != null)
            {
                float ignoreDur = settings.severedMaxAirTime + settings.meltDuration + 1f;
                slime.RegisterIgnoredCollider(col, ignoreDur);
                severedCol = col;
            }
        }

        private void RestoreFull()
        {
            if (bodyRenderer != null && mpb != null)
            {
                bodyRenderer.GetPropertyBlock(mpb);
                mpb.SetFloat(SliceActiveID, 0f);
                bodyRenderer.SetPropertyBlock(mpb);
            }
            if (slime != null) slime.SplitLaunchMultiplier = 1f;
            if (severedCol != null && slime != null) slime.UnregisterIgnoredCollider(severedCol);
            severedCol = null;
            state = State.Idle;
        }

        private void OnDestroy()
        {
            if (IsActive) RestoreFull();
        }

        private static int VertCount(GameObject go)
        {
            if (go == null) return 0;
            var mf = go.GetComponentInChildren<MeshFilter>();
            return mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount : 0;
        }

        private static Vector2 Rotate(Vector2 v, float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }
    }
}
