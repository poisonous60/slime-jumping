using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// Broadcasts a shared audio clip set to every Snowball in the active scene so
    /// individual snowballs / spawner-managed prefabs don't need per-instance assignment.
    /// Mirrors JumpBallAudioManager / IcicleAudioManager.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class SnowballAudioManager : MonoBehaviour
    {
        [Header("Clips")]
        [Tooltip("Looping clip while a Rolling snowball is Active.")]
        [SerializeField] AudioClip rollingClip;
        [Tooltip("Looping clip while a Flying snowball is Active.")]
        [SerializeField] AudioClip flyingClip;
        [Tooltip("One-shot played at EnterCrumbling.")]
        [SerializeField] AudioClip breakClip;

        [Header("Levels")]
        [Range(0f, 1f)] [SerializeField] float volume = 1f;

        [Header("Scope")]
        [SerializeField] bool includeInactive = true;
        [SerializeField] bool applyOnAwake = true;

        void Awake()
        {
            if (applyOnAwake) ApplyToAll();
        }

        [ContextMenu("Apply To All Snowballs")]
        public void ApplyToAll()
        {
            var snowballs = FindObjectsByType<Snowball>(
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            for (int i = 0; i < snowballs.Length; i++)
            {
                snowballs[i].ApplyAudioSettings(rollingClip, flyingClip, breakClip, volume);
#if UNITY_EDITOR
                if (!Application.isPlaying) EditorUtility.SetDirty(snowballs[i]);
#endif
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[SnowballAudioManager] Applied audio to {snowballs.Length} Snowball instance(s).", this);
#endif
        }
    }
}
