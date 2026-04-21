using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Platforms
{
    /// <summary>
    /// Clay/mud pillar: two vertical chains (left + right) of control points share a
    /// fixed Y ladder. Each level stores a leftX and rightX. Slime impacts push the
    /// struck side's X inward along the horizontal axis. Where the two sides cross
    /// (leftX ≥ rightX) the level has no material — the pillar splits at that Y and
    /// the slime passes through. Each connected vertical run of valid levels becomes
    /// a separate mesh island and a separate PolygonCollider2D path.
    ///
    /// Slime falls below the platform → all chains lerp back to rest.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(PolygonCollider2D))]
    [ExecuteAlways] // lets LateUpdate fire in edit mode so OnValidate changes show live
    public class DeformablePlatform : MonoBehaviour
    {
        [System.Serializable]
        public struct RestLevel
        {
            [Tooltip("Y position of this level (local).")]
            public float y;
            [Tooltip("Left boundary X at rest (local).")]
            public float leftX;
            [Tooltip("Right boundary X at rest (local).")]
            public float rightX;
        }

        [Header("Shape (authored, top → bottom)")]
        [Tooltip("Each entry is one Y level. First and last are treated as caps (do not deform). " +
                 "Use the component context menu (gear icon) to generate presets, or drag scene handles.")]
        [SerializeField] List<RestLevel> restLevels = new List<RestLevel>();

        [Header("Generator Defaults (used by context-menu presets only)")]
        [Min(0.1f)] [SerializeField] float genWidth = 1.5f;
        [Min(0.1f)] [SerializeField] float genHeight = 7f;
        [Min(3)]    [SerializeField] int   genSegmentCount = 22;

        [Header("Deformation")]
        [Tooltip("Meters-pushed per m/s of slime impact speed.")]
        [Min(0f)] [SerializeField] float impactToDisplacement = 0.04f;
        [Tooltip("Hard cap on how far a single hit can push any level (local units). " +
                 "Keep below the slime's surface-probe range or the slime will detach mid-dent.")]
        [Min(0.001f)] [SerializeField] float maxPerHitDisplacement = 0.12f;
        [Tooltip("Y distance (local units) over which neighboring levels also receive a weakened push.")]
        [Min(0.001f)] [SerializeField] float falloffYRange = 0.8f;
        [Tooltip("Additional push applied continuously while the slime is attached (per second).")]
        [Min(0f)] [SerializeField] float attachedCreepPerSec = 0f;

        [Header("3D Extrusion")]
        [Min(0.01f)] [SerializeField] float depth = 0.8f;
        [Tooltip("Back face deformation as a fraction of front's. 0 = back never moves; 1 = back moves same as front. " +
                 "Lower values make the near-camera (front) dent look deeper than the far-camera (back) side.")]
        [Range(0f, 1f)] [SerializeField] float rearDeformFactor = 0.1f;

        [Header("Front Surface Roughness")]
        [Tooltip("Interior columns per level row in the front face. 0 = no interior points (flat quad strip). 4–8 recommended.")]
        [Range(0, 16)] [SerializeField] int frontInteriorCols = 6;
        [Tooltip("Random X/Y offset applied to interior grid points. 0 = pure grid, 1 = full half-cell jitter.")]
        [Range(0f, 1f)] [SerializeField] float frontInteriorJitter = 0.4f;
        [Tooltip("Perlin noise amplitude (local Z units). Applied outward (toward camera) on interior front verts only; boundary stays flush with side strip.")]
        [Min(0f)] [SerializeField] float frontNoiseAmount = 0.15f;
        [Tooltip("Perlin frequency — higher = finer bumps.")]
        [Min(0.01f)] [SerializeField] float frontNoiseScale = 1.5f;
        [Tooltip("Seed for the interior jitter RNG. Change to re-roll the bump pattern.")]
        [SerializeField] int frontJitterSeed = 42;

        [Header("Reset")]
        [Tooltip("Seconds for all levels to lerp back to rest when a reset fires. " +
                 "Reset is triggered by the scene's DeformablePlatformActivator when the " +
                 "slime leaves its range — no per-platform fall-below check runs here anymore.")]
        [Min(0.01f)] [SerializeField] float resetDuration = 0.6f;

        [Header("Sand VFX — Impact (per-platform)")]
        [Tooltip("Spawn a short sand-falling VFX burst at the contact point on each new landing impact.")]
        [SerializeField] bool sandVFXEnabled = false;
        [Tooltip("One-shot prefabs to spawn on impact — a random entry is picked per hit for variety. " +
                 "'Sand VFX URP / Sand Falling' non-loop variants fit well. Each spawned instance " +
                 "auto-destroys when its particle system finishes emitting.")]
        [SerializeField] GameObject[] sandVFXPrefabs;
        [Tooltip("Minimum impact speed (m/s) to spawn VFX. Below this, gentle touches stay silent.")]
        [Min(0f)] [SerializeField] float sandVFXMinImpactSpeed = 1.5f;

        [Header("Sand VFX — Ambient (per-platform)")]
        [Tooltip("Attach a looping sand-falling VFX to the top of the platform, auto-sized to the rest width.")]
        [SerializeField] bool ambientSandEnabled = false;
        [Tooltip("Looping prefab to instantiate as a child. Any '_Loop' variant from the Sand Falling pack works.")]
        [SerializeField] GameObject ambientSandPrefab;
        [Tooltip("Extra scale factor on the ambient emitter's width (1 = exactly match rest width).")]
        [Min(0.01f)] [SerializeField] float ambientSandWidthScale = 1f;

        [Header("Debug")]
        [SerializeField] bool drawGizmos = true;

        // --- Runtime state ---------------------------------------------------

        struct Level
        {
            public float y;
            public float leftRestX, rightRestX;
            public float leftCurX, rightCurX;
            public bool  isCap; // top & bottom — don't deform so corners stay anchored.
        }

        Level[] levels;
        bool dirty;

        Mesh mesh;
        MeshFilter meshFilter;
        PolygonCollider2D poly;

        bool      resetting;
        float     resetTimer;
        float[]   resetFromLeft;
        float[]   resetFromRight;

        SlimeController slime;
        bool wasAttachedToMe;

        GameObject ambientSandInstance;

        // Scratch lists / buffers reused per rebuild (avoid per-frame GC).
        readonly List<Vector2>    scratchOutline    = new List<Vector2>();
        readonly List<int>        scratchEarIdx     = new List<int>();
        readonly List<int>        scratchEarTris    = new List<int>();
        readonly List<Vector3>    scratchVerts      = new List<Vector3>();
        readonly List<Vector3>    scratchNorms      = new List<Vector3>();
        readonly List<Vector2>    scratchUVs        = new List<Vector2>();
        readonly List<int>        scratchTris       = new List<int>();
        readonly List<Vector2[]>  scratchPiecePaths = new List<Vector2[]>();

        // --- Unity ----------------------------------------------------------

        void Reset()
        {
            if (levels == null || levels.Length < 3) BuildRuntime();
        }

        void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            poly = GetComponent<PolygonCollider2D>();
            BuildRuntime();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            // Inspector changes at edit time → re-emit mesh/collider so params read live.
            // Delayed to avoid touching assets during inspector GUI pass.
            if (Application.isPlaying) { dirty = true; return; }
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
                if (poly == null)       poly       = GetComponent<PolygonCollider2D>();
                if (levels == null || levels.Length < 3) BuildRuntime();
                else { Rebuild(); UpdateAmbientSandVFX(); }
            };
        }
#endif

        void FixedUpdate()
        {
            if (!Application.isPlaying) return;
            if (levels == null || levels.Length < 3) return;
            if (slime == null)
            {
                slime = FindAnyObjectByType<SlimeController>();
                if (slime == null) return;
            }

            // Treat Charging the same as Attached — the slime is still resting on us, and a
            // land+immediately-charge input would otherwise skip the Attached frame entirely
            // (Airborne → Attached → Charging in one physics step) and miss the landing impact.
            bool attachedToMe = (slime.CurrentState == SlimeController.SlimeState.Attached ||
                                 slime.CurrentState == SlimeController.SlimeState.Charging)
                                && slime.SurfaceCollider == poly;

            if (attachedToMe && !wasAttachedToMe)
            {
                float impactSpeed = slime.ImpactNormalSpeed;
                ApplyImpactAt(slime.Position, impactSpeed);
                if (sandVFXEnabled && impactSpeed >= sandVFXMinImpactSpeed)
                    SpawnSandVFX(slime.Position);
            }
            else if (attachedToMe && attachedCreepPerSec > 0f)
            {
                ApplyImpactAt(slime.Position, attachedCreepPerSec * Time.fixedDeltaTime);
            }

            wasAttachedToMe = attachedToMe;

            // Reset is driven exclusively by DeformablePlatformActivator.SetActiveByManager —
            // no local fall-below polling, so a platform whose slime is below-but-still-in-range
            // won't spuriously revert.
            if (resetting) TickReset(Time.fixedDeltaTime);
        }

        void LateUpdate()
        {
            if (dirty) { Rebuild(); dirty = false; }
        }

        // --- Build ----------------------------------------------------------

        [ContextMenu("Rebuild Runtime")]
        public void BuildRuntime()
        {
            if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
            if (poly == null)       poly       = GetComponent<PolygonCollider2D>();

            if (restLevels == null || restLevels.Count < 3)
                GenerateRectangle(); // seed an initial shape so a brand-new component isn't empty

            // Author order may be messy; enforce top→bottom (Y descending).
            restLevels.Sort((a, b) => b.y.CompareTo(a.y));

            int n = restLevels.Count;
            levels = new Level[n];
            for (int i = 0; i < n; i++)
            {
                levels[i].y           = restLevels[i].y;
                levels[i].leftRestX   = restLevels[i].leftX;
                levels[i].rightRestX  = restLevels[i].rightX;
                levels[i].leftCurX    = restLevels[i].leftX;
                levels[i].rightCurX   = restLevels[i].rightX;
                levels[i].isCap       = (i == 0 || i == n - 1);
            }

            Rebuild();
            UpdateAmbientSandVFX();
        }

        // --- Shape generators (context menu) --------------------------------

        [ContextMenu("Generate/Rectangle")]
        public void GenerateRectangle()
        {
            if (restLevels == null) restLevels = new List<RestLevel>();
            restLevels.Clear();
            int n = Mathf.Max(3, genSegmentCount);
            float hw = genWidth * 0.5f, hh = genHeight * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float y = Mathf.Lerp(hh, -hh, t);
                restLevels.Add(new RestLevel { y = y, leftX = -hw, rightX = +hw });
            }
        }

        [ContextMenu("Generate/Taper (narrow top)")]
        public void GenerateTaper()
        {
            if (restLevels == null) restLevels = new List<RestLevel>();
            restLevels.Clear();
            int n = Mathf.Max(3, genSegmentCount);
            float hw = genWidth * 0.5f, hh = genHeight * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float y = Mathf.Lerp(hh, -hh, t);
                float w = Mathf.Lerp(hw * 0.05f, hw, t);
                restLevels.Add(new RestLevel { y = y, leftX = -w, rightX = +w });
            }
        }

        [ContextMenu("Generate/Hourglass")]
        public void GenerateHourglass()
        {
            if (restLevels == null) restLevels = new List<RestLevel>();
            restLevels.Clear();
            int n = Mathf.Max(3, genSegmentCount);
            float hw = genWidth * 0.5f, hh = genHeight * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float y = Mathf.Lerp(hh, -hh, t);
                float narrowness = Mathf.Abs(t - 0.5f) * 2f; // 0 at middle, 1 at ends
                float w = Mathf.Lerp(hw * 0.2f, hw, narrowness);
                restLevels.Add(new RestLevel { y = y, leftX = -w, rightX = +w });
            }
        }

        [ContextMenu("Generate/Dome Top")]
        public void GenerateDomeTop()
        {
            if (restLevels == null) restLevels = new List<RestLevel>();
            restLevels.Clear();
            int n = Mathf.Max(3, genSegmentCount);
            float hw = genWidth * 0.5f, hh = genHeight * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float y = Mathf.Lerp(hh, -hh, t);
                // top ~30% is a quarter-sine dome, rest is straight
                float w = t < 0.3f ? hw * Mathf.Sin(t / 0.3f * Mathf.PI * 0.5f) : hw;
                restLevels.Add(new RestLevel { y = y, leftX = -w, rightX = +w });
            }
        }

        [ContextMenu("Generate/Subdivide (double density)")]
        public void Subdivide()
        {
            if (restLevels == null || restLevels.Count < 2) return;
            restLevels.Sort((a, b) => b.y.CompareTo(a.y));
            var result = new List<RestLevel>(restLevels.Count * 2 - 1);
            for (int i = 0; i < restLevels.Count - 1; i++)
            {
                result.Add(restLevels[i]);
                var a = restLevels[i];
                var b = restLevels[i + 1];
                result.Add(new RestLevel
                {
                    y      = (a.y      + b.y)      * 0.5f,
                    leftX  = (a.leftX  + b.leftX)  * 0.5f,
                    rightX = (a.rightX + b.rightX) * 0.5f,
                });
            }
            result.Add(restLevels[restLevels.Count - 1]);
            restLevels = result;
        }

        public int RestLevelCount => restLevels == null ? 0 : restLevels.Count;
        public RestLevel GetRestLevel(int i) => restLevels[i];
        public void SetRestLevel(int i, RestLevel v) => restLevels[i] = v;

        // --- Deformation ----------------------------------------------------

        void ApplyImpactAt(Vector2 worldPos, float strength)
        {
            if (strength <= 0f || levels == null || levels.Length < 3) return;

            Vector2 local = transform.InverseTransformPoint(worldPos);

            int nearest = 0;
            float bestDy = float.MaxValue;
            for (int i = 0; i < levels.Length; i++)
            {
                float dy = Mathf.Abs(levels[i].y - local.y);
                if (dy < bestDy) { bestDy = dy; nearest = i; }
            }

            // Pick the side nearer to the slime's local X at the nearest level.
            float centerX = (levels[nearest].leftRestX + levels[nearest].rightRestX) * 0.5f;
            bool hitLeftSide = local.x < centerX;

            bool any = false;
            for (int i = 0; i < levels.Length; i++)
            {
                if (levels[i].isCap) continue;
                float yDist = Mathf.Abs(levels[i].y - levels[nearest].y);
                if (yDist > falloffYRange) continue;
                float w = 1f - yDist / falloffYRange;
                w = w * w * (3f - 2f * w); // smoothstep
                float push = impactToDisplacement * strength * w;
                if (push <= 0f) continue;
                push = Mathf.Min(push, maxPerHitDisplacement);

                if (hitLeftSide)
                {
                    // Can only increase (push further into material).
                    float newX = levels[i].leftCurX + push;
                    // Cap at the opposite REST wall so the dent can cross (pass-through)
                    // but the chain can't run off to infinity.
                    newX = Mathf.Min(newX, levels[i].rightRestX);
                    if (newX > levels[i].leftCurX) { levels[i].leftCurX = newX; any = true; }
                }
                else
                {
                    float newX = levels[i].rightCurX - push;
                    newX = Mathf.Max(newX, levels[i].leftRestX);
                    if (newX < levels[i].rightCurX) { levels[i].rightCurX = newX; any = true; }
                }
            }
            if (any)
            {
                dirty = true;
                resetting = false; // a fresh impact overrides any in-progress restore
            }
        }

        // --- Sand VFX -------------------------------------------------------

        /// <summary>
        /// Overwrites the local <c>sandVFXPrefabs</c> array. Called by
        /// <see cref="DeformablePlatformActivator.ApplyToAll"/> so all platforms share a
        /// single VFX list configured in one place (mirrors the WaterZoneAudioManager pattern).
        /// </summary>
        public void ApplyImpactVFX(GameObject[] prefabs)
        {
            sandVFXPrefabs = prefabs;
        }

        void SpawnSandVFX(Vector2 worldPos)
        {
            if (sandVFXPrefabs == null || sandVFXPrefabs.Length == 0) return;
            var prefab = sandVFXPrefabs[Random.Range(0, sandVFXPrefabs.Length)];
            if (prefab == null) return; // user left a slot empty — skip this hit silently
            var inst = Instantiate(prefab, worldPos, Quaternion.identity);
            // Drive auto-cleanup for one-shot (non-looping) particle systems so instances
            // don't pile up in the scene. Looping prefabs are left alone (user's call).
            foreach (var ps in inst.GetComponentsInChildren<ParticleSystem>(includeInactive: false))
            {
                var main = ps.main;
                if (!main.loop) main.stopAction = ParticleSystemStopAction.Destroy;
            }
        }

        /// <summary>
        /// Ambient loop: (re)instantiate or destroy the looping sand VFX child based on the
        /// current toggle, and size its emission shape to the platform's rest top width.
        /// Safe to call any number of times — idempotent.
        /// </summary>
        void UpdateAmbientSandVFX()
        {
            bool shouldExist = ambientSandEnabled && ambientSandPrefab != null && levels != null && levels.Length >= 2;

            if (!shouldExist)
            {
                DestroyAmbientInstance();
                return;
            }

            if (ambientSandInstance == null)
            {
                ambientSandInstance = Instantiate(ambientSandPrefab, transform);
                ambientSandInstance.name = ambientSandPrefab.name + " (ambient)";
                ambientSandInstance.hideFlags = HideFlags.DontSave; // don't serialize a runtime clone into the scene
            }

            // Position at top-cap midpoint and widen the emission shape to rest width × scale factor.
            float topY      = levels[0].y;
            float topLeftX  = levels[0].leftRestX;
            float topRightX = levels[0].rightRestX;
            float topWidth  = (topRightX - topLeftX) * ambientSandWidthScale;
            float centerX   = (topLeftX + topRightX) * 0.5f;
            ambientSandInstance.transform.localPosition = new Vector3(centerX, topY, 0f);
            ambientSandInstance.transform.localRotation = Quaternion.identity;

            foreach (var ps in ambientSandInstance.GetComponentsInChildren<ParticleSystem>(includeInactive: true))
            {
                var shape = ps.shape;
                var s = shape.scale;
                shape.scale = new Vector3(topWidth, s.y, s.z);
            }
        }

        void DestroyAmbientInstance()
        {
            if (ambientSandInstance == null) return;
            if (Application.isPlaying) Destroy(ambientSandInstance);
            else DestroyImmediate(ambientSandInstance);
            ambientSandInstance = null;
        }

        void OnDisable()
        {
            // Avoid leaking instances when the component is disabled / the scene unloads.
            DestroyAmbientInstance();
        }

        // --- Activation (driven by DeformablePlatformActivator) -------------

        /// <summary>
        /// Called by the scene's DeformablePlatformActivator via OnTriggerEnter/Exit.
        /// Exit (slime left the keep-alive range) triggers a reset back to rest.
        /// Enter is a no-op — an already-running reset finishes on its own, and any
        /// new impact will abort the reset via ApplyImpactAt.
        /// </summary>
        public void SetActiveByManager(bool active)
        {
            if (active) return;
            if (resetting) return;
            if (!HasAnyDeformation()) return;
            BeginReset();
        }

        bool HasAnyDeformation()
        {
            if (levels == null) return false;
            for (int i = 0; i < levels.Length; i++)
            {
                if (!Mathf.Approximately(levels[i].leftCurX,  levels[i].leftRestX))  return true;
                if (!Mathf.Approximately(levels[i].rightCurX, levels[i].rightRestX)) return true;
            }
            return false;
        }

        // --- Reset ----------------------------------------------------------

        void BeginReset()
        {
            resetting = true;
            resetTimer = 0f;
            if (resetFromLeft == null || resetFromLeft.Length != levels.Length)
            {
                resetFromLeft  = new float[levels.Length];
                resetFromRight = new float[levels.Length];
            }
            for (int i = 0; i < levels.Length; i++)
            {
                resetFromLeft[i]  = levels[i].leftCurX;
                resetFromRight[i] = levels[i].rightCurX;
            }
        }

        void TickReset(float dt)
        {
            resetTimer += dt;
            float t = Mathf.Clamp01(resetTimer / resetDuration);
            float s = t * t * (3f - 2f * t);
            for (int i = 0; i < levels.Length; i++)
            {
                levels[i].leftCurX  = Mathf.Lerp(resetFromLeft[i],  levels[i].leftRestX,  s);
                levels[i].rightCurX = Mathf.Lerp(resetFromRight[i], levels[i].rightRestX, s);
            }
            dirty = true;
            if (t >= 1f) resetting = false;
        }

        // --- Rebuild (collider + mesh) --------------------------------------

        void Rebuild()
        {
            if (meshFilter == null || poly == null) return;
            if (levels == null || levels.Length < 3) return;

            // Walk level ladder, detect connected runs of valid (non-pinched) levels.
            // A level is "pinched" when leftCurX >= rightCurX.
            scratchPiecePaths.Clear();
            scratchVerts.Clear();
            scratchNorms.Clear();
            scratchUVs.Clear();
            scratchTris.Clear();

            int i = 0;
            while (i < levels.Length)
            {
                while (i < levels.Length && IsPinched(i)) i++;
                if (i >= levels.Length) break;
                int start = i;
                while (i < levels.Length && !IsPinched(i)) i++;
                int end = i - 1;
                // A piece needs at least 2 adjacent valid levels to form a quad.
                if (end > start) EmitPiece(start, end);
            }

            // Collider paths
            poly.pathCount = scratchPiecePaths.Count;
            for (int k = 0; k < scratchPiecePaths.Count; k++)
                poly.SetPath(k, scratchPiecePaths[k]);

            // Mesh
            if (mesh == null)
            {
                mesh = new Mesh { name = "DeformablePlatform_Mesh" };
                mesh.MarkDynamic();
                meshFilter.sharedMesh = mesh;
            }
            mesh.Clear();
            if (scratchVerts.Count > 0)
            {
                // Use 32-bit indices if vertex count is high (many pieces × many levels).
                mesh.indexFormat = scratchVerts.Count > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16;
                mesh.SetVertices(scratchVerts);
                mesh.SetNormals(scratchNorms);
                mesh.SetUVs(0, scratchUVs);
                mesh.SetTriangles(scratchTris, 0);
                mesh.RecalculateBounds();
            }
        }

        bool IsPinched(int i) => levels[i].leftCurX >= levels[i].rightCurX;

        // Emit a single piece spanning levels [start..end] (inclusive).
        // Appends to scratchPiecePaths (collider), scratchVerts/norms/uvs/tris (mesh).
        //
        // Each piece is built as K+1 "rings" along Z. Ring 0 is the front face
        // (fully deformed). Ring K is the back face (deformation multiplied by
        // rearDeformFactor). Intermediate rings interpolate. The side strip
        // stitches consecutive rings so a dent appears deeper from near-camera
        // than from far-camera — the far side is visible *through* the near dent.
        void EmitPiece(int start, int end)
        {
            // 1. Build deformed outline (front-facing) and rest outline (back-facing anchor).
            scratchOutline.Clear();
            for (int i = start; i <= end; i++)
                scratchOutline.Add(new Vector2(levels[i].leftCurX, levels[i].y));
            for (int i = end; i >= start; i--)
                scratchOutline.Add(new Vector2(levels[i].rightCurX, levels[i].y));
            int n = scratchOutline.Count;

            scratchRestOutline.Clear();
            for (int i = start; i <= end; i++)
                scratchRestOutline.Add(new Vector2(levels[i].leftRestX, levels[i].y));
            for (int i = end; i >= start; i--)
                scratchRestOutline.Add(new Vector2(levels[i].rightRestX, levels[i].y));

            // 2. Collider path (just the deformed 2D outline — z-tapering is purely visual).
            var pathArr = new Vector2[n];
            for (int k = 0; k < n; k++) pathArr[k] = scratchOutline[k];
            scratchPiecePaths.Add(pathArr);

            // 3. Build two Z-rings. Ring 0 is front (full deformation, z=-d/2),
            //    ring 1 is back (factor=rearDeformFactor, z=+d/2). The side strip is
            //    a single quad per edge between them — additional Z subdivisions added
            //    no visible detail so the param was dropped.
            const int K = 1;
            float zF = -depth * 0.5f;
            float zB = +depth * 0.5f;

            EnsureRingBuffers(K + 1, n);
            ringZ[0] = zF;
            ringZ[1] = zB;
            for (int j = 0; j < n; j++)
            {
                ringOutlines[0][j] = scratchOutline[j]; // front = fully deformed
                ringOutlines[1][j] = Vector2.Lerp(scratchRestOutline[j], scratchOutline[j], rearDeformFactor);
            }

            // 4. Ear-clip the deformed outline — produces CCW triangle indices usable by any ring.
            EarClip(n);

            // 5. Emit the three face groups.
            EmitFrontFaceGrid(ringZ[0], start, end);
            EmitBackFace(ringOutlines[K], ringZ[K], n);
            EmitSideStrip(K, n);
        }

        // Per-ring scratch buffers (resized lazily so we don't per-frame-allocate).
        Vector2[][] ringOutlines;
        float[]     ringZ;
        readonly List<Vector2> scratchRestOutline = new List<Vector2>();

        void EnsureRingBuffers(int ringCount, int n)
        {
            if (ringOutlines == null || ringOutlines.Length != ringCount)
                ringOutlines = new Vector2[ringCount][];
            for (int k = 0; k < ringCount; k++)
                if (ringOutlines[k] == null || ringOutlines[k].Length != n)
                    ringOutlines[k] = new Vector2[n];
            if (ringZ == null || ringZ.Length != ringCount)
                ringZ = new float[ringCount];
        }

        // Emit the front face as a strip-grid: each level row has (frontInteriorCols + 2)
        // vertices — col 0 on the left chain, col (cols-1) on the right chain, and cols
        // in between as jittered interior points. Interior verts get Perlin Z displacement;
        // boundary verts stay flush at z=zFront so the side strip meets them cleanly.
        //
        // Mirrors pogo_mud_generator_v1.py's "grid of scattered interior points + Delaunay"
        // approach but uses the chain's Y ladder as the grid row structure (no CDT needed).
        Vector3[,] frontGrid3D;
        void EmitFrontFaceGrid(float zFront, int pieceStart, int pieceEnd)
        {
            int rowCount = pieceEnd - pieceStart + 1;
            if (rowCount < 2) return;
            int cols = Mathf.Max(0, frontInteriorCols) + 2; // +2 for left + right boundary cols

            // Stable RNG so jitter doesn't flicker between rebuilds.
            var rng = new System.Random(unchecked(frontJitterSeed * 7919 ^ pieceStart * 1337));

            // Resize grid buffer if needed (reused across pieces).
            if (frontGrid3D == null || frontGrid3D.GetLength(0) < rowCount || frontGrid3D.GetLength(1) < cols)
                frontGrid3D = new Vector3[Mathf.Max(rowCount, frontGrid3D?.GetLength(0) ?? 0),
                                          Mathf.Max(cols,     frontGrid3D?.GetLength(1) ?? 0)];

            // Build grid of 3D positions.
            for (int r = 0; r < rowCount; r++)
            {
                int lvlIdx = pieceStart + r;
                float y     = levels[lvlIdx].y;
                float leftX = levels[lvlIdx].leftCurX;
                float rightX= levels[lvlIdx].rightCurX;
                float width = rightX - leftX;
                float cellW = width / (cols - 1);
                // Y cell height: average spacing to neighbors, used for vertical jitter.
                float cellH = 0f;
                if (rowCount > 1)
                {
                    if (r > 0)            cellH += Mathf.Abs(levels[pieceStart + r].y - levels[pieceStart + r - 1].y);
                    if (r < rowCount - 1) cellH += Mathf.Abs(levels[pieceStart + r].y - levels[pieceStart + r + 1].y);
                    cellH *= 0.5f;
                }

                bool rowIsCap = (r == 0 || r == rowCount - 1); // top cap + bottom cap rows stay flush

                for (int c = 0; c < cols; c++)
                {
                    float t = c / (float)(cols - 1);
                    float x = Mathf.Lerp(leftX, rightX, t);
                    float yy = y;

                    bool colIsBoundary   = (c == 0 || c == cols - 1);
                    bool isBoundaryVert  = rowIsCap || colIsBoundary;

                    if (!isBoundaryVert && frontInteriorJitter > 0f)
                    {
                        x  += ((float)rng.NextDouble() * 2f - 1f) * cellW * 0.5f * frontInteriorJitter;
                        yy += ((float)rng.NextDouble() * 2f - 1f) * cellH * 0.5f * frontInteriorJitter;
                    }

                    float z = zFront;
                    if (!isBoundaryVert && frontNoiseAmount > 0f)
                    {
                        float nv = Mathf.PerlinNoise(x * frontNoiseScale + 13.4f,
                                                      yy * frontNoiseScale + 71.2f);
                        z -= nv * frontNoiseAmount; // toward camera (-Z)
                    }

                    frontGrid3D[r, c] = new Vector3(x, yy, z);
                }
            }

            // Triangulate each cell with an alternating diagonal for a balanced pattern.
            for (int r = 0; r < rowCount - 1; r++)
            {
                for (int c = 0; c < cols - 1; c++)
                {
                    Vector3 A = frontGrid3D[r,     c    ]; // top-left  (higher Y, lower r)
                    Vector3 B = frontGrid3D[r,     c + 1]; // top-right
                    Vector3 C = frontGrid3D[r + 1, c + 1]; // bot-right
                    Vector3 D = frontGrid3D[r + 1, c    ]; // bot-left
                    bool altDiag = ((r + c) & 1) == 0;
                    if (altDiag)
                    {
                        // \ diagonal: (A,C) splits quad into (A,B,C) + (A,C,D)
                        EmitFlatTri(A, B, C);
                        EmitFlatTri(A, C, D);
                    }
                    else
                    {
                        // / diagonal: (B,D) splits quad into (A,B,D) + (B,C,D)
                        EmitFlatTri(A, B, D);
                        EmitFlatTri(B, C, D);
                    }
                }
            }
        }

        void EmitFlatTri(Vector3 A, Vector3 B, Vector3 C)
        {
            Vector3 nrm = Vector3.Cross(B - A, C - A).normalized;
            int vBase = scratchVerts.Count;
            scratchVerts.Add(A); scratchNorms.Add(nrm); scratchUVs.Add(new Vector2(A.x, A.y));
            scratchVerts.Add(B); scratchNorms.Add(nrm); scratchUVs.Add(new Vector2(B.x, B.y));
            scratchVerts.Add(C); scratchNorms.Add(nrm); scratchUVs.Add(new Vector2(C.x, C.y));
            scratchTris.Add(vBase); scratchTris.Add(vBase + 1); scratchTris.Add(vBase + 2);
        }

        // Emit the back face: single ring outline, flat normal, no subdivision or noise.
        // Triangle winding is reversed so it faces +Z (away from camera).
        void EmitBackFace(Vector2[] ring, float zBack, int n)
        {
            int vBase = scratchVerts.Count;
            for (int i = 0; i < n; i++)
            {
                scratchVerts.Add(new Vector3(ring[i].x, ring[i].y, zBack));
                scratchNorms.Add(new Vector3(0f, 0f, 1f));
                scratchUVs.Add(ring[i]);
            }
            for (int k = 0; k < scratchEarTris.Count; k += 3)
            {
                // Reverse winding so back face faces +Z.
                scratchTris.Add(vBase + scratchEarTris[k + 0]);
                scratchTris.Add(vBase + scratchEarTris[k + 2]);
                scratchTris.Add(vBase + scratchEarTris[k + 1]);
            }
        }

        // Emit the side strip as a ladder of flat-normal quads, K rings × n edges.
        // Each edge gets its own set of verts (flat per edge) and every edge-ring-pair
        // forms one quad. Normals are computed per quad so the taper reads properly lit.
        void EmitSideStrip(int K, int n)
        {
            float perimCursor = 0f;
            for (int e = 0; e < n; e++)
            {
                int eNext = (e + 1) % n;
                // Perimeter-length U so the mud texture tiles consistently around the rim.
                float edgeLen = Vector2.Distance(scratchOutline[e], scratchOutline[eNext]);
                float u0 = perimCursor;
                float u1 = perimCursor + edgeLen;
                perimCursor = u1;

                for (int k = 0; k < K; k++)
                {
                    // 4 verts of the quad (ring k and ring k+1, outline pt e and e+1).
                    Vector2 a0 = ringOutlines[k][e];
                    Vector2 b0 = ringOutlines[k][eNext];
                    Vector2 a1 = ringOutlines[k + 1][e];
                    Vector2 b1 = ringOutlines[k + 1][eNext];
                    float zA = ringZ[k];
                    float zB = ringZ[k + 1];

                    Vector3 A = new Vector3(a0.x, a0.y, zA);
                    Vector3 B = new Vector3(b0.x, b0.y, zA);
                    Vector3 C = new Vector3(a1.x, a1.y, zB);
                    Vector3 D = new Vector3(b1.x, b1.y, zB);
                    // Tri winding: (A, B, C) and (B, D, C) — same as before, outward for CCW outline.
                    Vector3 nrm = Vector3.Cross(B - A, C - A).normalized;

                    float v0 = k / (float)K;
                    float v1 = (k + 1) / (float)K;

                    int vBase = scratchVerts.Count;
                    scratchVerts.Add(A); scratchNorms.Add(nrm); scratchUVs.Add(new Vector2(u0, v0));
                    scratchVerts.Add(B); scratchNorms.Add(nrm); scratchUVs.Add(new Vector2(u1, v0));
                    scratchVerts.Add(C); scratchNorms.Add(nrm); scratchUVs.Add(new Vector2(u0, v1));
                    scratchVerts.Add(D); scratchNorms.Add(nrm); scratchUVs.Add(new Vector2(u1, v1));
                    scratchTris.Add(vBase + 0); scratchTris.Add(vBase + 1); scratchTris.Add(vBase + 2);
                    scratchTris.Add(vBase + 1); scratchTris.Add(vBase + 3); scratchTris.Add(vBase + 2);
                }
            }
        }

        // Ear clipping over scratchOutline (CCW). Writes triangle indices (into the outline)
        // to scratchEarTris.
        void EarClip(int n)
        {
            scratchEarTris.Clear();
            if (n < 3) return;

            scratchEarIdx.Clear();
            for (int k = 0; k < n; k++) scratchEarIdx.Add(k);

            int guard = n * n;
            while (scratchEarIdx.Count >= 3 && guard-- > 0)
            {
                int m = scratchEarIdx.Count;
                bool clipped = false;
                for (int k = 0; k < m; k++)
                {
                    int prev = scratchEarIdx[(k - 1 + m) % m];
                    int curr = scratchEarIdx[k];
                    int next = scratchEarIdx[(k + 1) % m];
                    if (IsEar(prev, curr, next))
                    {
                        scratchEarTris.Add(prev);
                        scratchEarTris.Add(curr);
                        scratchEarTris.Add(next);
                        scratchEarIdx.RemoveAt(k);
                        clipped = true;
                        break;
                    }
                }
                if (!clipped) break;
            }
        }

        bool IsEar(int prev, int curr, int next)
        {
            Vector2 a = scratchOutline[prev], b = scratchOutline[curr], c = scratchOutline[next];
            float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            if (cross <= 0f) return false;
            for (int k = 0; k < scratchEarIdx.Count; k++)
            {
                int idx = scratchEarIdx[k];
                if (idx == prev || idx == curr || idx == next) continue;
                if (PointInTriangle(scratchOutline[idx], a, b, c)) return false;
            }
            return true;
        }

        static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool hasNeg = (d1 < 0f) || (d2 < 0f) || (d3 < 0f);
            bool hasPos = (d1 > 0f) || (d2 > 0f) || (d3 > 0f);
            return !(hasNeg && hasPos);
        }

        // --- Gizmos ---------------------------------------------------------

        void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;

            // Authored rest outline (source of truth before runtime deform).
            if (restLevels != null && restLevels.Count >= 2)
            {
                Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.45f);
                for (int i = 0; i < restLevels.Count - 1; i++)
                {
                    var a = restLevels[i];
                    var b = restLevels[i + 1];
                    Vector3 aL = transform.TransformPoint(new Vector3(a.leftX,  a.y, 0f));
                    Vector3 bL = transform.TransformPoint(new Vector3(b.leftX,  b.y, 0f));
                    Vector3 aR = transform.TransformPoint(new Vector3(a.rightX, a.y, 0f));
                    Vector3 bR = transform.TransformPoint(new Vector3(b.rightX, b.y, 0f));
                    Gizmos.DrawLine(aL, bL);
                    Gizmos.DrawLine(aR, bR);
                }
                // Top and bottom caps
                var first = restLevels[0];
                var last  = restLevels[restLevels.Count - 1];
                Gizmos.DrawLine(
                    transform.TransformPoint(new Vector3(first.leftX,  first.y, 0f)),
                    transform.TransformPoint(new Vector3(first.rightX, first.y, 0f)));
                Gizmos.DrawLine(
                    transform.TransformPoint(new Vector3(last.leftX,   last.y, 0f)),
                    transform.TransformPoint(new Vector3(last.rightX,  last.y, 0f)));
            }

            if (levels == null) return;

            // Left chain (yellow) + right chain (cyan)
            Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.9f);
            for (int k = 0; k < levels.Length - 1; k++)
            {
                Vector3 a = transform.TransformPoint(new Vector3(levels[k].leftCurX,   levels[k].y,   0f));
                Vector3 b = transform.TransformPoint(new Vector3(levels[k+1].leftCurX, levels[k+1].y, 0f));
                Gizmos.DrawLine(a, b);
            }
            Gizmos.color = new Color(0.3f, 1f, 1f, 0.9f);
            for (int k = 0; k < levels.Length - 1; k++)
            {
                Vector3 a = transform.TransformPoint(new Vector3(levels[k].rightCurX,   levels[k].y,   0f));
                Vector3 b = transform.TransformPoint(new Vector3(levels[k+1].rightCurX, levels[k+1].y, 0f));
                Gizmos.DrawLine(a, b);
            }

            // Pinched levels: draw an X
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.8f);
            for (int k = 0; k < levels.Length; k++)
            {
                if (!IsPinched(k)) continue;
                Vector3 L = transform.TransformPoint(new Vector3(levels[k].leftCurX,  levels[k].y, 0f));
                Vector3 R = transform.TransformPoint(new Vector3(levels[k].rightCurX, levels[k].y, 0f));
                Gizmos.DrawLine(L, R);
            }
        }
    }
}
