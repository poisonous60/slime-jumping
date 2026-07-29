using UnityEngine;

namespace SlimeJumping.Platforms
{
    // Shared surface SlimeController consumes for every rope flavor; each
    // implementation owns its own rebound-bonus formula.
    public interface IRopePlatform
    {
        EdgeCollider2D Edge { get; }
        float MaxReboundSpeed { get; }

        Vector2 SampleVelocityAt(Vector2 worldPoint);
        void ReportRiderContact(Collider2D slimeCol, Vector2 worldContact, float mass, Vector2 riderVel);
        void NotifyFirstAttach(Vector2 worldContact);
        void NotifyLaunchRebound(Vector2 worldContact);

        // One-shot impulse on the airborne→attach frame, using the pre-attach velocity.
        // Separate channel because inflating steady-state riderForceMul over-sags a resting rider.
        void ApplyRiderImpact(Vector2 worldContact, Vector2 impactVelocity);

        // Launch-direction speed bonus at release. Implementations must return a
        // value already clamped to [0, MaxReboundSpeed].
        float GetLaunchBonus(Vector2 launchDir, Vector2 worldContact, float chargeRatio);

        // Final pass over launch velocity after GetLaunchBonus + drag-zone scaling.
        // TautLine forces a minimum upward component, else the spring rebound re-catches
        // a near-horizontal launch. Other ropes return input unchanged.
        Vector2 PostprocessLaunchVelocity(Vector2 launchVel);

        // Returns and clears the contact segment's displacement since the last call;
        // SlimeController adds it to rb.position or the slime floats off during dip-and-rebound.
        // Ropes without position coupling return Vector2.zero.
        Vector2 ConsumeRiderCarryDelta(Vector2 worldContact);
    }
}
