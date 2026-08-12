using UnityEngine;

namespace SlimeJumping.Audio
{
    /// One sound, as a single asset: its clips, how loud and at what pitch it plays, and which bus it
    /// routes to. Call sites name the sound and may scale it, they do not carry its settings — so the
    /// whole sound is readable from one place instead of a clip field plus a float field joined only by
    /// a line of code. Shape follows the standard sound-definition asset (clips + volume/pitch ranges,
    /// plus a bus handle like Master Audio's Bus or Sonity's SoundMix).
    [CreateAssetMenu(fileName = "Sound_", menuName = "SlimeJumping/Sound Definition")]
    public class SoundDef : ScriptableObject
    {
        public enum ClipOrder { Random, Sequential }

        [Tooltip("One clip, or several picked per Clip Order on each play.")]
        public AudioClip[] clips;
        [Tooltip("Sequential cycles the list in order; a bell that alternates strike samples needs it.")]
        public ClipOrder clipOrder = ClipOrder.Random;

        [Tooltip("Bus this sound routes to; SoundSettings maps it to an AudioMixerGroup.")]
        public SoundCategory category = SoundCategory.SFX_Object;

        [Tooltip("x = min, y = max. Equal values mean no randomisation.")]
        public Vector2 volumeRange = Vector2.one;
        [Tooltip("x = min, y = max. Equal values mean no randomisation.")]
        public Vector2 pitchRange = Vector2.one;

        [Tooltip("Minimum seconds between plays; a play inside the window is dropped. A creaky " +
                 "surface retriggering on every quick re-landing sounds broken. 0 = no gate.")]
        [Min(0f)] public float cooldownSeconds = 0f;

        [Header("3D")]
        public bool spatial3D = true;
        [Tooltip("Full volume within this distance. Negative keeps the SoundSettings default.")]
        public float minDistance = -1f;
        [Tooltip("Fully attenuated beyond this distance. Negative keeps the SoundSettings default.")]
        public float maxDistance = -1f;

        public bool HasClip
        {
            get
            {
                if (clips == null) return false;
                for (int i = 0; i < clips.Length; i++) if (clips[i] != null) return true;
                return false;
            }
        }

        // Sequential cursor lives on the asset, so objects sharing one SoundDef share the cycle.
        int nextClip;

        /// Null array entries are skipped, so a sparsely filled list never plays silence.
        public AudioClip PickClip()
        {
            if (clips == null || clips.Length == 0) return null;

            if (clipOrder == ClipOrder.Sequential)
            {
                for (int i = 0; i < clips.Length; i++)
                {
                    var clip = clips[nextClip % clips.Length];
                    nextClip = (nextClip + 1) % clips.Length;
                    if (clip != null) return clip;
                }
                return null;
            }

            int start = Random.Range(0, clips.Length);
            for (int i = 0; i < clips.Length; i++)
            {
                var clip = clips[(start + i) % clips.Length];
                if (clip != null) return clip;
            }
            return null;
        }

        public float PickVolume() => Random.Range(volumeRange.x, volumeRange.y);

        public float PickPitch() => Random.Range(pitchRange.x, pitchRange.y);
    }
}
