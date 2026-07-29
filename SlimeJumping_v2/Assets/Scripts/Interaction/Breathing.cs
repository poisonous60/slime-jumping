using UnityEngine;

namespace SlimeJumping.Interaction
{
    // Shared breathing driver used by SitPoseAnimator and BoxSleepAnimator. Owns its own
    // phase/elapsed instead of Time.time, which loses float precision over long sessions.
    [System.Serializable]
    public class Breathing
    {
        private float _amplitude, _frequency, _variation;
        private float _position, _curAmp;
        private float _phase, _elapsed, _nextVarTime;

        public float Value => _position;
        // Raw sine phase in [-1, +1], shared driver for bob/swell methods (no jitter applied).
        public float RawSin => Mathf.Sin(_phase);

        public void Configure(float amplitude, float frequency, float amplitudeVariation)
        {
            _amplitude = amplitude;
            _frequency = frequency;
            _variation = amplitudeVariation;
        }

        public void Reset()
        {
            _position = 0f;
            _curAmp = _amplitude;
            _phase = 0f;
            _elapsed = 0f;
            _nextVarTime = Random.Range(2f, 5f);
        }

        public void Tick(float dt)
        {
            _elapsed += dt;
            if (_elapsed > _nextVarTime)
            {
                // Clamp variation to amplitude so _curAmp never flips sign
                // (sign flip causes a phase discontinuity visible as a sudden jump).
                float maxVar = Mathf.Min(_variation, _amplitude);
                _curAmp = _amplitude + Random.Range(-maxVar, maxVar);
                _nextVarTime = _elapsed + Random.Range(2f, 5f);
            }
            _phase += dt * _frequency * Mathf.PI * 2f;
            if (_phase > Mathf.PI * 2f) _phase -= Mathf.PI * 2f;
            _position = Mathf.Sin(_phase) * _curAmp;
        }
    }
}
