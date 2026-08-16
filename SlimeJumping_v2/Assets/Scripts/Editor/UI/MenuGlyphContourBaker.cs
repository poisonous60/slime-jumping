using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using SlimeJumping.UI;
using SlimeJumping.UI.Framework;

namespace SlimeJumping.EditorTools.UI
{
    /// Bakes vector ink contours for the menu title glyphs: GPU-readback of the TMP
    /// SDF atlas (isReadable=false safe), marching squares at the edge iso value with
    /// sub-pixel interpolation, hole loops via ink-on-left winding, RDP simplify.
    /// Output = Assets/Resources/MenuGlyphContours.asset in font units (atlas px ==
    /// font units at glyph scale 1, measured). Title text/font come from the
    /// MainMenuPanel prefab so a title change only needs a re-bake.
    public static class MenuGlyphContourBaker
    {
        const float EdgeIso = 0.4f;        // SDF ink edge — 0.5=레귤러, 낮출수록 팽창(타이틀은 Bold 룩). 셰이더 엣지와 자동 동기.
        const float RdpEpsilonPx = 0.6f;
        const float MinLoopAreaPx = 3f;
        const int WindowPadPx = 3;         // sample margin so loops never touch the window border.
        const string AssetPath = "Assets/Resources/MenuGlyphContours.asset";

        [MenuItem("Tools/SlimeJumping/UI/Bake Menu Glyph Contours")]
        public static void BakeMenu()
        {
            var asset = Bake();
            if (asset != null) Selection.activeObject = asset;
        }

        public static MenuGlyphContours Bake()
        {
            if (!TryGetTitle(out string text, out TMP_FontAsset font)) return null;

            var atlas = font.atlasTexture;
            if (atlas == null)
            {
                Debug.LogError("[GlyphBake] Font has no atlas texture.");
                return null;
            }
            if (font.atlasTextures != null && font.atlasTextures.Length > 1)
                Debug.LogWarning("[GlyphBake] Multi-atlas font — only atlas 0 is sampled.");

            float[] field = ReadAtlas(atlas, out int aw, out int ah);
            var entries = new List<MenuGlyphContours.GlyphEntry>();
            var seen = new HashSet<uint>();

            foreach (char c in text)
            {
                uint u = c;
                if (char.IsWhiteSpace(c) || !seen.Add(u)) continue;
                if (!font.characterLookupTable.TryGetValue(u, out var ch))
                {
                    Debug.LogWarning($"[GlyphBake] '{c}' not in font — skipped.");
                    continue;
                }
                var paths = BakeGlyph(field, aw, ah, ch.glyph);
                if (paths.Count == 0)
                {
                    Debug.LogWarning($"[GlyphBake] '{c}' produced no contour — skipped.");
                    continue;
                }
                entries.Add(new MenuGlyphContours.GlyphEntry
                {
                    unicode = u,
                    paths = paths.ToArray(),
                });
                int pts = 0;
                foreach (var p in paths) pts += p.points.Length;
                Debug.Log($"[GlyphBake] '{c}': {paths.Count} loops / {pts} pts.");
            }

            var asset = AssetDatabase.LoadAssetAtPath<MenuGlyphContours>(AssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<MenuGlyphContours>();
                AssetDatabase.CreateAsset(asset, AssetPath);
            }
            asset.fontName = font.name;
            asset.edgeThreshold = EdgeIso;
            asset.glyphs = entries.ToArray();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log($"[GlyphBake] Saved {entries.Count} glyphs → {AssetPath}");
            return asset;
        }

        static bool TryGetTitle(out string text, out TMP_FontAsset font)
        {
            text = null;
            font = null;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MainMenuPanelSetup.PrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[GlyphBake] MainMenuPanel prefab missing: {MainMenuPanelSetup.PrefabPath}");
                return false;
            }
            foreach (var slot in prefab.GetComponentsInChildren<UISlot>(true))
            {
                if (slot.slotType != UISlotType.Title) continue;
                var tmp = slot.GetComponent<TMP_Text>();
                if (tmp == null) continue;
                text = tmp.text;
                font = tmp.font;
                return true;
            }
            Debug.LogError("[GlyphBake] No Title slot with TMP_Text in MainMenuPanel prefab.");
            return false;
        }

        // Blit→ReadPixels 는 isReadable=false 우회; Alpha8 은 .a 또는 .r 로 샘플되므로 max.
        static float[] ReadAtlas(Texture2D atlas, out int w, out int h)
        {
            w = atlas.width;
            h = atlas.height;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Graphics.Blit(atlas, rt);
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            var f = new float[w * h];
            for (int i = 0; i < px.Length; i++)
                f[i] = Mathf.Max(px[i].r, px[i].a) / 255f;
            return f;
        }

        static List<MenuGlyphContours.ContourPath> BakeGlyph(float[] field, int aw, int ah,
            UnityEngine.TextCore.Glyph glyph)
        {
            var r = glyph.glyphRect;
            int x0 = Mathf.Max(0, r.x - WindowPadPx);
            int y0 = Mathf.Max(0, r.y - WindowPadPx);
            int x1 = Mathf.Min(aw - 1, r.x + r.width + WindowPadPx);
            int y1 = Mathf.Min(ah - 1, r.y + r.height + WindowPadPx);
            int w = x1 - x0 + 1, h = y1 - y0 + 1;

            var f = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    f[y * w + x] = field[(y0 + y) * aw + (x0 + x)];

            var loops = MarchingSquares.Extract(f, w, h, EdgeIso);

            // 윈도px→폰트단위: glyphRect 좌하단 원점 y-up, 잉크 min = (bearingX, bearingY−height).
            var m = glyph.metrics;
            float ox = m.horizontalBearingX - (r.x - x0);
            float oy = (m.horizontalBearingY - m.height) - (r.y - y0);

            var result = new List<MenuGlyphContours.ContourPath>();
            foreach (var loop in loops)
            {
                if (Mathf.Abs(SignedArea(loop)) < MinLoopAreaPx) continue;
                OrientInkLeft(loop, f, w, h);
                var pts = SimplifyClosed(loop, RdpEpsilonPx);
                if (pts.Count < 3) continue;
                var arr = new Vector2[pts.Count];
                for (int i = 0; i < pts.Count; i++)
                    arr[i] = new Vector2(pts[i].x + ox, pts[i].y + oy);
                result.Add(new MenuGlyphContours.ContourPath { points = arr });
            }
            return result;
        }

        static float SignedArea(List<Vector2> p)
        {
            float a = 0f;
            for (int i = 0; i < p.Count; i++)
            {
                var q = p[(i + 1) % p.Count];
                a += p[i].x * q.y - q.x * p[i].y;
            }
            return a * 0.5f;
        }

        // winding 계약(진행방향 좌측=잉크) — 엣지 중점 좌측 필드를 다표본 투표로 판정.
        static void OrientInkLeft(List<Vector2> loop, float[] f, int w, int h)
        {
            int votes = 0, samples = Mathf.Min(9, loop.Count);
            for (int s = 0; s < samples; s++)
            {
                int i = s * loop.Count / samples;
                Vector2 a = loop[i], b = loop[(i + 1) % loop.Count];
                Vector2 t = (b - a).normalized;
                Vector2 left = new Vector2(-t.y, t.x);
                Vector2 probe = (a + b) * 0.5f + left * 0.75f;
                if (Bilinear(f, w, h, probe.x, probe.y) >= EdgeIso) votes++;
                else votes--;
            }
            if (votes < 0) loop.Reverse();
        }

        static float Bilinear(float[] f, int w, int h, float x, float y)
        {
            int ix = Mathf.Clamp(Mathf.FloorToInt(x), 0, w - 2);
            int iy = Mathf.Clamp(Mathf.FloorToInt(y), 0, h - 2);
            float tx = Mathf.Clamp01(x - ix), ty = Mathf.Clamp01(y - iy);
            float a = Mathf.Lerp(f[iy * w + ix], f[iy * w + ix + 1], tx);
            float b = Mathf.Lerp(f[(iy + 1) * w + ix], f[(iy + 1) * w + ix + 1], tx);
            return Mathf.Lerp(a, b, ty);
        }

        // Closed-path RDP, split at the point farthest from 0 (seam-artifact free).
        static List<Vector2> SimplifyClosed(List<Vector2> path, float eps)
        {
            int n = path.Count;
            if (n < 5) return path;
            int anchor = 0;
            float maxSq = -1f;
            for (int i = 1; i < n; i++)
            {
                float d = (path[i] - path[0]).sqrMagnitude;
                if (d > maxSq) { maxSq = d; anchor = i; }
            }
            var half1 = path.GetRange(0, anchor + 1);
            var half2 = path.GetRange(anchor, n - anchor);
            half2.Add(path[0]);
            var s1 = Rdp(half1, eps);
            var s2 = Rdp(half2, eps);
            var merged = new List<Vector2>(s1.Count + s2.Count);
            merged.AddRange(s1);
            for (int i = 1; i < s2.Count - 1; i++) merged.Add(s2[i]);
            return merged;
        }

        static List<Vector2> Rdp(List<Vector2> pts, float eps)
        {
            var keep = new bool[pts.Count];
            keep[0] = keep[pts.Count - 1] = true;
            var stack = new Stack<(int a, int b)>();
            stack.Push((0, pts.Count - 1));
            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                float maxDist = 0f;
                int split = -1;
                Vector2 ab = pts[b] - pts[a];
                float len = ab.magnitude;
                for (int i = a + 1; i < b; i++)
                {
                    float d = len < 1e-6f
                        ? (pts[i] - pts[a]).magnitude
                        : Mathf.Abs((pts[i].x - pts[a].x) * ab.y - (pts[i].y - pts[a].y) * ab.x) / len;
                    if (d > maxDist) { maxDist = d; split = i; }
                }
                if (split >= 0 && maxDist > eps)
                {
                    keep[split] = true;
                    stack.Push((a, split));
                    stack.Push((split, b));
                }
            }
            var result = new List<Vector2>();
            for (int i = 0; i < pts.Count; i++)
                if (keep[i]) result.Add(pts[i]);
            return result;
        }
    }

    /// Iso-contour extraction on a scalar grid. Cells sit between pixel centers;
    /// crossings are linearly interpolated, ambiguous saddles resolved by the cell
    /// center average. Returns closed loops (window border must stay below iso).
    static class MarchingSquares
    {
        // Edge id per cell: 0=bottom, 1=right, 2=top, 3=left, globalized below.
        static readonly int[][] CaseSegs =
        {
            /* 0*/ new int[0],        /* 1*/ new[]{3,0}, /* 2*/ new[]{0,1}, /* 3*/ new[]{3,1},
            /* 4*/ new[]{1,2},        /* 5*/ null,       /* 6*/ new[]{0,2}, /* 7*/ new[]{3,2},
            /* 8*/ new[]{2,3},        /* 9*/ new[]{2,0}, /*10*/ null,       /*11*/ new[]{2,1},
            /*12*/ new[]{1,3},        /*13*/ new[]{1,0}, /*14*/ new[]{0,3}, /*15*/ new int[0],
        };

        public static List<List<Vector2>> Extract(float[] f, int w, int h, float iso)
        {
            // Global edge id: horizontal edge at (x,y) = 2*(y*w+x), vertical = that+1.
            var pointByEdge = new Dictionary<long, Vector2>();
            var linksByEdge = new Dictionary<long, List<long>>();

            void Link(long a, long b)
            {
                if (!linksByEdge.TryGetValue(a, out var la)) linksByEdge[a] = la = new List<long>(2);
                la.Add(b);
                if (!linksByEdge.TryGetValue(b, out var lb)) linksByEdge[b] = lb = new List<long>(2);
                lb.Add(a);
            }

            for (int y = 0; y < h - 1; y++)
            {
                for (int x = 0; x < w - 1; x++)
                {
                    float bl = f[y * w + x], br = f[y * w + x + 1];
                    float tl = f[(y + 1) * w + x], tr = f[(y + 1) * w + x + 1];
                    int c = (bl >= iso ? 1 : 0) | (br >= iso ? 2 : 0)
                          | (tr >= iso ? 4 : 0) | (tl >= iso ? 8 : 0);
                    if (c == 0 || c == 15) continue;

                    long EdgeId(int e)
                    {
                        switch (e)
                        {
                            case 0: return 2L * (y * w + x);
                            case 1: return 2L * (y * w + x + 1) + 1;
                            case 2: return 2L * ((y + 1) * w + x);
                            default: return 2L * (y * w + x) + 1;
                        }
                    }
                    Vector2 EdgePoint(int e)
                    {
                        switch (e)
                        {
                            case 0: return new Vector2(x + Cross(bl, br, iso), y);
                            case 1: return new Vector2(x + 1, y + Cross(br, tr, iso));
                            case 2: return new Vector2(x + Cross(tl, tr, iso), y + 1);
                            default: return new Vector2(x, y + Cross(bl, tl, iso));
                        }
                    }
                    void Seg(int e0, int e1)
                    {
                        long i0 = EdgeId(e0), i1 = EdgeId(e1);
                        pointByEdge[i0] = EdgePoint(e0);
                        pointByEdge[i1] = EdgePoint(e1);
                        Link(i0, i1);
                    }

                    var segs = CaseSegs[c];
                    if (segs == null)
                    {
                        // Saddle: center average decides which corner pair connects.
                        bool centerInk = (bl + br + tl + tr) * 0.25f >= iso;
                        if (c == 5) { if (centerInk) { Seg(3, 2); Seg(1, 0); } else { Seg(3, 0); Seg(1, 2); } }
                        else { if (centerInk) { Seg(0, 3); Seg(2, 1); } else { Seg(0, 1); Seg(2, 3); } }
                    }
                    else
                    {
                        for (int i = 0; i < segs.Length; i += 2) Seg(segs[i], segs[i + 1]);
                    }
                }
            }

            var loops = new List<List<Vector2>>();
            var visited = new HashSet<long>();
            foreach (var start in linksByEdge.Keys)
            {
                if (visited.Contains(start)) continue;
                var loop = new List<Vector2>();
                long prev = -1, cur = start;
                bool closed = false;
                while (true)
                {
                    visited.Add(cur);
                    loop.Add(pointByEdge[cur]);
                    var links = linksByEdge[cur];
                    long next = -1;
                    foreach (var cand in links)
                        if (cand != prev && (cand == start || !visited.Contains(cand))) { next = cand; break; }
                    if (next == -1) break;
                    if (next == start) { closed = true; break; }
                    prev = cur;
                    cur = next;
                }
                if (closed && loop.Count >= 3) loops.Add(loop);
            }
            return loops;
        }

        static float Cross(float a, float b, float iso)
            => Mathf.Abs(b - a) < 1e-6f ? 0.5f : Mathf.Clamp01((iso - a) / (b - a));
    }
}
