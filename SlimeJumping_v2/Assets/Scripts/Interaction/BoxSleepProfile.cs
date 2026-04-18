using UnityEngine;

namespace SlimeJumping.Interaction
{
    [CreateAssetMenu(menuName = "SlimeJumping/BoxSleep Profile", fileName = "BoxSleepProfile")]
    public class BoxSleepProfile : ScriptableObject
    {
        [Header("Settle (squash to fit box)")]
        public float settleDuration = 0.4f;
        public Vector3 sleepScale = new Vector3(1.3f, 0.55f, 1.3f);
        public float sleepAsymmetry = 0.15f;
        public float sleepDip = -0.3f;
        [Tooltip("OutBack overshoot for the scale settle.")]
        public float sleepOvershoot = 1.4f;
        [Tooltip("Body rotation (Euler XYZ degrees) at the sleep pose. Applied during Settling, held through Sleeping, returned to identity during Waking. Cleared on Released.")]
        public Vector3 sleepEulerAngles = Vector3.zero;
        [Tooltip("Face position offset (XYZ delta from rest) at the sleep pose. Same lifecycle as sleepEulerAngles. Replaces face drift/saccade while active.")]
        public Vector3 sleepFaceOffset = Vector3.zero;

        [Header("Sleeping — same breathing as SitPose Idle")]
        public float breathFrequency = 0.45f;
        public float breathAmplitude = 0.04f;
        [Range(0f, 1f)] public float verticalAnchor = 1f;
        public float amplitudeVariation = 0.015f;

        [Header("Sleep Z effect")]
        public float zSpawnInterval = 0.9f;
        public Vector3 zSpawnLocalOffset = new Vector3(0.15f, 0.6f, -0.5f);
        public Color zColor = new Color(0.7f, 0.85f, 1f, 1f);
        public float zFontSize = 4f;

        [Header("Wake")]
        public Vector3 wakeAnticipationScale = new Vector3(0.85f, 1.25f, 0.85f);
        public float wakeAnticipationDuration = 0.12f;
        public float wakeSettleDuration = 0.35f;
        [Min(1f)] public float wakeSettleElasticity = 1.1f;
        public float wakeSettlePeriod = 0.35f;
    }
}
