using UnityEditor;
using UnityEngine;

namespace SlimeJumping.TerrainShape.Editor
{
    public class TerrainShape2DZPainterWindow : EditorWindow
    {
        TerrainShape2D _target;
        Texture2D _previewTex;
        bool _previewDirty = true;

        float _brushRadiusUV = 0.08f;
        float _brushValue = 0.75f;
        float _brushOpacity = 0.5f;
        int _brushMode = 0; // 0=Set, 1=Lift, 2=Lower, 3=Smooth
        bool _isPainting;

        public static void Open(TerrainShape2D src)
        {
            var w = GetWindow<TerrainShape2DZPainterWindow>("TerrainShape Z Painter");
            w._target = src;
            w._previewDirty = true;
            w.minSize = new Vector2(420, 600);
            w.Show();
        }

        public static void AllocateHeightmap(TerrainShape2D src, int w, int h)
        {
            Undo.RecordObject(src, "Allocate heightmap");
            src.heightmapW = w;
            src.heightmapH = h;
            src.heightmapData = new byte[w * h];
            for (int i = 0; i < src.heightmapData.Length; i++) src.heightmapData[i] = 128;
            EditorUtility.SetDirty(src);
            NotifyExternalDataChanged(src);
        }

        // Mark any open Z-Painter window's preview as stale so external loaders / generators
        // don't leave the canvas showing stale colors.
        public static void NotifyExternalDataChanged(TerrainShape2D src)
        {
            var windows = Resources.FindObjectsOfTypeAll<TerrainShape2DZPainterWindow>();
            foreach (var w in windows)
            {
                if (w._target == src)
                {
                    if (w._previewTex != null) DestroyImmediate(w._previewTex);
                    w._previewTex = null;
                    w._previewDirty = true;
                    w.Repaint();
                }
            }
        }

        void OnDestroy()
        {
            if (_previewTex != null) DestroyImmediate(_previewTex);
        }

        void OnSelectionChange() { Repaint(); }

        void OnGUI()
        {
            if (_target == null)
            {
                EditorGUILayout.HelpBox("No target. Open from a TerrainShape2D inspector.", MessageType.Warning);
                return;
            }
            if (!_target.HasHeightmap)
            {
                EditorGUILayout.HelpBox("Heightmap not allocated.", MessageType.Warning);
                if (GUILayout.Button("Allocate 128×128"))
                {
                    AllocateHeightmap(_target, 128, 128);
                    _previewDirty = true;
                    Repaint();
                }
                return;
            }

            EditorGUILayout.LabelField("Brush", EditorStyles.boldLabel);
            _brushMode = GUILayout.Toolbar(_brushMode, new[] { "Set", "Lift", "Lower", "Smooth" });
            _brushRadiusUV = EditorGUILayout.Slider("Radius (UV)", _brushRadiusUV, 0.01f, 0.4f);
            _brushOpacity = EditorGUILayout.Slider("Strength", _brushOpacity, 0.05f, 1f);
            _brushValue = EditorGUILayout.Slider(new GUIContent("Set value (0=blue/far, 1=red/near)", "0 paints the 'blue' (far / large Z) end of the range; 1 paints the 'red' (near / small Z) end."), _brushValue, 0f, 1f);
            float zVal = Mathf.Lerp(_target.heightmapZMax, _target.heightmapZMin, _brushValue);
            EditorGUILayout.LabelField($"  → Z at brush = {zVal:F3}");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Heightmap Z range", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            float zMin = EditorGUILayout.FloatField(new GUIContent("zMin (red, near)", "Red paint maps to this Z. Smaller Z = closer to camera."), _target.heightmapZMin);
            float zMax = EditorGUILayout.FloatField(new GUIContent("zMax (blue, far)", "Blue paint maps to this Z. Larger Z = further from camera."), _target.heightmapZMax);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_target, "Heightmap range");
                _target.heightmapZMin = zMin;
                _target.heightmapZMax = zMax;
                EditorUtility.SetDirty(_target);
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Canvas — paint Z height (cyan = front outline)", EditorStyles.boldLabel);

            EnsurePreview();
            Rect canvasRect = GUILayoutUtility.GetAspectRect(1f, GUILayout.MaxHeight(380));
            GUI.DrawTexture(canvasRect, _previewTex, ScaleMode.ScaleToFit);
            DrawOutlineOverlay(canvasRect);
            HandlePaintEvents(canvasRect);
            DrawBrushCursor(canvasRect);

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Fill mid (Z=0 of range)"))
            {
                Undo.RecordObject(_target, "Fill heightmap");
                for (int i = 0; i < _target.heightmapData.Length; i++) _target.heightmapData[i] = 128;
                _previewDirty = true;
                EditorUtility.SetDirty(_target);
            }
            if (GUILayout.Button("Reallocate 256×256"))
            {
                AllocateHeightmap(_target, 256, 256);
                if (_previewTex != null) DestroyImmediate(_previewTex);
                _previewTex = null;
                _previewDirty = true;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "Color ramp: red = small Z (toward camera, popping out), white = mid, blue = large Z (away from camera, sinking in). " +
                "Set: paints toward 'Set value'. Lift/Lower: nudge Z up/down. Smooth: blur.\n" +
                "When 'Use heightmap' is on at the component, NEW interior verts (Auto Subdivide / Interior Shift+click / Edges drag fallback) sample their Z from this canvas. " +
                "Already-placed verts keep their Z.",
                MessageType.None);

            if (Event.current.type == EventType.MouseMove && canvasRect.Contains(Event.current.mousePosition))
                Repaint();
        }

        void EnsurePreview()
        {
            if (_previewTex == null || _previewTex.width != _target.heightmapW || _previewTex.height != _target.heightmapH)
            {
                if (_previewTex != null) DestroyImmediate(_previewTex);
                _previewTex = new Texture2D(_target.heightmapW, _target.heightmapH, TextureFormat.RGBA32, false);
                _previewTex.hideFlags = HideFlags.HideAndDontSave;
                _previewTex.filterMode = FilterMode.Bilinear;
                _previewDirty = true;
            }
            if (_previewDirty) UpdatePreviewFromData();
        }

        void UpdatePreviewFromData()
        {
            int W = _target.heightmapW, H = _target.heightmapH;
            var pix = new Color[W * H];
            for (int i = 0; i < pix.Length; i++)
            {
                float v = _target.heightmapData[i] / 255f;
                pix[i] = HeightToColor(v);
            }
            _previewTex.SetPixels(pix);
            _previewTex.Apply(false);
            _previewDirty = false;
        }

        static Color HeightToColor(float v)
        {
            if (v < 0.5f)
            {
                float t = v * 2f;
                return new Color(t, t, 1f);
            }
            else
            {
                float t = (v - 0.5f) * 2f;
                return new Color(1f, 1f - t, 1f - t);
            }
        }

        void DrawOutlineOverlay(Rect canvas)
        {
            var L = _target.FrontLayer;
            if (L == null || L.loops == null || L.loops.Count == 0 || L.loops[0].points.Count < 3) return;
            Vector2 mn = L.loops[0].points[0].pos, mx = L.loops[0].points[0].pos;
            for (int i = 1; i < L.loops[0].points.Count; i++)
            {
                mn = Vector2.Min(mn, L.loops[0].points[i].pos);
                mx = Vector2.Max(mx, L.loops[0].points[i].pos);
            }
            Vector2 size = mx - mn;
            if (size.x < 1e-6f || size.y < 1e-6f) return;

            Vector2 ToCanvas(Vector2 lp)
            {
                float u = (lp.x - mn.x) / size.x;
                float v = (lp.y - mn.y) / size.y;
                return new Vector2(canvas.xMin + u * canvas.width, canvas.yMin + (1f - v) * canvas.height);
            }

            Handles.BeginGUI();
            Handles.color = Color.cyan;
            int n = L.loops[0].points.Count;
            var pts = new Vector3[n + 1];
            for (int i = 0; i < n; i++)
            {
                var p2 = ToCanvas(L.loops[0].points[i].pos);
                pts[i] = new Vector3(p2.x, p2.y, 0);
            }
            pts[n] = pts[0];
            Handles.DrawAAPolyLine(2f, pts);
            Handles.EndGUI();
        }

        void DrawBrushCursor(Rect canvas)
        {
            Vector2 m = Event.current.mousePosition;
            if (!canvas.Contains(m)) return;
            float radCanvas = _brushRadiusUV * canvas.width;
            Handles.BeginGUI();
            Handles.color = new Color(1f, 1f, 0.4f, 0.85f);
            Handles.DrawWireDisc(new Vector3(m.x, m.y, 0), Vector3.forward, radCanvas);
            Handles.EndGUI();
        }

        void HandlePaintEvents(Rect canvas)
        {
            var e = Event.current;
            if (e.button != 0) return;
            if (e.type == EventType.MouseDown && canvas.Contains(e.mousePosition))
            {
                _isPainting = true;
                Undo.RecordObject(_target, "Paint heightmap");
                PaintAtMouse(canvas, e.mousePosition);
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseDrag && _isPainting)
            {
                PaintAtMouse(canvas, e.mousePosition);
                EditorUtility.SetDirty(_target);
                TerrainShape2DEditor.NotifyHeightmapChanged(_target);
                SceneView.RepaintAll();
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseUp && _isPainting)
            {
                _isPainting = false;
                EditorUtility.SetDirty(_target);
                TerrainShape2DEditor.NotifyHeightmapChanged(_target);
                SceneView.RepaintAll();
                Repaint();
            }
        }

        void PaintAtMouse(Rect canvas, Vector2 mouse)
        {
            float u = Mathf.Clamp01((mouse.x - canvas.xMin) / canvas.width);
            float v = Mathf.Clamp01(1f - (mouse.y - canvas.yMin) / canvas.height);
            int W = _target.heightmapW, H = _target.heightmapH;
            int cx = Mathf.RoundToInt(u * (W - 1));
            int cy = Mathf.RoundToInt(v * (H - 1));
            int radPx = Mathf.Max(1, Mathf.RoundToInt(_brushRadiusUV * Mathf.Max(W, H)));

            for (int dy = -radPx; dy <= radPx; dy++)
            {
                int py = cy + dy;
                if (py < 0 || py >= H) continue;
                for (int dx = -radPx; dx <= radPx; dx++)
                {
                    int px = cx + dx;
                    if (px < 0 || px >= W) continue;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist > radPx) continue;
                    float falloff = 1f - dist / radPx;
                    float w = falloff * _brushOpacity;
                    int idx = py * W + px;
                    float current = _target.heightmapData[idx] / 255f;
                    float newV = current;
                    switch (_brushMode)
                    {
                        case 0:
                            newV = Mathf.Lerp(current, _brushValue, w);
                            break;
                        case 1:
                            newV = Mathf.Min(1f, current + w * 0.05f);
                            break;
                        case 2:
                            newV = Mathf.Max(0f, current - w * 0.05f);
                            break;
                        case 3:
                            float sum = 0f; int cnt = 0;
                            for (int oy = -1; oy <= 1; oy++)
                            {
                                int qy = py + oy;
                                if (qy < 0 || qy >= H) continue;
                                for (int ox = -1; ox <= 1; ox++)
                                {
                                    int qx = px + ox;
                                    if (qx < 0 || qx >= W) continue;
                                    sum += _target.heightmapData[qy * W + qx] / 255f;
                                    cnt++;
                                }
                            }
                            newV = Mathf.Lerp(current, sum / cnt, w);
                            break;
                    }
                    _target.heightmapData[idx] = (byte)Mathf.RoundToInt(Mathf.Clamp01(newV) * 255f);
                }
            }
            _previewDirty = true;
            EnsurePreview();
        }
    }
}
