using TMPro;
using UnityEngine;

namespace SlimeJumping.Interaction
{
    // Lifetime + drift + fade for a single Z spawned by SleepZEmitter.
    // Self-destructs at end of life.
    public class SleepZParticle : MonoBehaviour
    {
        public float lifetime = 1.5f;
        public float riseSpeed = 0.4f;
        public float swayAmplitude = 0.1f;
        public float swayFrequency = 2f;

        private float _age;
        private Vector3 _spawnPos;
        private float _phaseSeed;
        private TMP_Text _tmp;
        private SpriteRenderer _sprite;
        private Color _baseColor;

        private void Awake()
        {
            _tmp = GetComponent<TMP_Text>();
            _sprite = GetComponent<SpriteRenderer>();
            if (_tmp != null) _baseColor = _tmp.color;
            else if (_sprite != null) _baseColor = _sprite.color;
            _spawnPos = transform.position;
            _phaseSeed = Random.Range(0f, Mathf.PI * 2f);
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_age >= lifetime)
            {
                Destroy(gameObject);
                return;
            }

            float t = _age / lifetime;
            // Position: rise + sway
            float swayX = Mathf.Sin(_age * swayFrequency + _phaseSeed) * swayAmplitude;
            transform.position = _spawnPos + new Vector3(swayX, _age * riseSpeed, 0f);

            // Scale ramp-up over first 30%
            float scale = Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(t / 0.3f));
            transform.localScale = Vector3.one * scale;

            // Alpha fade-out over last 60% (stays opaque for first 40%)
            float alphaT = Mathf.Clamp01((t - 0.4f) / 0.6f);
            float alpha = Mathf.Lerp(1f, 0f, alphaT);
            Color c = _baseColor;
            c.a = _baseColor.a * alpha;
            if (_tmp != null) _tmp.color = c;
            else if (_sprite != null) _sprite.color = c;
        }
    }
}
