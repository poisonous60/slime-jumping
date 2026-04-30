using System.Collections;
using System.Reflection;
using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Visual;

namespace SlimeJumping.Debugging
{
    /// <summary>
    /// Debug tool to isolate where the burn-mark pipeline is failing. Three
    /// independent test paths, each binds to one key:
    ///
    ///   B — fires SlimeController.ApplyHazardPush with a fake hit point on the
    ///       slime's right edge. Goes through the *full* normal pipeline
    ///       (controller event → SlimeBurnVisual.HandleHazardPushed → MPB push).
    ///       If this works, the pipeline is fine and the real bug is upstream
    ///       (granite blast not calling ApplyHazardPush, or the hit point is
    ///       outside the slime's local space).
    ///
    ///   N — bypasses SlimeBurnVisual entirely. Writes a giant burn (huge OS
    ///       radius covering the whole mesh) directly into the body
    ///       MeshRenderer's MaterialPropertyBlock. If this works, the shader
    ///       path is OK and the bug is in SlimeBurnVisual's hit→OS conversion
    ///       or its MPB push timing. If THIS doesn't darken the body, the
    ///       shader itself is the problem (not bound, wrong shader on material,
    ///       SampleBurn not being called, etc).
    ///
    ///   M — clears all burns by zeroing _BurnCount via direct MPB push.
    ///
    /// HUD shows: body MR's current shader name, current _Burns array snapshot
    /// (read back from the renderer's MPB), and the result of the last test
    /// fire so you can compare expected vs observed.
    /// </summary>
    [DefaultExecutionOrder(150)]
    public class SlimeBurnVisualDebug : MonoBehaviour
    {
        [SerializeField] private SlimeController slimeController;
        [SerializeField] private SlimeBurnVisual burnVisual;
        [SerializeField] private SlimeVisual slimeVisual;

        [Header("Keys")]
        [SerializeField] private KeyCode hitPipelineKey = KeyCode.B;
        [SerializeField] private KeyCode directMpbKey = KeyCode.N;
        [SerializeField] private KeyCode clearKey = KeyCode.M;
        [Tooltip("Press to dump a full state snapshot to the Unity console.")]
        [SerializeField] private KeyCode logKey = KeyCode.L;
        [Tooltip("Press to force-set SlimeBurnVisual.burnStrength=1 via reflection (workaround when the inspector value didn't migrate after script edits).")]
        [SerializeField] private KeyCode forceStrengthKey = KeyCode.K;
        [Tooltip("If true, every received OnHazardPushed event is logged automatically.")]
        [SerializeField] private bool autoLogHits = true;

        private MeshRenderer bodyMR;
        private MeshFilter bodyMF;
        private MaterialPropertyBlock probe;
        private string lastAction = "(none yet)";
        private float lastActionTime = -999f;

        // Snoop on SlimeBurnVisual's internal state via reflection so we can see
        // why a hit isn't producing a burn. None of these escape the debug build —
        // pure read-only inspection.
        private FieldInfo bvBurnsField;
        private FieldInfo bvMeshTransformField;
        private FieldInfo bvBodyMRField;
        private FieldInfo bvBurnStrengthField;
        private FieldInfo bvBurnRadiusField;
        private string lastReceivedHit = "(no hits seen yet)";
        private float lastReceivedHitTime = -999f;

        private static readonly int BurnsID = Shader.PropertyToID("_Burns");
        private static readonly int BurnCountID = Shader.PropertyToID("_BurnCount");
        private static readonly int BurnStrengthID = Shader.PropertyToID("_BurnStrength");

        void Awake()
        {
            // SlimeVisual lives on a SIBLING (Slime/SlimeVisual), not a parent —
            // so GetComponentInParent doesn't reach it. Walk up to the slime root
            // (the GO that holds SlimeController) and search its children.
            if (slimeController == null) slimeController = GetComponentInParent<SlimeController>();
            Transform slimeRoot = slimeController != null ? slimeController.transform : transform.root;
            if (slimeVisual == null) slimeVisual = slimeRoot.GetComponentInChildren<SlimeVisual>(true);
            if (burnVisual == null) burnVisual = slimeRoot.GetComponentInChildren<SlimeBurnVisual>(true);
            ResolveBody();
            probe = new MaterialPropertyBlock();

            if (burnVisual != null)
            {
                var t = typeof(SlimeBurnVisual);
                var bf = BindingFlags.NonPublic | BindingFlags.Instance;
                bvBurnsField = t.GetField("burns", bf);
                bvMeshTransformField = t.GetField("meshTransform", bf);
                bvBodyMRField = t.GetField("bodyMR", bf);
                bvBurnStrengthField = t.GetField("burnStrength", bf);
                bvBurnRadiusField = t.GetField("burnRadius", bf);
            }
            // Subscribe to the same hit event SlimeBurnVisual subscribes to, so
            // we can see whether hits are even reaching the slime.
            if (slimeController != null) slimeController.OnHazardPushed += OnHitObserved;
        }

        void OnDestroy()
        {
            if (slimeController != null) slimeController.OnHazardPushed -= OnHitObserved;
        }

        private void OnHitObserved(Vector2 deltaV, Vector2 hitPoint)
        {
            Vector3 osCoord = Vector3.zero;
            if (bodyMF != null)
            {
                Vector3 hitWS = new Vector3(hitPoint.x, hitPoint.y, bodyMF.transform.position.z);
                osCoord = bodyMF.transform.InverseTransformPoint(hitWS);
            }
            lastReceivedHit = $"hitPoint={hitPoint} | computed OS={osCoord}";
            lastReceivedHitTime = Time.time;
            if (autoLogHits)
            {
                Debug.Log($"[BurnDebug] HIT received  deltaV={deltaV}  hitWS={hitPoint}  computed_OS={osCoord}");
                Debug.Log(BuildStateSnapshot());
            }
        }

        private string BuildStateSnapshot()
        {
            string shaderName = "(no body MR)";
            string meshBoundsStr = "(no mesh)";
            string slimeMeshTransformInfo = "(unresolved)";
            string mpbSnapshot = "(no MR)";
            string bvState = "(no SlimeBurnVisual)";

            if (bodyMR == null) ResolveBody();
            if (bodyMR != null && bodyMR.sharedMaterial != null && bodyMR.sharedMaterial.shader != null)
                shaderName = bodyMR.sharedMaterial.shader.name;
            if (bodyMF != null && bodyMF.sharedMesh != null)
            {
                Bounds b = bodyMF.sharedMesh.bounds;
                meshBoundsStr = $"size={b.size} extents={b.extents}";
            }
            if (bodyMF != null)
                slimeMeshTransformInfo = $"name={bodyMF.transform.name} lossyScale={bodyMF.transform.lossyScale} pos={bodyMF.transform.position}";

            if (bodyMR != null)
            {
                bodyMR.GetPropertyBlock(probe);
                int count = probe.GetInt(BurnCountID);
                float strength = probe.GetFloat(BurnStrengthID);
                Vector4 first = probe.GetVector(BurnsID);
                mpbSnapshot = $"_BurnCount={count}  _BurnStrength={strength:F3}  _Burns[0]={first}";
            }

            if (burnVisual != null)
            {
                int burnCount = -1;
                Transform bvMeshT = null;
                MeshRenderer bvBodyMR = null;
                if (bvBurnsField != null)
                {
                    var list = bvBurnsField.GetValue(burnVisual) as IList;
                    if (list != null) burnCount = list.Count;
                }
                if (bvMeshTransformField != null) bvMeshT = bvMeshTransformField.GetValue(burnVisual) as Transform;
                if (bvBodyMRField != null) bvBodyMR = bvBodyMRField.GetValue(burnVisual) as MeshRenderer;
                float bvStrength = bvBurnStrengthField != null ? (float)bvBurnStrengthField.GetValue(burnVisual) : float.NaN;
                float bvRadius = bvBurnRadiusField != null ? (float)bvBurnRadiusField.GetValue(burnVisual) : float.NaN;
                bvState = $"enabled={burnVisual.enabled}  burns.Count={burnCount}  burnStrength_C#={bvStrength:F3}  burnRadius_C#={bvRadius:F3}  meshTransform={(bvMeshT != null ? bvMeshT.name : "NULL")}  bodyMR={(bvBodyMR != null ? bvBodyMR.name : "NULL")}";
            }

            return
                "[BurnDebug snapshot]\n" +
                $"  body shader:           {shaderName}\n" +
                $"  mesh bounds:           {meshBoundsStr}\n" +
                $"  meshTransform:         {slimeMeshTransformInfo}\n" +
                $"  GPU MPB snapshot:      {mpbSnapshot}\n" +
                $"  SlimeBurnVisual state: {bvState}\n" +
                $"  last hit ({(Time.time - lastReceivedHitTime):F1}s ago): {lastReceivedHit}\n" +
                $"  last action ({(Time.time - lastActionTime):F1}s ago): {lastAction}";
        }

        private void ResolveBody()
        {
            Transform body3d = slimeVisual != null ? slimeVisual.Body3d : null;
            if (body3d == null && slimeController != null)
            {
                // Last-ditch fallback: search for a known mesh name in the slime
                // subtree. The actual mesh GO is "SlimeMesh" per the project's
                // body3d hierarchy (Slime/SlimeVisual3d/MeshBaseline/MeshDepthFlatten/SlimeMesh).
                var mfs = slimeController.GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < mfs.Length; i++)
                {
                    if (mfs[i].name == "SlimeMesh" || (mfs[i].sharedMesh != null && mfs[i].sharedMesh.name.Contains("Slime")))
                    {
                        bodyMF = mfs[i];
                        bodyMR = mfs[i].GetComponent<MeshRenderer>();
                        return;
                    }
                }
                return;
            }
            if (body3d == null) return;
            bodyMF = body3d.GetComponentInChildren<MeshFilter>();
            if (bodyMF != null) bodyMR = bodyMF.GetComponent<MeshRenderer>();
        }

        void Update()
        {
            if (Input.GetKeyDown(hitPipelineKey)) FireHitPipeline(new Vector2(0.5f, 0f), "B (right)");
            else if (Input.GetKeyDown(directMpbKey)) ForceDirectMpb();
            else if (Input.GetKeyDown(clearKey)) ClearDirect();
            else if (Input.GetKeyDown(logKey)) Debug.Log(BuildStateSnapshot());
            else if (Input.GetKeyDown(forceStrengthKey)) ForceBurnStrength();
            // Arrow-key directional tests — hit lands on the named side.
            else if (Input.GetKeyDown(KeyCode.UpArrow))    FireHitPipeline(new Vector2( 0f,  0.5f), "↑ top");
            else if (Input.GetKeyDown(KeyCode.DownArrow))  FireHitPipeline(new Vector2( 0f, -0.5f), "↓ bottom");
            else if (Input.GetKeyDown(KeyCode.LeftArrow))  FireHitPipeline(new Vector2(-0.5f, 0f), "← left");
            else if (Input.GetKeyDown(KeyCode.RightArrow)) FireHitPipeline(new Vector2( 0.5f, 0f), "→ right");
        }

        private void ForceBurnStrength()
        {
            if (burnVisual == null || bvBurnStrengthField == null)
            {
                lastAction = "FAIL: cannot reflect SlimeBurnVisual.burnStrength";
                lastActionTime = Time.time;
                return;
            }
            bvBurnStrengthField.SetValue(burnVisual, 1f);
            if (bvBurnRadiusField != null)
            {
                float r = (float)bvBurnRadiusField.GetValue(burnVisual);
                if (r <= 0f) bvBurnRadiusField.SetValue(burnVisual, 0.25f);
            }
            lastAction = "K: forced SlimeBurnVisual.burnStrength=1 (radius bumped to 0.25 if it was 0).";
            lastActionTime = Time.time;
            Debug.Log("[BurnDebug] forced SlimeBurnVisual.burnStrength=1");
        }

        private void FireHitPipeline(Vector2 worldOffsetFromCenter, string label)
        {
            if (slimeController == null) { lastAction = "FAIL: no SlimeController"; lastActionTime = Time.time; return; }
            Vector2 center = slimeController.Position;
            Vector2 hitPoint = center + worldOffsetFromCenter;
            slimeController.ApplyHazardPush(Vector2.zero, hitPoint);
            lastAction = $"{label}: ApplyHazardPush(hitPoint={hitPoint}, center={center}, offset={worldOffsetFromCenter})";
            lastActionTime = Time.time;
        }

        // While true, our LateUpdate keeps re-pushing the giant burn every frame —
        // necessary because SlimeBurnVisual.LateUpdate (order 100) also writes to
        // _Burns/_BurnCount, and would clobber a one-shot push from Update.
        private bool forceBurnActive;

        private void ForceDirectMpb()
        {
            if (bodyMR == null) ResolveBody();
            if (bodyMR == null) { lastAction = "FAIL: no body MeshRenderer"; lastActionTime = Time.time; return; }
            // Disable the real burn visual so it stops overwriting our push every frame.
            if (burnVisual != null) burnVisual.enabled = false;
            forceBurnActive = true;
            PushGiantBurn();
            lastAction = "N: SlimeBurnVisual DISABLED + direct MPB push (giant burn). Will re-push every LateUpdate while active.";
            lastActionTime = Time.time;
        }

        private void PushGiantBurn()
        {
            // Giant burn at OS origin with radius 100 — covers the entire mesh OS
            // (any sane mesh fits in [-50, 50] OS units), so the whole body should
            // turn black if SampleBurn is being executed.
            var arr = new Vector4[24];
            arr[0] = new Vector4(0f, 0f, 0f, 100f);
            bodyMR.GetPropertyBlock(probe);
            probe.SetVectorArray(BurnsID, arr);
            probe.SetInt(BurnCountID, 1);
            probe.SetFloat(BurnStrengthID, 1f);
            bodyMR.SetPropertyBlock(probe);
        }

        private void ClearDirect()
        {
            if (bodyMR == null) ResolveBody();
            if (bodyMR == null) { lastAction = "FAIL: no body MeshRenderer"; lastActionTime = Time.time; return; }
            forceBurnActive = false;
            // Re-enable the real burn visual; its next LateUpdate will write
            // _BurnCount=0 (empty list) which is what we want.
            if (burnVisual != null) burnVisual.enabled = true;
            // Also do an immediate clear in case burnVisual is null.
            var arr = new Vector4[24];
            bodyMR.GetPropertyBlock(probe);
            probe.SetVectorArray(BurnsID, arr);
            probe.SetInt(BurnCountID, 0);
            bodyMR.SetPropertyBlock(probe);
            lastAction = "M: SlimeBurnVisual re-enabled, _BurnCount cleared";
            lastActionTime = Time.time;
        }

        // LateUpdate runs at default order 0; with [DefaultExecutionOrder(150)]
        // we run AFTER SlimeBurnVisual (order 100). When force mode is on, we
        // re-push the giant burn here so it survives even if SlimeBurnVisual
        // wakes back up.
        void LateUpdate()
        {
            if (!forceBurnActive) return;
            if (bodyMR == null) ResolveBody();
            if (bodyMR == null) return;
            PushGiantBurn();
        }

    }
}
