using UnityEngine;

namespace SlimeJumping.TerrainShape.Editor
{
    public enum HeightmapProcMode
    {
        Perlin,
        RidgedPerlin,
        Cone,
        LinearGradient,
        Random,
        Flat,
    }

    public static class HeightmapProcGen
    {
        public struct Params
        {
            public float scale;
            public int octaves;
            public float persistence;
            public float lacunarity;
            public float angleDeg;
            public int seed;
            public float flatValue; // for Flat mode (0..1)
            public float minValue;  // 0..1, output's "blue/far" end (default 0)
            public float maxValue;  // 0..1, output's "red/near" end (default 1)
        }

        public static byte[] Generate(HeightmapProcMode mode, int W, int H, Params p)
        {
            var data = new byte[W * H];
            var rng = new System.Random(p.seed);

            int octaves = Mathf.Max(1, p.octaves);
            var octaveOffsets = new Vector2[octaves];
            for (int i = 0; i < octaves; i++)
            {
                octaveOffsets[i] = new Vector2(
                    (float)rng.NextDouble() * 1000f - 500f,
                    (float)rng.NextDouble() * 1000f - 500f);
            }

            float angRad = p.angleDeg * Mathf.Deg2Rad;
            Vector2 gradDir = new Vector2(Mathf.Cos(angRad), Mathf.Sin(angRad));
            float scale = Mathf.Max(0.001f, p.scale);
            byte flatByte = (byte)Mathf.RoundToInt(Mathf.Clamp01(p.flatValue) * 255f);
            float lo = Mathf.Clamp01(p.minValue);
            float hi = Mathf.Clamp01(p.maxValue);

            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    float u = W <= 1 ? 0.5f : x / (float)(W - 1);
                    float v = H <= 1 ? 0.5f : y / (float)(H - 1);
                    float h;
                    switch (mode)
                    {
                        case HeightmapProcMode.Perlin:
                            h = FractalPerlin(u, v, scale, octaves, p.persistence, p.lacunarity, octaveOffsets);
                            break;
                        case HeightmapProcMode.RidgedPerlin:
                            h = FractalRidged(u, v, scale, octaves, p.persistence, p.lacunarity, octaveOffsets);
                            break;
                        case HeightmapProcMode.Cone:
                            {
                                Vector2 d = new Vector2(u - 0.5f, v - 0.5f) * 2f; // -1..1
                                float r = d.magnitude;
                                h = Mathf.Clamp01(1f - r / scale);
                            }
                            break;
                        case HeightmapProcMode.LinearGradient:
                            {
                                Vector2 d = new Vector2(u - 0.5f, v - 0.5f);
                                float t = Vector2.Dot(d, gradDir) + 0.5f;
                                h = Mathf.Clamp01(t);
                            }
                            break;
                        case HeightmapProcMode.Random:
                            h = (float)rng.NextDouble();
                            break;
                        case HeightmapProcMode.Flat:
                        default:
                            data[y * W + x] = flatByte;
                            continue;
                    }
                    float remapped = Mathf.Lerp(lo, hi, Mathf.Clamp01(h));
                    data[y * W + x] = (byte)Mathf.RoundToInt(Mathf.Clamp01(remapped) * 255f);
                }
            }
            return data;
        }

        static float FractalPerlin(float u, float v, float scale, int octaves, float persistence, float lacunarity, Vector2[] offsets)
        {
            float total = 0f, amp = 1f, freq = 1f, max = 0f;
            for (int o = 0; o < octaves; o++)
            {
                float fx = (u / scale) * freq + offsets[o].x;
                float fy = (v / scale) * freq + offsets[o].y;
                total += Mathf.PerlinNoise(fx, fy) * amp;
                max += amp;
                amp *= persistence;
                freq *= lacunarity;
            }
            return total / Mathf.Max(0.0001f, max);
        }

        static float FractalRidged(float u, float v, float scale, int octaves, float persistence, float lacunarity, Vector2[] offsets)
        {
            float total = 0f, amp = 1f, freq = 1f, max = 0f;
            for (int o = 0; o < octaves; o++)
            {
                float fx = (u / scale) * freq + offsets[o].x;
                float fy = (v / scale) * freq + offsets[o].y;
                float n = 1f - Mathf.Abs(Mathf.PerlinNoise(fx, fy) * 2f - 1f);
                n *= n;
                total += n * amp;
                max += amp;
                amp *= persistence;
                freq *= lacunarity;
            }
            return total / Mathf.Max(0.0001f, max);
        }
    }
}
