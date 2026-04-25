using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SlimeJumping.TerrainShape.Editor
{
    [CustomEditor(typeof(TerrainShape2D))]
    public class TerrainShape2DEditor : UnityEditor.Editor
    {
        enum ActiveTool { Outline, Interior, Edges }

        ActiveTool _tool = ActiveTool.Outline;
        int _activeLayer = 0;
        bool _cacheDirty = true;
        TerrainShape2DMeshBuilder.BuildResult _cache;
        bool _edgesDragging = false;
        int _edgesDragFromId = 0;

        struct EdgeDragSnap
        {
            public bool valid;
            public Vector3 posLocal;     // XYZ in shape local space
            public bool toExistingVertex;
            public int vertexId;
            public bool toOutlineEdge;
            public int outlineEdgeAfterIdx;
            public bool toInteriorPoint;  // create new interior at posLocal
        }

        const float HANDLE_SIZE = 0.08f;
        const float TANGENT_HANDLE_SIZE = 0.06f;
        const float CLICK_RADIUS_PX = 12f;

        static System.Action<TerrainShape2D> _heightmapChanged;

        public static void NotifyHeightmapChanged(TerrainShape2D src)
        {
            _heightmapChanged?.Invoke(src);
        }

        void OnHeightmapChanged(TerrainShape2D src)
        {
            if (src == (TerrainShape2D)target) MarkDirty();
        }

        void OnEnable()
        {
            _cacheDirty = true;
            Undo.undoRedoPerformed += OnUndoRedo;
            _heightmapChanged += OnHeightmapChanged;
            EnsureInitialized();
        }

        void EnsureInitialized()
        {
            var src = target as TerrainShape2D;
            if (src == null) return;
            if (src.layers != null && src.layers.Count > 0) return;
            Undo.RecordObject(src, "Initialize TerrainShape");
            if (src.layers == null) src.layers = new System.Collections.Generic.List<OutlineLayer>();
            var front = new OutlineLayer { name = "Front", z = 0f };
            front.outline.Add(new OutlinePoint { id = src.AllocateId(), pos = new Vector2(-1f, -1f) });
            front.outline.Add(new OutlinePoint { id = src.AllocateId(), pos = new Vector2(1f, -1f) });
            front.outline.Add(new OutlinePoint { id = src.AllocateId(), pos = new Vector2(1f, 1f) });
            front.outline.Add(new OutlinePoint { id = src.AllocateId(), pos = new Vector2(-1f, 1f) });
            src.layers.Add(front);
            src.AddLayerCopy(0, 0.5f);
            if (src.layers.Count > 1) src.layers[1].name = "Back";
            EditorUtility.SetDirty(src);
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            _heightmapChanged -= OnHeightmapChanged;
        }

        void OnUndoRedo()
        {
            _cacheDirty = true;
            Rebuild();
            SceneView.RepaintAll();
        }

        // ──────────────── Inspector ────────────────

        public override void OnInspectorGUI()
        {
            var src = (TerrainShape2D)target;
            serializedObject.Update();
            if (src.layers == null || src.layers.Count == 0) EnsureInitialized();
            ClampActiveLayer(src);

            DrawLayerSection(src);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Active Tool", EditorStyles.boldLabel);
            var newTool = (ActiveTool)GUILayout.Toolbar((int)_tool, new[] { "Outline", "Interior", "Edges" });
            if (newTool != _tool)
            {
                _tool = newTool;
                _edgesDragFromId = 0;
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Shape", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            int newSubs = EditorGUILayout.IntSlider("Outline samples / curve", src.subdivisionsPerSegment, 2, 32);
            bool newDouble = EditorGUILayout.Toggle(new GUIContent("Double-sided front", "Render the front face from both sides — useful when forced edges or Z bumps make some triangles back-face the camera and look black."), src.doubleSidedFront);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(src, "TerrainShape settings");
                src.subdivisionsPerSegment = newSubs;
                src.doubleSidedFront = newDouble;
                EditorUtility.SetDirty(src);
                MarkDirty();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Low-Poly (Decimate-style)", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            bool newFlat = EditorGUILayout.Toggle(new GUIContent("Flat shading", "Faceted look — every triangle gets its own face normal (no smoothing across edges)."), src.flatShaded);
            float newPlanar = EditorGUILayout.Slider(new GUIContent("Planar simplify (Z dev)", "Like Blender Decimate-Planar: drops AUTO-generated interior points (yellow dots) whose |Z − frontLayerZ| is below this threshold. Manually-placed points (orange dots) are never affected."), src.planarSimplifyThreshold, 0f, 0.5f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(src, "Low-poly settings");
                src.flatShaded = newFlat;
                src.planarSimplifyThreshold = newPlanar;
                EditorUtility.SetDirty(src);
                MarkDirty();
                SceneView.RepaintAll();
            }
            int autoCount = 0, dissolveCount = 0, manualCount = 0;
            float frontZForCount = src.FrontLayer != null ? src.FrontLayer.z : 0f;
            foreach (var ip in src.interior)
            {
                if (ip.autoGenerated)
                {
                    autoCount++;
                    if (src.planarSimplifyThreshold > 0f && Mathf.Abs(ip.pos.z - frontZForCount) < src.planarSimplifyThreshold)
                        dissolveCount++;
                }
                else manualCount++;
            }
            EditorGUILayout.LabelField($"   → preview: dissolves {dissolveCount} of {autoCount} auto, keeps all {manualCount} manual", EditorStyles.miniLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Z Heightmap (paint to set vertex Z)", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            bool useH = EditorGUILayout.Toggle(new GUIContent("Use heightmap", "When on, NEW interior vertices (Auto Subdivide / Interior Shift+click / Edges drag fallback) sample their Z from the painted heightmap."), src.useHeightmap);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(src, "Use heightmap");
                src.useHeightmap = useH;
                EditorUtility.SetDirty(src);
                MarkDirty();
            }
            EditorGUI.BeginChangeCheck();
            bool showH = EditorGUILayout.Toggle(new GUIContent("Show heightmap in scene", "Overlay the painted heightmap as a colored grid on top of the front face in the scene view."), src.showHeightmapInScene);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(src, "Show heightmap toggle");
                src.showHeightmapInScene = showH;
                EditorUtility.SetDirty(src);
                SceneView.RepaintAll();
            }
            EditorGUILayout.LabelField($"   {(src.HasHeightmap ? $"{src.heightmapW}×{src.heightmapH}" : "(not allocated)")}    Z range: [{src.heightmapZMin:F2} … {src.heightmapZMax:F2}]", EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(src.HasHeightmap ? "Reallocate 128×128" : "Allocate 128×128"))
            {
                TerrainShape2DZPainterWindow.AllocateHeightmap(src, 128, 128);
                MarkDirty();
            }
            using (new EditorGUI.DisabledScope(!src.HasHeightmap))
            {
                if (GUILayout.Button("Open Z Painter"))
                    TerrainShape2DZPainterWindow.Open(src);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Auto Subdivide", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            float newSpacing = EditorGUILayout.Slider("Grid spacing", src.autoSpacing, 0.05f, 2f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(src, "Auto spacing");
                src.autoSpacing = newSpacing;
                EditorUtility.SetDirty(src);
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Auto Subdivide → Interior")) AutoSubdivide(src);
            if (GUILayout.Button("Clear Interior"))
            {
                Undo.RecordObject(src, "Clear interior");
                src.interior.Clear();
                src.forcedEdges.Clear();
                EditorUtility.SetDirty(src);
                MarkDirty();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Layers: {src.layers.Count}    Outline pts/layer: {src.OutlineCount}    Interior: {src.interior.Count}    Forced edges: {src.forcedEdges.Count}");
            int triCount = _cache.mesh != null ? _cache.mesh.triangles.Length / 3 : 0;
            int vertCount = _cache.mesh != null ? _cache.mesh.vertexCount : 0;
            EditorGUILayout.LabelField($"Mesh: {vertCount} verts, {triCount} tris");

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_cache.mesh == null || _cache.mesh.vertexCount < 3))
            {
                if (GUILayout.Button("Save Mesh As Asset")) SaveMeshAsset(src);
            }

            EditorGUILayout.HelpBox(
                "Layers: select active layer; outline tool edits its points. + Add Layer duplicates the active one.\n" +
                "Outline: drag yellow handles. Shift+click an edge to insert (propagates to all layers). Ctrl+click a point to delete.\n" +
                "Toggle 'smooth' on a row → cyan tangent handles appear at that point. Drag the cyan endpoints to shape the curve.\n" +
                "Interior (Z bumps on FRONT face only): Shift+click inside polygon to add. PositionHandle drags X/Y/Z. Ctrl+click to delete.\n" +
                "Edges (front face only): drag from vertex A — yellow line follows mouse, snaps to nearest vertex (cyan ring). Release on snap to add forced edge.\n   Hold Shift while dragging — orange line snaps to nearest edge or interior point (green ring). Release to insert a new vertex there and link it.\n   Ctrl+click an existing forced edge to remove.",
                MessageType.Info);

            EditorGUILayout.Space();
            DrawActiveLayerOutlineList(src);

            serializedObject.ApplyModifiedProperties();
        }

        void DrawLayerSection(TerrainShape2D src)
        {
            EditorGUILayout.LabelField("Layers (front → back)", EditorStyles.boldLabel);
            int deleteIdx = -1;
            for (int i = 0; i < src.layers.Count; i++)
            {
                var L = src.layers[i];
                EditorGUILayout.BeginHorizontal();
                bool isActive = (i == _activeLayer);
                bool tg = EditorGUILayout.Toggle(isActive, GUILayout.Width(18));
                if (tg && !isActive) _activeLayer = i;
                EditorGUILayout.LabelField($"#{i}", GUILayout.Width(22));

                EditorGUI.BeginChangeCheck();
                string nm = EditorGUILayout.TextField(L.name, GUILayout.MinWidth(50));
                float nz = EditorGUILayout.FloatField(L.z, GUILayout.Width(50));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(src, "Edit layer");
                    L.name = nm;
                    L.z = nz;
                    EditorUtility.SetDirty(src);
                    MarkDirty();
                }

                using (new EditorGUI.DisabledScope(src.layers.Count <= 1))
                {
                    if (GUILayout.Button("X", GUILayout.Width(22)))
                        deleteIdx = i;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (deleteIdx >= 0)
            {
                Undo.RecordObject(src, "Remove layer");
                src.RemoveLayerAt(deleteIdx);
                if (_activeLayer >= src.layers.Count) _activeLayer = src.layers.Count - 1;
                EditorUtility.SetDirty(src);
                MarkDirty();
                return;
            }

            if (GUILayout.Button("+ Add Layer (copy active)"))
            {
                Undo.RecordObject(src, "Add layer");
                src.AddLayerCopy(_activeLayer, 0.5f);
                _activeLayer = src.layers.Count - 1;
                EditorUtility.SetDirty(src);
                MarkDirty();
            }
        }

        void DrawActiveLayerOutlineList(TerrainShape2D src)
        {
            if (src.layers.Count == 0) return;
            var L = src.layers[_activeLayer];
            EditorGUILayout.LabelField($"Outline points (layer #{_activeLayer} {L.name})", EditorStyles.boldLabel);
            for (int i = 0; i < L.outline.Count; i++)
            {
                var p = L.outline[i];
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"#{i} id={p.id}", GUILayout.Width(80));
                EditorGUI.BeginChangeCheck();
                bool s = EditorGUILayout.ToggleLeft("smooth", p.smooth, GUILayout.Width(70));
                Vector2 pos = EditorGUILayout.Vector2Field(GUIContent.none, p.pos);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(src, "Edit outline point");
                    if (s != p.smooth)
                    {
                        p.smooth = s;
                        if (s) InitTangentsFor(L, i);
                        else { p.tangentIn = Vector2.zero; p.tangentOut = Vector2.zero; }
                    }
                    p.pos = pos;
                    EditorUtility.SetDirty(src);
                    MarkDirty();
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        void InitTangentsFor(OutlineLayer L, int i)
        {
            int n = L.outline.Count;
            var p = L.outline[i];
            var pPrev = L.outline[(i - 1 + n) % n].pos;
            var pNext = L.outline[(i + 1) % n].pos;
            Vector2 chord = pNext - pPrev;
            float mag = Mathf.Max(0.1f, Vector2.Distance(p.pos, pNext) * 0.33f);
            Vector2 dir = chord.sqrMagnitude > 1e-6f ? chord.normalized : Vector2.right;
            p.tangentOut = dir * mag;
            p.tangentIn = -p.tangentOut;
        }

        // ──────────────── Scene GUI ────────────────

        void OnSceneGUI()
        {
            var src = (TerrainShape2D)target;
            if (src.layers == null || src.layers.Count == 0) return;
            ClampActiveLayer(src);
            EnsureCache(src);

            int passiveID = GUIUtility.GetControlID(FocusType.Passive);
            if (Event.current.type == EventType.Layout)
                HandleUtility.AddDefaultControl(passiveID);

            DrawHeightmapSceneOverlay(src);
            DrawAllLayerOutlines(src);
            DrawSideWallPreview(src);
            DrawFrontFaceWireframe(src);
            DrawInteriorDissolvePreview(src);

            switch (_tool)
            {
                case ActiveTool.Outline: HandleOutlineTool(src); break;
                case ActiveTool.Interior: HandleInteriorTool(src); break;
                case ActiveTool.Edges: HandleEdgesTool(src); break;
            }

            DrawForcedEdges(src);
        }

        // ──────────────── Drawing ────────────────

        void DrawAllLayerOutlines(TerrainShape2D src)
        {
            if (_cache.sampledOutlinesPerLayer == null) return;
            var t = src.transform;
            for (int li = 0; li < src.layers.Count && li < _cache.sampledOutlinesPerLayer.Count; li++)
            {
                var pts = _cache.sampledOutlinesPerLayer[li];
                if (pts == null) continue;
                bool active = (li == _activeLayer);
                Handles.color = active ? new Color(1f, 1f, 1f, 0.95f) : new Color(0.6f, 0.7f, 1f, 0.5f);
                float z = src.layers[li].z;
                for (int i = 0; i < pts.Count; i++)
                {
                    Vector3 a = t.TransformPoint(new Vector3(pts[i].x, pts[i].y, z));
                    Vector3 b = t.TransformPoint(new Vector3(pts[(i + 1) % pts.Count].x, pts[(i + 1) % pts.Count].y, z));
                    Handles.DrawLine(a, b, active ? 2f : 1f);
                }
            }
        }

        void DrawHeightmapSceneOverlay(TerrainShape2D src)
        {
            if (!src.showHeightmapInScene) return;
            if (!src.HasHeightmap) return;
            if (src.FrontLayer == null || src.FrontLayer.outline.Count < 3) return;
            if (_cache.sampledOutlinesPerLayer == null || _cache.sampledOutlinesPerLayer.Count == 0) return;
            var outlinePts = _cache.sampledOutlinesPerLayer[0];
            if (outlinePts == null || outlinePts.Count < 3) return;

            var t = src.transform;
            float frontZ = src.FrontLayer.z;
            float overlayZ = frontZ - 0.005f; // slight push toward camera (smaller Z) to avoid z-fighting

            Vector2 mn = src.FrontLayer.outline[0].pos, mx = src.FrontLayer.outline[0].pos;
            for (int i = 1; i < src.FrontLayer.outline.Count; i++)
            {
                mn = Vector2.Min(mn, src.FrontLayer.outline[i].pos);
                mx = Vector2.Max(mx, src.FrontLayer.outline[i].pos);
            }
            Vector2 size = mx - mn;
            if (size.x < 1e-6f || size.y < 1e-6f) return;

            const int RES = 28;
            float zRange = Mathf.Max(1e-6f, src.heightmapZMax - src.heightmapZMin);
            for (int j = 0; j < RES; j++)
            {
                float v0 = (float)j / RES, v1 = (float)(j + 1) / RES;
                for (int i = 0; i < RES; i++)
                {
                    float u0 = (float)i / RES, u1 = (float)(i + 1) / RES;
                    Vector2 c00 = new Vector2(mn.x + u0 * size.x, mn.y + v0 * size.y);
                    Vector2 c10 = new Vector2(mn.x + u1 * size.x, mn.y + v0 * size.y);
                    Vector2 c01 = new Vector2(mn.x + u0 * size.x, mn.y + v1 * size.y);
                    Vector2 c11 = new Vector2(mn.x + u1 * size.x, mn.y + v1 * size.y);
                    Vector2 center = (c00 + c11) * 0.5f;
                    if (!TerrainShape2DMeshBuilder.IsPointInside(outlinePts, center)) continue;

                    float zS = src.SampleHeightmapZ(center);
                    // Match painter ramp: smaller Z (closer to camera) → red. Larger Z → blue.
                    float norm = Mathf.Clamp01(1f - (zS - src.heightmapZMin) / zRange);
                    Color c = HeightToColor(norm);
                    c.a = 0.55f;
                    Handles.color = c;

                    Vector3 w00 = t.TransformPoint(new Vector3(c00.x, c00.y, overlayZ));
                    Vector3 w10 = t.TransformPoint(new Vector3(c10.x, c10.y, overlayZ));
                    Vector3 w11 = t.TransformPoint(new Vector3(c11.x, c11.y, overlayZ));
                    Vector3 w01 = t.TransformPoint(new Vector3(c01.x, c01.y, overlayZ));
                    Handles.DrawAAConvexPolygon(w00, w10, w11, w01);
                }
            }
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

        void DrawInteriorDissolvePreview(TerrainShape2D src)
        {
            if (src.interior == null || src.interior.Count == 0) return;
            var t = src.transform;
            float frontZ = src.FrontLayer != null ? src.FrontLayer.z : 0f;
            float thr = src.planarSimplifyThreshold;
            for (int i = 0; i < src.interior.Count; i++)
            {
                var ip = src.interior[i];
                Vector3 effLocal = src.GetEffectiveInteriorPos(ip);
                Vector3 wp = t.TransformPoint(effLocal);
                float dotR = HandleUtility.GetHandleSize(wp) * 0.05f;
                bool willDissolve = ip.autoGenerated && thr > 0f && Mathf.Abs(ip.pos.z - frontZ) < thr;
                if (willDissolve)
                {
                    Handles.color = new Color(0.55f, 0.55f, 0.55f, 0.45f);
                    Handles.SphereHandleCap(0, wp, t.rotation, dotR * 0.7f, EventType.Repaint);
                    Handles.color = new Color(1f, 0.25f, 0.25f, 0.9f);
                    float r = HandleUtility.GetHandleSize(wp) * 0.05f;
                    Vector3 right = t.right * r;
                    Vector3 up = t.up * r;
                    Handles.DrawLine(wp - right - up, wp + right + up, 1.5f);
                    Handles.DrawLine(wp - right + up, wp + right - up, 1.5f);
                }
                else
                {
                    Handles.color = ip.autoGenerated ? new Color(1f, 0.95f, 0.3f, 0.95f) : new Color(1f, 0.55f, 0.1f, 0.95f);
                    Handles.SphereHandleCap(0, wp, t.rotation, dotR, EventType.Repaint);
                }
            }
        }

        const float CREASE_ANGLE_DEG = 1f;

        class EdgeRec { public Vector3 a, b; public int triA = -1, triB = -1; public int count; }

        void DrawFrontFaceWireframe(TerrainShape2D src)
        {
            if (_cache.frontFaceVerts == null || _cache.frontFaceTris == null) return;
            var t = src.transform;

            var simpVisible = ExtractVisibleEdges(_cache.frontFaceVerts, _cache.frontFaceTris, CREASE_ANGLE_DEG);
            var simpKeys = new HashSet<long>();
            foreach (var e in simpVisible) simpKeys.Add(e.key);

            HashSet<long> origKeys = null;
            List<VisibleEdge> origVisible = null;
            if (_cache.originalFrontFaceVerts != null && _cache.originalFrontFaceTris != null)
            {
                origVisible = ExtractVisibleEdges(_cache.originalFrontFaceVerts, _cache.originalFrontFaceTris, CREASE_ANGLE_DEG);
                origKeys = new HashSet<long>();
                foreach (var e in origVisible) origKeys.Add(e.key);
            }

            if (origVisible != null)
            {
                Handles.color = new Color(1f, 0.25f, 0.25f, 0.85f);
                foreach (var e in origVisible)
                {
                    if (simpKeys.Contains(e.key)) continue;
                    Handles.DrawLine(t.TransformPoint(e.a), t.TransformPoint(e.b), 1.5f);
                }
            }

            foreach (var e in simpVisible)
            {
                bool added = origKeys != null && !origKeys.Contains(e.key);
                if (added)
                {
                    Handles.color = new Color(0.3f, 1f, 0.4f, 0.95f);
                    Handles.DrawLine(t.TransformPoint(e.a), t.TransformPoint(e.b), 2.5f);
                }
                else
                {
                    Handles.color = new Color(0.4f, 0.7f, 1f, 0.6f);
                    Handles.DrawLine(t.TransformPoint(e.a), t.TransformPoint(e.b), 1.5f);
                }
            }
        }

        struct VisibleEdge { public Vector3 a, b; public long key; }

        static List<VisibleEdge> ExtractVisibleEdges(Vector3[] verts, int[] tris, float creaseAngleDeg)
        {
            var result = new List<VisibleEdge>();
            int triCount = tris.Length / 3;
            if (triCount == 0) return result;

            var faceNormals = new Vector3[triCount];
            for (int i = 0; i < triCount; i++)
            {
                int t = i * 3;
                Vector3 a = verts[tris[t]], b = verts[tris[t + 1]], c = verts[tris[t + 2]];
                faceNormals[i] = Vector3.Cross(b - a, c - a).normalized;
            }

            var edges = new Dictionary<long, EdgeRec>();
            for (int i = 0; i < triCount; i++)
            {
                int t = i * 3;
                Add(verts[tris[t]], verts[tris[t + 1]], i, edges);
                Add(verts[tris[t + 1]], verts[tris[t + 2]], i, edges);
                Add(verts[tris[t + 2]], verts[tris[t]], i, edges);
            }

            float cosT = Mathf.Cos(creaseAngleDeg * Mathf.Deg2Rad);
            foreach (var kv in edges)
            {
                var r = kv.Value;
                bool show;
                if (r.count <= 1) show = true;
                else if (r.count == 2)
                {
                    float d = Vector3.Dot(faceNormals[r.triA], faceNormals[r.triB]);
                    show = d < cosT;
                }
                else show = true;
                if (show) result.Add(new VisibleEdge { a = r.a, b = r.b, key = kv.Key });
            }
            return result;
        }

        static void Add(Vector3 va, Vector3 vb, int triIdx, Dictionary<long, EdgeRec> edges)
        {
            long key = EdgeKey(va, vb);
            if (edges.TryGetValue(key, out var r))
            {
                r.triB = triIdx;
                r.count++;
            }
            else
            {
                edges[key] = new EdgeRec { a = va, b = vb, triA = triIdx, count = 1 };
            }
        }

        static long PointHash(Vector3 v)
        {
            long x = (long)Mathf.RoundToInt(v.x * 1e5f);
            long y = (long)Mathf.RoundToInt(v.y * 1e5f);
            long z = (long)Mathf.RoundToInt(v.z * 1e5f);
            unchecked
            {
                return (x * 73856093L) ^ (y * 19349663L) ^ (z * 83492791L);
            }
        }

        static long EdgeKey(Vector3 a, Vector3 b)
        {
            long ka = PointHash(a);
            long kb = PointHash(b);
            unchecked
            {
                return ka < kb ? (ka * 1000003L) ^ kb : (kb * 1000003L) ^ ka;
            }
        }

        void DrawSideWallPreview(TerrainShape2D src)
        {
            if (src.layers.Count < 2) return;
            var t = src.transform;
            Handles.color = new Color(0.4f, 0.7f, 1f, 0.35f);
            for (int li = 0; li < src.layers.Count - 1; li++)
            {
                var A = src.layers[li];
                var B = src.layers[li + 1];
                int n = Mathf.Min(A.outline.Count, B.outline.Count);
                for (int k = 0; k < n; k++)
                {
                    Vector3 wa = t.TransformPoint(new Vector3(A.outline[k].pos.x, A.outline[k].pos.y, A.z));
                    Vector3 wb = t.TransformPoint(new Vector3(B.outline[k].pos.x, B.outline[k].pos.y, B.z));
                    Handles.DrawLine(wa, wb);
                }
            }
        }

        void DrawForcedEdges(TerrainShape2D src)
        {
            var t = src.transform;
            Handles.color = new Color(1f, 0.6f, 0.1f, 1f);
            for (int i = 0; i < src.forcedEdges.Count; i++)
            {
                if (!src.TryGetPositionByIdOnFront(src.forcedEdges[i].idA, out var a)) continue;
                if (!src.TryGetPositionByIdOnFront(src.forcedEdges[i].idB, out var b)) continue;
                Handles.DrawLine(t.TransformPoint(a), t.TransformPoint(b), 3f);
            }
        }

        // ──────────────── Outline tool ────────────────

        void HandleOutlineTool(TerrainShape2D src)
        {
            var t = src.transform;
            var e = Event.current;
            var layer = src.layers[_activeLayer];

            for (int i = 0; i < layer.outline.Count; i++)
            {
                var p = layer.outline[i];
                Vector3 wp = t.TransformPoint(new Vector3(p.pos.x, p.pos.y, layer.z));
                float size = HandleUtility.GetHandleSize(wp) * HANDLE_SIZE;
                Handles.color = p.smooth ? new Color(0.4f, 1f, 0.4f) : Color.yellow;
                EditorGUI.BeginChangeCheck();
                Vector3 nw = Handles.FreeMoveHandle(wp, size, Vector3.zero, Handles.SphereHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(src, "Move outline point");
                    Vector3 lp = t.InverseTransformPoint(nw);
                    p.pos = new Vector2(lp.x, lp.y);
                    EditorUtility.SetDirty(src);
                    MarkDirty();
                }

                if (p.smooth) DrawTangentHandles(src, layer, i, t);
            }

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (e.control && TryFindClickedOutlineIndex(src, layer, e.mousePosition, out int delIdx))
                {
                    if (layer.outline.Count > 3)
                    {
                        Undo.RecordObject(src, "Delete outline point");
                        src.RemoveOutlinePointInAllLayers(delIdx);
                        EditorUtility.SetDirty(src);
                        MarkDirty();
                        e.Use();
                    }
                }
                else if (e.shift && TryProjectMouseToActiveLayerXY(src, e.mousePosition, out Vector2 lxy))
                {
                    if (TryFindClosestOutlineEdge(layer, lxy, out int edgeI, out Vector2 onEdge))
                    {
                        Undo.RecordObject(src, "Insert outline point");
                        src.InsertOutlinePointInAllLayers(edgeI, _activeLayer, onEdge);
                        EditorUtility.SetDirty(src);
                        MarkDirty();
                        e.Use();
                    }
                }
            }
        }

        void DrawTangentHandles(TerrainShape2D src, OutlineLayer layer, int i, Transform t)
        {
            var p = layer.outline[i];
            Vector3 wpAnchor = t.TransformPoint(new Vector3(p.pos.x, p.pos.y, layer.z));

            Vector3 wpOut = t.TransformPoint(new Vector3(p.pos.x + p.tangentOut.x, p.pos.y + p.tangentOut.y, layer.z));
            Vector3 wpIn = t.TransformPoint(new Vector3(p.pos.x + p.tangentIn.x, p.pos.y + p.tangentIn.y, layer.z));

            Handles.color = new Color(0.3f, 1f, 0.9f, 0.8f);
            Handles.DrawLine(wpAnchor, wpOut, 1.5f);
            Handles.DrawLine(wpAnchor, wpIn, 1.5f);

            float sizeOut = HandleUtility.GetHandleSize(wpOut) * TANGENT_HANDLE_SIZE;
            float sizeIn = HandleUtility.GetHandleSize(wpIn) * TANGENT_HANDLE_SIZE;

            Handles.color = Color.cyan;
            EditorGUI.BeginChangeCheck();
            Vector3 newOut = Handles.FreeMoveHandle(wpOut, sizeOut, Vector3.zero, Handles.SphereHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(src, "Move tangent");
                Vector3 lp = t.InverseTransformPoint(newOut);
                Vector2 newTan = new Vector2(lp.x - p.pos.x, lp.y - p.pos.y);
                p.tangentOut = newTan;
                p.tangentIn = -newTan;
                EditorUtility.SetDirty(src);
                MarkDirty();
            }

            EditorGUI.BeginChangeCheck();
            Vector3 newIn = Handles.FreeMoveHandle(wpIn, sizeIn, Vector3.zero, Handles.SphereHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(src, "Move tangent");
                Vector3 lp = t.InverseTransformPoint(newIn);
                Vector2 newTan = new Vector2(lp.x - p.pos.x, lp.y - p.pos.y);
                p.tangentIn = newTan;
                p.tangentOut = -newTan;
                EditorUtility.SetDirty(src);
                MarkDirty();
            }
        }

        bool TryFindClickedOutlineIndex(TerrainShape2D src, OutlineLayer layer, Vector2 mouseGUI, out int index)
        {
            index = -1;
            float best = CLICK_RADIUS_PX;
            var t = src.transform;
            for (int i = 0; i < layer.outline.Count; i++)
            {
                Vector3 wp = t.TransformPoint(new Vector3(layer.outline[i].pos.x, layer.outline[i].pos.y, layer.z));
                Vector2 g = HandleUtility.WorldToGUIPoint(wp);
                float d = Vector2.Distance(g, mouseGUI);
                if (d < best) { best = d; index = i; }
            }
            return index >= 0;
        }

        bool TryFindClosestOutlineEdge(OutlineLayer layer, Vector2 lxy, out int edgeIndex, out Vector2 onEdgeLocal)
        {
            edgeIndex = -1;
            onEdgeLocal = Vector2.zero;
            float best = float.MaxValue;
            int n = layer.outline.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = layer.outline[i].pos;
                Vector2 b = layer.outline[(i + 1) % n].pos;
                Vector2 ab = b - a;
                float l2 = ab.sqrMagnitude;
                if (l2 < 1e-9f) continue;
                float t = Mathf.Clamp01(Vector2.Dot(lxy - a, ab) / l2);
                Vector2 cp = a + ab * t;
                float d = Vector2.Distance(cp, lxy);
                if (d < best) { best = d; edgeIndex = i; onEdgeLocal = cp; }
            }
            return edgeIndex >= 0;
        }

        // ──────────────── Interior tool (front layer) ────────────────

        void HandleInteriorTool(TerrainShape2D src)
        {
            var t = src.transform;
            var e = Event.current;

            for (int i = 0; i < src.interior.Count; i++)
            {
                var p = src.interior[i];
                Vector3 effLocal = src.GetEffectiveInteriorPos(p);
                Vector3 wp = t.TransformPoint(effLocal);
                EditorGUI.BeginChangeCheck();
                Vector3 nw = CompactPositionHandle(wp, t.rotation, 0.35f);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(src, "Move interior point");
                    Vector3 newLocal = t.InverseTransformPoint(nw);
                    p.pos = new Vector3(newLocal.x, newLocal.y, p.pos.z);
                    if (src.useHeightmap && src.HasHeightmap)
                    {
                        float baseZ = src.SampleHeightmapZ(new Vector2(newLocal.x, newLocal.y));
                        p.zOffset = newLocal.z - baseZ;
                    }
                    else
                    {
                        p.pos = new Vector3(newLocal.x, newLocal.y, newLocal.z);
                    }
                    EditorUtility.SetDirty(src);
                    MarkDirty();
                }
            }

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (e.control && TryFindClickedInteriorIndex(src, e.mousePosition, out int delIdx))
                {
                    Undo.RecordObject(src, "Delete interior point");
                    int delId = src.interior[delIdx].id;
                    src.interior.RemoveAt(delIdx);
                    src.forcedEdges.RemoveAll(fe => fe.idA == delId || fe.idB == delId);
                    EditorUtility.SetDirty(src);
                    MarkDirty();
                    e.Use();
                }
                else if (e.shift && TryProjectMouseToFrontLayerXY(src, e.mousePosition, out Vector2 lxy))
                {
                    var frontPts = (_cache.sampledOutlinesPerLayer != null && _cache.sampledOutlinesPerLayer.Count > 0)
                        ? _cache.sampledOutlinesPerLayer[0] : null;
                    if (frontPts != null && TerrainShape2DMeshBuilder.IsPointInside(frontPts, lxy))
                    {
                        Undo.RecordObject(src, "Insert interior point");
                        float z = src.SampleHeightmapZ(lxy);
                        src.interior.Add(new InteriorPoint { id = src.AllocateId(), pos = new Vector3(lxy.x, lxy.y, z) });
                        EditorUtility.SetDirty(src);
                        MarkDirty();
                        e.Use();
                    }
                }
            }
        }

        Vector3 CompactPositionHandle(Vector3 wp, Quaternion rot, float scale)
        {
            float s = HandleUtility.GetHandleSize(wp) * scale;
            Handles.color = new Color(0.85f, 0.3f, 0.3f);
            wp = Handles.Slider(wp, rot * Vector3.right, s, Handles.ArrowHandleCap, 0f);
            Handles.color = new Color(0.3f, 0.85f, 0.3f);
            wp = Handles.Slider(wp, rot * Vector3.up, s, Handles.ArrowHandleCap, 0f);
            Handles.color = new Color(0.3f, 0.5f, 1f);
            wp = Handles.Slider(wp, rot * Vector3.forward, s, Handles.ArrowHandleCap, 0f);
            Handles.color = new Color(1f, 1f, 1f, 0.5f);
            wp = Handles.FreeMoveHandle(wp, s * 0.25f, Vector3.zero, Handles.SphereHandleCap);
            return wp;
        }

        bool TryFindClickedInteriorIndex(TerrainShape2D src, Vector2 mouseGUI, out int index)
        {
            index = -1;
            float best = CLICK_RADIUS_PX;
            var t = src.transform;
            for (int i = 0; i < src.interior.Count; i++)
            {
                Vector3 wp = t.TransformPoint(src.GetEffectiveInteriorPos(src.interior[i]));
                Vector2 g = HandleUtility.WorldToGUIPoint(wp);
                float d = Vector2.Distance(g, mouseGUI);
                if (d < best) { best = d; index = i; }
            }
            return index >= 0;
        }

        // ──────────────── Edges tool ────────────────

        void HandleEdgesTool(TerrainShape2D src)
        {
            var t = src.transform;
            var e = Event.current;

            if (src.FrontLayer != null)
            {
                for (int i = 0; i < src.FrontLayer.outline.Count; i++)
                {
                    int id = src.FrontLayer.outline[i].id;
                    Color c = (_edgesDragging && id == _edgesDragFromId) ? Color.cyan : Color.white;
                    DrawVertexPickerDot(t, new Vector3(src.FrontLayer.outline[i].pos.x, src.FrontLayer.outline[i].pos.y, src.FrontLayer.z), c);
                }
            }
            for (int i = 0; i < src.interior.Count; i++)
            {
                int id = src.interior[i].id;
                Color c = (_edgesDragging && id == _edgesDragFromId) ? Color.cyan : new Color(1f, 0.8f, 0.4f);
                DrawVertexPickerDot(t, src.GetEffectiveInteriorPos(src.interior[i]), c);
            }

            EdgeDragSnap snap = default;
            if (_edgesDragging)
            {
                snap = ComputeEdgeSnap(src, e.mousePosition, e.shift);
                DrawDragPreview(src, t, e.mousePosition, snap, e.shift);
                SceneView.RepaintAll();
            }

            if (e.type == EventType.MouseDown && e.button == 0 && !_edgesDragging)
            {
                if (e.control)
                {
                    if (TryFindClickedForcedEdge(src, e.mousePosition, out int feIdx))
                    {
                        Undo.RecordObject(src, "Remove forced edge");
                        src.forcedEdges.RemoveAt(feIdx);
                        EditorUtility.SetDirty(src);
                        MarkDirty();
                        e.Use();
                    }
                }
                else if (TryFindClickedAnyVertexId(src, e.mousePosition, out int vid))
                {
                    _edgesDragging = true;
                    _edgesDragFromId = vid;
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && _edgesDragging)
            {
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0 && _edgesDragging)
            {
                if (snap.valid) CompleteEdgeDrag(src, snap);
                _edgesDragging = false;
                _edgesDragFromId = 0;
                e.Use();
                SceneView.RepaintAll();
            }
        }

        void DrawDragPreview(TerrainShape2D src, Transform t, Vector2 mouseGUI, EdgeDragSnap snap, bool shiftHeld)
        {
            if (!src.TryGetPositionByIdOnFront(_edgesDragFromId, out var fromPos)) return;
            float frontZ = src.FrontLayer != null ? src.FrontLayer.z : 0f;
            Vector3 fromW = t.TransformPoint(fromPos);
            Vector3 toW;
            if (snap.valid)
            {
                toW = t.TransformPoint(snap.posLocal);
            }
            else if (TryProjectMouseToFrontLayerXY(src, mouseGUI, out var lxy))
            {
                toW = t.TransformPoint(new Vector3(lxy.x, lxy.y, frontZ));
            }
            else
            {
                toW = fromW;
            }

            Handles.color = shiftHeld ? new Color(1f, 0.5f, 0.2f, 0.95f) : new Color(1f, 1f, 0.2f, 0.95f);
            Handles.DrawLine(fromW, toW, 3f);

            if (snap.valid)
            {
                float r = HandleUtility.GetHandleSize(toW) * 0.12f;
                Handles.color = snap.toExistingVertex ? Color.cyan : new Color(0.4f, 1f, 0.4f);
                Handles.DrawWireDisc(toW, t.forward, r);
                Handles.DrawWireDisc(toW, t.forward, r * 0.6f);
            }
        }

        EdgeDragSnap ComputeEdgeSnap(TerrainShape2D src, Vector2 mouseGUI, bool shift)
        {
            var t = src.transform;
            float frontZ = src.FrontLayer != null ? src.FrontLayer.z : 0f;
            EdgeDragSnap result = default;
            const float SNAP_PX = 18f;

            if (!shift)
            {
                int bestId = 0;
                float bestD = SNAP_PX;
                Vector3 bestPos = Vector3.zero;
                if (src.FrontLayer != null)
                {
                    foreach (var p in src.FrontLayer.outline)
                    {
                        if (p.id == _edgesDragFromId) continue;
                        Vector3 wp = t.TransformPoint(new Vector3(p.pos.x, p.pos.y, frontZ));
                        float d = Vector2.Distance(HandleUtility.WorldToGUIPoint(wp), mouseGUI);
                        if (d < bestD) { bestD = d; bestId = p.id; bestPos = new Vector3(p.pos.x, p.pos.y, frontZ); }
                    }
                }
                foreach (var ip in src.interior)
                {
                    if (ip.id == _edgesDragFromId) continue;
                    Vector3 effLocal = src.GetEffectiveInteriorPos(ip);
                    Vector3 wp = t.TransformPoint(effLocal);
                    float d = Vector2.Distance(HandleUtility.WorldToGUIPoint(wp), mouseGUI);
                    if (d < bestD) { bestD = d; bestId = ip.id; bestPos = effLocal; }
                }
                if (bestId != 0)
                {
                    result.valid = true;
                    result.toExistingVertex = true;
                    result.vertexId = bestId;
                    result.posLocal = bestPos;
                }
                return result;
            }

            float bestEdgeD = SNAP_PX;
            if (src.FrontLayer != null)
            {
                int n = src.FrontLayer.outline.Count;
                for (int i = 0; i < n; i++)
                {
                    Vector2 a = src.FrontLayer.outline[i].pos;
                    Vector2 b = src.FrontLayer.outline[(i + 1) % n].pos;
                    Vector3 wa = t.TransformPoint(new Vector3(a.x, a.y, frontZ));
                    Vector3 wb = t.TransformPoint(new Vector3(b.x, b.y, frontZ));
                    Vector2 ga = HandleUtility.WorldToGUIPoint(wa);
                    Vector2 gb = HandleUtility.WorldToGUIPoint(wb);
                    Vector2 ab = gb - ga;
                    float l2 = ab.sqrMagnitude;
                    if (l2 < 1e-6f) continue;
                    float u = Mathf.Clamp01(Vector2.Dot(mouseGUI - ga, ab) / l2);
                    Vector2 cp = ga + u * ab;
                    float d = Vector2.Distance(cp, mouseGUI);
                    if (d < bestEdgeD)
                    {
                        bestEdgeD = d;
                        Vector2 onEdge = Vector2.Lerp(a, b, u);
                        result = new EdgeDragSnap
                        {
                            valid = true,
                            toOutlineEdge = true,
                            outlineEdgeAfterIdx = i,
                            posLocal = new Vector3(onEdge.x, onEdge.y, frontZ)
                        };
                    }
                }
            }
            if (_cache.frontFaceVerts != null && _cache.frontFaceTris != null)
            {
                var verts = _cache.frontFaceVerts;
                var tris = _cache.frontFaceTris;
                var seen = new HashSet<long>();
                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    int A = tris[i], B = tris[i + 1], C = tris[i + 2];
                    TestEdge(A, B); TestEdge(B, C); TestEdge(C, A);
                }

                void TestEdge(int X, int Y)
                {
                    long key = X < Y ? ((long)X << 32) | (uint)Y : ((long)Y << 32) | (uint)X;
                    if (!seen.Add(key)) return;
                    Vector3 wa = t.TransformPoint(verts[X]);
                    Vector3 wb = t.TransformPoint(verts[Y]);
                    Vector2 ga = HandleUtility.WorldToGUIPoint(wa);
                    Vector2 gb = HandleUtility.WorldToGUIPoint(wb);
                    Vector2 ab = gb - ga;
                    float l2 = ab.sqrMagnitude;
                    if (l2 < 1e-6f) return;
                    float u = Mathf.Clamp01(Vector2.Dot(mouseGUI - ga, ab) / l2);
                    Vector2 cp = ga + u * ab;
                    float d = Vector2.Distance(cp, mouseGUI);
                    if (d < bestEdgeD)
                    {
                        bestEdgeD = d;
                        Vector3 lerped = Vector3.Lerp(verts[X], verts[Y], u);
                        result = new EdgeDragSnap
                        {
                            valid = true,
                            toInteriorPoint = true,
                            posLocal = lerped
                        };
                    }
                }
            }

            if (!result.valid && TryProjectMouseToFrontLayerXY(src, mouseGUI, out var lxy))
            {
                if (_cache.sampledOutlinesPerLayer != null && _cache.sampledOutlinesPerLayer.Count > 0
                    && TerrainShape2DMeshBuilder.IsPointInside(_cache.sampledOutlinesPerLayer[0], lxy))
                {
                    float zSampled = src.SampleHeightmapZ(lxy);
                    result = new EdgeDragSnap
                    {
                        valid = true,
                        toInteriorPoint = true,
                        posLocal = new Vector3(lxy.x, lxy.y, zSampled)
                    };
                }
            }

            return result;
        }

        void CompleteEdgeDrag(TerrainShape2D src, EdgeDragSnap snap)
        {
            int targetId = 0;

            if (snap.toExistingVertex)
            {
                targetId = snap.vertexId;
                Undo.RecordObject(src, "Add forced edge");
            }
            else if (snap.toOutlineEdge)
            {
                Undo.RecordObject(src, "Insert outline point + edge");
                targetId = src.InsertOutlinePointInAllLayers(snap.outlineEdgeAfterIdx, 0, new Vector2(snap.posLocal.x, snap.posLocal.y));
            }
            else if (snap.toInteriorPoint)
            {
                Undo.RecordObject(src, "Insert interior point + edge");
                int newId = src.AllocateId();
                src.interior.Add(new InteriorPoint { id = newId, pos = snap.posLocal });
                targetId = newId;
            }

            if (targetId != 0 && targetId != _edgesDragFromId)
            {
                src.forcedEdges.Add(new ForcedEdge { idA = _edgesDragFromId, idB = targetId });
                EditorUtility.SetDirty(src);
                MarkDirty();
            }
        }

        void DrawVertexPickerDot(Transform t, Vector3 localPos, Color c)
        {
            Vector3 wp = t.TransformPoint(localPos);
            float size = HandleUtility.GetHandleSize(wp) * 0.06f;
            Handles.color = c;
            Handles.SphereHandleCap(0, wp, t.rotation, size, EventType.Repaint);
        }

        bool TryFindClickedAnyVertexId(TerrainShape2D src, Vector2 mouseGUI, out int id)
        {
            id = 0;
            float best = CLICK_RADIUS_PX;
            var t = src.transform;
            if (src.FrontLayer != null)
            {
                var L = src.FrontLayer;
                for (int i = 0; i < L.outline.Count; i++)
                {
                    Vector3 wp = t.TransformPoint(new Vector3(L.outline[i].pos.x, L.outline[i].pos.y, L.z));
                    Vector2 g = HandleUtility.WorldToGUIPoint(wp);
                    float d = Vector2.Distance(g, mouseGUI);
                    if (d < best) { best = d; id = L.outline[i].id; }
                }
            }
            for (int i = 0; i < src.interior.Count; i++)
            {
                Vector3 wp = t.TransformPoint(src.GetEffectiveInteriorPos(src.interior[i]));
                Vector2 g = HandleUtility.WorldToGUIPoint(wp);
                float d = Vector2.Distance(g, mouseGUI);
                if (d < best) { best = d; id = src.interior[i].id; }
            }
            return id != 0;
        }

        bool TryFindClickedForcedEdge(TerrainShape2D src, Vector2 mouseGUI, out int idx)
        {
            idx = -1;
            float best = 10f;
            var t = src.transform;
            for (int i = 0; i < src.forcedEdges.Count; i++)
            {
                if (!src.TryGetPositionByIdOnFront(src.forcedEdges[i].idA, out var a)) continue;
                if (!src.TryGetPositionByIdOnFront(src.forcedEdges[i].idB, out var b)) continue;
                Vector2 ga = HandleUtility.WorldToGUIPoint(t.TransformPoint(a));
                Vector2 gb = HandleUtility.WorldToGUIPoint(t.TransformPoint(b));
                float d = HandleUtility.DistancePointToLineSegment(mouseGUI, ga, gb);
                if (d < best) { best = d; idx = i; }
            }
            return idx >= 0;
        }

        // ──────────────── Auto Subdivide ────────────────

        void AutoSubdivide(TerrainShape2D src)
        {
            EnsureCache(src);
            if (_cache.sampledOutlinesPerLayer == null || _cache.sampledOutlinesPerLayer.Count == 0) return;
            var pts = _cache.sampledOutlinesPerLayer[0];
            if (pts == null || pts.Count < 3) return;

            float spacing = Mathf.Max(0.05f, src.autoSpacing);
            Vector2 min = pts[0], max = pts[0];
            for (int i = 1; i < pts.Count; i++) { min = Vector2.Min(min, pts[i]); max = Vector2.Max(max, pts[i]); }

            float frontZ = src.FrontLayer != null ? src.FrontLayer.z : 0f;
            Undo.RecordObject(src, "Auto Subdivide");
            for (float x = min.x + spacing * 0.5f; x <= max.x; x += spacing)
            {
                for (float y = min.y + spacing * 0.5f; y <= max.y; y += spacing)
                {
                    var p = new Vector2(x, y);
                    if (!TerrainShape2DMeshBuilder.IsPointInside(pts, p)) continue;
                    if (TerrainShape2DMeshBuilder.DistanceToPolyline(p, pts) < spacing * 0.5f) continue;
                    bool tooClose = false;
                    for (int k = 0; k < src.interior.Count; k++)
                        if (Vector2.Distance(new Vector2(src.interior[k].pos.x, src.interior[k].pos.y), p) < spacing * 0.5f)
                        { tooClose = true; break; }
                    if (tooClose) continue;
                    float sampledZ = src.SampleHeightmapZ(p);
                    src.interior.Add(new InteriorPoint { id = src.AllocateId(), pos = new Vector3(p.x, p.y, sampledZ), autoGenerated = true });
                }
            }
            EditorUtility.SetDirty(src);
            MarkDirty();
        }

        // ──────────────── Misc ────────────────

        void ClampActiveLayer(TerrainShape2D src)
        {
            if (src.layers.Count == 0) { _activeLayer = 0; return; }
            _activeLayer = Mathf.Clamp(_activeLayer, 0, src.layers.Count - 1);
        }

        bool TryProjectMouseToActiveLayerXY(TerrainShape2D src, Vector2 mouseGUI, out Vector2 lxy)
        {
            float z = src.layers[_activeLayer].z;
            return TryProjectMouseToZPlane(src, mouseGUI, z, out lxy);
        }

        bool TryProjectMouseToFrontLayerXY(TerrainShape2D src, Vector2 mouseGUI, out Vector2 lxy)
        {
            float z = src.FrontLayer != null ? src.FrontLayer.z : 0f;
            return TryProjectMouseToZPlane(src, mouseGUI, z, out lxy);
        }

        bool TryProjectMouseToZPlane(TerrainShape2D src, Vector2 mouseGUI, float localZ, out Vector2 lxy)
        {
            lxy = Vector2.zero;
            var t = src.transform;
            Vector3 planePoint = t.TransformPoint(new Vector3(0f, 0f, localZ));
            Vector3 planeNormal = t.forward;
            Plane plane = new Plane(planeNormal, planePoint);
            Ray r = HandleUtility.GUIPointToWorldRay(mouseGUI);
            if (!plane.Raycast(r, out float dist)) return false;
            Vector3 wp = r.GetPoint(dist);
            Vector3 lp = t.InverseTransformPoint(wp);
            lxy = new Vector2(lp.x, lp.y);
            return true;
        }

        void EnsureCache(TerrainShape2D src)
        {
            if (!_cacheDirty && _cache.mesh != null) return;
            Rebuild();
        }

        void MarkDirty()
        {
            _cacheDirty = true;
            Rebuild();
        }

        void Rebuild()
        {
            var src = (TerrainShape2D)target;
            if (src == null) return;
            if (_cache.mesh != null) Object.DestroyImmediate(_cache.mesh);
            _cache = TerrainShape2DMeshBuilder.Build(src);
            var mf = src.GetComponent<MeshFilter>();
            if (mf != null) mf.sharedMesh = _cache.mesh;
            _cacheDirty = false;
        }

        void SaveMeshAsset(TerrainShape2D src)
        {
            const string folder = "Assets/GeneratedMeshes";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets", "GeneratedMeshes");
            var meshCopy = Object.Instantiate(_cache.mesh);
            meshCopy.name = $"TerrainShape_{src.name}";
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{meshCopy.name}.asset");
            AssetDatabase.CreateAsset(meshCopy, path);
            AssetDatabase.SaveAssets();
            var mf = src.GetComponent<MeshFilter>();
            if (mf != null) mf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            EditorGUIUtility.PingObject(mf.sharedMesh);
            Debug.Log($"[TerrainShape2D] saved {path}");
        }
    }
}
