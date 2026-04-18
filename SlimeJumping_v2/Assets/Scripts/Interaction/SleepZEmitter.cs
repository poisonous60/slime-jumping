using TMPro;
using UnityEngine;

namespace SlimeJumping.Interaction
{
    // Spawns SleepZParticle instances at a regular interval while IsEmitting is true.
    // Driven by BoxSleepInteractable, which polls BoxSleepAnimator.IsSleeping.
    //
    // Each Z is created at runtime via TextMeshPro — no prefab authoring needed.
    public class SleepZEmitter : MonoBehaviour
    {
        public float spawnInterval = 0.9f;
        public Vector3 spawnLocalOffset = new Vector3(0.15f, 0.6f, -0.5f);
        public Color zColor = new Color(0.7f, 0.85f, 1f, 1f);
        public float fontSize = 4f;
        public string text = "Z";

        public bool IsEmitting { get; set; }

        private float _spawnTimer;

        public void Configure(BoxSleepProfile profile)
        {
            if (profile == null) return;
            spawnInterval = profile.zSpawnInterval;
            spawnLocalOffset = profile.zSpawnLocalOffset;
            zColor = profile.zColor;
            fontSize = profile.zFontSize;
        }

        private void Update()
        {
            if (!IsEmitting) { _spawnTimer = 0f; return; }
            _spawnTimer -= Time.deltaTime;
            if (_spawnTimer > 0f) return;
            _spawnTimer = spawnInterval;
            SpawnZ();
        }

        private void SpawnZ()
        {
            var go = new GameObject("SleepZ");
            go.transform.position = transform.TransformPoint(spawnLocalOffset);

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = zColor;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Bold;

            go.AddComponent<SleepZParticle>();
        }
    }
}
