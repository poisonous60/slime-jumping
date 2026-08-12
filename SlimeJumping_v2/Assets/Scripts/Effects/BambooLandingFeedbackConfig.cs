using UnityEngine;
using SlimeJumping.Audio;

namespace SlimeJumping.Effects
{
    /// Shared BambooLandingFeedback tuning; canonical asset <c>Assets/Resources/BambooLandingFeedbackConfig.asset</c>
    /// (auto-loaded). Per-stalk variants go through the component's Config Override field.
    [CreateAssetMenu(fileName = "BambooLandingFeedbackConfig",
        menuName = "SlimeJumping/Bamboo Landing Feedback Config", order = 0)]
    public sealed class BambooLandingFeedbackConfig : ScriptableObject
    {
        [Header("Sound")]
        [Tooltip("Bend creak. One clip — pitch carries the bend depth, so a hard landing needs no second one.")]
        public SoundDef bendSound;
        [Tooltip("Volume at Min Bend Deg.")]
        [Range(0f, 1f)] public float volumeAtMinBend = 0.35f;
        [Tooltip("Volume at Large Bend Deg.")]
        [Range(0f, 1f)] public float volumeAtLargeBend = 1f;
        [Tooltip("Pitch at Min Bend Deg. Flat by default: pitching a weak landing up brightens it " +
                 "into sounding louder than a hard one, so bend depth rides volume alone.")]
        [Min(0.01f)] public float pitchAtMinBend = 1f;
        [Tooltip("Pitch at Large Bend Deg.")]
        [Min(0.01f)] public float pitchAtLargeBend = 1f;
        [Tooltip("Ramp-down once the stalk stops moving or the rider leaves. Too short and the creak " +
                 "reads as interrupted.")]
        [Min(0f)] public float fadeOutSeconds = 0.25f;
        [Tooltip("Bend speed (deg/s) under which the stalk is barely moving and goes quiet. Raise it " +
                 "to end the creak sooner, lower it to let small motion still sound.")]
        [Min(0f)] public float settleSpeedDegPerSec = 25f;

        [Header("Bend grading")]
        [Tooltip("Predicted bend below this plays nothing — a graze must not creak.")]
        [Min(0f)] public float minBendDeg = 2f;
        [Tooltip("Predicted bend at or above this uses the lowest pitch and the full dust burst.")]
        [Min(0f)] public float largeBendDeg = 20f;

        [Header("Dust")]
        [Tooltip("Puff spawned at the contact point. playOnAwake must be OFF; Play() is called explicitly.")]
        public GameObject dustPuffPrefab;
        [Tooltip("Puffs spawned at Min Bend Deg.")]
        [Min(0)] public int puffsAtMinBend = 1;
        [Tooltip("Puffs spawned at Large Bend Deg.")]
        [Min(0)] public int puffsAtLargeBend = 3;
        [Tooltip("Puff scale at Min Bend Deg.")]
        [Min(0.01f)] public float puffScaleAtMinBend = 0.6f;
        [Tooltip("Puff scale at Large Bend Deg.")]
        [Min(0.01f)] public float puffScaleAtLargeBend = 1.4f;
        [Tooltip("World spread of the extra puffs around the contact point.")]
        [Min(0f)] public float puffSpread = 0.15f;

        const string ResourcesPath = "BambooLandingFeedbackConfig";

        /// Override if assigned, else the Resources asset, else null — callers fall back to class defaults.
        public static BambooLandingFeedbackConfig Resolve(BambooLandingFeedbackConfig @override)
        {
            if (@override != null) return @override;
            return Resources.Load<BambooLandingFeedbackConfig>(ResourcesPath);
        }
    }
}
