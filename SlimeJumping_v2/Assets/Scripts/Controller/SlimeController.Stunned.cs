using UnityEngine;
using SlimeJumping.Physics;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // --- State: Stunned ---
        // Slides along surfaces, never attaches. No air control, no W/S gravity modulation.

        private void FixedUpdateStunned(float dt)
        {
            stunTimer -= dt;

            velocity += -gravityUp * GravityMagnitude * dt;

            // Cancel normal velocity only — tangent survives.
            if (isTouchingSurface)
            {
                surfaceProbe.ProjectVelocityAgainstContacts(ref velocity, surfaceNormal);
                ApplyStunGroundFriction(dt);
                // Same depenetration Attached uses, minus the velocity kill that used to pin the
                // slime. A body shrinking mid-stun stays seated instead of dropping when stun ends.
                SnapToSurface();
            }

            if (stunTimer <= 0f)
            {
                stunTimer = 0f;
                launchSpeed = 0f;
                launchPosition = rb.position;
                conversionCount = 0;
                ChangeState(SlimeState.Airborne, intentional: true);
            }
        }

        // Below this the decay twitches on its asymptote instead of resting.
        private const float StunFrictionStopSpeed = 0.2f;

        // Tangent damping ramped 0 → full over the back of the stun (stunFrictionRampStart), scaled
        // by the surface's slideResistance multiplier: ice keeps sliding, mud stops sooner. Rate is
        // proportional (1/s), not m/s² — a fixed decel is invisible against an 8 m/s knockback.
        private void ApplyStunGroundFriction(float dt)
        {
            if (stunDuration <= 0f) return;

            float progress = 1f - Mathf.Clamp01(stunTimer / stunDuration);
            float ramp = Mathf.InverseLerp(settings.stunFrictionRampStart, 1f, progress);
            if (ramp <= 0f) return;

            float rate = settings.stunFrictionDampRate * ResolveStunSurfaceResistance() * ramp;
            if (rate <= 0f) return;

            float tangentSpeed = Vector2.Dot(velocity, surfaceTangent);
            float damped = tangentSpeed * Mathf.Exp(-rate * dt);
            if (Mathf.Abs(damped) < StunFrictionStopSpeed) damped = 0f;
            velocity += surfaceTangent * (damped - tangentSpeed);
        }

        // surfaceMod stays pinned to defaults for the whole stun, so pull the resistance multiplier
        // off the touched surface's tag directly instead of reading the stale modifier set.
        private float ResolveStunSurfaceResistance()
        {
            float angle = GetSurfaceAngle();
            SurfaceMaterial mat = null;
            if (surfaceCollider != null)
            {
                var tags = surfaceCollider.GetComponentsInParent<SurfaceTag>();
                for (int i = 0; i < tags.Length; i++)
                {
                    if (tags[i].enabled) { mat = tags[i].material; break; }
                }
            }
            return (mat != null)
                ? mat.slideResistanceMultiplier.Evaluate(angle)
                : settings.slideResistanceMultiplier.Evaluate(angle);
        }
    }
}
