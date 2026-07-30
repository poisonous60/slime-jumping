using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    [RequireComponent(typeof(Collider2D))]
    public class WindZone2D : MonoBehaviour
    {
        [Header("Wind")]
        [Tooltip("Wind direction and strength (units/s²). Magnitude = acceleration.")]
        [SerializeField] private Vector2 windAcceleration = new Vector2(15f, 0f);

        [Tooltip("Max speed along wind direction before falloff kills acceleration. 0 = unlimited.")]
        [SerializeField] private float terminalSpeed = 15f;

        [Header("State Multipliers")]
        [Tooltip("Multiplier when slime is Airborne.")]
        [SerializeField] private float airborneMultiplier = 1f;

        [Tooltip("Multiplier when slime is Attached (crawling/sliding on surface).")]
        [SerializeField] private float attachedMultiplier = 0.7f;

        [Tooltip("Multiplier when slime is Charging.")]
        [SerializeField] private float chargingMultiplier = 0.2f;

        [Header("Visual")]
        [SerializeField] private ParticleSystem windParticles;
        [Tooltip("Particle visual speed (units/s). Independent of physics acceleration.")]
        [SerializeField] private float particleSpeed = 8f;

        private SlimeController trackedSlime;
        private int overlappingColliderCount;

        void Reset()
        {
            GetComponent<Collider2D>().isTrigger = true;
        }

        void OnValidate()
        {
            SyncParticles();
        }

        void SyncParticles()
        {
            var col = GetComponent<Collider2D>();
            if (col == null || windParticles == null) return;

            float mag = windAcceleration.magnitude;
            if (mag < 0.0001f || particleSpeed < 0.01f) return;

            Vector2 windDir = windAcceleration / mag;
            Vector2 perpDir = new Vector2(-windDir.y, windDir.x);

            if (!TryGetWindAlignedBounds(col, windDir, perpDir,
                    out Vector2 localCenter, out float alongWind, out float acrossWind))
                return;

            float angle = Mathf.Atan2(windDir.y, windDir.x) * Mathf.Rad2Deg;
            windParticles.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            windParticles.transform.localPosition = (Vector3)localCenter;

            float lifetime = alongWind / particleSpeed;

            var main = windParticles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.85f, lifetime);
            main.startSpeed = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            // Shape: thin strip at upwind edge (local X of rotated PS)
            var shape = windParticles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = new Vector3(-(alongWind * 0.5f - 0.25f), 0f, 0f);
            shape.scale = new Vector3(0.5f, acrossWind, 0.1f);

            var vel = windParticles.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = windDir.x * particleSpeed;
            vel.y = windDir.y * particleSpeed;
            vel.z = 0f;

            var emis = windParticles.emission;
            emis.rateOverTime = acrossWind * 12f;
        }

        static bool TryGetWindAlignedBounds(Collider2D col, Vector2 windDir, Vector2 perpDir,
            out Vector2 localCenter, out float alongWind, out float acrossWind)
        {
            localCenter = default;
            alongWind = 0f;
            acrossWind = 0f;

            if (col is BoxCollider2D box)
            {
                Vector2 s = box.size;
                alongWind = Mathf.Abs(s.x * windDir.x) + Mathf.Abs(s.y * windDir.y);
                acrossWind = Mathf.Abs(s.x * perpDir.x) + Mathf.Abs(s.y * perpDir.y);
                localCenter = box.offset;
                return alongWind > 0.0001f && acrossWind > 0.0001f;
            }

            if (col is PolygonCollider2D poly)
            {
                float aMin = float.PositiveInfinity, aMax = float.NegativeInfinity;
                float rMin = float.PositiveInfinity, rMax = float.NegativeInfinity;
                for (int i = 0; i < poly.pathCount; i++)
                {
                    var path = poly.GetPath(i);
                    for (int k = 0; k < path.Length; k++)
                    {
                        Vector2 p = path[k] + poly.offset;
                        float a = Vector2.Dot(p, windDir);
                        float r = Vector2.Dot(p, perpDir);
                        if (a < aMin) aMin = a;
                        if (a > aMax) aMax = a;
                        if (r < rMin) rMin = r;
                        if (r > rMax) rMax = r;
                    }
                }
                if (float.IsInfinity(aMin)) return false;
                alongWind = aMax - aMin;
                acrossWind = rMax - rMin;
                localCenter = windDir * ((aMin + aMax) * 0.5f) + perpDir * ((rMin + rMax) * 0.5f);
                return alongWind > 0.0001f && acrossWind > 0.0001f;
            }

            return false;
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            var slime = other.GetComponentInParent<SlimeController>();
            if (slime == null) return;

            if (trackedSlime != slime)
            {
                trackedSlime = slime;
                overlappingColliderCount = 0;
            }
            overlappingColliderCount++;
        }

        void OnTriggerExit2D(Collider2D other)
        {
            var slime = other.GetComponentInParent<SlimeController>();
            if (slime == null || slime != trackedSlime) return;

            overlappingColliderCount--;
            if (overlappingColliderCount > 0) return; // other colliders still inside

            trackedSlime = null;
        }

        void FixedUpdate()
        {
            if (trackedSlime == null) return;

            float dt = Time.fixedDeltaTime;
            float mag = windAcceleration.magnitude;
            if (mag < 0.0001f) return;

            Vector2 windDir = windAcceleration / mag;

            float multiplier;
            switch (trackedSlime.CurrentState)
            {
                case SlimeController.SlimeState.Airborne:
                case SlimeController.SlimeState.Stunned:
                    multiplier = airborneMultiplier; break;
                case SlimeController.SlimeState.Charging:  multiplier = chargingMultiplier; break;
                default:                                   multiplier = attachedMultiplier; break;
            }
            if (multiplier <= 0f) return;

            float falloff = 1f;
            if (terminalSpeed > 0f)
            {
                float alongWind = Vector2.Dot(trackedSlime.Velocity, windDir);
                if (alongWind > 0f)
                    falloff = Mathf.Clamp01(1f - alongWind / terminalSpeed);
            }

            Vector2 deltaV = windDir * (mag * multiplier * falloff * dt);
            trackedSlime.AddExternalVelocity(deltaV);
        }

        void OnDrawGizmos()
        {
            if (windAcceleration.sqrMagnitude < 0.0001f) return;

            Vector3 center = transform.position;
            Vector3 dir = ((Vector3)(Vector2)windAcceleration).normalized;
            float len = Mathf.Clamp(windAcceleration.magnitude * 0.1f, 0.5f, 3f);

            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.6f);
            Gizmos.DrawLine(center, center + dir * len);

            Vector3 perp = Vector3.Cross(dir, Vector3.forward).normalized;
            Vector3 tip = center + dir * len;
            Gizmos.DrawLine(tip, tip - dir * 0.25f + perp * 0.12f);
            Gizmos.DrawLine(tip, tip - dir * 0.25f - perp * 0.12f);
        }

        void OnDrawGizmosSelected()
        {
            var col = GetComponent<Collider2D>();
            if (col == null) return;

            Color fill = new Color(0.3f, 0.7f, 1f, 0.12f);
            Color outline = new Color(0.3f, 0.7f, 1f, 0.5f);

            if (col is BoxCollider2D box)
            {
                Matrix4x4 old = Gizmos.matrix;
                Gizmos.matrix = Matrix4x4.TRS(
                    transform.TransformPoint(box.offset),
                    transform.rotation, transform.lossyScale);
                Gizmos.color = fill;
                Gizmos.DrawCube(Vector3.zero, box.size);
                Gizmos.color = outline;
                Gizmos.DrawWireCube(Vector3.zero, box.size);
                Gizmos.matrix = old;
            }
            else if (col is PolygonCollider2D poly)
            {
                Gizmos.color = outline;
                for (int i = 0; i < poly.pathCount; i++)
                {
                    var path = poly.GetPath(i);
                    if (path.Length < 2) continue;
                    for (int k = 0; k < path.Length; k++)
                    {
                        Vector3 a = transform.TransformPoint((Vector3)(path[k] + poly.offset));
                        Vector3 b = transform.TransformPoint((Vector3)(path[(k + 1) % path.Length] + poly.offset));
                        Gizmos.DrawLine(a, b);
                    }
                }
            }

            if (windAcceleration.sqrMagnitude < 0.0001f) return;

            Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.4f);
            Vector3 dir = ((Vector3)(Vector2)windAcceleration).normalized;
            float len = Mathf.Clamp(windAcceleration.magnitude * 0.1f, 0.5f, 3f);
            Vector3 perp = Vector3.Cross(dir, Vector3.forward).normalized;
            for (int i = -2; i <= 2; i++)
            {
                Vector3 offset = perp * (i * 0.35f);
                Vector3 start = transform.position + offset;
                Gizmos.DrawLine(start, start + dir * len);
            }
        }
    }
}
