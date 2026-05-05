using UnityEngine;

namespace SlimeJumping.Platforms
{
    // Shared surface SlimeController consumes for both sag-based Rope (v1) and
    // timing-based RopeV2. Lets the controller stay rope-flavor-agnostic while
    // each implementation owns its own rebound-bonus formula.
    public interface IRopePlatform
    {
        EdgeCollider2D Edge { get; }
        float MaxReboundSpeed { get; }

        Vector2 SampleVelocityAt(Vector2 worldPoint);
        void ReportRiderContact(Collider2D slimeCol, Vector2 worldContact, float mass, Vector2 riderVel);
        void NotifyFirstAttach(Vector2 worldContact);
        void NotifyLaunchRebound(Vector2 worldContact);

        // One-shot impact impulse on the airborne→attach frame. Lets the rope
        // dip deep on a hard landing without having to inflate steady-state
        // riderForceMul (which would also over-sag a stationary rider).
        // impactVelocity is the slime's pre-attach world velocity; the rope
        // distributes it to nearby nodes via its own riderInfluenceSpacing
        // falloff, scaled by the rope-side impactGain.
        void ApplyRiderImpact(Vector2 worldContact, Vector2 impactVelocity);

        // Computes the launch-direction speed bonus the rope should add to the
        // slime at the moment of release. v1 ignores chargeRatio and returns
        // sag×perMeter; v2 evaluates two designer-authored AnimationCurves over
        // (rope-velocity-projection, chargeRatio). Always returns a value
        // already clamped to [0, MaxReboundSpeed].
        float GetLaunchBonus(Vector2 launchDir, Vector2 worldContact, float chargeRatio);

        // V3-only positional coupling. Returns and clears the world-space
        // displacement the rider's contact segment underwent since the last
        // call; SlimeController applies it to rb.position the same frame so
        // the slime stays glued to the rope through dip-and-rebound rather
        // than floating above a surface that's been pulled out from under it.
        // v1/v2 return Vector2.zero — they have no position-coupling channel.
        Vector2 ConsumeRiderCarryDelta(Vector2 worldContact);
    }
}
