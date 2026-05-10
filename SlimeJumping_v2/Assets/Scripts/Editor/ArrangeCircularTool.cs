using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SlimeJumping.EditorTools
{
    public class ArrangeCircularTool : EditorWindow
    {
        Vector2 center = Vector2.zero;
        float radius = 3f;
        float startDeg = 0f;
        float endDeg = 270f;
        bool faceOutward = true;
        float rotationOffset = 0f;
        bool reverseOrder = false;
        bool autoCenterFromBounds = false;
        bool livePreview = true;

        readonly Dictionary<Transform, TRS> originals = new Dictionary<Transform, TRS>();
        Transform[] cachedSelection;

        struct TRS { public Vector3 pos; public Quaternion rot; }

        [MenuItem("Tools/Arrange Circular")]
        static void Open() => GetWindow<ArrangeCircularTool>("Arrange Circular");

        void OnEnable()
        {
            Selection.selectionChanged += OnSelectionChanged;
            CaptureSelection();
        }

        void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            RestoreOriginals();
        }

        void OnSelectionChanged()
        {
            RestoreOriginals();
            CaptureSelection();
            if (livePreview) ApplyPreview();
            Repaint();
        }

        void CaptureSelection()
        {
            originals.Clear();
            cachedSelection = Selection.gameObjects
                .Where(g => g != null && g.transform != null)
                .Select(g => g.transform)
                .ToArray();
            foreach (var t in cachedSelection)
                originals[t] = new TRS { pos = t.position, rot = t.rotation };
        }

        void RestoreOriginals()
        {
            if (cachedSelection == null) return;
            foreach (var t in cachedSelection)
            {
                if (t == null) continue;
                if (!originals.TryGetValue(t, out var trs)) continue;
                t.position = trs.pos;
                t.rotation = trs.rot;
            }
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField($"Selection: {(cachedSelection?.Length ?? 0)} objects", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();

            autoCenterFromBounds = EditorGUILayout.Toggle(new GUIContent("Auto Center (Bounds)", "현재 선택의 평균 위치를 중심으로 사용"), autoCenterFromBounds);
            using (new EditorGUI.DisabledScope(autoCenterFromBounds))
                center = EditorGUILayout.Vector2Field("Center", center);

            radius = EditorGUILayout.FloatField("Radius", radius);
            startDeg = EditorGUILayout.FloatField("Start Angle (deg)", startDeg);
            endDeg = EditorGUILayout.FloatField("End Angle (deg)", endDeg);
            EditorGUILayout.MinMaxSlider(ref startDeg, ref endDeg, -360f, 720f);

            faceOutward = EditorGUILayout.Toggle(new GUIContent("Face Outward", "각 오브젝트가 원의 바깥을 향하도록 회전"), faceOutward);
            using (new EditorGUI.DisabledScope(!faceOutward))
                rotationOffset = EditorGUILayout.FloatField("Rotation Offset (deg)", rotationOffset);

            reverseOrder = EditorGUILayout.Toggle("Reverse Order", reverseOrder);
            livePreview = EditorGUILayout.Toggle("Live Preview", livePreview);

            if (EditorGUI.EndChangeCheck() && livePreview)
                ApplyPreview();

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(cachedSelection == null || cachedSelection.Length < 2))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Apply"))
                        ApplyWithUndo();
                    if (GUILayout.Button("Reset"))
                        RestoreOriginals();
                }
            }

            EditorGUILayout.HelpBox(
                "Hierarchy 선택 순서대로 호(arc)에 균등 분포됩니다.\n" +
                "Live Preview 끄면 Apply 눌러야 반영됩니다.\n" +
                "Apply는 Undo 가능, 창을 닫거나 선택을 바꾸면 미리보기는 자동 복원됩니다.",
                MessageType.Info);
        }

        Vector2 ResolveCenter()
        {
            if (!autoCenterFromBounds || cachedSelection == null || cachedSelection.Length == 0)
                return center;
            Vector3 sum = Vector3.zero;
            foreach (var t in cachedSelection)
            {
                if (!originals.TryGetValue(t, out var trs)) continue;
                sum += trs.pos;
            }
            sum /= cachedSelection.Length;
            return new Vector2(sum.x, sum.y);
        }

        void ComputeTransform(int i, int n, Vector2 c, out Vector3 pos, out Quaternion rot, Transform t)
        {
            int idx = reverseOrder ? (n - 1 - i) : i;
            float u = n <= 1 ? 0f : (float)idx / (n - 1);
            float deg = Mathf.Lerp(startDeg, endDeg, u);
            float rad = deg * Mathf.Deg2Rad;
            pos = new Vector3(
                c.x + Mathf.Cos(rad) * radius,
                c.y + Mathf.Sin(rad) * radius,
                originals.TryGetValue(t, out var trs) ? trs.pos.z : t.position.z);
            rot = faceOutward
                ? Quaternion.Euler(0, 0, deg - 90f + rotationOffset)
                : (originals.TryGetValue(t, out var trs2) ? trs2.rot : t.rotation);
        }

        void ApplyPreview()
        {
            if (cachedSelection == null || cachedSelection.Length < 2) return;
            var c = ResolveCenter();
            for (int i = 0; i < cachedSelection.Length; i++)
            {
                var t = cachedSelection[i];
                if (t == null) continue;
                ComputeTransform(i, cachedSelection.Length, c, out var p, out var r, t);
                t.position = p;
                t.rotation = r;
            }
            SceneView.RepaintAll();
        }

        void ApplyWithUndo()
        {
            if (cachedSelection == null || cachedSelection.Length < 2) return;

            RestoreOriginals();

            var objs = cachedSelection.Where(t => t != null).Cast<Object>().ToArray();
            Undo.RecordObjects(objs, "Arrange Circular");

            var c = ResolveCenter();
            for (int i = 0; i < cachedSelection.Length; i++)
            {
                var t = cachedSelection[i];
                if (t == null) continue;
                ComputeTransform(i, cachedSelection.Length, c, out var p, out var r, t);
                t.position = p;
                t.rotation = r;
            }

            CaptureSelection();
            SceneView.RepaintAll();
        }
    }
}
