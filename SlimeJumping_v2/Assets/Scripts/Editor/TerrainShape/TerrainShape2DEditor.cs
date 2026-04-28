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

        // Multi-select state. IDs are stable across rebuilds, so we key by id rather than index.
        readonly HashSet<int> _selectedOutlineIds = new HashSet<int>();
        readonly HashSet<int> _selectedInteriorIds = new HashSet<int>();
        bool _marqueeActive;
        Vector2 _marqueeStartGUI;
        // Which mode the in-progress marquee uses. 0=replace, 1=add (Shift), 2=subtract (Ctrl).
        int _marqueeMode;

        // Heightmap preset / procedural-generator UI state (session-only, not serialized).
        HeightmapPreset _heightmapPreset;
        string _presetSaveName = "";
        bool _presetsFoldout = false;

        // Shape templates & vertex transform UI state (session-only).
        bool _templatesFoldout = false;
        float _circleRadius = 1f;
        int _circlePoints = 16;
        bool _transformFoldout = false;
        float _rotateAngle = 90f;
        // 0 = auto (selection centroid if 2+ selected, else all-vertices centroid)
        // 1 = all-vertices centroid
        // 2 = origin (0,0 in local space)
        int _rotatePivotMode = 0;
        HeightmapProcMode _procMode = HeightmapProcMode.Perlin;
        float _procScale = 0.3f;
        int _procOctaves = 4;
        float _procPersistence = 0.5f;
        float _procLacunarity = 2f;
        float _procAngle = 0f;
        int _procSeed = 1;
        float _procFlatValue = 0.5f;
        float _procMinValue = 0f; // blue/far end of generated range
        float _procMaxValue = 1f; // red/near end of generated range

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
            DrawTemplatesSection(src);
            DrawTransformVerticesSection(src);

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
            int newSubs = EditorGUILayout.IntSlider(
                new GUIContent("Outline samples / segment",
                    "Base sample count for each straight segment between two outline points."),
                src.subdivisionsPerSegment, 2, 128);
            int newCurveMult = EditorGUILayout.IntSlider(
                new GUIContent("Curve subdivision ×",
                    "Multiplier applied to 'Outline samples / segment' on smooth (curved) segments only. Bezier curvature needs much finer subdivision than flat edges. 1 = same as straight, 4 = default, higher = smoother curves at the cost of more triangles."),
                src.curveSubdivisionMultiplier, 1, 16);
            int curvedSamples = Mathf.Max(newSubs, newSubs * Mathf.Max(1, newCurveMult));
            EditorGUILayout.LabelField($"   → straight: {newSubs} samples / segment, curved: {curvedSamples} samples / segment", EditorStyles.miniLabel);
            bool newDouble = EditorGUILayout.Toggle(new GUIContent("Double-sided front", "Render the front face from both sides — useful when forced edges or Z bumps make some triangles back-face the camera and look black."), src.doubleSidedFront);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(src, "TerrainShape settings");
                src.subdivisionsPerSegment = newSubs;
                src.curveSubdivisionMultiplier = newCurveMult;
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

            DrawHeightmapPresetsSection(src);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Auto Subdivide", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var newMode = (AutoSubdivideMode)EditorGUILayout.EnumPopup(
                new GUIContent("Mode",
                    "Grid: deterministic regular grid (clean but mechanical).\n" +
                    "JitteredGrid: grid + random offset per cell.\n" +
                    "PoissonDisk: Bridson's blue-noise sampling — organic, even spacing, no clustering.\n" +
                    "Halton: low-discrepancy quasi-random — even coverage, deterministic, less regular than grid.\n" +
                    "Adaptive: per-cell most-interior pick — narrow strips (width < Spacing) still receive one centerline point per cell."),
                src.autoMode);
            float newSpacing = EditorGUILayout.Slider(
                new GUIContent("Spacing", "Grid cell size, or minimum point distance for PoissonDisk/Halton."),
                src.autoSpacing, 0.05f, 2f);
            int newSeed = EditorGUILayout.IntField(
                new GUIContent("Random seed", "Deterministic seed for JitteredGrid and PoissonDisk. Same seed → same layout."),
                src.autoSeed);
            int newLloyd = EditorGUILayout.IntSlider(
                new GUIContent("Lloyd relax iters",
                    "Centroidal Voronoi relaxation passes after generation. Smooths spacing toward an even Voronoi tessellation. 0 = off. 2-4 is usually enough."),
                src.autoLloydIters, 0, 8);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(src, "Auto subdivide settings");
                src.autoMode = newMode;
                src.autoSpacing = newSpacing;
                src.autoSeed = newSeed;
                src.autoLloydIters = newLloyd;
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
            EditorGUILayout.LabelField("Save & Install", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var newMat = (Material)EditorGUILayout.ObjectField(
                new GUIContent("Install material", "Material applied to the GameObject created by 'Save & Install'."),
                src.installMaterial, typeof(Material), false);
            bool newAutoCol = EditorGUILayout.Toggle(
                new GUIContent("Auto generate Collider2D",
                    "After 'Save & Install', run SlimeJumping/Generate PolygonCollider2D from Mesh on the new GameObject. " +
                    "Slices the mesh at world Z=0 and attaches a PolygonCollider2D to an identity-rotated wrapper above it."),
                src.installAutoGenerateCollider2D);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(src, "Set install options");
                src.installMaterial = newMat;
                src.installAutoGenerateCollider2D = newAutoCol;
                EditorUtility.SetDirty(src);
            }
            using (new EditorGUI.DisabledScope(_cache.mesh == null || _cache.mesh.vertexCount < 3))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Save Mesh As Asset")) SaveMeshAsset(src);
                if (GUILayout.Button(new GUIContent("Save & Install", "Save the mesh asset and spawn a new GameObject at this TerrainShape's transform with MeshFilter + MeshRenderer using the install material.")))
                    SaveAndInstall(src);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.HelpBox(
                "Layers: select active layer; outline tool edits its points. + Add Layer duplicates the active one.\n" +
                "Outline: drag yellow handles. Shift+click an edge to insert (propagates to all layers). Ctrl+click a point to delete. Alt+click a point to toggle smooth.\n" +
                "Drag in empty space to marquee-select multiple vertices (cyan = selected); Shift+drag adds, Ctrl+drag subtracts. Drag a selected vertex to move the whole group.\n" +
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
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Layers (front → back)", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            var prevColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.55f, 0.55f);
            if (GUILayout.Button(new GUIContent("Reset Shape",
                "Wipe all layers, outline points, interior, and forced edges, and reinitialize a default 2-layer rectangle."),
                GUILayout.Width(100)))
            {
                if (EditorUtility.DisplayDialog("Reset TerrainShape2D",
                    "All layers, outline points, interior points, and forced edges will be wiped and reinitialized to the default rectangle. This cannot be undone with a single Ctrl+Z if you have followed up with other edits. Continue?",
                    "Reset", "Cancel"))
                {
                    ResetShape(src);
                }
            }
            GUI.backgroundColor = prevColor;
            EditorGUILayout.EndHorizontal();
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
                        for (int li = 0; li < src.layers.Count; li++)
                        {
                            var Lp = src.layers[li].outline[i];
                            Lp.smooth = s;
                            if (s) InitTangentsFor(src.layers[li], i);
                            else { Lp.tangentIn = Vector2.zero; Lp.tangentOut = Vector2.zero; }
                        }
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

            // Handle modifier+click BEFORE FreeMoveHandle, otherwise the handle eats MouseDown.
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (e.control && TryFindClickedOutlineIndex(src, layer, e.mousePosition, out int delIdx))
                {
                    if (layer.outline.Count > 3)
                    {
                        Undo.RecordObject(src, "Delete outline point");
                        int delId = layer.outline[delIdx].id;
                        src.RemoveOutlinePointInAllLayers(delIdx);
                        _selectedOutlineIds.Remove(delId);
                        EditorUtility.SetDirty(src);
                        MarkDirty();
                        e.Use();
                        return;
                    }
                }
                else if (e.alt && TryFindClickedOutlineIndex(src, layer, e.mousePosition, out int smoothIdx))
                {
                    Undo.RecordObject(src, "Toggle outline smooth");
                    bool newSmooth = !layer.outline[smoothIdx].smooth;
                    // Propagate smooth + tangents to the same index in every layer; otherwise
                    // SampleLayerOutline produces different sample counts per layer and the
                    // mesh builder's equality guard discards the whole mesh.
                    for (int li = 0; li < src.layers.Count; li++)
                    {
                        var Lp = src.layers[li].outline[smoothIdx];
                        Lp.smooth = newSmooth;
                        if (newSmooth) InitTangentsFor(src.layers[li], smoothIdx);
                        else { Lp.tangentIn = Vector2.zero; Lp.tangentOut = Vector2.zero; }
                    }
                    EditorUtility.SetDirty(src);
                    MarkDirty();
                    e.Use();
                    return;
                }
            }

            HandleMarquee(e, _selectedOutlineIds,
                () => CollectOutlineIdsInRect(src, layer),
                () => TryFindClickedOutlineIndex(src, layer, e.mousePosition, out _)
                   || TryFindClickedTangentHandle(src, layer, e.mousePosition, out _, out _),
                (mode, mouseGUI) =>
                {
                    // Shift+click on empty (no drag): insert outline point on closest edge.
                    if (mode != 1) return;
                    if (!TryProjectMouseToActiveLayerXY(src, mouseGUI, out Vector2 lxy)) return;
                    if (!TryFindClosestOutlineEdge(layer, lxy, out int edgeI, out Vector2 onEdge)) return;
                    Undo.RecordObject(src, "Insert outline point");
                    src.InsertOutlinePointInAllLayers(edgeI, _activeLayer, onEdge);
                    EditorUtility.SetDirty(src);
                    MarkDirty();
                });

            // Suppress FreeMoveHandle's hot-control grab while a modifier is held or a marquee
            // is in progress — Layout events fire under the handle even before MouseDown.
            bool suppressHandles = ((e.control || e.alt) && (e.type == EventType.Layout || e.type == EventType.MouseDown))
                                || _marqueeActive;

            // For group-drag we need to remember the anchor point's old position so we can
            // compute world-space delta when the FreeMoveHandle reports a change.
            for (int i = 0; i < layer.outline.Count; i++)
            {
                var p = layer.outline[i];
                bool selected = _selectedOutlineIds.Contains(p.id);
                Vector3 wp = t.TransformPoint(new Vector3(p.pos.x, p.pos.y, layer.z));
                float size = HandleUtility.GetHandleSize(wp) * HANDLE_SIZE;
                Handles.color = selected
                    ? new Color(0.3f, 0.9f, 1f)
                    : (p.smooth ? new Color(0.4f, 1f, 0.4f) : Color.yellow);
                if (!suppressHandles)
                {
                    EditorGUI.BeginChangeCheck();
                    Vector3 nw = Handles.FreeMoveHandle(wp, size, Vector3.zero, Handles.SphereHandleCap);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Vector3 lp = t.InverseTransformPoint(nw);
                        Vector2 newPos = new Vector2(lp.x, lp.y);
                        Vector2 delta = newPos - p.pos;
                        if (selected && _selectedOutlineIds.Count > 1)
                        {
                            Undo.RecordObject(src, "Move outline points");
                            foreach (var op in layer.outline)
                                if (_selectedOutlineIds.Contains(op.id))
                                    op.pos += delta;
                        }
                        else
                        {
                            Undo.RecordObject(src, "Move outline point");
                            p.pos = newPos;
                        }
                        EditorUtility.SetDirty(src);
                        MarkDirty();
                    }
                }
                else if (e.type == EventType.Repaint)
                {
                    Handles.SphereHandleCap(0, wp, t.rotation, size, EventType.Repaint);
                }

                if (p.smooth) DrawTangentHandles(src, layer, i, t);
            }
        }

        HashSet<int> CollectOutlineIdsInRect(TerrainShape2D src, OutlineLayer layer)
        {
            var hits = new HashSet<int>();
            var rect = MarqueeRect();
            var t = src.transform;
            foreach (var p in layer.outline)
            {
                Vector3 wp = t.TransformPoint(new Vector3(p.pos.x, p.pos.y, layer.z));
                Vector2 g = HandleUtility.WorldToGUIPoint(wp);
                if (rect.Contains(g)) hits.Add(p.id);
            }
            return hits;
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

        // Returns true if the mouse is within CLICK_RADIUS_PX of either tangent endpoint
        // of any smooth outline point on the active layer. `side` is 0 for tangentOut, 1 for tangentIn.
        bool TryFindClickedTangentHandle(TerrainShape2D src, OutlineLayer layer, Vector2 mouseGUI, out int index, out int side)
        {
            index = -1;
            side = -1;
            float best = CLICK_RADIUS_PX;
            var t = src.transform;
            for (int i = 0; i < layer.outline.Count; i++)
            {
                var p = layer.outline[i];
                if (!p.smooth) continue;
                Vector3 wpOut = t.TransformPoint(new Vector3(p.pos.x + p.tangentOut.x, p.pos.y + p.tangentOut.y, layer.z));
                Vector3 wpIn = t.TransformPoint(new Vector3(p.pos.x + p.tangentIn.x, p.pos.y + p.tangentIn.y, layer.z));
                float dOut = Vector2.Distance(HandleUtility.WorldToGUIPoint(wpOut), mouseGUI);
                float dIn = Vector2.Distance(HandleUtility.WorldToGUIPoint(wpIn), mouseGUI);
                if (dOut < best) { best = dOut; index = i; side = 0; }
                if (dIn < best) { best = dIn; index = i; side = 1; }
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

            // Modifier+click first, before the slider handles register their controls.
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (e.control && TryFindClickedInteriorIndex(src, e.mousePosition, out int delIdx))
                {
                    Undo.RecordObject(src, "Delete interior point");
                    int delId = src.interior[delIdx].id;
                    src.interior.RemoveAt(delIdx);
                    src.forcedEdges.RemoveAll(fe => fe.idA == delId || fe.idB == delId);
                    _selectedInteriorIds.Remove(delId);
                    EditorUtility.SetDirty(src);
                    MarkDirty();
                    e.Use();
                    return;
                }
            }

            HandleMarquee(e, _selectedInteriorIds,
                () => CollectInteriorIdsInRect(src),
                () => TryFindClickedInteriorIndex(src, e.mousePosition, out _),
                (mode, mouseGUI) =>
                {
                    // Shift+click on empty (no drag): insert interior point at mouse if inside polygon.
                    if (mode != 1) return;
                    if (!TryProjectMouseToFrontLayerXY(src, mouseGUI, out Vector2 lxy)) return;
                    var frontPts = (_cache.sampledOutlinesPerLayer != null && _cache.sampledOutlinesPerLayer.Count > 0)
                        ? _cache.sampledOutlinesPerLayer[0] : null;
                    if (frontPts == null || !TerrainShape2DMeshBuilder.IsPointInside(frontPts, lxy)) return;
                    Undo.RecordObject(src, "Insert interior point");
                    float z = src.SampleHeightmapZ(lxy);
                    src.interior.Add(new InteriorPoint { id = src.AllocateId(), pos = new Vector3(lxy.x, lxy.y, z) });
                    EditorUtility.SetDirty(src);
                    MarkDirty();
                });

            bool suppressHandles = (e.control && (e.type == EventType.Layout || e.type == EventType.MouseDown))
                                || _marqueeActive;

            for (int i = 0; i < src.interior.Count; i++)
            {
                var p = src.interior[i];
                bool selected = _selectedInteriorIds.Contains(p.id);
                Vector3 effLocal = src.GetEffectiveInteriorPos(p);
                Vector3 wp = t.TransformPoint(effLocal);
                if (!suppressHandles)
                {
                    EditorGUI.BeginChangeCheck();
                    Vector3 nw = CompactPositionHandle(wp, t.rotation, 0.35f, selected);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Vector3 newLocal = t.InverseTransformPoint(nw);
                        Vector3 deltaLocal = newLocal - effLocal;
                        if (selected && _selectedInteriorIds.Count > 1)
                        {
                            Undo.RecordObject(src, "Move interior points");
                            foreach (var ip in src.interior)
                            {
                                if (!_selectedInteriorIds.Contains(ip.id)) continue;
                                Vector3 ipEff = src.GetEffectiveInteriorPos(ip);
                                Vector3 ipNew = ipEff + deltaLocal;
                                ApplyInteriorMove(src, ip, ipNew);
                            }
                        }
                        else
                        {
                            Undo.RecordObject(src, "Move interior point");
                            ApplyInteriorMove(src, p, newLocal);
                        }
                        EditorUtility.SetDirty(src);
                        MarkDirty();
                    }
                }
                else if (e.type == EventType.Repaint)
                {
                    float s = HandleUtility.GetHandleSize(wp) * 0.35f * 0.25f;
                    Handles.color = selected ? new Color(0.3f, 0.9f, 1f) : new Color(1f, 1f, 1f, 0.6f);
                    Handles.SphereHandleCap(0, wp, t.rotation, s, EventType.Repaint);
                }
            }
        }

        static void ApplyInteriorMove(TerrainShape2D src, InteriorPoint ip, Vector3 newEffLocal)
        {
            if (src.useHeightmap && src.HasHeightmap)
            {
                float baseZ = src.SampleHeightmapZ(new Vector2(newEffLocal.x, newEffLocal.y));
                ip.pos = new Vector3(newEffLocal.x, newEffLocal.y, ip.pos.z);
                ip.zOffset = newEffLocal.z - baseZ;
            }
            else
            {
                ip.pos = newEffLocal;
            }
        }

        HashSet<int> CollectInteriorIdsInRect(TerrainShape2D src)
        {
            var hits = new HashSet<int>();
            var rect = MarqueeRect();
            var t = src.transform;
            foreach (var ip in src.interior)
            {
                Vector3 wp = t.TransformPoint(src.GetEffectiveInteriorPos(ip));
                Vector2 g = HandleUtility.WorldToGUIPoint(wp);
                if (rect.Contains(g)) hits.Add(ip.id);
            }
            return hits;
        }

        Vector3 CompactPositionHandle(Vector3 wp, Quaternion rot, float scale, bool selected = false)
        {
            float s = HandleUtility.GetHandleSize(wp) * scale;
            Handles.color = selected ? new Color(0.3f, 0.9f, 1f) : new Color(0.85f, 0.3f, 0.3f);
            wp = Handles.Slider(wp, rot * Vector3.right, s, Handles.ArrowHandleCap, 0f);
            Handles.color = selected ? new Color(0.3f, 0.9f, 1f) : new Color(0.3f, 0.85f, 0.3f);
            wp = Handles.Slider(wp, rot * Vector3.up, s, Handles.ArrowHandleCap, 0f);
            Handles.color = selected ? new Color(0.3f, 0.9f, 1f) : new Color(0.3f, 0.5f, 1f);
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
            var existing = new List<Vector2>(src.interior.Count);
            foreach (var ip in src.interior) existing.Add(new Vector2(ip.pos.x, ip.pos.y));

            var rng = new System.Random(src.autoSeed);
            List<Vector2> candidates;
            switch (src.autoMode)
            {
                case AutoSubdivideMode.JitteredGrid: candidates = SampleJitteredGrid(pts, spacing, existing, rng); break;
                case AutoSubdivideMode.PoissonDisk:  candidates = SamplePoissonDisk(pts, spacing, existing, rng); break;
                case AutoSubdivideMode.Halton:       candidates = SampleHalton(pts, spacing, existing); break;
                case AutoSubdivideMode.Adaptive:     candidates = SampleAdaptive(pts, spacing, existing); break;
                case AutoSubdivideMode.Grid:
                default:                             candidates = SampleGrid(pts, spacing, existing); break;
            }

            if (src.autoLloydIters > 0 && candidates.Count > 0)
                candidates = LloydRelax(candidates, pts, existing, src.autoLloydIters);

            Undo.RecordObject(src, "Auto Subdivide");
            foreach (var p in candidates)
            {
                float sampledZ = src.SampleHeightmapZ(p);
                src.interior.Add(new InteriorPoint
                {
                    id = src.AllocateId(),
                    pos = new Vector3(p.x, p.y, sampledZ),
                    autoGenerated = true,
                });
            }
            EditorUtility.SetDirty(src);
            MarkDirty();
        }

        static void PolyBounds(List<Vector2> poly, out Vector2 min, out Vector2 max)
        {
            min = poly[0]; max = poly[0];
            for (int i = 1; i < poly.Count; i++) { min = Vector2.Min(min, poly[i]); max = Vector2.Max(max, poly[i]); }
        }

        static bool AcceptCandidate(Vector2 p, List<Vector2> poly, float minDist, float boundaryBuffer, List<Vector2> all)
        {
            if (!TerrainShape2DMeshBuilder.IsPointInside(poly, p)) return false;
            if (TerrainShape2DMeshBuilder.DistanceToPolyline(p, poly) < boundaryBuffer) return false;
            for (int k = 0; k < all.Count; k++)
                if (Vector2.SqrMagnitude(all[k] - p) < minDist * minDist) return false;
            return true;
        }

        static List<Vector2> SampleGrid(List<Vector2> poly, float spacing, List<Vector2> existing)
        {
            var result = new List<Vector2>();
            if (poly.Count < 3) return result;
            PolyBounds(poly, out var min, out var max);
            var all = new List<Vector2>(existing);
            float minDist = spacing * 0.5f;
            float buf = spacing * 0.5f;
            for (float x = min.x + spacing * 0.5f; x <= max.x; x += spacing)
            for (float y = min.y + spacing * 0.5f; y <= max.y; y += spacing)
            {
                var p = new Vector2(x, y);
                if (!AcceptCandidate(p, poly, minDist, buf, all)) continue;
                all.Add(p); result.Add(p);
            }
            return result;
        }

        static List<Vector2> SampleJitteredGrid(List<Vector2> poly, float spacing, List<Vector2> existing, System.Random rng)
        {
            var result = new List<Vector2>();
            if (poly.Count < 3) return result;
            PolyBounds(poly, out var min, out var max);
            var all = new List<Vector2>(existing);
            float minDist = spacing * 0.45f;
            float buf = spacing * 0.4f;
            float jitter = spacing * 0.45f;
            for (float x = min.x + spacing * 0.5f; x <= max.x; x += spacing)
            for (float y = min.y + spacing * 0.5f; y <= max.y; y += spacing)
            {
                float jx = ((float)rng.NextDouble() * 2f - 1f) * jitter;
                float jy = ((float)rng.NextDouble() * 2f - 1f) * jitter;
                var p = new Vector2(x + jx, y + jy);
                if (!AcceptCandidate(p, poly, minDist, buf, all)) continue;
                all.Add(p); result.Add(p);
            }
            return result;
        }

        // Bridson's Poisson-disk sampling: O(N) blue-noise distribution where every pair
        // of points is at least `r` apart. Uses a background grid (cell = r/√2 → max one
        // sample per cell) so neighbour queries are O(1).
        // Reference: Bridson 2007, "Fast Poisson Disk Sampling in Arbitrary Dimensions".
        static List<Vector2> SamplePoissonDisk(List<Vector2> poly, float r, List<Vector2> existing, System.Random rng)
        {
            var result = new List<Vector2>();
            if (poly.Count < 3) return result;
            PolyBounds(poly, out var min, out var max);
            Vector2 size = max - min;
            if (size.x <= 0f || size.y <= 0f) return result;

            float cell = r / Mathf.Sqrt(2f);
            int gw = Mathf.Max(1, Mathf.CeilToInt(size.x / cell) + 1);
            int gh = Mathf.Max(1, Mathf.CeilToInt(size.y / cell) + 1);
            var grid = new int[gw * gh];
            for (int i = 0; i < grid.Length; i++) grid[i] = -1;
            var all = new List<Vector2>();
            float buf = r * 0.5f;

            void Place(Vector2 p)
            {
                int gx = Mathf.Clamp((int)((p.x - min.x) / cell), 0, gw - 1);
                int gy = Mathf.Clamp((int)((p.y - min.y) / cell), 0, gh - 1);
                grid[gy * gw + gx] = all.Count;
                all.Add(p);
            }

            bool TooClose(Vector2 p)
            {
                int gx = (int)((p.x - min.x) / cell);
                int gy = (int)((p.y - min.y) / cell);
                for (int dy = -2; dy <= 2; dy++)
                {
                    int y = gy + dy;
                    if (y < 0 || y >= gh) continue;
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int x = gx + dx;
                        if (x < 0 || x >= gw) continue;
                        int idx = grid[y * gw + x];
                        if (idx < 0) continue;
                        if (Vector2.SqrMagnitude(all[idx] - p) < r * r) return true;
                    }
                }
                return false;
            }

            foreach (var ex in existing)
            {
                if (ex.x < min.x || ex.x > max.x || ex.y < min.y || ex.y > max.y) continue;
                Place(ex);
            }

            var active = new List<Vector2>();
            for (int tries = 0; tries < 200; tries++)
            {
                var seed = new Vector2(
                    min.x + (float)rng.NextDouble() * size.x,
                    min.y + (float)rng.NextDouble() * size.y);
                if (!TerrainShape2DMeshBuilder.IsPointInside(poly, seed)) continue;
                if (TerrainShape2DMeshBuilder.DistanceToPolyline(seed, poly) < buf) continue;
                if (TooClose(seed)) continue;
                Place(seed); active.Add(seed); result.Add(seed);
                break;
            }

            const int K = 30;
            while (active.Count > 0)
            {
                int ai = rng.Next(active.Count);
                Vector2 a = active[ai];
                bool placed = false;
                for (int k = 0; k < K; k++)
                {
                    float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float dist = r * (1f + (float)rng.NextDouble());
                    var c = a + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist;
                    if (c.x < min.x || c.x > max.x || c.y < min.y || c.y > max.y) continue;
                    if (!TerrainShape2DMeshBuilder.IsPointInside(poly, c)) continue;
                    if (TerrainShape2DMeshBuilder.DistanceToPolyline(c, poly) < buf) continue;
                    if (TooClose(c)) continue;
                    Place(c); active.Add(c); result.Add(c);
                    placed = true;
                    break;
                }
                if (!placed) active.RemoveAt(ai);
            }
            return result;
        }

        // Halton low-discrepancy sequence: deterministic, evenly-distributed, repeatable
        // without RNG. We use bases (2, 3) for X and Y, then reject points outside the polygon.
        static List<Vector2> SampleHalton(List<Vector2> poly, float r, List<Vector2> existing)
        {
            var result = new List<Vector2>();
            if (poly.Count < 3) return result;
            PolyBounds(poly, out var min, out var max);
            Vector2 size = max - min;
            if (size.x <= 0f || size.y <= 0f) return result;

            float area = size.x * size.y;
            int target = Mathf.Max(8, Mathf.RoundToInt(area / (r * r * 0.85f)));
            var all = new List<Vector2>(existing);
            float minDist = r;
            float buf = r * 0.5f;

            int maxTries = target * 16;
            for (int i = 1; result.Count < target && i < maxTries; i++)
            {
                var p = new Vector2(min.x + Halton(i, 2) * size.x, min.y + Halton(i, 3) * size.y);
                if (!AcceptCandidate(p, poly, minDist, buf, all)) continue;
                all.Add(p); result.Add(p);
            }
            return result;
        }

        // Adaptive: each spacing×spacing cell yields its single most-interior point
        // (sub-sampled approximation of the local medial-axis anchor). Unlike the
        // other samplers there is no fixed boundary buffer — narrow strips of width
        // W < spacing still receive one centerline point per cell instead of being
        // entirely rejected. The min-distance check between accepted points is
        // relaxed in proportion to local thickness (bestD), so two adjacent
        // strip-cells can both keep their centerline anchor.
        static List<Vector2> SampleAdaptive(List<Vector2> poly, float spacing, List<Vector2> existing)
        {
            var result = new List<Vector2>();
            if (poly.Count < 3) return result;
            PolyBounds(poly, out var min, out var max);
            Vector2 size = max - min;
            if (size.x <= 0f || size.y <= 0f) return result;

            var all = new List<Vector2>(existing);

            const int SUB = 5;
            int nx = Mathf.Max(1, Mathf.CeilToInt(size.x / spacing));
            int ny = Mathf.Max(1, Mathf.CeilToInt(size.y / spacing));

            for (int cy = 0; cy < ny; cy++)
            for (int cx = 0; cx < nx; cx++)
            {
                Vector2 cellMin = new Vector2(min.x + cx * spacing, min.y + cy * spacing);

                Vector2 bestP = default;
                float bestD = -1f;
                for (int sy = 0; sy < SUB; sy++)
                for (int sx = 0; sx < SUB; sx++)
                {
                    float t = (sx + 0.5f) / SUB;
                    float u = (sy + 0.5f) / SUB;
                    Vector2 p = new Vector2(
                        cellMin.x + spacing * t,
                        cellMin.y + spacing * u);
                    if (p.x > max.x || p.y > max.y) continue;
                    if (!TerrainShape2DMeshBuilder.IsPointInside(poly, p)) continue;
                    float d = TerrainShape2DMeshBuilder.DistanceToPolyline(p, poly);
                    if (d > bestD) { bestD = d; bestP = p; }
                }

                if (bestD <= 0f) continue;

                float minDist = Mathf.Min(spacing * 0.6f, bestD * 1.5f);
                bool tooClose = false;
                float md2 = minDist * minDist;
                for (int k = 0; k < all.Count; k++)
                {
                    if (Vector2.SqrMagnitude(all[k] - bestP) < md2) { tooClose = true; break; }
                }
                if (tooClose) continue;

                all.Add(bestP);
                result.Add(bestP);
            }
            return result;
        }

        static float Halton(int index, int b)
        {
            float f = 1f, r = 0f;
            while (index > 0)
            {
                f /= b;
                r += f * (index % b);
                index /= b;
            }
            return r;
        }

        // Lloyd's relaxation toward a Centroidal Voronoi Tessellation. Each iteration:
        //   1. discretise the polygon into a sample grid,
        //   2. assign each sample to its nearest movable point (existing manual points stay fixed),
        //   3. move each movable point to the centroid of its assigned samples.
        // Converges to evenly-spaced point distributions; 2-4 iterations is usually enough.
        static List<Vector2> LloydRelax(List<Vector2> movable, List<Vector2> poly, List<Vector2> fixedPts, int iters)
        {
            if (movable.Count == 0) return movable;
            PolyBounds(poly, out var min, out var max);
            Vector2 size = max - min;
            if (size.x <= 0f || size.y <= 0f) return movable;

            int RES = 80;
            var combined = new List<Vector2>(movable.Count + fixedPts.Count);
            combined.AddRange(movable);
            int movableCount = movable.Count;
            combined.AddRange(fixedPts);

            for (int it = 0; it < iters; it++)
            {
                var sumX = new float[combined.Count];
                var sumY = new float[combined.Count];
                var count = new int[combined.Count];

                for (int gy = 0; gy < RES; gy++)
                {
                    float py = min.y + size.y * (gy + 0.5f) / RES;
                    for (int gx = 0; gx < RES; gx++)
                    {
                        float px = min.x + size.x * (gx + 0.5f) / RES;
                        var g = new Vector2(px, py);
                        if (!TerrainShape2DMeshBuilder.IsPointInside(poly, g)) continue;
                        int best = 0;
                        float bestD = float.MaxValue;
                        for (int j = 0; j < combined.Count; j++)
                        {
                            float d = Vector2.SqrMagnitude(combined[j] - g);
                            if (d < bestD) { bestD = d; best = j; }
                        }
                        sumX[best] += g.x;
                        sumY[best] += g.y;
                        count[best]++;
                    }
                }

                for (int j = 0; j < movableCount; j++)
                {
                    if (count[j] <= 0) continue;
                    var newPos = new Vector2(sumX[j] / count[j], sumY[j] / count[j]);
                    if (TerrainShape2DMeshBuilder.IsPointInside(poly, newPos))
                        combined[j] = newPos;
                }
            }

            return combined.GetRange(0, movableCount);
        }

        // ──────────────── Heightmap presets / generator ────────────────

        void DrawHeightmapPresetsSection(TerrainShape2D src)
        {
            EditorGUILayout.Space();
            _presetsFoldout = EditorGUILayout.Foldout(_presetsFoldout, "Heightmap presets / generator", true, EditorStyles.foldoutHeader);
            if (!_presetsFoldout) return;

            // ── Load ──
            EditorGUILayout.LabelField("Load preset", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            _heightmapPreset = (HeightmapPreset)EditorGUILayout.ObjectField(_heightmapPreset, typeof(HeightmapPreset), false);
            using (new EditorGUI.DisabledScope(_heightmapPreset == null || _heightmapPreset.data == null))
            {
                if (GUILayout.Button("Load", GUILayout.Width(60))) LoadHeightmapPreset(src);
            }
            EditorGUILayout.EndHorizontal();

            // Quick picker: scan Assets/HeightmapPresets/ for all presets so the user can
            // browse without navigating the project window.
            var allPresets = AssetDatabase.FindAssets("t:HeightmapPreset");
            if (allPresets != null && allPresets.Length > 0)
            {
                var names = new string[allPresets.Length + 1];
                names[0] = "(quick pick…)";
                for (int i = 0; i < allPresets.Length; i++)
                    names[i + 1] = System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(allPresets[i]));
                int chosen = EditorGUILayout.Popup(0, names);
                if (chosen > 0)
                {
                    var path = AssetDatabase.GUIDToAssetPath(allPresets[chosen - 1]);
                    _heightmapPreset = AssetDatabase.LoadAssetAtPath<HeightmapPreset>(path);
                }
            }

            EditorGUILayout.Space();

            // ── Save ──
            EditorGUILayout.LabelField("Save current as preset", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            _presetSaveName = EditorGUILayout.TextField(_presetSaveName);
            using (new EditorGUI.DisabledScope(!src.HasHeightmap || string.IsNullOrWhiteSpace(_presetSaveName)))
            {
                if (GUILayout.Button("Save", GUILayout.Width(60))) SaveHeightmapPreset(src, _presetSaveName.Trim());
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("   → Saved under Assets/HeightmapPresets/", EditorStyles.miniLabel);

            EditorGUILayout.Space();

            // ── Procedural generator ──
            EditorGUILayout.LabelField("Procedural generator", EditorStyles.miniBoldLabel);
            _procMode = (HeightmapProcMode)EditorGUILayout.EnumPopup(
                new GUIContent("Mode",
                    "Perlin: classic smooth fractal noise.\n" +
                    "RidgedPerlin: ridge / mountain look.\n" +
                    "Cone: radial falloff from center (peak at center).\n" +
                    "LinearGradient: directional ramp.\n" +
                    "Random: pure white noise.\n" +
                    "Flat: fill with a single value."),
                _procMode);

            switch (_procMode)
            {
                case HeightmapProcMode.Perlin:
                case HeightmapProcMode.RidgedPerlin:
                    _procScale = EditorGUILayout.Slider(new GUIContent("Scale", "UV-space tile size (smaller = finer detail)."), _procScale, 0.05f, 1.5f);
                    _procOctaves = EditorGUILayout.IntSlider("Octaves", _procOctaves, 1, 8);
                    _procPersistence = EditorGUILayout.Slider(new GUIContent("Persistence", "Amplitude falloff per octave (0..1)."), _procPersistence, 0f, 1f);
                    _procLacunarity = EditorGUILayout.Slider(new GUIContent("Lacunarity", "Frequency multiplier per octave (>1)."), _procLacunarity, 1f, 4f);
                    _procSeed = EditorGUILayout.IntField("Seed", _procSeed);
                    break;
                case HeightmapProcMode.Cone:
                    _procScale = EditorGUILayout.Slider(new GUIContent("Radius", "Falloff radius in normalised units (1.0 reaches the corners)."), _procScale, 0.1f, 2f);
                    break;
                case HeightmapProcMode.LinearGradient:
                    _procAngle = EditorGUILayout.Slider("Angle (°)", _procAngle, 0f, 360f);
                    break;
                case HeightmapProcMode.Random:
                    _procSeed = EditorGUILayout.IntField("Seed", _procSeed);
                    break;
                case HeightmapProcMode.Flat:
                    _procFlatValue = EditorGUILayout.Slider(new GUIContent("Value (0=blue/far, 1=red/near)"), _procFlatValue, 0f, 1f);
                    break;
            }

            // Output range: remap the generator's 0..1 result to [min..max]. Flat already
            // writes a single value directly, so the remap doesn't apply there.
            if (_procMode != HeightmapProcMode.Flat)
            {
                EditorGUILayout.LabelField(new GUIContent("Output range", "Remap the generator's 0..1 output to [min..max]. Min = blue/far end, Max = red/near end."), EditorStyles.miniBoldLabel);
                float lo = _procMinValue, hi = _procMaxValue;
                EditorGUILayout.MinMaxSlider(new GUIContent("Min (blue) / Max (red)"), ref lo, ref hi, 0f, 1f);
                lo = EditorGUILayout.Slider(new GUIContent("Min (blue/far)", "Lowest value the generator can produce. 0 = full blue/far."), lo, 0f, 1f);
                hi = EditorGUILayout.Slider(new GUIContent("Max (red/near)", "Highest value the generator can produce. 1 = full red/near."), hi, 0f, 1f);
                if (hi < lo) hi = lo;
                _procMinValue = lo;
                _procMaxValue = hi;
            }

            using (new EditorGUI.DisabledScope(!src.HasHeightmap))
            {
                if (GUILayout.Button("Generate (overwrite heightmap)")) GenerateHeightmap(src);
            }
        }

        void LoadHeightmapPreset(TerrainShape2D src)
        {
            if (_heightmapPreset == null || _heightmapPreset.data == null) return;
            int expected = _heightmapPreset.width * _heightmapPreset.height;
            if (_heightmapPreset.data.Length != expected)
            {
                EditorUtility.DisplayDialog("Bad preset",
                    $"Preset's data length ({_heightmapPreset.data.Length}) does not match its declared size ({_heightmapPreset.width}×{_heightmapPreset.height} = {expected}).",
                    "OK");
                return;
            }
            Undo.RecordObject(src, "Load heightmap preset");
            src.heightmapW = _heightmapPreset.width;
            src.heightmapH = _heightmapPreset.height;
            src.heightmapData = (byte[])_heightmapPreset.data.Clone();
            src.heightmapZMin = _heightmapPreset.zMin;
            src.heightmapZMax = _heightmapPreset.zMax;
            EditorUtility.SetDirty(src);
            TerrainShape2DZPainterWindow.NotifyExternalDataChanged(src);
            NotifyHeightmapChanged(src);
            MarkDirty();
            Debug.Log($"[TerrainShape2D] Loaded heightmap preset '{_heightmapPreset.name}' ({_heightmapPreset.width}×{_heightmapPreset.height}).");
        }

        void SaveHeightmapPreset(TerrainShape2D src, string presetName)
        {
            if (!src.HasHeightmap) return;
            const string folder = "Assets/HeightmapPresets";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets", "HeightmapPresets");
            string path = $"{folder}/{presetName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<HeightmapPreset>(path);
            bool overwrote = existing != null;
            if (overwrote && !EditorUtility.DisplayDialog("Overwrite preset?",
                $"'{presetName}' already exists. Overwrite?", "Overwrite", "Cancel"))
                return;

            var preset = existing ?? ScriptableObject.CreateInstance<HeightmapPreset>();
            preset.width = src.heightmapW;
            preset.height = src.heightmapH;
            preset.data = (byte[])src.heightmapData.Clone();
            preset.zMin = src.heightmapZMin;
            preset.zMax = src.heightmapZMax;

            if (!overwrote) AssetDatabase.CreateAsset(preset, path);
            EditorUtility.SetDirty(preset);
            AssetDatabase.SaveAssets();
            _heightmapPreset = preset;
            EditorGUIUtility.PingObject(preset);
            Debug.Log($"[TerrainShape2D] Saved heightmap preset to {path}");
        }

        void GenerateHeightmap(TerrainShape2D src)
        {
            if (!src.HasHeightmap) return;
            Undo.RecordObject(src, "Generate heightmap");
            var p = new HeightmapProcGen.Params
            {
                scale = _procScale,
                octaves = _procOctaves,
                persistence = _procPersistence,
                lacunarity = _procLacunarity,
                angleDeg = _procAngle,
                seed = _procSeed,
                flatValue = _procFlatValue,
                minValue = _procMinValue,
                maxValue = _procMaxValue,
            };
            src.heightmapData = HeightmapProcGen.Generate(_procMode, src.heightmapW, src.heightmapH, p);
            EditorUtility.SetDirty(src);
            TerrainShape2DZPainterWindow.NotifyExternalDataChanged(src);
            NotifyHeightmapChanged(src);
            MarkDirty();

            // Roll a fresh seed so the next click produces a new variation. Modes that ignore
            // the seed (Cone / LinearGradient / Flat) are unaffected; the field still updates
            // so the user sees the change.
            _procSeed = new System.Random().Next(int.MinValue, int.MaxValue);
        }

        // ──────────────── Marquee selection ────────────────

        // Drives a screen-space rectangle selection. Plain drag = replace; Shift+drag = add;
        // Ctrl+drag = subtract. Click without drag clears selection (or fires the modifier
        // click action via `onClickWithoutDrag` — needed because Shift+click vs Shift+drag
        // can only be told apart at MouseUp).
        // `isOverHandle` lets the caller bail out when the click lands on a vertex — the
        // FreeMoveHandle / Slider for that vertex must receive the MouseDown instead.
        void HandleMarquee(Event e, HashSet<int> selection,
                           System.Func<HashSet<int>> hitsInRect, System.Func<bool> isOverHandle,
                           System.Action<int /*marqueeMode*/, Vector2 /*mousePos*/> onClickWithoutDrag = null)
        {
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && !_marqueeActive)
            {
                if (isOverHandle != null && isOverHandle()) return;
                _marqueeActive = true;
                _marqueeStartGUI = e.mousePosition;
                _marqueeMode = e.shift ? 1 : (e.control ? 2 : 0);
                e.Use();
                return;
            }

            if (!_marqueeActive) return;

            if (e.type == EventType.MouseDrag && e.button == 0)
            {
                SceneView.RepaintAll();
                e.Use();
                return;
            }

            if (e.type == EventType.MouseUp && e.button == 0)
            {
                Rect r = MarqueeRect();
                int mode = _marqueeMode;
                _marqueeActive = false;
                _marqueeMode = 0;
                if (r.width < 3f && r.height < 3f)
                {
                    // No drag — interpret as a click. Modifier-click actions (Shift = insert
                    // point) fire here so they don't pre-empt a Shift+drag marquee.
                    if (onClickWithoutDrag != null)
                        onClickWithoutDrag(mode, e.mousePosition);
                    if (mode == 0) selection.Clear();
                }
                else
                {
                    var hits = hitsInRect();
                    if (mode == 0) { selection.Clear(); foreach (var h in hits) selection.Add(h); }
                    else if (mode == 1) foreach (var h in hits) selection.Add(h);
                    else if (mode == 2) foreach (var h in hits) selection.Remove(h);
                }
                SceneView.RepaintAll();
                e.Use();
                return;
            }

            if (e.type == EventType.Repaint)
            {
                Rect r = MarqueeRect();
                Handles.BeginGUI();
                var fill = new Color(0.3f, 0.7f, 1f, 0.18f);
                var border = new Color(0.4f, 0.85f, 1f, 0.9f);
                EditorGUI.DrawRect(r, fill);
                // 1px border (top/bottom/left/right)
                EditorGUI.DrawRect(new Rect(r.xMin, r.yMin, r.width, 1f), border);
                EditorGUI.DrawRect(new Rect(r.xMin, r.yMax - 1f, r.width, 1f), border);
                EditorGUI.DrawRect(new Rect(r.xMin, r.yMin, 1f, r.height), border);
                EditorGUI.DrawRect(new Rect(r.xMax - 1f, r.yMin, 1f, r.height), border);
                Handles.EndGUI();
            }
        }

        Rect MarqueeRect()
        {
            Vector2 a = _marqueeStartGUI;
            Vector2 b = Event.current != null ? Event.current.mousePosition : a;
            return new Rect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                            Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
        }

        // ──────────────── Shape templates ────────────────

        void DrawTemplatesSection(TerrainShape2D src)
        {
            EditorGUILayout.Space();
            _templatesFoldout = EditorGUILayout.Foldout(_templatesFoldout, "Shape templates (overwrite current)", true, EditorStyles.foldoutHeader);
            if (!_templatesFoldout) return;

            _circleRadius = EditorGUILayout.Slider(new GUIContent("Circle radius", "Radius in local units."), _circleRadius, 0.1f, 10f);
            _circlePoints = EditorGUILayout.IntSlider(new GUIContent("Circle points", "Number of vertices on the circle outline. Higher = smoother but more triangles."), _circlePoints, 3, 128);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Make Circle"))
            {
                if (EditorUtility.DisplayDialog("Replace shape with circle?",
                    $"Wipes all layers / interior / forced edges and creates a circle ({_circlePoints} pts, r={_circleRadius:0.##}). Continue?",
                    "Replace", "Cancel"))
                {
                    MakeCircleShape(src, _circleRadius, _circlePoints);
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        void MakeCircleShape(TerrainShape2D src, float radius, int pointCount)
        {
            Undo.RecordObject(src, "Make Circle");
            src.layers.Clear();
            src.interior.Clear();
            src.forcedEdges.Clear();
            _selectedOutlineIds.Clear();
            _selectedInteriorIds.Clear();

            int n = Mathf.Max(3, pointCount);
            var front = new OutlineLayer { name = "Front", z = 0f };
            for (int i = 0; i < n; i++)
            {
                // -π/2 start so the first vertex is at the bottom (matches the rectangle Reset's
                // bottom-left starting feel — useful when comparing the two templates side-by-side).
                float a = -Mathf.PI * 0.5f + (i / (float)n) * Mathf.PI * 2f;
                front.outline.Add(new OutlinePoint
                {
                    id = src.AllocateId(),
                    pos = new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius),
                });
            }
            src.layers.Add(front);
            src.AddLayerCopy(0, 0.5f);
            if (src.layers.Count > 1) src.layers[1].name = "Back";

            _activeLayer = 0;
            EditorUtility.SetDirty(src);
            MarkDirty();
            SceneView.RepaintAll();
        }

        // ──────────────── Vertex transform (rotation) ────────────────

        void DrawTransformVerticesSection(TerrainShape2D src)
        {
            EditorGUILayout.Space();
            _transformFoldout = EditorGUILayout.Foldout(_transformFoldout, "Rotate vertices (without touching object Transform)", true, EditorStyles.foldoutHeader);
            if (!_transformFoldout) return;

            int selectionTotal = _selectedOutlineIds.Count + _selectedInteriorIds.Count;
            EditorGUILayout.LabelField($"Selection: {_selectedOutlineIds.Count} outline + {_selectedInteriorIds.Count} interior = {selectionTotal} vertex(es)", EditorStyles.miniLabel);

            _rotatePivotMode = EditorGUILayout.Popup(
                new GUIContent("Pivot",
                    "Auto: rotates the current selection around its centroid when 2+ vertices are selected; otherwise rotates everything around the polygon centroid.\n" +
                    "All-vertices centroid: ignores selection — rotates every outline+interior vertex around the polygon centroid.\n" +
                    "Origin: rotates around (0,0) in local space, regardless of selection."),
                _rotatePivotMode,
                new[] { "Auto (selection if 2+, else all)", "All-vertices centroid", "Origin (0,0)" });

            _rotateAngle = EditorGUILayout.FloatField(new GUIContent("Angle (°)", "Positive = CCW. Negative = CW."), _rotateAngle);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply")) RotateVertices(src, _rotateAngle);
            if (GUILayout.Button("-90°", GUILayout.Width(50))) RotateVertices(src, -90f);
            if (GUILayout.Button("+90°", GUILayout.Width(50))) RotateVertices(src, 90f);
            if (GUILayout.Button("180°", GUILayout.Width(50))) RotateVertices(src, 180f);
            EditorGUILayout.EndHorizontal();
        }

        void RotateVertices(TerrainShape2D src, float deg)
        {
            // Decide which vertices to rotate, and the pivot.
            int selectionTotal = _selectedOutlineIds.Count + _selectedInteriorIds.Count;
            bool partial = (_rotatePivotMode == 0) && selectionTotal >= 2;

            Vector2 pivot;
            if (_rotatePivotMode == 2)
            {
                pivot = Vector2.zero;
            }
            else if (partial)
            {
                pivot = ComputeSelectionCentroid(src);
            }
            else
            {
                pivot = ComputeAllVerticesCentroid(src);
            }

            float rad = deg * Mathf.Deg2Rad;
            float cs = Mathf.Cos(rad), sn = Mathf.Sin(rad);
            Vector2 Rot(Vector2 v) => new Vector2(v.x * cs - v.y * sn, v.x * sn + v.y * cs);

            Undo.RecordObject(src, partial ? "Rotate selected vertices" : "Rotate all vertices");

            // Outline points + tangents. Apply across ALL layers — outline IDs map across layers,
            // and the same index/id must keep the same shape so the side walls don't shear.
            for (int li = 0; li < src.layers.Count; li++)
            {
                foreach (var op in src.layers[li].outline)
                {
                    if (partial && !_selectedOutlineIds.Contains(op.id)) continue;
                    op.pos = pivot + Rot(op.pos - pivot);
                    if (op.smooth)
                    {
                        // Tangents are direction vectors — rotate without translating by the pivot.
                        op.tangentIn = Rot(op.tangentIn);
                        op.tangentOut = Rot(op.tangentOut);
                    }
                }
            }

            // Interior points. Rotate XY; preserve effective Z (the same logic as drag-move).
            foreach (var ip in src.interior)
            {
                if (partial && !_selectedInteriorIds.Contains(ip.id)) continue;
                Vector2 oldXY = new Vector2(ip.pos.x, ip.pos.y);
                Vector2 newXY = pivot + Rot(oldXY - pivot);
                if (src.useHeightmap && src.HasHeightmap)
                {
                    float oldEffZ = src.GetEffectiveInteriorPos(ip).z;
                    float newBaseZ = src.SampleHeightmapZ(newXY);
                    ip.pos = new Vector3(newXY.x, newXY.y, ip.pos.z);
                    ip.zOffset = oldEffZ - newBaseZ;
                }
                else
                {
                    ip.pos = new Vector3(newXY.x, newXY.y, ip.pos.z);
                }
            }

            EditorUtility.SetDirty(src);
            MarkDirty();
            SceneView.RepaintAll();
        }

        Vector2 ComputeSelectionCentroid(TerrainShape2D src)
        {
            Vector2 sum = Vector2.zero;
            int count = 0;
            // Use only the FRONT layer's outline points for the centroid — every layer shares the
            // same XY ID-set, so picking one layer is enough and avoids double-counting.
            if (src.FrontLayer != null)
            {
                foreach (var op in src.FrontLayer.outline)
                {
                    if (!_selectedOutlineIds.Contains(op.id)) continue;
                    sum += op.pos; count++;
                }
            }
            foreach (var ip in src.interior)
            {
                if (!_selectedInteriorIds.Contains(ip.id)) continue;
                sum += new Vector2(ip.pos.x, ip.pos.y);
                count++;
            }
            return count > 0 ? sum / count : ComputeAllVerticesCentroid(src);
        }

        Vector2 ComputeAllVerticesCentroid(TerrainShape2D src)
        {
            Vector2 sum = Vector2.zero;
            int count = 0;
            if (src.FrontLayer != null)
            {
                foreach (var op in src.FrontLayer.outline) { sum += op.pos; count++; }
            }
            foreach (var ip in src.interior)
            {
                sum += new Vector2(ip.pos.x, ip.pos.y); count++;
            }
            return count > 0 ? sum / count : Vector2.zero;
        }

        // ──────────────── Misc ────────────────

        void ResetShape(TerrainShape2D src)
        {
            Undo.RecordObject(src, "Reset TerrainShape");
            src.layers.Clear();
            src.interior.Clear();
            src.forcedEdges.Clear();
            _selectedOutlineIds.Clear();
            _selectedInteriorIds.Clear();

            var front = new OutlineLayer { name = "Front", z = 0f };
            front.outline.Add(new OutlinePoint { id = src.AllocateId(), pos = new Vector2(-1f, -1f) });
            front.outline.Add(new OutlinePoint { id = src.AllocateId(), pos = new Vector2(1f, -1f) });
            front.outline.Add(new OutlinePoint { id = src.AllocateId(), pos = new Vector2(1f, 1f) });
            front.outline.Add(new OutlinePoint { id = src.AllocateId(), pos = new Vector2(-1f, 1f) });
            src.layers.Add(front);
            src.AddLayerCopy(0, 0.5f);
            if (src.layers.Count > 1) src.layers[1].name = "Back";

            _activeLayer = 0;
            EditorUtility.SetDirty(src);
            MarkDirty();
            SceneView.RepaintAll();
        }

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

        Mesh SaveMeshAssetCore(TerrainShape2D src)
        {
            if (_cache.mesh == null) return null;
            const string folder = "Assets/GeneratedMeshes";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets", "GeneratedMeshes");
            var meshCopy = Object.Instantiate(_cache.mesh);
            meshCopy.name = $"TerrainShape_{src.name}";
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{meshCopy.name}.asset");
            AssetDatabase.CreateAsset(meshCopy, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"[TerrainShape2D] saved {path}");
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        void SaveMeshAsset(TerrainShape2D src)
        {
            var asset = SaveMeshAssetCore(src);
            if (asset == null) return;
            var mf = src.GetComponent<MeshFilter>();
            if (mf != null) mf.sharedMesh = asset;
            EditorGUIUtility.PingObject(asset);
        }

        void SaveAndInstall(TerrainShape2D src)
        {
            var asset = SaveMeshAssetCore(src);
            if (asset == null) return;

            var go = new GameObject(asset.name);
            Undo.RegisterCreatedObjectUndo(go, "Install terrain mesh");
            go.transform.SetParent(src.transform.parent, worldPositionStays: false);
            go.transform.position = src.transform.position;
            go.transform.rotation = src.transform.rotation;
            go.transform.localScale = src.transform.localScale;

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = asset;
            var mr = go.AddComponent<MeshRenderer>();
            if (src.installMaterial != null)
                mr.sharedMaterial = src.installMaterial;
            else
                Debug.LogWarning($"[TerrainShape2D] '{go.name}' installed with no material — assign 'Install material' on the source.");

            Selection.activeGameObject = go;

            if (src.installAutoGenerateCollider2D)
            {
                // MeshToCollider2DGenerator.Generate() reads Selection.activeGameObject and
                // slices the mesh at world Z=0. We just selected `go`, so it picks it up.
                var col = SlimeJumping.EditorTools.MeshToCollider2DGenerator.Generate();
                if (col != null)
                {
                    // Generate() reparents `go` under a new wrapper and selects the wrapper —
                    // ping that instead so the user lands on the final hierarchy result.
                    EditorGUIUtility.PingObject(col.gameObject);
                    return;
                }
            }

            EditorGUIUtility.PingObject(go);
        }
    }
}
