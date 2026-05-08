using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SlimeJumping.TerrainShape.Editor.Drawing
{
    // Tab-key dropdown for picking a Stamp preset by thumbnail + name search. Shown via
    // EditorWindow.ShowAsDropDown so it dismisses cleanly when focus leaves.
    internal class StampPickerPopup : EditorWindow
    {
        // Static channel so the editor's HandleEditorHotkeys can yield 1-9 to the popup while
        // it's visible. Without this the outer 1-6 would keep firing under the popup.
        public static bool IsOpen { get; private set; }

        Action<ShapePreset> _onPick;
        ShapePreset[] _all;
        // Filtered + ordered list as currently shown in the grid. 1-9 hotkey indexes into this.
        readonly List<ShapePreset> _visible = new List<ShapePreset>();
        string _search = "";
        Vector2 _scroll;

        public static void Show(Vector2 mouseScreen, Action<ShapePreset> onPick)
        {
            var w = ScriptableObject.CreateInstance<StampPickerPopup>();
            w._onPick = onPick;
            w.Refresh();
            w.ShowAsDropDown(new Rect(mouseScreen, Vector2.zero), new Vector2(360, 480));
        }

        void OnEnable()  { IsOpen = true; }
        void OnDisable() { IsOpen = false; }

        void Refresh()
        {
            var guids = AssetDatabase.FindAssets("t:ShapePreset");
            _all = new ShapePreset[guids.Length];
            for (int i = 0; i < guids.Length; i++)
                _all[i] = AssetDatabase.LoadAssetAtPath<ShapePreset>(AssetDatabase.GUIDToAssetPath(guids[i]));
        }

        void OnGUI()
        {
            // ── Keyboard ──
            // Esc = close. Alpha1..9 = pick the N-th visible preset.
            // Handle BEFORE the TextField so typing into search doesn't compete for digits.
            // (Note: while a TextField is focused, Unity routes KeyDown to it first, so this
            // intercept only fires when search field is not focused.)
            var ev = Event.current;
            if (ev.type == EventType.KeyDown)
            {
                if (ev.keyCode == KeyCode.Escape)
                {
                    Close();
                    ev.Use();
                    return;
                }
                if (ev.keyCode >= KeyCode.Alpha1 && ev.keyCode <= KeyCode.Alpha9
                    && !EditorGUIUtility.editingTextField)
                {
                    int idx = (int)ev.keyCode - (int)KeyCode.Alpha1;
                    if (idx >= 0 && idx < _visible.Count)
                    {
                        _onPick?.Invoke(_visible[idx]);
                        ev.Use();
                        Close();
                        return;
                    }
                }
            }

            EditorGUILayout.LabelField("Pick a Stamp preset", EditorStyles.boldLabel);
            _search = EditorGUILayout.TextField(_search);

            _visible.Clear();
            if (_all != null)
            {
                foreach (var p in _all)
                {
                    if (p == null) continue;
                    if (!string.IsNullOrEmpty(_search) &&
                        p.name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    _visible.Add(p);
                }
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (_visible.Count == 0)
            {
                EditorGUILayout.HelpBox("No ShapePreset assets found. Save one from the Draw section first.", MessageType.Info);
            }
            else
            {
                const int cols = 3;
                bool inRow = false;
                int placedThisRow = 0;
                for (int i = 0; i < _visible.Count; i++)
                {
                    if (!inRow)
                    {
                        EditorGUILayout.BeginHorizontal();
                        inRow = true;
                        placedThisRow = 0;
                    }

                    DrawCell(_visible[i], i);
                    placedThisRow++;

                    if (placedThisRow >= cols)
                    {
                        EditorGUILayout.EndHorizontal();
                        inRow = false;
                    }
                }
                if (inRow) EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawCell(ShapePreset p, int visibleIndex)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(110));
            var rect = GUILayoutUtility.GetRect(96, 96, GUILayout.Width(96), GUILayout.Height(96));
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f, 1f));
            if (p.thumbnail != null) GUI.DrawTexture(rect, p.thumbnail, ScaleMode.ScaleToFit);
            // Slot number badge top-left for the first 9 — hint that 1-9 hotkeys map to these.
            if (visibleIndex < 9)
            {
                GUI.Label(new Rect(rect.x + 2, rect.y, 20, 14),
                    (visibleIndex + 1).ToString(), EditorStyles.miniBoldLabel);
            }
            EditorGUILayout.LabelField(p.name, EditorStyles.miniLabel, GUILayout.Width(100));
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                _onPick?.Invoke(p);
                Event.current.Use();
                Close();
            }
            EditorGUILayout.EndVertical();
        }
    }
}
