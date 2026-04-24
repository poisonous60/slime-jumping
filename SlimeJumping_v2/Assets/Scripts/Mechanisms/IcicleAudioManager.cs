using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// Broadcasts a shared audio clip set to every IcicleHazard in the active
    /// scene so individual icicles don't need per-instance inspector assignment.
    /// Mirrors the WaterZoneAudioManager / ThinIcePlatformAudioManager pattern.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class IcicleAudioManager : MonoBehaviour
    {
        [Header("Clips")]
        [Tooltip("Played when the slime enters the detection box and the icicle begins to shake.")]
        [SerializeField] AudioClip shakeStartClip;
        [Tooltip("Played at the moment the icicle begins to fall.")]
        [SerializeField] AudioClip fallingClip;
        [Tooltip("Played when the falling icicle hits the ground and breaks.")]
        [SerializeField] AudioClip impactClip;

        [Header("Levels")]
        [Tooltip("Shared volume applied to all three one-shots.")]
        [Range(0f, 1f)] [SerializeField] float volume = 1f;

        [Header("Scope")]
        [Tooltip("Include inactive IcicleHazard objects when applying.")]
        [SerializeField] bool includeInactive = true;
        [Tooltip("Apply automatically on Awake at runtime. If false, call ApplyToAll manually.")]
        [SerializeField] bool applyOnAwake = true;

        void Awake()
        {
            if (applyOnAwake) ApplyToAll();
        }

        [ContextMenu("Apply To All Icicles")]
        public void ApplyToAll()
        {
            var icicles = FindObjectsByType<IcicleHazard>(
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            for (int i = 0; i < icicles.Length; i++)
            {
                icicles[i].ApplyAudioSettings(shakeStartClip, fallingClip, impactClip, volume);
#if UNITY_EDITOR
                if (!Application.isPlaying) EditorUtility.SetDirty(icicles[i]);
#endif
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[IcicleAudioManager] Applied audio to {icicles.Length} IcicleHazard instance(s).", this);
#endif
        }
    }
}
