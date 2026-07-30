using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    /// <summary>Sequence authoring for LightningStrikeField: list order = fire order, per-row gap, absolute times shown inline and in the scene.</summary>
    [CustomEditor(typeof(LightningStrikeField))]
    public class LightningStrikeFieldEditor : UnityEditor.Editor
    {
        private SerializedProperty config, hitShakeEmitter, startDelay, repeat, cycleGap, strikes;
        private float uniformGap = 0.3f;

        private static readonly GUILayoutOption W20 = GUILayout.Width(22f);

        void OnEnable()
        {
            config = serializedObject.FindProperty("config");
            hitShakeEmitter = serializedObject.FindProperty("hitShakeEmitter");
            startDelay = serializedObject.FindProperty("startDelay");
            repeat = serializedObject.FindProperty("repeat");
            cycleGap = serializedObject.FindProperty("cycleGap");
            strikes = serializedObject.FindProperty("strikes");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(config);
            EditorGUILayout.PropertyField(hitShakeEmitter);
            EditorGUILayout.Space(4f);
            EditorGUILayout.PropertyField(startDelay);
            EditorGUILayout.PropertyField(repeat);
            using (new EditorGUI.DisabledScope(!repeat.boolValue))
                EditorGUILayout.PropertyField(cycleGap);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Sequence (list order = fire order)", EditorStyles.boldLabel);

            int moveFrom = -1, moveTo = -1, deleteAt = -1;
            float t = startDelay.floatValue;

            for (int i = 0; i < strikes.arraySize; i++)
            {
                var element = strikes.GetArrayElementAtIndex(i);
                var anchor = element.FindPropertyRelative("anchor");
                var gap = element.FindPropertyRelative("gap");
                t += Mathf.Max(0f, gap.floatValue);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"#{i}", GUILayout.Width(24f));
                    EditorGUILayout.LabelField($"t={t:0.00}s", GUILayout.Width(58f));
                    EditorGUILayout.PropertyField(anchor, GUIContent.none);
                    EditorGUILayout.LabelField("+", GUILayout.Width(10f));
                    EditorGUILayout.PropertyField(gap, GUIContent.none, GUILayout.Width(42f));

                    using (new EditorGUI.DisabledScope(i == 0))
                        if (GUILayout.Button("▲", W20)) { moveFrom = i; moveTo = i - 1; }
                    using (new EditorGUI.DisabledScope(i == strikes.arraySize - 1))
                        if (GUILayout.Button("▼", W20)) { moveFrom = i; moveTo = i + 1; }
                    if (GUILayout.Button("✕", W20)) deleteAt = i;
                }
            }

            if (strikes.arraySize == 0)
                EditorGUILayout.HelpBox("No strikes. Add one below — leave its anchor empty to fire from this object.", MessageType.Info);
            else
                EditorGUILayout.LabelField(repeat.boolValue
                    ? $"Cycle length {(t - startDelay.floatValue) + Mathf.Max(0.02f, cycleGap.floatValue):0.00}s  ({strikes.arraySize} bolts, last at t={t:0.00}s)"
                    : $"One-shot, last bolt at t={t:0.00}s", EditorStyles.miniLabel);

            var cfg = ((LightningStrikeField)target).Config;
            if (cfg != null && strikes.arraySize > 0)
                EditorGUILayout.LabelField(
                    $"Each strike = {Mathf.Max(1, cfg.boltsPerStrike)} bolts over ~{Mathf.Max(0f, cfg.boltStagger) * Mathf.Max(0, cfg.boltsPerStrike - 1):0.00}s (config)",
                    EditorStyles.miniLabel);

            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ Add Strike")) AddStrike();
                uniformGap = EditorGUILayout.FloatField(uniformGap, GUILayout.Width(42f));
                if (GUILayout.Button("Apply Gap To All")) ApplyUniformGap();
            }

            if (moveFrom >= 0) strikes.MoveArrayElement(moveFrom, moveTo);
            if (deleteAt >= 0) strikes.DeleteArrayElementAtIndex(deleteAt);

            serializedObject.ApplyModifiedProperties();
        }

        // New anchor is a child GO offset from the last one, so a row is usable the moment it appears.
        private void AddStrike()
        {
            var field = (LightningStrikeField)target;
            int n = strikes.arraySize;

            Vector3 local = new Vector3(0f, 10f, 0f);
            if (n > 0)
            {
                var prev = strikes.GetArrayElementAtIndex(n - 1).FindPropertyRelative("anchor").objectReferenceValue as Transform;
                if (prev != null && prev.parent == field.transform) local = prev.localPosition + new Vector3(4f, 0f, 0f);
            }

            var go = new GameObject($"Anchor_{n}");
            Undo.RegisterCreatedObjectUndo(go, "Add Lightning Strike");
            go.transform.SetParent(field.transform, false);
            go.transform.localPosition = local;

            strikes.arraySize = n + 1;
            var added = strikes.GetArrayElementAtIndex(n);
            added.FindPropertyRelative("anchor").objectReferenceValue = go.transform;
            added.FindPropertyRelative("gap").floatValue = n == 0 ? 0f : uniformGap;
        }

        private void ApplyUniformGap()
        {
            for (int i = 0; i < strikes.arraySize; i++)
                strikes.GetArrayElementAtIndex(i).FindPropertyRelative("gap").floatValue = i == 0 ? 0f : uniformGap;
        }

        void OnSceneGUI()
        {
            var field = (LightningStrikeField)target;
            if (field.Config == null) return;

            var style = new GUIStyle(EditorStyles.boldLabel);
            style.normal.textColor = new Color(0.8f, 0.95f, 1f);

            for (int i = 0; i < field.Strikes.Count; i++)
            {
                Vector3 origin = field.OriginAt(i);
                Handles.Label(origin + Vector3.up * 0.6f, $"#{i}  t={field.FireTimeAt(i):0.00}s", style);
            }
        }
    }
}
