using UnityEngine;

namespace SlimeJumping.Interaction
{
    [CreateAssetMenu(menuName = "SlimeJumping/Memo Profile", fileName = "MemoProfile")]
    public class MemoProfile : ScriptableObject
    {
        [Header("Layout")]
        [Tooltip("Prefab variant of MemoPanel_Base that defines panel size, slot positions, fonts.")]
        public GameObject layoutPrefab;

        [Header("Background (optional overrides)")]
        [Tooltip("If set, replaces the prefab's background sprite. Leave null to keep the prefab's default.")]
        public Sprite backgroundSprite;
        [Tooltip("Multiplicative tint on top of the prefab's default background color. White = keep prefab color.")]
        public Color backgroundTint = Color.white;

        [Header("Content (any subset)")]
        public string titleText;
        [TextArea(3, 10)] public string bodyText;
        public Sprite illustration;

        [Header("Fade (uses Time.unscaledDeltaTime)")]
        [Min(0f)] public float fadeInDuration = 0.4f;
        [Min(0f)] public float fadeOutDuration = 0.3f;

        [Header("Parallax Follow")]
        [Tooltip("Fraction of (slime - anchor) vector applied as offset. 0 = no follow, 0.1 ~ subtle.")]
        [Range(0f, 0.5f)] public float followStrength = 0.1f;
        [Tooltip("Max world-space offset magnitude in meters.")]
        [Min(0f)] public float maxFollowOffset = 0.4f;
        [Tooltip("Higher = snappier; lower = smoother. Also uses unscaled time.")]
        [Min(0.01f)] public float followSmoothing = 8f;
    }
}
