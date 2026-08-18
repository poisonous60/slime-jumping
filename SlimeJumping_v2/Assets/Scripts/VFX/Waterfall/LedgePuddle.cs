using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.VFX.Waterfall
{
    /// <summary>
    /// 흙 턱에 파인 구멍을 채우는 웅덩이.
    ///
    /// 발자국을 원이나 타원으로 authored 하면 지형이 어떻게 파여 있든 그 모양이 안 나온다.
    /// 그래서 지형 메시를 직접 샘플해 수면보다 낮은 셀만 물로 만든다 — 구멍 윤곽이 곧 물가가 된다.
    /// 윤곽선은 격자 해상도가 아니라 지형이 정한다.
    ///
    /// 높이 샘플은 Physics2D 로 못 구한다. 2D 레이는 Z 를 못 봐서 X 컬럼당 값 하나만 나오고
    /// 그걸 Z 로 복사하면 웅덩이가 구멍 밖으로 삐져나온다. 여기서는 메시 삼각형을 XZ 로 투영해
    /// 격자점을 품는 삼각형에서 y 를 보간하고, 껍질이라 위아래가 겹치므로 최댓값을 상면으로 쓴다.
    ///
    /// 세로 두께는 구멍 벽이 만든다. 물은 수면 한 장이고 옆은 바위가 가린다 — 물에 벽을 붙이면
    /// 지형과 겹쳐 z-fight 만 난다.
    ///
    /// 움직이지 않는 물이다. 수위는 고정이고 유입으로 오르지 않는다. 반응은 슬라임 착지 찰박 하나.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [AddComponentMenu("SlimeJumping/VFX/Waterfall Ledge Puddle")]
    public sealed class LedgePuddle : MonoBehaviour
    {
        struct Ripple
        {
            public Vector2 xz;
            public float age;
            public float amp;
        }

        [SerializeField] WaterfallConfig configOverride;

        [Tooltip("웅덩이 윤곽을 유도할 지형 메시. 비면 물이 안 생긴다. 에디터 전용 - 런타임은 구운 메시를 쓴다.")]
        [SerializeField] MeshFilter terrainSource;

        [Tooltip("에디터에서 구운 웅덩이 메시. 지형 메시가 Read/Write 꺼져 있으면 빌드에서 정점을 못 읽어 물이 통째로 사라지므로 런타임은 이걸 쓴다.")]
        [SerializeField] Mesh bakedMesh;
        [SerializeField] [HideInInspector] int bakedTopVertCount;
        [SerializeField] [HideInInspector] Bounds bakedWetBounds;

        [Tooltip("슬라임이 웅덩이에 세게 떨어질 때. 비우면 무음.")]
        [SerializeField] SlimeJumping.Audio.SoundDef landHardSound;

        [Tooltip("살살 내려앉을 때. 비우면 무음.")]
        [SerializeField] SlimeJumping.Audio.SoundDef landSoftSound;

        [Tooltip("이 낙하 속도 이상이면 hard. 슬라임 착지음 규약과 같은 기본값 8.")]
        [Min(0f)] [SerializeField] float landHardThreshold = 8f;

        Mesh mesh;
        int topVertCount;
        Vector3[] restVerts;
        Vector2[] restXZ;
        float[] restEdge;
        Vector3[] verts;
        Color[] colors;
        Bounds wetBounds;

        readonly List<Ripple> ripples = new List<Ripple>();
        float lastSlimeSplash = -99f;

        WaterfallConfig Cfg => WaterfallConfig.Resolve(configOverride);

        public Bounds WetBounds => wetBounds;

        void OnEnable()
        {
            WaterfallConfig.Changed += RebuildDeferred;
            Rebuild();
        }

        void OnDisable() => WaterfallConfig.Changed -= RebuildDeferred;

        void RebuildDeferred()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () => { if (this != null) Rebuild(); };
#endif
        }

        void OnValidate() => RebuildDeferred();

        [ContextMenu("Rebuild")]
        public void Rebuild()
        {
            if (Application.isPlaying && bakedMesh != null) { LoadBaked(); return; }
            Generate();
        }

        /// <summary>구운 메시를 그대로 올리고 파문에 필요한 것만 되살린다. 지형은 안 훑는다.</summary>
        void LoadBaked()
        {
            EnsureMesh();
            mesh.Clear();
            mesh.indexFormat = bakedMesh.indexFormat;
            mesh.vertices = bakedMesh.vertices;
            mesh.uv = bakedMesh.uv;
            mesh.colors = bakedMesh.colors;
            mesh.triangles = bakedMesh.triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            restVerts = mesh.vertices;
            colors = mesh.colors;
            int n = restVerts.Length;
            verts = new Vector3[n];
            restXZ = new Vector2[n];
            restEdge = new float[n];
            var l2w = transform.localToWorldMatrix;
            for (int i = 0; i < n; i++)
            {
                var w = l2w.MultiplyPoint3x4(restVerts[i]);
                restXZ[i] = new Vector2(w.x, w.z);
                restEdge[i] = colors[i].a;
            }
            System.Array.Copy(restVerts, verts, n);
            topVertCount = bakedTopVertCount;
            wetBounds = bakedWetBounds;
            FitTrigger(Cfg);
        }

        void Generate()
        {
            var cfg = Cfg;
            int nx = Mathf.Max(4, cfg.puddleGridX);
            int nz = Mathf.Max(4, cfg.puddleGridZ);
            int stride = nx + 1;
            int vc = stride * (nz + 1);

            var terrain = SampleTerrainHeights(cfg, nx, nz);

            restVerts = new Vector3[vc];
            restXZ = new Vector2[vc];
            restEdge = new float[vc];
            verts = new Vector3[vc];
            colors = new Color[vc];
            var uvs = new Vector2[vc];

            var w2l = transform.worldToLocalMatrix;
            float y = cfg.puddleWaterY + cfg.puddleSurfaceLift;
            float feather = Mathf.Max(0.005f, cfg.puddleShoreFeather);

            for (int r = 0; r <= nz; r++)
            {
                float z = Mathf.Lerp(cfg.puddleAreaZMin, cfg.puddleAreaZMax, r / (float)nz);
                for (int c = 0; c <= nx; c++)
                {
                    float x = Mathf.Lerp(cfg.puddleAreaXMin, cfg.puddleAreaXMax, c / (float)nx);
                    int i = r * stride + c;

                    restXZ[i] = new Vector2(x, z);
                    restVerts[i] = w2l.MultiplyPoint3x4(new Vector3(x, y, z));
                    uvs[i] = new Vector2(x - cfg.puddleAreaXMin, z - cfg.puddleAreaZMin);

                    float depth = cfg.puddleWaterY - terrain[i];
                    restEdge[i] = 1f - Mathf.Clamp01(depth / feather);
                }
            }

            var tris = new List<int>(nx * nz * 6);
            bool any = false;
            var min = new Vector3(float.MaxValue, y, float.MaxValue);
            var max = new Vector3(float.MinValue, y, float.MinValue);

            for (int r = 0; r < nz; r++)
            {
                for (int c = 0; c < nx; c++)
                {
                    int i0 = r * stride + c;
                    int i1 = i0 + 1;
                    int i2 = i0 + stride;
                    int i3 = i2 + 1;
                    // 네 꼭짓점이 전부 물 위면 셀을 버린다. 남는 셀 경계가 구멍 윤곽이다.
                    if (terrain[i0] >= cfg.puddleWaterY && terrain[i1] >= cfg.puddleWaterY &&
                        terrain[i2] >= cfg.puddleWaterY && terrain[i3] >= cfg.puddleWaterY) continue;

                    tris.Add(i0); tris.Add(i2); tris.Add(i1);
                    tris.Add(i1); tris.Add(i2); tris.Add(i3);
                    any = true;
                    foreach (int q in new[] { i0, i1, i2, i3 })
                    {
                        min.x = Mathf.Min(min.x, restXZ[q].x); max.x = Mathf.Max(max.x, restXZ[q].x);
                        min.z = Mathf.Min(min.z, restXZ[q].y); max.z = Mathf.Max(max.z, restXZ[q].y);
                    }
                }
            }

            wetBounds = any
                ? new Bounds((min + max) * 0.5f, new Vector3(max.x - min.x, 0.02f, max.z - min.z))
                : new Bounds(new Vector3(0f, y, 0f), Vector3.zero);

            // 물가는 수심이 0 이라 벽이 없지만, 스캔 경계에서 잘린 쪽은 물기둥이 그대로 노출된다.
            // 그 경계마다 지형까지 벽을 세운다. 벽 윗변은 수면 정점을 공유해 한 덩어리로 이어진다.
            topVertCount = vc;
            var skirtPos = new List<Vector3>();
            var skirtUV = new List<Vector2>();
            var skirtEdge = new List<float>();
            var bottomOf = new Dictionary<int, int>();

            System.Func<int, int> Bottom = gi =>
            {
                if (bottomOf.TryGetValue(gi, out int found)) return found;
                float ty = terrain[gi];
                if (ty > cfg.puddleWaterY) ty = cfg.puddleWaterY;
                int idx = vc + skirtPos.Count;
                skirtPos.Add(w2l.MultiplyPoint3x4(new Vector3(restXZ[gi].x, ty, restXZ[gi].y)));
                skirtUV.Add(uvs[gi]);
                skirtEdge.Add(restEdge[gi]);
                bottomOf[gi] = idx;
                return idx;
            };
            System.Func<int, int, bool> CellWet = (rr, cc) =>
            {
                if (rr < 0 || cc < 0 || rr >= nz || cc >= nx) return false;
                int a = rr * stride + cc;
                return terrain[a] < cfg.puddleWaterY || terrain[a + 1] < cfg.puddleWaterY ||
                       terrain[a + stride] < cfg.puddleWaterY || terrain[a + stride + 1] < cfg.puddleWaterY;
            };
            System.Action<int, int> Wall = (ga, gb) =>
            {
                int pa = Bottom(ga), pb = Bottom(gb);
                tris.Add(ga); tris.Add(gb); tris.Add(pb);
                tris.Add(ga); tris.Add(pb); tris.Add(pa);
            };

            for (int r = 0; r < nz; r++)
            {
                for (int c = 0; c < nx; c++)
                {
                    if (!CellWet(r, c)) continue;
                    int i0 = r * stride + c;
                    if (!CellWet(r, c - 1)) Wall(i0, i0 + stride);
                    if (!CellWet(r, c + 1)) Wall(i0 + 1, i0 + stride + 1);
                    if (!CellWet(r - 1, c)) Wall(i0, i0 + 1);
                    if (!CellWet(r + 1, c)) Wall(i0 + stride, i0 + stride + 1);
                }
            }

            int total = vc + skirtPos.Count;
            if (skirtPos.Count > 0)
            {
                System.Array.Resize(ref restVerts, total);
                System.Array.Resize(ref restXZ, total);
                System.Array.Resize(ref restEdge, total);
                System.Array.Resize(ref verts, total);
                System.Array.Resize(ref colors, total);
                System.Array.Resize(ref uvs, total);
                for (int i = 0; i < skirtPos.Count; i++)
                {
                    restVerts[vc + i] = skirtPos[i];
                    uvs[vc + i] = skirtUV[i];
                    restEdge[vc + i] = skirtEdge[i];
                    restXZ[vc + i] = Vector2.zero;
                }
            }

            System.Array.Copy(restVerts, verts, total);
            for (int i = 0; i < total; i++) colors[i] = new Color(1f, 1f, 1f, restEdge[i]);

            EnsureMesh();
            mesh.Clear();
            mesh.indexFormat = total > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors = colors;
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            FitTrigger(cfg);
        }

        // 격자점을 품는 삼각형에서 y 를 보간한다. 닫힌 껍질이라 위아래가 겹치므로 최댓값이 상면이다.
        float[] SampleTerrainHeights(WaterfallConfig cfg, int nx, int nz)
        {
            int stride = nx + 1;
            var heights = new float[stride * (nz + 1)];
            for (int i = 0; i < heights.Length; i++) heights[i] = float.MaxValue;
            if (terrainSource == null || terrainSource.sharedMesh == null) return heights;

            var src = terrainSource.sharedMesh.vertices;
            var idx = terrainSource.sharedMesh.triangles;
            var l2w = terrainSource.transform.localToWorldMatrix;

            var world = new Vector3[src.Length];
            for (int i = 0; i < src.Length; i++) world[i] = l2w.MultiplyPoint3x4(src[i]);

            float pad = 0.05f;
            for (int t = 0; t < idx.Length; t += 3)
            {
                Vector3 a = world[idx[t]], b = world[idx[t + 1]], c = world[idx[t + 2]];
                float tXMin = Mathf.Min(a.x, Mathf.Min(b.x, c.x));
                float tXMax = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
                float tZMin = Mathf.Min(a.z, Mathf.Min(b.z, c.z));
                float tZMax = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
                if (tXMax < cfg.puddleAreaXMin - pad || tXMin > cfg.puddleAreaXMax + pad) continue;
                if (tZMax < cfg.puddleAreaZMin - pad || tZMin > cfg.puddleAreaZMax + pad) continue;

                int cLo = Mathf.Max(0, Mathf.FloorToInt(Mathf.InverseLerp(cfg.puddleAreaXMin, cfg.puddleAreaXMax, tXMin) * nx));
                int cHi = Mathf.Min(nx, Mathf.CeilToInt(Mathf.InverseLerp(cfg.puddleAreaXMin, cfg.puddleAreaXMax, tXMax) * nx));
                int rLo = Mathf.Max(0, Mathf.FloorToInt(Mathf.InverseLerp(cfg.puddleAreaZMin, cfg.puddleAreaZMax, tZMin) * nz));
                int rHi = Mathf.Min(nz, Mathf.CeilToInt(Mathf.InverseLerp(cfg.puddleAreaZMin, cfg.puddleAreaZMax, tZMax) * nz));

                float det = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(det) < 1e-9f) continue;

                for (int r = rLo; r <= rHi; r++)
                {
                    float pz = Mathf.Lerp(cfg.puddleAreaZMin, cfg.puddleAreaZMax, r / (float)nz);
                    for (int c2 = cLo; c2 <= cHi; c2++)
                    {
                        float px = Mathf.Lerp(cfg.puddleAreaXMin, cfg.puddleAreaXMax, c2 / (float)nx);
                        float w0 = ((b.z - c.z) * (px - c.x) + (c.x - b.x) * (pz - c.z)) / det;
                        if (w0 < -1e-4f || w0 > 1.0001f) continue;
                        float w1 = ((c.z - a.z) * (px - c.x) + (a.x - c.x) * (pz - c.z)) / det;
                        if (w1 < -1e-4f || w1 > 1.0001f) continue;
                        float w2 = 1f - w0 - w1;
                        if (w2 < -1e-4f) continue;

                        float hy = a.y * w0 + b.y * w1 + c.y * w2;
                        int k = r * stride + c2;
                        if (heights[k] == float.MaxValue || hy > heights[k]) heights[k] = hy;
                    }
                }
            }
            return heights;
        }

        void FitTrigger(WaterfallConfig cfg)
        {
            var box = GetComponent<BoxCollider2D>();
            if (box == null) return;
            box.isTrigger = true;
            box.offset = transform.InverseTransformPoint(
                new Vector3(wetBounds.center.x, cfg.puddleWaterY, 0f));
            box.size = new Vector2(Mathf.Max(0.1f, wetBounds.size.x), 0.4f);
        }

        void LateUpdate()
        {
            if (!Application.isPlaying || verts == null || ripples.Count == 0) return;
            var cfg = Cfg;
            float dt = Time.deltaTime;

            for (int i = ripples.Count - 1; i >= 0; i--)
            {
                var rp = ripples[i];
                rp.age += dt;
                if (rp.age >= cfg.rippleLife) ripples.RemoveAt(i);
                else ripples[i] = rp;
            }

            float k = Mathf.PI * 2f / Mathf.Max(0.05f, cfg.rippleWavelength);
            float ampRef = Mathf.Max(0.01f, cfg.slimeAmplitude);

            for (int i = 0; i < verts.Length; i++)
            {
                if (i >= topVertCount) { verts[i] = restVerts[i]; continue; }
                float disp = 0f;
                for (int j = 0; j < ripples.Count; j++)
                {
                    var rp = ripples[j];
                    float d = Vector2.Distance(restXZ[i], rp.xz);
                    float env = (1f - rp.age / cfg.rippleLife) * Mathf.Exp(-d * cfg.rippleDecay);
                    if (env < 0.001f) continue;
                    // 파면이 바깥으로 퍼지도록. 아직 파면이 안 닿은 곳은 건드리지 않는다.
                    float lead = d - cfg.rippleSpeed * rp.age;
                    if (lead > 0f) continue;
                    disp += rp.amp * Mathf.Sin(k * lead) * env;
                }

                var p = restVerts[i];
                p.y += disp;
                verts[i] = p;
                colors[i] = new Color(1f, 1f, 1f,
                    Mathf.Clamp01(restEdge[i] + disp / ampRef * cfg.rippleAlpha));
            }

            mesh.vertices = verts;
            mesh.colors = colors;
            mesh.RecalculateBounds();
        }

        public void AddRipple(Vector2 xz, float amplitude)
        {
            var cfg = Cfg;
            if (ripples.Count >= cfg.maxRipples) ripples.RemoveAt(0);
            ripples.Add(new Ripple { xz = xz, age = 0f, amp = amplitude });
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            var cfg = Cfg;
            var slime = other.GetComponentInParent<SlimeController>();
            if (slime == null) return;
            if (Time.time - lastSlimeSplash < cfg.slimeSplashCooldown) return;

            // 2.5D 라 X/Y 만 겹치고 Z 로는 웅덩이 밖일 수 있다. 트리거는 Z 를 안 보므로 여기서 거른다.
            // x/y 는 규약대로 rb.position, z 는 2D 강체에 없으니 트랜스폼에서 읽는다.
            var p = new Vector3(slime.Position.x, slime.Position.y, slime.transform.position.z);
            float zMin = wetBounds.min.z - cfg.puddleTriggerZMargin;
            float zMax = wetBounds.max.z + cfg.puddleTriggerZMargin;
            if (p.z < zMin || p.z > zMax) return;

            lastSlimeSplash = Time.time;
            float splashZ = Mathf.Clamp(p.z, wetBounds.min.z, wetBounds.max.z);
            // 수면을 뚫고 들어오는 속도라 세로 성분만 본다. 옆으로 지나가는 건 튀김이 아니다.
            float impact = Mathf.Abs(slime.Velocity.y);
            bool hard = impact >= landHardThreshold;
            AddRipple(new Vector2(p.x, splashZ), hard ? cfg.slimeAmplitudeHard : cfg.slimeAmplitude);
            var splashPoint = new Vector3(p.x, cfg.puddleWaterY, splashZ);
            SlimeJumping.Audio.SoundService.Play(hard ? landHardSound : landSoftSound, splashPoint);
        }

#if UNITY_EDITOR
        [ContextMenu("Bake Mesh Asset")]
        void BakeMeshAsset()
        {
            Generate();
            const string dir = "Assets/Settings/Waterfall";
            System.IO.Directory.CreateDirectory(dir);
            string path = dir + "/LedgePuddleMesh.asset";

            var baked = new Mesh { name = "LedgePuddleMesh", indexFormat = mesh.indexFormat };
            baked.vertices = restVerts;
            baked.uv = mesh.uv;
            baked.colors = colors;
            baked.triangles = mesh.triangles;
            baked.RecalculateNormals();
            baked.RecalculateBounds();

            var existing = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { UnityEditor.EditorUtility.CopySerialized(baked, existing); baked = existing; }
            else UnityEditor.AssetDatabase.CreateAsset(baked, path);

            bakedMesh = baked;
            bakedTopVertCount = topVertCount;
            bakedWetBounds = wetBounds;
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssets();
            Debug.Log("[LedgePuddle] baked " + baked.vertexCount + "v -> " + path);
        }
#endif

        void EnsureMesh()
        {
            var mf = GetComponent<MeshFilter>();
            if (mesh == null)
            {
                mesh = new Mesh { name = "LedgePuddle" };
                mesh.MarkDynamic();
            }
            if (mf.sharedMesh != mesh) mf.sharedMesh = mesh;
        }
    }
}
