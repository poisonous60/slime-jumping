using UnityEngine;
using UnityEditor;
using SlimeJumping.Platforms;

namespace SlimeJumping.Editor
{
    // 레인 인스펙터 하나에서 밴드 config(공유 asset)와 자식 구름들의 속도를 같이 편집.
    // 속도는 구름 컴포넌트 소유고 여기선 모아 보여줄 뿐이다.
    [CustomEditor(typeof(DriftingCloudLane))]
    public class DriftingCloudLaneEditor : UnityEditor.Editor
    {
        UnityEditor.Editor cfgEditor;

        void OnDisable()
        {
            if (cfgEditor != null) DestroyImmediate(cfgEditor);
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var lane = (DriftingCloudLane)target;
            var cfg = lane.Cfg;
            if (cfg == null) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(AssetDatabase.GetAssetPath(cfg), EditorStyles.miniLabel);

            CreateCachedEditor(cfg, null, ref cfgEditor);
            EditorGUI.indentLevel++;
            cfgEditor.OnInspectorGUI();
            EditorGUI.indentLevel--;

            DrawCloudSpeeds(lane);
        }

        static void DrawCloudSpeeds(DriftingCloudLane lane)
        {
            var clouds = lane.GetComponentsInChildren<DriftingCloudPlatform>();
            if (clouds.Length == 0) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("구름 속도 (units/s)", EditorStyles.boldLabel);

            foreach (var cloud in clouds)
            {
                var so = new SerializedObject(cloud);
                var p = so.FindProperty("speed");
                EditorGUILayout.PropertyField(p, new GUIContent(cloud.name));
                so.ApplyModifiedProperties();
            }
        }
    }
}
