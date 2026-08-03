using UnityEngine;

namespace SlimeJumping.VFX
{
    /// Anchors a Snow.prefab instance to a fixed spawn box instead of the camera, so snow can start
    /// under a cloud and then fall exactly like the map-wide snow does.
    /// Overrides the shape and the emission rate and nothing else — lifetime, fall speed, drift,
    /// noise, limit velocity, colour ramp and material all stay as Snow.prefab authored them.
    /// The box is a column, not a ceiling strip. Flakes are born throughout it, the same way the
    /// map-wide snow fills the whole viewport — a strip at the cloud would run dry below itself
    /// because a flake only falls 14-25 units in its lifetime.
    [ExecuteAlways]
    [RequireComponent(typeof(ParticleSystem))]
    [AddComponentMenu("SlimeJumping/VFX/Snowfall Volume")]
    public sealed class SnowfallVolume : MonoBehaviour
    {
        [Tooltip("Spawn column in local units. Height has to cover the whole stretch the snow should be visible over: a flake only falls 14-25 units before it dies, so a short box empties out below itself instead of reaching down.")]
        [SerializeField] private Vector2 spawnSize = new Vector2(8f, 30f);

        [Tooltip("1 = the same flakes-per-area as the map-wide snow. Raise it to make a cloud read as actively snowing.")]
        [Min(0f)][SerializeField] private float densityScale = 1f;

        // Map-wide snow measured in flakes per square unit. Its rate asks for 1540 alive but
        // maxParticles caps it at 600, so the cap is what actually reaches the screen, spread over
        // its 54.45-wide spawn box plus the ~19 units the flakes fall before dying.
        const float AmbientFlakesPerSquareUnit = 600f / (54.45f * 45.46f);

        private ParticleSystem _ps;

        private void OnEnable()
        {
            _ps = GetComponent<ParticleSystem>();
            Apply();
        }

        // Reparenting keeps world scale by rewriting localScale, and scalingMode Local reads that
        // scale straight into flake size and speed. Dragging the volume onto a scaled platform is
        // the normal way to use it, so the scale has to be pinned back every time.
        private void OnTransformParentChanged()
        {
            _ps = GetComponent<ParticleSystem>();
            Apply();
        }

        private void OnValidate()
        {
            spawnSize.x = Mathf.Max(0f, spawnSize.x);
            spawnSize.y = Mathf.Max(0f, spawnSize.y);

            _ps = GetComponent<ParticleSystem>();
            if (_ps == null) return;
            Apply();
        }

        private void Apply()
        {
            if (_ps == null) return;

            // Snow.prefab runs scalingMode Local, so this transform's own scale multiplies flake size
            // and speed. Parenting to a cloud rewrites localScale to preserve world scale, which
            // shrinks and skews the flakes. Pinning it to one keeps them prefab-sized under any host;
            // spawnSize is the knob for extent.
            if (transform.localScale != Vector3.one) transform.localScale = Vector3.one;

            // Snow.prefab kills anything outside its EmitterArea child via the Trigger module. That
            // child is gone here, and Kill-outside with no collider left kills everything.
            var trigger = _ps.trigger;
            trigger.enabled = false;

            var shape = _ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(spawnSize.x, spawnSize.y, 0f);
            shape.position = Vector3.zero;
            shape.rotation = Vector3.zero;
            shape.boxThickness = Vector3.zero;

            float rate = SteadyStateRate();
            var emission = _ps.emission;
            emission.enabled = true;
            emission.rateOverTime = rate;

            var main = _ps.main;

            // The map-wide snow rides the camera so it is never offscreen. A volume sits on a cloud
            // the player may be nowhere near, and World simulation plus External Forces makes the
            // system non-procedural, which drops Automatic culling back to always simulating.
            main.cullingMode = ParticleSystemCullingMode.Pause;

            // A wide or dense column outruns the prefab's 3000 budget, and the overflow is dropped
            // silently — it looks like the emission rate simply stopped working.
            int needed = Mathf.CeilToInt(rate * main.startLifetime.constantMax * 1.2f);
            if (main.maxParticles < needed) main.maxParticles = needed;
        }

        // Flakes occupy the spawn box plus however far they fall before dying, so a short box needs a
        // higher rate than a tall one to read at the same density. Lifetime and fall speed come from
        // Snow.prefab, so retuning the snow there carries over instead of drifting.
        private float SteadyStateRate()
        {
            var main = _ps.main;
            float lifetime = Mathf.Max(0.01f, (main.startLifetime.constantMin + main.startLifetime.constantMax) * 0.5f);

            var velocity = _ps.velocityOverLifetime;
            float fallSpeed = Mathf.Abs(velocity.y.constantMin + velocity.y.constantMax) * 0.5f;
            // Limit Velocity can clamp the fall. Snow.prefab runs it per-axis, where the magnitude
            // limit is unused and reading it would clamp the fall to a value nothing enforces.
            var limit = _ps.limitVelocityOverLifetime;
            if (limit.enabled)
            {
                var cap = limit.separateAxes ? limit.limitY : limit.limit;
                fallSpeed = Mathf.Min(fallSpeed, cap.constant);
            }

            float occupiedHeight = spawnSize.y + fallSpeed * lifetime;
            return AmbientFlakesPerSquareUnit * densityScale * spawnSize.x * occupiedHeight / lifetime;
        }

        private void OnDrawGizmosSelected()
        {
            // Scale left out to match scalingMode Local — localToWorldMatrix would skew under a
            // scaled parent and draw a box the snow does not occupy.
            var m = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            float hx = spawnSize.x * 0.5f;
            float hy = spawnSize.y * 0.5f;

            Gizmos.color = new Color(0.7f, 0.9f, 1f, 0.8f);
            Vector3 tl = m.MultiplyPoint3x4(new Vector3(-hx, hy, 0f));
            Vector3 tr = m.MultiplyPoint3x4(new Vector3(hx, hy, 0f));
            Vector3 br = m.MultiplyPoint3x4(new Vector3(hx, -hy, 0f));
            Vector3 bl = m.MultiplyPoint3x4(new Vector3(-hx, -hy, 0f));
            Gizmos.DrawLine(tl, tr);
            Gizmos.DrawLine(tr, br);
            Gizmos.DrawLine(br, bl);
            Gizmos.DrawLine(bl, tl);

            // Stubs below the box as a reminder that snow keeps falling past it, unbounded.
            Gizmos.color = new Color(0.7f, 0.9f, 1f, 0.3f);
            for (int i = 0; i <= 4; i++)
            {
                float x = Mathf.Lerp(-hx, hx, i * 0.25f);
                Gizmos.DrawLine(
                    m.MultiplyPoint3x4(new Vector3(x, -hy, 0f)),
                    m.MultiplyPoint3x4(new Vector3(x, -hy - spawnSize.y * 0.5f, 0f)));
            }
        }
    }
}
