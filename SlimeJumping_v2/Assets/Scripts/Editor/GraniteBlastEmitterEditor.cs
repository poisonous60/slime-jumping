using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    // Hides mode-specific Header sections in the inspector so only fields for the
    // currently selected FireMode appear. Detection is purely [Header]-attribute
    // driven via reflection — adding new fields under an existing header needs no
    // change here, and the [Header] decorator still draws naturally because we
    // either render the field (header above it) or skip it entirely.
    [CustomEditor(typeof(GraniteBlastEmitter))]
    public class GraniteBlastEmitterEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var modeProp = serializedObject.FindProperty("mode");
            var mode = (GraniteBlastEmitter.FireMode)(modeProp != null ? modeProp.enumValueIndex : 0);

            Type targetType = target.GetType();
            var iter = serializedObject.GetIterator();
            bool enterChildren = true;
            bool sectionVisible = true;

            while (iter.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (iter.propertyPath == "m_Script")
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.PropertyField(iter);
                    continue;
                }

                FieldInfo field = GetField(targetType, iter.name);
                var header = field?.GetCustomAttribute<HeaderAttribute>();
                if (header != null)
                    sectionVisible = IsSectionVisible(header.header, mode);

                if (sectionVisible)
                    EditorGUILayout.PropertyField(iter, true);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private static FieldInfo GetField(Type t, string name)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo f = null;
            while (t != null && f == null)
            {
                f = t.GetField(name, Flags);
                t = t.BaseType;
            }
            return f;
        }

        private static bool IsSectionVisible(string header, GraniteBlastEmitter.FireMode mode)
        {
            if (string.IsNullOrEmpty(header)) return true;
            if (header == "Branching Mode") return mode == GraniteBlastEmitter.FireMode.Branching;
            if (header == "Beam Mode") return mode == GraniteBlastEmitter.FireMode.Beam;
            if (header.StartsWith("Burst Mode")) return mode == GraniteBlastEmitter.FireMode.Burst;
            return true;
        }
    }
}
