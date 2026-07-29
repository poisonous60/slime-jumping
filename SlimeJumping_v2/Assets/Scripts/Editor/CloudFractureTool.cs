using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 구름 메시를 N개 청크로 분할(k-means). 새 지오메트리 없이 원본 삼각형을 나눠 각 삼각형이
// 정확히 1번씩 쓰이므로 조립하면 원본과 동일(seam 원천 없음).
// 청크는 자기 centroid 로 recenter + GameObject offset → 흩어짐이 자기 중심 기준.
public static class CloudFractureTool
{
    public const int DefaultPieces = 6;

    // cloudPrefab 의 첫 메시를 N조각으로 분할해 pieces prefab 생성(실패 시 null).
    public static GameObject Generate(GameObject cloudPrefab, int pieces, string outDir)
    {
        if (cloudPrefab == null) { Debug.LogError("[CloudFracture] cloudPrefab null"); return null; }
        var mf = cloudPrefab.GetComponentInChildren<MeshFilter>();
        var mr = cloudPrefab.GetComponentInChildren<MeshRenderer>();
        if (mf == null || mf.sharedMesh == null) { Debug.LogError("[CloudFracture] no MeshFilter/mesh"); return null; }
        var srcMesh = mf.sharedMesh;
        var mat = mr != null ? mr.sharedMaterial : null;

        EnsureReadable(srcMesh);

        var verts = srcMesh.vertices;
        var normals = srcMesh.normals;
        var uvs = srcMesh.uv;
        var tris = srcMesh.triangles;
        int triCount = tris.Length / 3;
        if (triCount < pieces) pieces = Mathf.Max(1, triCount);

        var triCentroid = new Vector3[triCount];
        for (int t = 0; t < triCount; t++)
        {
            Vector3 a = verts[tris[t * 3]], b = verts[tris[t * 3 + 1]], c = verts[tris[t * 3 + 2]];
            triCentroid[t] = (a + b + c) / 3f;
        }

        // 시드 = farthest-point sampling (골고루 퍼지게).
        var seeds = new Vector3[pieces];
        seeds[0] = triCentroid[0];
        for (int s = 1; s < pieces; s++)
        {
            float best = -1f; int bestT = 0;
            for (int t = 0; t < triCount; t++)
            {
                float d = float.MaxValue;
                for (int k = 0; k < s; k++) d = Mathf.Min(d, (triCentroid[t] - seeds[k]).sqrMagnitude);
                if (d > best) { best = d; bestT = t; }
            }
            seeds[s] = triCentroid[bestT];
        }

        // k-means (몇 회 반복).
        var assign = new int[triCount];
        for (int iter = 0; iter < 8; iter++)
        {
            for (int t = 0; t < triCount; t++)
            {
                float best = float.MaxValue; int bestK = 0;
                for (int k = 0; k < pieces; k++)
                {
                    float d = (triCentroid[t] - seeds[k]).sqrMagnitude;
                    if (d < best) { best = d; bestK = k; }
                }
                assign[t] = bestK;
            }
            var sum = new Vector3[pieces]; var cnt = new int[pieces];
            for (int t = 0; t < triCount; t++) { sum[assign[t]] += triCentroid[t]; cnt[assign[t]]++; }
            for (int k = 0; k < pieces; k++) if (cnt[k] > 0) seeds[k] = sum[k] / cnt[k];
        }

        EnsureFolder(outDir);
        string name = cloudPrefab.name;
        string meshesPath = $"{outDir}/{name}_Pieces.asset";
        AssetDatabase.DeleteAsset(meshesPath);

        var root = new GameObject($"{name}_Pieces");
        Mesh containerMain = null;
        int made = 0;
        bool hasN = normals != null && normals.Length == verts.Length;
        bool hasUV = uvs != null && uvs.Length == verts.Length;

        for (int k = 0; k < pieces; k++)
        {
            // 이 클러스터의 삼각형 수집 + 사용된 정점만 remap.
            var remap = new Dictionary<int, int>();
            var pv = new List<Vector3>(); var pn = new List<Vector3>(); var pu = new List<Vector2>();
            var pt = new List<int>();
            for (int t = 0; t < triCount; t++)
            {
                if (assign[t] != k) continue;
                for (int e = 0; e < 3; e++)
                {
                    int vi = tris[t * 3 + e];
                    if (!remap.TryGetValue(vi, out int ni))
                    {
                        ni = pv.Count; remap[vi] = ni;
                        pv.Add(verts[vi]);
                        if (hasN) pn.Add(normals[vi]);
                        if (hasUV) pu.Add(uvs[vi]);
                    }
                    pt.Add(ni);
                }
            }
            if (pv.Count == 0) continue;

            Vector3 c = Vector3.zero;
            for (int i = 0; i < pv.Count; i++) c += pv[i];
            c /= pv.Count;
            for (int i = 0; i < pv.Count; i++) pv[i] -= c;

            var pm = new Mesh { name = $"{name}_Piece_{k:D2}" };
            pm.SetVertices(pv);
            if (hasN) pm.SetNormals(pn); else pm.RecalculateNormals();
            if (hasUV) pm.SetUVs(0, pu);
            pm.SetTriangles(pt, 0);
            pm.RecalculateBounds();

            if (containerMain == null) { containerMain = pm; AssetDatabase.CreateAsset(pm, meshesPath); }
            else AssetDatabase.AddObjectToAsset(pm, containerMain);

            var go = new GameObject($"Piece_{k:D2}");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = c;                    // 조립 = 원본 정확히 복원.
            var gmf = go.AddComponent<MeshFilter>(); gmf.sharedMesh = pm;
            var gmr = go.AddComponent<MeshRenderer>();
            gmr.sharedMaterial = mat;
            gmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            made++;
        }

        if (made == 0) { Object.DestroyImmediate(root); Debug.LogError("[CloudFracture] 0 pieces made"); return null; }

        AssetDatabase.SaveAssets();
        string prefabPath = $"{outDir}/{name}_Pieces.prefab";
        AssetDatabase.DeleteAsset(prefabPath);
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        Debug.Log($"[CloudFracture] {made} pieces -> {prefabPath}", prefab);
        return prefab;
    }

    static void EnsureReadable(Mesh m)
    {
        string p = AssetDatabase.GetAssetPath(m);
        var imp = AssetImporter.GetAtPath(p) as ModelImporter;
        if (imp != null && !imp.isReadable) { imp.isReadable = true; imp.SaveAndReimport(); }
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
        string leaf = System.IO.Path.GetFileName(folder);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
