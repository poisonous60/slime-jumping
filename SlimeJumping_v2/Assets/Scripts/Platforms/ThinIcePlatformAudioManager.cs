using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SlimeJumping.Platforms
{
    /// <summary>
    /// Broadcasts a shared audio clip set to every ThinIcePlatform in the active
    /// scene so individual tiles don't need per-instance inspector assignment.
    /// Mirrors the WaterZoneAudioManager pattern.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class ThinIcePlatformAudioManager : MonoBehaviour
    {
        [Header("Clips")]
        [Tooltip("Played when the slime first attaches — the ice starts to crack.")]
        [SerializeField] AudioClip crackClip;
        [Tooltip("Played at the moment the tile shatters and disappears.")]
        [SerializeField] AudioClip shatterClip;
        [Tooltip("Played when the tile begins its regen fade-in.")]
        [SerializeField] AudioClip regenClip;

        [Header("Levels")]
        [Tooltip("Shared volume applied to all three one-shots.")]
        [Range(0f, 1f)] [SerializeField] float volume = 1f;

        [Header("Scope")]
        [Tooltip("Include inactive ThinIcePlatform objects when applying.")]
        [SerializeField] bool includeInactive = true;
        [Tooltip("Apply automatically on Awake at runtime. If false, call ApplyToAll manually.")]
        [SerializeField] bool applyOnAwake = true;

        void Awake()
        {
            if (applyOnAwake) ApplyToAll();
        }

        [ContextMenu("Apply To All Thin Ice Platforms")]
        public void ApplyToAll()
        {
            var tiles = FindObjectsByType<ThinIcePlatform>(
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            for (int i = 0; i < tiles.Length; i++)
            {
                tiles[i].ApplyAudioSettings(crackClip, shatterClip, regenClip, volume);
#if UNITY_EDITOR
                if (!Application.isPlaying) EditorUtility.SetDirty(tiles[i]);
#endif
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[ThinIcePlatformAudioManager] Applied audio to {tiles.Length} ThinIcePlatform instance(s).", this);
#endif
        }
    }
}
