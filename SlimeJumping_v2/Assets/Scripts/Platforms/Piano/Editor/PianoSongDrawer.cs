using UnityEditor;
using UnityEngine;

namespace SlimeJumping.Platforms.Piano.EditorTools
{
    /// Draws one song as a single header row — [x] Name (summary) — so a piece can be toggled out of the rotation without expanding it.
    [CustomPropertyDrawer(typeof(PianoSong))]
    public class PianoSongDrawer : PropertyDrawer
    {
        private const float ToggleWidth = 16f;

        private static readonly string[] Ornaments =
        {
            "rolledChord", "voiceLeading", "pedalPoint", "alberti", "chiptuneArp", "appoggiatura",
            "suspension", "dropTwo", "sustainPedal", "resonance", "glissando",
        };

        // Header tags, index-aligned with Ornaments.
        private static readonly string[] Tags =
        {
            "roll", "vl", "ped.pt", "alberti", "chip", "appog",
            "susp", "drop2", "sustain", "reson", "gliss",
        };

        public override void OnGUI(Rect pos, SerializedProperty prop, GUIContent label)
        {
            var enabledP = prop.FindPropertyRelative("enabled");
            var labelP = prop.FindPropertyRelative("label");
            var clipP = prop.FindPropertyRelative("clip");

            float line = EditorGUIUtility.singleLineHeight;
            var row = new Rect(pos.x, pos.y, pos.width, line);

            var foldRect = new Rect(row.x, row.y, 14f, line);
            var toggleRect = new Rect(row.x + 14f, row.y, ToggleWidth, line);
            var nameRect = new Rect(toggleRect.xMax + 2f, row.y, row.width - 16f - ToggleWidth - 2f, line);

            prop.isExpanded = EditorGUI.Foldout(foldRect, prop.isExpanded, GUIContent.none);

            EditorGUI.BeginChangeCheck();
            bool on = EditorGUI.Toggle(toggleRect, enabledP.boolValue);
            if (EditorGUI.EndChangeCheck()) enabledP.boolValue = on;

            string name = !string.IsNullOrEmpty(labelP.stringValue)
                ? labelP.stringValue
                : (clipP.objectReferenceValue != null ? clipP.objectReferenceValue.name : "(empty)");

            var prev = GUI.color;
            if (!on) GUI.color = new Color(prev.r, prev.g, prev.b, 0.45f);
            EditorGUI.LabelField(nameRect, new GUIContent(name), new GUIContent(Summary(prop)));
            GUI.color = prev;

            if (!prop.isExpanded) return;

            EditorGUI.indentLevel++;
            float y = row.yMax + EditorGUIUtility.standardVerticalSpacing;
            var it = prop.Copy();
            var end = prop.GetEndProperty();
            it.NextVisible(true);
            while (!SerializedProperty.EqualContents(it, end))
            {
                if (it.name != "enabled")
                {
                    float h = EditorGUI.GetPropertyHeight(it, true);
                    EditorGUI.PropertyField(new Rect(pos.x, y, pos.width, h), it, true);
                    y += h + EditorGUIUtility.standardVerticalSpacing;
                }
                if (!it.NextVisible(false)) break;
            }
            EditorGUI.indentLevel--;
        }

        public override float GetPropertyHeight(SerializedProperty prop, GUIContent label)
        {
            float h = EditorGUIUtility.singleLineHeight;
            if (!prop.isExpanded) return h;

            var it = prop.Copy();
            var end = prop.GetEndProperty();
            it.NextVisible(true);
            while (!SerializedProperty.EqualContents(it, end))
            {
                if (it.name != "enabled")
                    h += EditorGUI.GetPropertyHeight(it, true) + EditorGUIUtility.standardVerticalSpacing;
                if (!it.NextVisible(false)) break;
            }
            return h + EditorGUIUtility.standardVerticalSpacing;
        }

        /// "no score" / "auto key — roll sustain gliss". Reads only serialized flags, never the JSON.
        private static string Summary(SerializedProperty prop)
        {
            var scoreP = prop.FindPropertyRelative("scoreJson");
            if (prop.FindPropertyRelative("clip").objectReferenceValue == null) return "no clip";
            if (scoreP.objectReferenceValue == null) return "no score";

            var orn = prop.FindPropertyRelative("ornaments");
            string s = "";
            for (int i = 0; i < Ornaments.Length; i++)
            {
                if (Ornaments[i] == "voiceLeading") continue;   // on everywhere; not a distinguishing gesture
                var p = orn.FindPropertyRelative(Ornaments[i]);
                if (p != null && p.boolValue) s += (s.Length > 0 ? " " : "") + Tags[i];
            }

            int tonic = prop.FindPropertyRelative("tonicMidi").intValue;
            string key = tonic < 0 ? "auto key" : $"tonic {tonic}";
            return s.Length > 0 ? $"{key} — {s}" : key;
        }
    }
}
