using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using SlimeJumping.Platforms.ThinIceV2;

namespace SlimeJumping.Platforms.ThinIceV2.EditorOnly
{
    /// One-time generator for the shared ThinIce shard set. Mesh origin = base-polygon centroid.
    public static class ThinIceSharedShardSetGenerator
    {
        const string AssetPath = "Assets/GeneratedShards/SharedThinIceShardSet.asset";

        const int   DefaultCount     = 24;
        const float DefaultBaseRad   = 0.10f;  // average shard radius (XY)
        const float DefaultRadJitter = 0.45f;  // ± multiplier on per-vertex radius
        const float DefaultThickness = 0.08f;  // depth (Z) extrusion

        [MenuItem("SlimeJumping/Thin Ice v2/Generate Shared Shard Set")]
        public static void GenerateMenu()
        {
            var set = Generate(seed: 1, count: DefaultCount, baseRadius: DefaultBaseRad,
                               radiusJitter: DefaultRadJitter, thickness: DefaultThickness);
            if (set != null)
            {
                Selection.activeObject = set;
                EditorGUIUtility.PingObject(set);
                Debug.Log($"[SharedThinIceShardSet] Generated {set.shards.Count} shards at {AssetPath}.", set);
            }
        }

        public static ThinIceShardSet Generate(int seed, int count, float baseRadius, float radiusJitter, float thickness)
        {
            EnsureFolder("Assets/GeneratedShards");

            // Always overwrite at the canonical path so all platforms keep referencing the same asset.
            var existing = AssetDatabase.LoadAssetAtPath<ThinIceShardSet>(AssetPath);
            if (existing != null) AssetDatabase.DeleteAsset(AssetPath);

            var set = ScriptableObject.CreateInstance<ThinIceShardSet>();
            set.shards = new List<ShardData>(count);
            set.sourceShapeGuid = ""; // shared, not bound to a single shape
            set.siteSeed = seed;
            set.density  = baseRadius;

            AssetDatabase.CreateAsset(set, AssetPath);

            var rng = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                var mesh = BuildIrregularChunkMesh(rng, baseRadius, radiusJitter, thickness);
                if (mesh == null) continue;
                mesh.name = $"SharedShard_{i:D2}";
                AssetDatabase.AddObjectToAsset(mesh, set);
                set.shards.Add(new ShardData
                {
                    mesh = mesh,
                    localCentroid = Vector2.zero, // already centered
                    localBounds = mesh.bounds,
                });
            }

            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(AssetPath);
            return set;
        }


        // Irregular convex polygon extruded along Z into a closed prism.
        static Mesh BuildIrregularChunkMesh(System.Random rng, float baseRadius, float radiusJitter, float thickness)
        {
            int sides = 5 + rng.Next(0, 4); // 5..8 sides
            var ringXY = new Vector2[sides];

            // Jitter is only a fraction of a step: keeps the polygon convex, no winding pathologies.
            float angleStep = Mathf.PI * 2f / sides;
            for (int i = 0; i < sides; i++)
            {
                float angleJitter = ((float)rng.NextDouble() - 0.5f) * angleStep * 0.4f;
                float angle = angleStep * i + angleJitter;
                float r = baseRadius * (1f + ((float)rng.NextDouble() - 0.5f) * 2f * radiusJitter);
                r = Mathf.Max(baseRadius * 0.4f, r);
                ringXY[i] = new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
            }

            // Recenter to centroid so spawn position == shard origin.
            Vector2 c = Vector2.zero;
            for (int i = 0; i < sides; i++) c += ringXY[i];
            c /= sides;
            for (int i = 0; i < sides; i++) ringXY[i] -= c;

            float halfT = thickness * 0.5f;

            int frontStart  = 0;
            int backStart   = sides;
            int frontCenter = sides * 2;
            int backCenter  = sides * 2 + 1;

            var verts = new Vector3[sides * 2 + 2];
            for (int i = 0; i < sides; i++)
            {
                verts[frontStart + i] = new Vector3(ringXY[i].x, ringXY[i].y, -halfT);
                verts[backStart  + i] = new Vector3(ringXY[i].x, ringXY[i].y, +halfT);
            }
            verts[frontCenter] = new Vector3(0f, 0f, -halfT);
            verts[backCenter]  = new Vector3(0f, 0f, +halfT);

            var tris = new List<int>(sides * 12);
            // Front fan (CCW when viewed from -Z toward +Z)
            for (int i = 0; i < sides; i++)
            {
                int a = frontStart + i;
                int b = frontStart + (i + 1) % sides;
                tris.Add(frontCenter); tris.Add(a); tris.Add(b);
            }
            // Back fan (reverse winding so it faces +Z)
            for (int i = 0; i < sides; i++)
            {
                int a = backStart + i;
                int b = backStart + (i + 1) % sides;
                tris.Add(backCenter); tris.Add(b); tris.Add(a);
            }
            // Side strip (one quad per edge)
            for (int i = 0; i < sides; i++)
            {
                int f0 = frontStart + i;
                int f1 = frontStart + (i + 1) % sides;
                int b0 = backStart  + i;
                int b1 = backStart  + (i + 1) % sides;
                tris.Add(f0); tris.Add(f1); tris.Add(b1);
                tris.Add(f0); tris.Add(b1); tris.Add(b0);
            }

            // Front ring maps into a circular [0..1] disc for the ThinIceV2 shader.
            // Back/side clone the front UV — debris is small, rim seams don't read.
            var uvs = new Vector2[verts.Length];
            for (int i = 0; i < sides; i++)
            {
                Vector2 unit = ringXY[i].normalized * 0.5f + new Vector2(0.5f, 0.5f);
                uvs[frontStart + i] = unit;
                uvs[backStart  + i] = unit;
            }
            uvs[frontCenter] = new Vector2(0.5f, 0.5f);
            uvs[backCenter]  = new Vector2(0.5f, 0.5f);

            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
            string leaf   = System.IO.Path.GetFileName(folder);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
