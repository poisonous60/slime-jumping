using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SlimeJumping.Platforms
{
    /// <summary>Scene-wide broadcaster of the shared impact VFX list to every DeformablePlatform.
    /// Rest-restore is driven per-group by DeformableRespawnZone, not from here.</summary>
    public class DeformablePlatformActivator : MonoBehaviour
    {
        [Header("Impact VFX (broadcast to all DeformablePlatforms)")]
        [Tooltip("One-shot VFX prefabs spawned on slime impact. A random entry is picked per hit " +
                 "for variety. These overwrite per-platform 'Sand VFX Prefabs' assignments — set " +
                 "once on this manager instead of on each platform.")]
        [SerializeField] GameObject[] impactVFXPrefabs;
        [Tooltip("Include inactive DeformablePlatform objects when broadcasting.")]
        [SerializeField] bool includeInactive = true;
        [Tooltip("Push the VFX prefab list to every platform on Awake. If false, call ApplyToAll() manually.")]
        [SerializeField] bool applyOnAwake = true;

        void Awake()
        {
            if (applyOnAwake) ApplyToAll();
        }

        [ContextMenu("Apply To All Platforms")]
        public void ApplyToAll()
        {
            var platforms = FindObjectsByType<DeformablePlatform>(
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (int i = 0; i < platforms.Length; i++)
            {
                platforms[i].ApplyImpactVFX(impactVFXPrefabs);
#if UNITY_EDITOR
                if (!Application.isPlaying) EditorUtility.SetDirty(platforms[i]);
#endif
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[DeformablePlatformActivator] Applied impact VFX to {platforms.Length} DeformablePlatform instance(s).", this);
#endif
        }
    }
}
