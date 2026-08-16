using UnityEngine;
using UnityEngine.Video;

namespace SlimeJumping.UI
{
    /// Shared main-menu video backdrop tuning; canonical asset
    /// <c>Assets/Resources/MenuBackgroundVideoConfig.asset</c> (auto-loaded).
    /// Per-scene variants go through the component's Config Override field.
    [CreateAssetMenu(fileName = "MenuBackgroundVideoConfig",
        menuName = "SlimeJumping/Menu Background Video Config", order = 0)]
    public sealed class MenuBackgroundVideoConfig : ScriptableObject
    {
        [Tooltip("Pool the backdrop picks one clip from at random each time the menu loads.")]
        public VideoClip[] clips;

        [Tooltip("Multiplied over the video. White = untouched; darken it when menu text " +
                 "gets hard to read against bright footage.")]
        public Color tint = new Color(0.55f, 0.6f, 0.58f, 1f);

        [Tooltip("Edge darkening layered over the video, 0 = off. Center stays open so the " +
                 "footage reads while the frame keeps the dark title mood.")]
        [Range(0f, 1f)] public float vignetteStrength = 0.6f;

        [Tooltip("Color the vignette darkens toward at full strength.")]
        public Color vignetteColor = new Color(0.01f, 0.04f, 0.03f, 1f);

        [Tooltip("Playback rate of the clip. 1 = as encoded.")]
        [Min(0.1f)] public float playbackSpeed = 1f;

        const string ResourcesPath = "MenuBackgroundVideoConfig";

        /// Override if assigned, else the Resources asset, else null.
        public static MenuBackgroundVideoConfig Resolve(MenuBackgroundVideoConfig @override)
        {
            if (@override != null) return @override;
            return Resources.Load<MenuBackgroundVideoConfig>(ResourcesPath);
        }
    }
}
