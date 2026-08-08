using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using SlimeJumping.Platforms;
using UnityEditor;

namespace SlimeJumping.Editor
{
    /// <summary>
    /// 아스키아트 발판 빌드: .txt → world-space TMP(mspace 고정폭) + 점유 그리드 외곽선
    /// → PolygonCollider2D. 셀 크기는 TMP 실측(첫 글자 advance, 라인 baseline 간격)으로
    /// 재고 같은 값으로 콜라이더를 깔아 비주얼-콜라이더 정합을 맞춘다. 외곽선은 셀 경계
    /// 엣지를 "내부가 왼쪽" 방향으로 잇고(교차점은 최좌회전 우선 — 대각 접촉 분리),
    /// 공선 병합 + RDP 로 점을 줄인다. 구멍 루프는 even-odd 로 뚫린다.
    /// </summary>
    public static class AsciiArtPlatformBuilder
    {
        const string VisualName = "Visual";

        // 격자 방향: 0=+x, 1=위(r 감소), 2=-x, 3=아래. 좌회전 = (d+1)%4.
        static readonly int[] DirDc = { 1, 0, -1, 0 };
        static readonly int[] DirDr = { 0, -1, 0, 1 };

        public static bool Build(AsciiArtPlatform platform, TextAsset art, AsciiArtPlatformConfig cfg)
        {
            if (platform == null || art == null || cfg == null)
            {
                Debug.LogError("[AsciiArt] Build called with null platform/art/config.");
                return false;
            }
            if (art.text.Contains("</noparse>"))
            {
                Debug.LogError("[AsciiArt] Art contains literal '</noparse>' — TMP would resume tag parsing and the visual would diverge from the collider grid.", art);
                return false;
            }
            string[] lines = NormalizeLines(art.text);
            if (lines.Length == 0)
            {
                Debug.LogError("[AsciiArt] Art text is empty.", art);
                return false;
            }

            TextMeshPro tmp = EnsureVisual(platform.transform);
            ConfigureText(tmp, lines, cfg);
            // 피벗·컨테이너 변경 직후 1회차 레이아웃이 미정착일 수 있어 두 번 갱신.
            tmp.ForceMeshUpdate();
            tmp.ForceMeshUpdate();

            var ti = tmp.textInfo;
            if (ti.characterCount == 0)
            {
                Debug.LogError("[AsciiArt] TMP produced no characters.", platform);
                return false;
            }

            // mspace 는 글리프를 셀 안에 중앙 정렬해 origin~xAdvance 가 셀폭이 아니다 — 인접 origin 차로 잰다.
            float cellW = 0f;
            for (int i = 1; i < ti.characterCount; i++)
            {
                var prev = ti.characterInfo[i - 1];
                var cur = ti.characterInfo[i];
                if (cur.lineNumber != prev.lineNumber) continue;
                float adv = cur.origin - prev.origin;
                if (adv > 1e-4f) { cellW = adv; break; }
            }
            if (cellW <= 0f)
            {
                // 한 줄에 글자 2개가 없으면(1열 아트) 실측 불가 — 계산값 폴백 (3D TMP 1pt = 0.1u).
                cellW = cfg.charWidthEm * cfg.fontSize * 0.1f;
                Debug.LogWarning("[AsciiArt] Could not measure advance (single-column art?) — using computed cell width.", platform);
            }
            float cellH = ti.lineCount > 1
                ? ti.lineInfo[0].baseline - ti.lineInfo[1].baseline
                : cellW / Mathf.Max(0.01f, cfg.charWidthEm);
            // TopLeft 정렬 = 라인 시작/첫 줄 ascender 가 rect 좌상단에 고정.
            float left = tmp.rectTransform.rect.xMin;
            float top = tmp.rectTransform.rect.yMax;

            var mb = tmp.mesh.bounds;
            bool sane = mb.max.y <= top + cellH * 0.5f && mb.max.y > top - cellH
                     && mb.min.x >= left - cellW * 0.5f;
            if (!sane)
            {
                Debug.LogError($"[AsciiArt] Layout mismatch: mesh {mb.min}~{mb.max} vs grid origin ({left},{top}) — collider would misalign.", platform);
                return false;
            }

            int cols = 0;
            foreach (var l in lines) cols = Mathf.Max(cols, l.Length);

            // 경계를 글자별 잉크 높이 계단으로 — 셀/행 단위 높이는 낮은 글자 위·디센더 옆에서 슬라임이 떠 보인다.
            var inkTop = new float[lines.Length, Mathf.Max(1, cols)];
            var inkBottom = new float[lines.Length, Mathf.Max(1, cols)];
            MeasureCellInk(ti, lines.Length, cols, left, top, cellW, cellH, inkTop, inkBottom);

            var loops = BuildOutlines(lines, left, top, cellW, cellH, inkTop, inkBottom, cfg);
            if (loops == null)
            {
                Debug.LogError("[AsciiArt] Outline extraction failed — collider not applied.", platform);
                return false;
            }

            var poly = platform.GetComponent<PolygonCollider2D>();
            if (poly == null) poly = platform.gameObject.AddComponent<PolygonCollider2D>();
            // 비활성 상태에서 경로를 다 넣고 활성화 — 경로마다 형상 재생성하는 낭비 회피.
            poly.enabled = false;
            poly.pathCount = loops.Count;
            for (int i = 0; i < loops.Count; i++) poly.SetPath(i, loops[i]);
            poly.enabled = true;

            var so = new SerializedObject(platform);
            so.FindProperty("sourceArt").objectReferenceValue = art;
            so.FindProperty("visualText").objectReferenceValue = tmp;
            so.FindProperty("gridTopLeft").vector2Value = new Vector2(left, top);
            so.FindProperty("cellWidth").floatValue = cellW;
            so.FindProperty("cellHeight").floatValue = cellH;
            so.FindProperty("columns").intValue = cols;
            so.FindProperty("rows").intValue = lines.Length;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (platform.GetComponent<AsciiArtWobble>() == null)
                platform.gameObject.AddComponent<AsciiArtWobble>();

            int points = 0;
            foreach (var loop in loops) points += loop.Length;
            Debug.Log($"[AsciiArt] Built '{art.name}': {lines.Length} rows × {cols} cols, " +
                      $"{loops.Count} loops / {points} collider points, cell {cellW:F3}×{cellH:F3}.", platform);
            return true;
        }

        static string[] NormalizeLines(string text)
        {
            var lines = new List<string>(text.Replace("\r\n", "\n").Replace('\t', ' ').Split('\n'));
            while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0)
                lines.RemoveAt(lines.Count - 1);
            return lines.ToArray();
        }

        static TextMeshPro EnsureVisual(Transform root)
        {
            var found = root.Find(VisualName);
            GameObject go = found != null ? found.gameObject : new GameObject(VisualName);
            go.transform.SetParent(root, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var tmp = go.GetComponent<TextMeshPro>();
            if (tmp == null) tmp = go.AddComponent<TextMeshPro>();
            return tmp;
        }

        static void ConfigureText(TextMeshPro tmp, string[] lines, AsciiArtPlatformConfig cfg)
        {
            tmp.autoSizeTextContainer = false; // 3D TMP 기본 auto-size 가 rect 를 다시 움직인다.
            tmp.richText = true;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.fontSize = cfg.fontSize;
            tmp.color = cfg.textColor;
            tmp.lineSpacing = cfg.lineSpacing;
            var rt = tmp.rectTransform;
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = Vector2.one;
            string mspace = cfg.charWidthEm.ToString(CultureInfo.InvariantCulture);
            tmp.text = $"<mspace={mspace}em><noparse>{string.Join("\n", lines)}</noparse>";
        }

        // -------- 점유 그리드 → 외곽 루프 --------

        static void MeasureCellInk(TMP_TextInfo ti, int rows, int cols, float left, float top,
                                   float cellW, float cellH, float[,] inkTop, float[,] inkBottom)
        {
            // 기본값 = 셀 전체(잉크 못 잰 점유 셀 안전망).
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    inkTop[r, c] = top - r * cellH;
                    inkBottom[r, c] = top - (r + 1) * cellH;
                }

            for (int i = 0; i < ti.characterCount; i++)
            {
                var ci = ti.characterInfo[i];
                if (!ci.isVisible || ci.lineNumber >= rows) continue;
                int r = ci.lineNumber;
                // mspace 가 글리프를 셀 중앙에 놓으므로 쿼드 중심 x 로 열을 찾는 게 안전하다.
                float cx = (ci.bottomLeft.x + ci.topRight.x) * 0.5f;
                int c = Mathf.Clamp(Mathf.FloorToInt((cx - left) / cellW), 0, cols - 1);

                float latTop = top - r * cellH;
                float latBot = latTop - cellH;
                inkTop[r, c] = Mathf.Clamp(ci.topRight.y, latBot + 0.1f * cellH, latTop);
                inkBottom[r, c] = Mathf.Clamp(ci.bottomLeft.y, latBot, inkTop[r, c] - 0.05f * cellH);
            }
        }

        static List<Vector2[]> BuildOutlines(string[] lines, float left, float top,
                                             float cellW, float cellH,
                                             float[,] inkTop, float[,] inkBottom,
                                             AsciiArtPlatformConfig cfg)
        {
            int rows = lines.Length;
            int cols = 0;
            foreach (var l in lines) cols = Mathf.Max(cols, l.Length);
            if (cols > 0xFFFFF)
            {
                Debug.LogError($"[AsciiArt] {cols} columns exceeds the 20-bit vertex key range.");
                return null;
            }

            bool[,] occ = new bool[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < lines[r].Length; c++)
                    occ[r, c] = lines[r][c] != ' ';

            // startVertex → 미사용 outgoing 방향 목록.
            var edges = new Dictionary<long, List<int>>();
            void AddEdge(int c, int r, int dir)
            {
                long key = Key(c, r);
                if (!edges.TryGetValue(key, out var list)) edges[key] = list = new List<int>(2);
                list.Add(dir);
            }

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (!occ[r, c]) continue;
                    if (r + 1 >= rows || !occ[r + 1, c]) AddEdge(c, r + 1, 0);      // 아래변 →
                    if (c + 1 >= cols || !occ[r, c + 1]) AddEdge(c + 1, r + 1, 1);  // 오른변 ↑
                    if (r - 1 < 0 || !occ[r - 1, c]) AddEdge(c + 1, r, 2);          // 윗변 ←
                    if (c - 1 < 0 || !occ[r, c - 1]) AddEdge(c, r, 3);              // 왼변 ↓
                }
            }

            float eps = cfg.simplifyEpsilonCells * Mathf.Min(cellW, cellH);
            var result = new List<Vector2[]>();

            while (edges.Count > 0)
            {
                long startKey = 0;
                int startDir = -1;
                foreach (var kv in edges) { startKey = kv.Key; startDir = kv.Value[0]; break; }

                // 코너마다 in/out 방향을 남긴다 — 수평 경계 엣지의 y 를 그 행 잉크 높이로 조정해야 해서.
                var corners = new List<(int c, int r, int inDir, int outDir)>();
                int c0 = UnpackC(startKey), r0 = UnpackR(startKey);
                int c = c0, r = r0, dir = startDir;
                ConsumeEdge(edges, startKey, startDir);
                c += DirDc[dir]; r += DirDr[dir];

                while (c != c0 || r != r0)
                {
                    int next = PickNextDir(edges, c, r, dir);
                    if (next < 0)
                    {
                        Debug.LogError($"[AsciiArt] Outline chain broke at ({c},{r}).");
                        return null;
                    }
                    if (next != dir) corners.Add((c, r, dir, next)); // 공선 병합.
                    ConsumeEdge(edges, Key(c, r), next);
                    dir = next;
                    c += DirDc[dir]; r += DirDr[dir];
                }
                if (dir != startDir) corners.Insert(0, (c0, r0, dir, startDir));
                if (corners.Count < 3) continue;

                // 수평 경계만 글자별 잉크 계단으로 emit — 수직 엣지는 이웃 계단 끝점끼리 자동 연결이라 점 불요.
                var pts = new List<Vector2>(corners.Count * 2);
                void Push(Vector2 p)
                {
                    if (pts.Count == 0 || (pts[pts.Count - 1] - p).sqrMagnitude > 1e-10f) pts.Add(p);
                }
                int m = corners.Count;
                for (int k = 0; k < m; k++)
                {
                    var cur = corners[k];
                    var nxt = corners[(k + 1) % m];
                    if (cur.outDir == 0)
                    {
                        for (int cc = cur.c; cc < nxt.c; cc++)
                        {
                            float y = inkBottom[cur.r - 1, cc];
                            Push(new Vector2(left + cc * cellW, y));
                            Push(new Vector2(left + (cc + 1) * cellW, y));
                        }
                    }
                    else if (cur.outDir == 2)
                    {
                        for (int cc = cur.c - 1; cc >= nxt.c; cc--)
                        {
                            float y = inkTop[cur.r, cc];
                            Push(new Vector2(left + (cc + 1) * cellW, y));
                            Push(new Vector2(left + cc * cellW, y));
                        }
                    }
                }
                if (pts.Count > 1 && (pts[0] - pts[pts.Count - 1]).sqrMagnitude <= 1e-10f)
                    pts.RemoveAt(pts.Count - 1);
                if (pts.Count < 3) continue;

                var path = pts.ToArray();
                if (eps > 0f) path = SimplifyClosedPath(path, eps);
                result.Add(path);
            }
            return result;
        }

        // 최좌회전 우선(좌 > 직진 > 우 > 유턴) — 대각으로만 닿은 두 덩어리를 딴 루프로 가른다.
        static int PickNextDir(Dictionary<long, List<int>> edges, int c, int r, int incomingDir)
        {
            if (!edges.TryGetValue(Key(c, r), out var list)) return -1;
            for (int turn = 1; turn >= -2; turn--)
            {
                int want = (incomingDir + turn + 4) % 4;
                if (list.Contains(want)) return want;
            }
            return -1;
        }

        static void ConsumeEdge(Dictionary<long, List<int>> edges, long key, int dir)
        {
            var list = edges[key];
            list.Remove(dir);
            if (list.Count == 0) edges.Remove(key);
        }

        static long Key(int c, int r) => ((long)r << 20) | (uint)c;
        static int UnpackC(long key) => (int)(key & 0xFFFFF);
        static int UnpackR(long key) => (int)(key >> 20);

        // -------- RDP 폐곡선 — TerrainColliderOptimizer 와 같은 발상, asmdef(autoReferenced:false) 밖이라 소형 재구현 --------

        static Vector2[] SimplifyClosedPath(Vector2[] path, float epsilon)
        {
            int n = path.Length;
            if (n < 5) return path;

            // 고정 분할점 대신 0번에서 가장 먼 점을 앵커로 — 이음새 아티팩트 방지.
            int anchor = 0;
            float maxSq = -1f;
            for (int i = 1; i < n; i++)
            {
                float d = (path[i] - path[0]).sqrMagnitude;
                if (d > maxSq) { maxSq = d; anchor = i; }
            }

            var half1 = new Vector2[anchor + 1];
            for (int i = 0; i <= anchor; i++) half1[i] = path[i];
            var half2 = new Vector2[n - anchor + 1];
            for (int i = anchor; i < n; i++) half2[i - anchor] = path[i];
            half2[n - anchor] = path[0];

            var simp1 = Rdp(half1, epsilon);
            var simp2 = Rdp(half2, epsilon);

            var merged = new List<Vector2>(simp1.Count + simp2.Count);
            merged.AddRange(simp1);
            for (int i = 1; i < simp2.Count - 1; i++) merged.Add(simp2[i]);
            return merged.Count >= 3 ? merged.ToArray() : path;
        }

        static List<Vector2> Rdp(Vector2[] pts, float epsilon)
        {
            var keep = new bool[pts.Length];
            keep[0] = keep[pts.Length - 1] = true;
            var stack = new Stack<(int a, int b)>();
            stack.Push((0, pts.Length - 1));
            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                float maxDist = 0f;
                int split = -1;
                for (int i = a + 1; i < b; i++)
                {
                    float d = PerpendicularDistance(pts[i], pts[a], pts[b]);
                    if (d > maxDist) { maxDist = d; split = i; }
                }
                if (split >= 0 && maxDist > epsilon)
                {
                    keep[split] = true;
                    stack.Push((a, split));
                    stack.Push((split, b));
                }
            }
            var result = new List<Vector2>();
            for (int i = 0; i < pts.Length; i++)
                if (keep[i]) result.Add(pts[i]);
            return result;
        }

        static float PerpendicularDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len = ab.magnitude;
            if (len < 1e-6f) return (p - a).magnitude;
            return Mathf.Abs((p.x - a.x) * ab.y - (p.y - a.y) * ab.x) / len;
        }
    }
}
