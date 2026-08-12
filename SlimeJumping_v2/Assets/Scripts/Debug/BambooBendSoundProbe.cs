using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Effects;
using SlimeJumping.Platforms;

namespace SlimeJumping.Debugging
{
    /// Logs every term BambooLandingFeedback uses to pick a bend creak's loudness, one line per
    /// landing on a stalk, so a landing that sounds louder than the one before it can be read off
    /// numbers instead of guessed at. Recomputes the prediction from the same shared config, so a
    /// mismatch between what is logged and what is heard means the component, not the formula.
    /// Sits on the Slime and pulls; the stalk is resolved per frame from SurfaceCollider.
    /// Order 3002 so the rider has already pushed this frame's impulse in.
    [DefaultExecutionOrder(3002)]
    [AddComponentMenu("SlimeJumping/Debug/Bamboo Bend Sound Probe")]
    public class BambooBendSoundProbe : MonoBehaviour
    {
        [SerializeField] SlimeController slime;
        [SerializeField] bool logging = true;

        BambooLandingFeedbackConfig config;
        BambooPlatformV3 wasOn;
        float lastLandTime;

        void Reset()
        {
            if (slime == null) slime = GetComponentInParent<SlimeController>();
        }

        void Awake()
        {
            if (slime == null) slime = GetComponentInParent<SlimeController>();
            config = BambooLandingFeedbackConfig.Resolve(null);
        }

        void FixedUpdate()
        {
            if (!logging || slime == null) return;

            var col = slime.SurfaceCollider;
            BambooPlatformV3 stalk = col != null ? col.GetComponentInParent<BambooPlatformV3>() : null;
            if (stalk != null && !stalk.RiderAttached) stalk = null;

            if (stalk != null && wasOn == null) LogLanding(stalk);
            wasOn = stalk;
        }

        void LogLanding(BambooPlatformV3 stalk)
        {
            float sag = Mathf.Abs(stalk.CurrentDeflectionDeg);
            int kind = stalk.LastRiderImpulseKind;
            float impulseDeg = Mathf.Abs(stalk.LastRiderImpulseDeg);
            float omegaN = 2f * Mathf.PI * stalk.NaturalFreqHz;
            float kick = (kind == 1 && omegaN >= 1e-4f) ? impulseDeg / omegaN : 0f;

            float omega = stalk.AngularVelocity + (kind == 1 ? stalk.LastRiderImpulseDeg : 0f);
            float rate = omegaN >= 1e-4f ? omega / omegaN : 0f;
            float theta = stalk.CurrentDeflectionDeg;
            float ampRaw = Mathf.Sqrt(theta * theta + rate * rate);
            float ampDeg = stalk.MaxDeflectionDeg > 0f ? Mathf.Min(ampRaw, stalk.MaxDeflectionDeg) : ampRaw;
            float launch = ampDeg * Mathf.Deg2Rad * omegaN * stalk.BambooLength;
            if (stalk.MaxTipSpeed > 0f) launch = Mathf.Min(launch, stalk.MaxTipSpeed);

            float eject = stalk.PredictedEjectSpeed;
            bool throwCapable = stalk.ThrowCapable;

            float minLaunch = config != null ? config.minLaunchSpeed : 2f;
            float largeLaunch = config != null ? config.largeLaunchSpeed : 20f;
            float t = Mathf.InverseLerp(minLaunch, Mathf.Max(largeLaunch, minLaunch + 0.01f), eject);
            float volume = config != null
                ? Mathf.Lerp(config.volumeAtMinBend, config.volumeAtLargeBend, t)
                : t;
            float defVolume = (config != null && config.bendSound != null)
                ? config.bendSound.volumeRange.x : 1f;

            float gap = lastLandTime > 0f ? Time.fixedTime - lastLandTime : -1f;
            lastLandTime = Time.fixedTime;

            Debug.Log(string.Format(
                "[BambooBendSoundProbe] {0} gap={1:F2}s | slime v={2:F2} vn={3:F2} | impulse={4:F0} " +
                "| EJECT={5:F2} throwCapable={6} -> t={7:F2} vol={8:F3} (x{9:F2} = {10:F4}) " +
                "| oldLaunch={11:F2} ampRaw={12:F1} sag={13:F2} lockout={14:F2} len={15:F1}",
                stalk.name, gap, slime.Velocity.magnitude, slime.ImpactNormalSpeed, impulseDeg,
                eject, throwCapable, t, volume, defVolume, volume * defVolume,
                launch, ampRaw, sag, stalk.EjectLockoutTimer, stalk.BambooLength), stalk);
        }
    }
}
