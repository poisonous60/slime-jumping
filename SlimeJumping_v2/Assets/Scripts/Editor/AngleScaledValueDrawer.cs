using UnityEditor;
using UnityEngine;
using SlimeJumping.Physics;

namespace SlimeJumping.Editor
{
    [CustomPropertyDrawer(typeof(AngleScaledValue))]
    public class AngleScaledValueDrawer : PropertyDrawer
    {
        private const float ToggleWidth = 18f;

        private static GUIStyle s_MiniLabelLeft;
        private static GUIStyle s_MiniLabelRight;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (property.isExpanded)
                return EditorGUIUtility.singleLineHeight * 4 + EditorGUIUtility.standardVerticalSpacing * 3;

            return EditorGUIUtility.singleLineHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EnsureInitialized(property);

            EditorGUI.BeginProperty(position, label, property);

            var baseVal = property.FindPropertyRelative("baseValue");
            var fullVal = property.FindPropertyRelative("fullValue");
            var angleMin = property.FindPropertyRelative("angleMin");
            var angleMax = property.FindPropertyRelative("angleMax");

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            // First line: toggle + label + value
            Rect firstLine = new Rect(position.x, position.y, position.width, lineHeight);

            // Toggle button
            Rect toggleRect = new Rect(firstLine.x, firstLine.y, ToggleWidth, lineHeight);
            string toggleLabel = property.isExpanded ? "\u25BC" : "\u25B6";
            if (GUI.Button(toggleRect, toggleLabel, EditorStyles.miniButton))
            {
                property.isExpanded = !property.isExpanded;
            }

            Rect fieldRect = new Rect(firstLine.x + ToggleWidth + 2f, firstLine.y,
                firstLine.width - ToggleWidth - 2f, lineHeight);

            if (!property.isExpanded)
            {
                // Collapsed: single float field editing baseValue, sync fullValue
                EditorGUI.BeginChangeCheck();
                float newVal = EditorGUI.FloatField(fieldRect, label, baseVal.floatValue);
                if (EditorGUI.EndChangeCheck())
                {
                    baseVal.floatValue = newVal;
                    fullVal.floatValue = newVal;
                }
            }
            else
            {
                // Expanded header
                EditorGUI.LabelField(fieldRect, label);

                // Indent sub-fields
                int indent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = indent + 1;

                float y = position.y + lineHeight + spacing;
                float labelW = EditorGUIUtility.labelWidth;

                // Row 1: baseValue / fullValue
                Rect baseRect = new Rect(position.x, y, position.width * 0.5f, lineHeight);
                Rect fullRect = new Rect(position.x + position.width * 0.5f, y, position.width * 0.5f, lineHeight);
                baseVal.floatValue = EditorGUI.FloatField(baseRect, "Base", baseVal.floatValue);
                fullVal.floatValue = EditorGUI.FloatField(fullRect, "Full", fullVal.floatValue);

                // Row 2: angleMin / angleMax
                y += lineHeight + spacing;
                Rect minRect = new Rect(position.x, y, position.width * 0.5f, lineHeight);
                Rect maxRect = new Rect(position.x + position.width * 0.5f, y, position.width * 0.5f, lineHeight);
                angleMin.floatValue = EditorGUI.FloatField(minRect, "Angle Min", angleMin.floatValue);
                angleMax.floatValue = EditorGUI.FloatField(maxRect, "Angle Max", angleMax.floatValue);

                // Row 3: preview bar
                y += lineHeight + spacing;
                Rect previewRect = new Rect(position.x + labelW * 0.5f, y,
                    position.width - labelW * 0.5f, lineHeight);
                DrawPreviewBar(previewRect, baseVal.floatValue, fullVal.floatValue,
                    angleMin.floatValue, angleMax.floatValue);

                EditorGUI.indentLevel = indent;
            }

            EditorGUI.EndProperty();
        }

        private void EnsureInitialized(SerializedProperty property)
        {
            if (!property.isExpanded)
            {
                var baseVal = property.FindPropertyRelative("baseValue");
                var fullVal = property.FindPropertyRelative("fullValue");
                if (!Mathf.Approximately(baseVal.floatValue, fullVal.floatValue))
                    property.isExpanded = true;
            }
        }

        private void DrawPreviewBar(Rect rect, float baseVal, float fullVal, float angleMin, float angleMax)
        {
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));

            float maxAbsVal = Mathf.Max(Mathf.Abs(baseVal), Mathf.Abs(fullVal), 0.001f);

            int segments = Mathf.Max(1, (int)rect.width);
            float segmentWidth = rect.width / segments;

            for (int i = 0; i < segments; i++)
            {
                float angle = segments > 1 ? (i / (float)(segments - 1)) * 90f : 0f;
                float t = Mathf.InverseLerp(angleMin, angleMax, angle);
                float val = Mathf.Lerp(baseVal, fullVal, t);
                float normalizedVal = Mathf.Abs(val) / maxAbsVal;
                float barHeight = normalizedVal * rect.height;

                Color barColor = val >= 0
                    ? Color.Lerp(new Color(0.2f, 0.5f, 0.8f), new Color(0.3f, 0.8f, 0.4f), t)
                    : Color.Lerp(new Color(0.8f, 0.3f, 0.3f), new Color(0.8f, 0.6f, 0.2f), t);

                Rect barRect = new Rect(
                    rect.x + i * segmentWidth,
                    rect.y + rect.height - barHeight,
                    segmentWidth + 0.5f,
                    barHeight);

                EditorGUI.DrawRect(barRect, barColor);
            }

            // Labels
            if (s_MiniLabelLeft == null)
            {
                s_MiniLabelLeft = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.UpperLeft,
                    normal = { textColor = Color.white }
                };
            }
            if (s_MiniLabelRight == null)
            {
                s_MiniLabelRight = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.UpperRight,
                    normal = { textColor = Color.white }
                };
            }

            EditorGUI.LabelField(rect, "0\u00b0", s_MiniLabelLeft);
            EditorGUI.LabelField(rect, "90\u00b0", s_MiniLabelRight);
        }
    }
}
