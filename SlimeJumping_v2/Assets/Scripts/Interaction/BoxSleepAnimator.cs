using System;
using UnityEngine;

namespace SlimeJumping.Interaction
{
    // Sleep sequence: Inactive → Settling → Sleeping → Waking → Released.
    // Settling/Waking interpolate from the phase-entry snapshot; Sleeping breathes.
    public class BoxSleepAnimator : MonoBehaviour
    {
        [SerializeField] private SlimeVisual slimeVisual;

        public enum State { Inactive, Settling, Sleeping, Waking, Released }

        public event Action OnFullyReleased;
        public State CurrentState => _state;
        public bool IsSleeping => _state == State.Sleeping;

        private State _state = State.Inactive;
        private BoxSleepProfile _profile;
        private readonly Breathing _breathing = new Breathing();

        private float _phaseTimer;
        private bool _pendingWake;

        // Captured at phase entry so transitions interpolate from the current visual.
        private Vector3 _enterScale;
        private float _enterAsymmetry;
        private float _enterDip;
        private Quaternion _enterRotation;
        private Vector3 _enterFaceOffset;

        // Output cache (read by next phase's enter logic).
        private Vector3 _lastScale = Vector3.one;
        private float _lastAsymmetry;
        private float _lastDip;
        private Quaternion _lastRotation = Quaternion.identity;
        private Vector3 _lastFaceOffset = Vector3.zero;

        // _smoothedFaceOffset is what actually reaches SlimeVisual.
        private Vector3 _smoothedFaceOffset;
        private Vector3 _faceVelocity;

        private void Awake()
        {
            if (slimeVisual == null) slimeVisual = GetComponentInChildren<SlimeVisual>();
            if (slimeVisual == null)
                Debug.LogError($"[BoxSleepAnimator] {name} could not find SlimeVisual in children.", this);
        }

        public void BeginBoxSleep(BoxSleepProfile profile)
        {
            if (profile == null)
            {
                Debug.LogError($"[BoxSleepAnimator] {name} BeginBoxSleep called with null profile.", this);
                return;
            }
            _profile = profile;
            _breathing.Configure(profile.breathAmplitude, profile.breathFrequency, profile.amplitudeVariation);
            _pendingWake = false;
            _smoothedFaceOffset = Vector3.zero;
            _faceVelocity = Vector3.zero;
            EnterState(State.Settling);
            // Push t=0 now, else a one-frame (1,1,1) flicker between IsExternallyAnimated reset and the next Update().
            Evaluate(0f);
        }

        public void RequestWake()
        {
            switch (_state)
            {
                case State.Settling:
                    _pendingWake = true;
                    break;
                case State.Sleeping:
                    EnterState(State.Waking);
                    break;
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
                case State.Settling:
                    _enterScale = Vector3.one;
                    _enterAsymmetry = 0f;
                    _enterDip = 0f;
                    _enterRotation = Quaternion.identity;
                    _enterFaceOffset = Vector3.zero;
                    if (slimeVisual != null) slimeVisual.SetEyesForced(EyesForceState.Closed);
                    break;
                case State.Sleeping:
                    _breathing.Reset();
                    break;
                case State.Waking:
                    _enterScale = _lastScale;
                    _enterAsymmetry = _lastAsymmetry;
                    _enterDip = _lastDip;
                    _enterRotation = _lastRotation;
                    _enterFaceOffset = _lastFaceOffset;
                    if (slimeVisual != null) slimeVisual.SetEyesForced(EyesForceState.Open);
                    break;
                case State.Released:
                    // Overrides stay applied through Waking; clearing earlier snaps the slime to rest mid-animation.
                    if (slimeVisual != null)
                    {
                        slimeVisual.SetExternalRotation(null);
                        slimeVisual.SetExternalFaceOffset(null);
                    }
                    OnFullyReleased?.Invoke();
                    break;
            }
        }

        private void Evaluate(float t)
        {
            Vector3 scale = Vector3.one;
            float asym = 0f;
            float dip = 0f;
            Quaternion rot = Quaternion.identity;
            Vector3 faceOffset = Vector3.zero;
            Quaternion sleepRot = Quaternion.Euler(_profile.sleepEulerAngles);

            switch (_state)
            {
                case State.Settling:
                {
                    float u = Mathf.Clamp01(t / _profile.settleDuration);
                    float backEased = EaseOutBack(u, _profile.sleepOvershoot);
                    float sineEased = EaseInOutSine(u);
                    scale = Vector3.LerpUnclamped(_enterScale, _profile.sleepScale, backEased);
                    asym = Mathf.Lerp(_enterAsymmetry, _profile.sleepAsymmetry, sineEased);
                    dip = Mathf.Lerp(_enterDip, _profile.sleepDip, EaseOutQuad(u));
                    rot = Quaternion.SlerpUnclamped(_enterRotation, sleepRot, sineEased);
                    faceOffset = Vector3.Lerp(_enterFaceOffset, _profile.sleepFaceOffset, sineEased);
                    if (t >= _profile.settleDuration)
                    {
                        if (_pendingWake) { _pendingWake = false; EnterState(State.Waking); return; }
                        EnterState(State.Sleeping);
                        return;
                    }
                    break;
                }
                case State.Sleeping:
                {
                    _breathing.Tick(Time.deltaTime);
                    float d = _breathing.Value;
                    scale = new Vector3(
                        _profile.sleepScale.x - d * 0.5f,
                        _profile.sleepScale.y + d,
                        _profile.sleepScale.z - d * 0.5f);
                    asym = _profile.sleepAsymmetry;
                    dip = _profile.sleepDip + d * 2f;
                    rot = sleepRot;
                    faceOffset = _profile.sleepFaceOffset;
                    break;
                }
                case State.Waking:
                {
                    float antDur = _profile.wakeAnticipationDuration;
                    float setDur = _profile.wakeSettleDuration;
                    if (t < antDur)
                    {
                        float u = Mathf.Clamp01(t / antDur);
                        float e = EaseOutQuad(u);
                        scale = Vector3.Lerp(_enterScale, _profile.wakeAnticipationScale, e);
                        asym = Mathf.Lerp(_enterAsymmetry, 0f, e);
                        dip = Mathf.Lerp(_enterDip, 0f, e);
                        // Rotation + face offset unwind here so they are at rest before the elastic settle starts.
                        rot = Quaternion.Slerp(_enterRotation, Quaternion.identity, e);
                        faceOffset = Vector3.Lerp(_enterFaceOffset, Vector3.zero, e);
                    }
                    else
                    {
                        float u = Mathf.Clamp01((t - antDur) / setDur);
                        float elastic = EaseOutElastic(u, _profile.wakeSettleElasticity, _profile.wakeSettlePeriod);
                        scale = Vector3.LerpUnclamped(_profile.wakeAnticipationScale, Vector3.one, elastic);
                        asym = 0f;
                        dip = 0f;
                        rot = Quaternion.identity;
                        faceOffset = Vector3.zero;
                        if (t >= antDur + setDur)
                        {
                            scale = Vector3.one;
                            Push(scale, asym, dip, Quaternion.identity, Vector3.zero);
                            EnterState(State.Released);
                            return;
                        }
                    }
                    break;
                }
            }

            Push(scale, asym, dip, rot, faceOffset);
        }

        private void Push(Vector3 scale, float asym, float dip, Quaternion rot, Vector3 faceOffset)
        {
            _lastScale = scale;
            _lastAsymmetry = asym;
            _lastDip = dip;
            _lastRotation = rot;
            _lastFaceOffset = faceOffset;

            if (slimeVisual == null) return;

            // Pivot-compensation: keep anchored edge planted when scale.y deviates from 1.
            // Computed first because the face-follow target adds it as a Y delta.
            float halfHeight = slimeVisual.BodyNativeHeight * 0.5f;
            float anchorBias = (_profile.verticalAnchor * 2f) - 1f;
            float bodyPivotY = halfHeight * (scale.y - 1f) * anchorBias;
            Vector3 localPos = new Vector3(0f, bodyPivotY, 0f);

            // SmoothDamp applied in every phase, not just sleep idle, so transitions carry the same softness.
            Vector3 faceTarget = faceOffset + new Vector3(0f, bodyPivotY * _profile.faceFollowRatio, 0f);
            _smoothedFaceOffset = Vector3.SmoothDamp(
                _smoothedFaceOffset, faceTarget, ref _faceVelocity, _profile.faceFollowSmoothTime);

            // SetExternalDeform composes _externalRotationOverride as it runs, so set the rotation
            // first or body3d applies last frame's.
            slimeVisual.SetExternalRotation(rot);
            slimeVisual.SetExternalFaceOffset(_smoothedFaceOffset);

            slimeVisual.SetExternalDeform(scale, localPos, asym, dip);
        }

        // --- Easing (same set as SitPoseAnimator) ---

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
