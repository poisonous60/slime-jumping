using System.Collections.Generic;
using UnityEngine;
using DynamicMeshCutter;

namespace SlimeJumping.Effects
{
    /// <summary>
    /// Runtime "mesh-generator only" wrapper around DynamicMeshCutter.
    ///
    /// A single slash = a single plane cut = exactly 2 chunks. We drive DMC's
    /// low-level <see cref="MeshCutting"/> directly (the same path
    /// <see cref="CutterBehaviour.Cut"/> takes when UseAsync=false) so creation
    /// is fully synchronous and doesn't depend on a scene CutterBehaviour's
    /// Update pump. The proxy MeshTarget is configured with NO 3D physics
    /// (CreateRigidbody/CreateMeshCollider = false) — the slime world is 2D, so
    /// the caller attaches Rigidbody2D + Collider2D to the returned chunks.
    ///
    /// DMC is used ONLY to birth the cut geometry (unique silhouette every hit);
    /// no DMC component lives in the scene and none survives the call.
    /// </summary>
    public static class SlimeChunkCutter
    {
        /// <summary>
        /// Cut <paramref name="proxyMesh"/> by a world-space plane through
        /// <paramref name="worldPos"/> with normal <paramref name="cutNormalWS"/>.
        /// The plane passes through the proxy's center, so the cut always yields
        /// 2 pieces. Returns the chunk ROOT GameObjects (each carries a child
        /// MeshFilter+MeshRenderer; no physics). Returns an empty list if the
        /// cut degenerated (all verts on one side).
        /// </summary>
        /// <param name="planeOffset">Shifts the cut plane along its normal (world units)
        /// off the proxy center, producing an ASYMMETRIC split — one big piece, one small.
        /// 0 = through center (50/50). Positive moves the plane toward +normal, shrinking
        /// the +normal side.</param>
        public static List<GameObject> Cut(
            Mesh proxyMesh, Material chunkMaterial,
            Vector3 worldPos, Quaternion worldRot, Vector3 worldScale,
            Vector3 cutNormalWS, float planeOffset = 0f)
        {
            var result = new List<GameObject>(2);
            if (proxyMesh == null) return result;

            // --- Throwaway proxy carrying the slime blob mesh ---
            var proxy = new GameObject("SlimeCutProxy");
            proxy.transform.SetPositionAndRotation(worldPos, worldRot);
            proxy.transform.localScale = worldScale;

            // HideAndDontSave: never shows in the hierarchy, never serialized into the
            // scene (so a stray proxy can't be saved/accumulate). Renderer disabled: DMC
            // reads the mesh's CPU data, not the rendered image, so we avoid a 1-frame
            // flash of the full proxy at the slime's position.
            proxy.hideFlags = HideFlags.HideAndDontSave;
            var mf = proxy.AddComponent<MeshFilter>();
            mf.sharedMesh = proxyMesh;
            var mr = proxy.AddComponent<MeshRenderer>();
            mr.sharedMaterial = chunkMaterial;
            mr.enabled = false;

            var mt = proxy.AddComponent<MeshTarget>();
            mt.GameobjectRoot = proxy;
            mt.OverrideFaceMaterial = chunkMaterial;
            mt.CreateRigidbody = new bool[] { false, false };
            mt.CreateMeshCollider = new bool[] { false, false };
            mt.DefaultBehaviour = new DynamicMeshCutter.Behaviour[]
                { DynamicMeshCutter.Behaviour.Stone, DynamicMeshCutter.Behaviour.Stone };

            // --- World -> local plane (mirrors CutterBehaviour.Cut; MeshFilter
            //     target so RequireLocal is false, no extra scaling matrix) ---
            Vector3 cutNormalN = cutNormalWS.normalized;
            // Offset the plane off-center along its normal → asymmetric pieces.
            Vector3 planePointWS = worldPos + cutNormalN * planeOffset;
            Matrix4x4 w2l = proxy.transform.worldToLocalMatrix;
            Vector3 localP = w2l.MultiplyPoint3x4(planePointWS);
            // Normal transforms by the inverse-transpose of world->local.
            Vector3 localN = w2l.inverse.transpose.MultiplyVector(cutNormalN).normalized;

            try
            {
                var plane = new VirtualPlane(localP, localN, planePointWS, cutNormalN);
                // NOTE: DMC reads the mesh's CPU data (vertices/normals/uv/indices). The
                // proxy mesh MUST be Read/Write enabled in its import settings, otherwise
                // this throws at runtime ("Not allowed to access ... isReadable is false").
                // Caught below so a misconfigured mesh degrades to "no split" instead of
                // leaving the slime stuck under override.
                var info = new Info(mt, plane, null, null, null);

                VirtualMesh[] meshes = new MeshCutting().Cut(ref info);
                if (meshes != null)
                {
                    info.CreatedMeshes = meshes;
                    MeshCreationData cd = MeshCreation.CreateObjects(info, chunkMaterial, 0);
                    if (cd != null)
                    {
                        for (int i = 0; i < cd.CreatedObjects.Length; i++)
                        {
                            var o = cd.CreatedObjects[i];
                            if (o != null)
                            {
                                // Strip the MeshTarget DMC stamps on each chunk — we don't
                                // re-cut, and leaving it registers a stray shepherd entry.
                                var stray = o.GetComponentInChildren<MeshTarget>();
                                if (stray != null) Object.Destroy(stray);
                                result.Add(o);
                            }
                        }
                    }
                }
                // meshes == null → degenerate cut (plane missed); return empty, caller no-splits.
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[SlimeChunkCutter] Cut failed (is the proxy mesh Read/Write enabled?): " + e.Message);
                for (int i = 0; i < result.Count; i++)
                    if (result[i] != null) Object.Destroy(result[i]);
                result.Clear();
            }

            DestroyProxy(proxy);
            return result;
        }

        // Object.Destroy is deferred and does NOT run in edit mode (no game tick), which
        // would leak a SlimeCutProxy into the scene every edit-mode call. Use
        // DestroyImmediate when not playing so nothing accumulates.
        private static void DestroyProxy(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Object.Destroy(go);
            else Object.DestroyImmediate(go);
        }
    }
}
