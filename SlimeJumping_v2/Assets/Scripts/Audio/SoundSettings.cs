using UnityEngine;
using UnityEngine.Audio;

namespace SlimeJumping.Audio
{
    [CreateAssetMenu(fileName = "SoundSettings", menuName = "SlimeJumping/Sound Settings")]
    public class SoundSettings : ScriptableObject
    {
        [Header("Mixer")]
        public AudioMixer mixer;
        public AudioMixerGroup bgmGroup;
        public AudioMixerGroup sfxSlimeGroup;
        public AudioMixerGroup sfxEnvironmentGroup;
        public AudioMixerGroup sfxObjectGroup;
        public AudioMixerGroup uiGroup;

        [Header("Exposed Mixer Parameter Names (dB)")]
        public string masterVolumeParam = "MasterVolume";
        public string bgmVolumeParam = "BGMVolume";
        public string sfxVolumeParam = "SFXVolume";
        public string uiVolumeParam = "UIVolume";

        [Header("Pool")]
        [Tooltip("Number of AudioSources preallocated per category. Soft-steal oldest if exhausted.")]
        [Min(1)] public int poolSizePerCategory = 8;

        [Header("3D Defaults")]
        [Tooltip("Below this distance (world units), 3D sounds play at full volume.")]
        [Min(0f)] public float default3DMinDistance = 3f;
        [Tooltip("Beyond this distance, 3D sounds are fully attenuated.")]
        [Min(0.1f)] public float default3DMaxDistance = 20f;
        public AudioRolloffMode default3DRolloff = AudioRolloffMode.Linear;
    }
}
