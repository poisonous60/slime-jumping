using UnityEngine;
using UnityEditor;

namespace SlimeJumping.Editor
{
    // Keeps the reveal shader's edit-mode alpha floor alive: hidden platforms render at the
    // config's preview alpha while authoring, and drop to fully invisible the moment play
    // starts. Scene-independent — works with or without a BellSoundWave in the scene.
    [InitializeOnLoad]
    public static class BellRevealEditPreview
    {
        static readonly int IdPreviewAlpha = Shader.PropertyToID("_BellWaveEditPreviewAlpha");

        static BellRevealEditPreview()
        {
            EditorApplication.update += Apply;
        }

        static void Apply()
        {
            float alpha = EditorApplication.isPlayingOrWillChangePlaymode
                ? 0f
                : SlimeJumping.Platforms.Bell.BellSoundWaveConfig.Resolve(null).editorPreviewAlpha;
            Shader.SetGlobalFloat(IdPreviewAlpha, alpha);
        }
    }
}
