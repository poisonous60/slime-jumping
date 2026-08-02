using UnityEditor;
using UnityEngine;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    // Keeps the SceneView repainting while a LaserHazard is selected so its edit-mode
    // patrol preview gizmo animates.
    [InitializeOnLoad]
    public static class LaserHazardScenePreview
    {
        static LaserHazardScenePreview()
        {
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var go = Selection.activeGameObject;
            if (go != null && go.GetComponentInParent<LaserHazard>() != null)
                SceneView.RepaintAll();
        }
    }
}
