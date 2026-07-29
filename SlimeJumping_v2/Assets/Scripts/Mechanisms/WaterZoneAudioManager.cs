using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SlimeJumping.Mechanisms
{
    /// <summary>Pushes one shared clip set onto every WaterZone_v2 in the scene, replacing per-instance assignment.</summary>
    public class WaterZoneAudioManager : MonoBehaviour
    {
        [Header("Clips")]
        [Tooltip("Played on entry when impact speed >= WaterZone_v2.hardEnterThreshold.")]
        [SerializeField] AudioClip hardEnterClip;
        [Tooltip("Played on entry when impact speed < WaterZone_v2.hardEnterThreshold.")]
        [SerializeField] AudioClip softEnterClip;
        [Tooltip("Played once when the slime leaves the water.")]
        [SerializeField] AudioClip exitClip;
        [Tooltip("Looped while the slime is submerged.")]
        [SerializeField] AudioClip underwaterLoopClip;

        [Header("Levels")]
        [Tooltip("Base volume at audioRefSpeed impact for one-shot splashes.")]
        [Range(0f, 1f)] [SerializeField] float splashVolume = 0.9f;
        [Tooltip("±random pitch jitter applied to each one-shot.")]
        [Range(0f, 0.3f)] [SerializeField] float splashPitchJitter = 0.08f;
        [Tooltip("Volume of the looping underwater ambient sound.")]
        [Range(0f, 1f)] [SerializeField] float underwaterLoopVolume = 0.7f;

        [Header("Scope")]
        [Tooltip("Include inactive WaterZone_v2 objects when applying.")]
        [SerializeField] bool includeInactive = true;
        [Tooltip("Apply automatically on Awake at runtime. If false, call ApplyToAll manually.")]
        [SerializeField] bool applyOnAwake = true;

        void Awake()
        {
            if (applyOnAwake) ApplyToAll();
        }

        [ContextMenu("Apply To All Water Zones")]
        public void ApplyToAll()
        {
            var zones = FindObjectsByType<WaterZone_v2>(
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            for (int i = 0; i < zones.Length; i++)
            {
                zones[i].ApplyAudioSettings(hardEnterClip, softEnterClip, exitClip, underwaterLoopClip,
                                            splashVolume, splashPitchJitter, underwaterLoopVolume);
#if UNITY_EDITOR
                if (!Application.isPlaying) EditorUtility.SetDirty(zones[i]);
#endif
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[WaterZoneAudioManager] Applied audio to {zones.Length} WaterZone_v2 instance(s).", this);
#endif
        }
    }
}
