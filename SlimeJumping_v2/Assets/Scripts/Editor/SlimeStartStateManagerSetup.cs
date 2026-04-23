using UnityEngine;
using UnityEditor;
using SlimeJumping.Controller;
using SlimeJumping.Interaction;
using SlimeJumping.UI;

namespace SlimeJumping.Editor
{
    public static class SlimeStartStateManagerSetup
    {
        const string ObjectName = "SlimeStartStateManager";

        [MenuItem("SlimeJumping/Setup Slime Start State Manager")]
        public static void Setup()
        {
            var existing = GameObject.Find(ObjectName);
            if (existing != null)
            {
                Selection.activeGameObject = existing;
                Debug.Log($"[{nameof(SlimeStartStateManagerSetup)}] Manager already exists — selected it.");
                return;
            }

            var go = new GameObject(ObjectName);
            Undo.RegisterCreatedObjectUndo(go, "Create SlimeStartStateManager");

            var mgr = Undo.AddComponent<SlimeStartStateManager>(go);

            var slime = Object.FindFirstObjectByType<SlimeController>();
            var firstBoxSleep = Object.FindFirstObjectByType<BoxSleepInteractable>();

            var so = new SerializedObject(mgr);
            if (slime != null) so.FindProperty("slime").objectReferenceValue = slime;
            if (firstBoxSleep != null) so.FindProperty("startInteraction").objectReferenceValue = firstBoxSleep;
            so.ApplyModifiedProperties();

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = go;

            string wiring = (slime != null ? "slime ✓" : "slime ✗") + ", "
                          + (firstBoxSleep != null ? "BoxSleep ✓" : "startInteraction ✗ (assign manually)");
            Debug.Log($"[{nameof(SlimeStartStateManagerSetup)}] Created '{ObjectName}' — {wiring}. "
                      + "Mode defaults to OnlyFromMenu; change in inspector to Always to test directly from MainScene.");
        }
    }
}
