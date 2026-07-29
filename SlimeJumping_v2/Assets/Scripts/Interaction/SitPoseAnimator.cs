using System;
using UnityEngine;

namespace SlimeJumping.Interaction
{
    public class SitPoseAnimator : MonoBehaviour
    {
        [SerializeField] private SlimeVisual slimeVisual;

        public enum State { Inactive, SittingDown, Idle, GettingUp, Released }

        public event Action OnFullyReleased;
        public State CurrentState => _state;

        private State _state = State.Inactive;
        private SitPoseProfile _profile;
        private readonly Breathing _breathing = new Breathing();

        private float _phaseTimer;
        private Vector3 _enterScale;
        private float _enterAsymmetry;
        private float _enterDip;
        private bool _pendingGetUp;

        private void Awake()
        {
            if (slimeVisual == null) slimeVisual = GetComponentInChildren<SlimeVisual>();
            if (slimeVisual == null)
                Debug.LogError($"[SitPoseAnimator] {name} could not find SlimeVisual in children.", this);
        }

        public void BeginSitPose(SitPoseProfile profile)
        {
            if (profile == null)
            {
                Debug.LogError($"[SitPoseAnimator] {name} BeginSitPose called with null profile.", this);
                return;
            }
            _profile = profile;
            _breathing.Configure(profile.breathAmplitude, profile.breathFrequency, profile.amplitudeVariation);
            _pendingGetUp = false;
            EnterState(State.SittingDown);
            // t=0 now, else a one-frame (1,1,1) flicker between IsExternallyAnimated reset and Update.
            Evaluate(0f);
        }

        // Inactive / GettingUp / Released deliberately ignore the request.
        public void RequestGetUp()
        {
            switch (_state)
            {
                case State.SittingDown: _pendingGetUp = true; break;
                case State.Idle: EnterState(State.GettingUp); break;
            }
        }

        private void Update()
        {
            if (_state == State.Inactive || _state == State.Released) return;
            _phaseTimer += Time.deltaTime;
            Evaluate(_phaseTimer);
        }

        private void EnterState(State next)
        {
            _state = next;
            _phaseTimer = 0f;
            switch (next)
            {
                case State.SittingDown:
                    _enterScale = Vector3.one;
                    _enterAsymmetry = 0f;
                    _enterDip = 0f;
                    break;
                case State.Idle:
                    _breathing.Reset();
                    break;
                case State.GettingUp:
                    // Capture current output (could be Idle+breathing or mid-SittingDown).
                    _enterScale = _lastScale;
                    _enterAsymmetry = _lastAsymmetry;
                    _enterDip = _lastDip;
                    break;
                case State.Released:
                    OnFullyReleased?.Invoke();
                    break;
            }
        }

        private Vector3 _lastScale = Vector3.one;
        private float _lastAsymmetry;
        private float _lastDip;
        private float _idleBobY; // method A accumulator, only written during Idle

        private void Evaluate(float t)
        {
            Vector3 scale = Vector3.one;
            float asym = 0f;
            float dip = 0f;

            switch (_state)
            {
                case State.SittingDown:
                {
                    float u = Mathf.Clamp01(t / _profile.sitDuration);
                    float backEased = EaseOutBack(u, _profile.sitOvershoot);
                    scale = Vector3.LerpUnclamped(_enterScale, _profile.sitScale, backEased);
                    asym = Mathf.Lerp(_enterAsymmetry, _profile.sitAsymmetry, EaseInOutSine(u));
                    dip = Mathf.Lerp(_enterDip, _profile.sitChargeDip, EaseOutQuad(u));
                    if (t >= _profile.sitDuration)
                    {
                        if (_pendingGetUp) { _pendingGetUp = false; EnterState(State.GettingUp); return; }
                        EnterState(State.Idle);
                        return;
                    }
                    break;
                }
                case State.Idle:
                {
                    _breathing.Tick(Time.deltaTime);
                    float d = _breathing.Value;        // method D (scale delta, with jitter)
                    float s = _breathing.RawSin;       // methods A/C (pure sin)
                    scale = new Vector3(
                        _profile.sitScale.x - d * 0.5f,
                        _profile.sitScale.y + d,
                        _profile.sitScale.z - d * 0.5f);
                    asym = _profile.sitAsymmetry + s * _profile.topSwellAmplitude; // method C
                    dip = _profile.sitChargeDip + d * 2f;
                    _idleBobY = s * _profile.bobAmplitude;  // method A
                    break;
                }
                case State.GettingUp:
                {
                    float antDur = _profile.anticipationDuration;
                    float setDur = _profile.settleDuration;
                    if (t < antDur)
                    {
                        float u = Mathf.Clamp01(t / antDur);
                        float e = EaseOutQuad(u);
                        scale = Vector3.Lerp(_enterScale, _profile.anticipationScale, e);
                        asym = Mathf.Lerp(_enterAsymmetry, 0f, e);
                        dip = Mathf.Lerp(_enterDip, 0f, e);
                    }
                    else
                    {
                        float u = Mathf.Clamp01((t - antDur) / setDur);
                        float elastic = EaseOutElastic(u, _profile.settleElasticity, _profile.settlePeriod);
                        scale = Vector3.LerpUnclamped(_profile.anticipationScale, Vector3.one, elastic);
                        asym = 0f;
                        dip = 0f;
                        if (t >= antDur + setDur)
                        {
                            scale = Vector3.one;
                            PushDeform(scale, asym, dip);
                            EnterState(State.Released);
                            return;
                        }
                    }
                    break;
                }
            }

            PushDeform(scale, asym, dip);
        }

        private void PushDeform(Vector3 scale, float asym, float dip)
        {
            _lastScale = scale;
            _lastAsymmetry = asym;
            _lastDip = dip;
            // Center pivot moves both edges under Y scale; localPos.y compensates so the edge at
            // profile.verticalAnchor (0=top, 1=bottom) stays put.
            float halfHeight = slimeVisual != null ? slimeVisual.BodyNativeHeight * 0.5f : 0.5f;
            float anchorBias = (_profile.verticalAnchor * 2f) - 1f;
            float bobY = (_state == State.Idle) ? _idleBobY : 0f; // method A — Idle only
            Vector3 localPos = new Vector3(0f, halfHeight * (scale.y - 1f) * anchorBias + bobY, 0f);
            // Face follows body automatically via UpdateFaceOffset (runs even under the external gate).
            if (slimeVisual != null) slimeVisual.SetExternalDeform(scale, localPos, asym, dip);
        }

        // --- Easing ---

        private static float EaseOutQuad(float t)
        {
            float inv = 1f - t;
            return 1f - inv * inv;
        }

        private static float EaseInOutSine(float t)
        {
            return -(Mathf.Cos(Mathf.PI * Mathf.Clamp01(t)) - 1f) * 0.5f;
        }

        private static float EaseOutBack(float t, float s)
        {
            float c1 = s;
            float c3 = c1 + 1f;
            float x = t - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }

        private static float EaseOutElastic(float t, float amp, float period)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            float a = Mathf.Max(amp, 1f);
            float s = period / (2f * Mathf.PI) * Mathf.Asin(1f / a);
            return a * Mathf.Pow(2f, -10f * t)
                   * Mathf.Sin((t - s) * (2f * Mathf.PI) / period) + 1f;
        }

    }
}
