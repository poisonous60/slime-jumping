using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SlimeJumping.Mechanisms
{
    /// <summary>Broadcasts a shared clip set to every JumpBall in the scene so balls need no per-instance assignment.</summary>
    [DefaultExecutionOrder(-50)]
    public class JumpBallAudioManager : MonoBehaviour
    {
        [Header("Clips")]
        [Tooltip("Played the moment the slime eats the ball (consume animation start).")]
        [SerializeField] AudioClip consumeClip;
        [Tooltip("Played when the ball begins its respawn (grow-in) animation.")]
        [SerializeField] AudioClip respawnClip;

        [Header("Levels")]
        [Tooltip("Shared volume applied to both one-shots.")]
        [Range(0f, 1f)] [SerializeField] float volume = 1f;

        [Header("Scope")]
        [Tooltip("Include inactive JumpBall objects when applying.")]
        [SerializeField] bool includeInactive = true;
        [Tooltip("Apply automatically on Awake at runtime. If false, call ApplyToAll manually.")]
        [SerializeField] bool applyOnAwake = true;

        void Awake()
        {
            if (applyOnAwake) ApplyToAll();
        }

        [ContextMenu("Apply To All Jump Balls")]
        public void ApplyToAll()
        {
            var balls = FindObjectsByType<JumpBall>(
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            for (int i = 0; i < balls.Length; i++)
            {
                balls[i].ApplyAudioSettings(consumeClip, respawnClip, volume);
#if UNITY_EDITOR
                if (!Application.isPlaying) EditorUtility.SetDirty(balls[i]);
#endif
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[JumpBallAudioManager] Applied audio to {balls.Length} JumpBall instance(s).", this);
#endif
        }
    }
}
