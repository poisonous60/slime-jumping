using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SlimeJumping.Mechanisms
{
    /// <summary>Broadcasts a shared clip set to every IcicleHazard in the scene, so icicles need no per-instance assignment.</summary>
    [DefaultExecutionOrder(-50)]
    public class IcicleAudioManager : MonoBehaviour
    {
        [Header("Clips")]
        [Tooltip("Played when the slime enters the detection box and the icicle begins to shake.")]
        [SerializeField] SlimeJumping.Audio.SoundDef shakeStartSound;
        [Tooltip("Played at the moment the icicle begins to fall.")]
        [SerializeField] SlimeJumping.Audio.SoundDef fallingSound;
        [Tooltip("Played when the falling icicle hits the ground and breaks.")]
        [SerializeField] SlimeJumping.Audio.SoundDef impactSound;

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
                icicles[i].ApplyAudioSettings(shakeStartSound, fallingSound, impactSound);
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
