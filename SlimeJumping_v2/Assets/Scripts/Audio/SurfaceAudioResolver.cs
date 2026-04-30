using UnityEngine;
using SlimeJumping.Physics;
using SlimeJumping.Platforms.Snow;

namespace SlimeJumping.Audio
{
    /// Bridges the two surface-identification mechanisms (SurfaceTag and SnowField)
    /// to a single audio-clip lookup. SnowField wins when the contact point still has
    /// covered snow (depth ≥ kSnowAudioMinDepth), because snow is a *visual layer on
    /// top* of a host platform that already carries its own SurfaceTag — querying the
    /// host's tag first would short-circuit and snow audio would never play.
    /// Falls through to the host's SurfaceTag when (a) no SnowField is registered for
    /// the surface, (b) snow is fully carved away at the contact, or (c) snow has no
    /// snowSurfaceMaterial wired. Individual clip slots stay null when unset; caller
    /// is responsible for per-clip fallback to SlimeLocomotionAudio defaults.
    public static class SurfaceAudioResolver
    {
        // Below this remaining snow coverage, treat the contact as host ground for
        // audio purposes — keeps deep-carved tracks sounding like the underlying
        // platform instead of phantom snow.
        const float kSnowAudioMinDepth = 0.1f;

        public static bool TryResolve(Collider2D surface, Vector2 contactPoint, out SurfaceMaterial material)
        {
            if (surface == null) { material = null; return false; }

            if (SnowFieldRegistry.TryGet(surface, out var field)
                && field != null
                && field.settings != null
                && field.settings.snowSurfaceMaterial != null
                && field.SampleDepthRatio01(contactPoint) >= kSnowAudioMinDepth)
            {
                material = field.settings.snowSurfaceMaterial;
                return true;
            }

            var tag = surface.GetComponentInParent<SurfaceTag>();
            if (tag != null && tag.enabled && tag.material != null)
            {
                material = tag.material;
                return true;
            }

            material = null;
            return false;
        }
    }
}
