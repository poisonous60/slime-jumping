using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    // Slime push + stripe particles + ParticleSystemForceField sync for snow.
    // Snow particles need ExternalForces enabled with influenceMask = WindForceField layer.
    // windAcceleration is local-space: rotating the transform rotates the wind with it.
    [RequireComponent(typeof(Collider2D))]
    public class WindZone2D_v2 : MonoBehaviour
    {
        [Header("Wind")]
        [Tooltip("Wind direction and strength in local space (units/s²). Magnitude = acceleration. Rotates with the transform.")]
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

        [Header("Visual — In-zone Stripe")]
        [SerializeField] private ParticleSystem windParticles;
        [Tooltip("Stripe particle visual speed (units/s). Independent of physics acceleration.")]
        [SerializeField] private float particleSpeed = 8f;

        [Header("Visual — Snow Force Field")]
        [Tooltip("Box ParticleSystemForceField that pushes snow (and any other ExternalForces-enabled PS on the WindForceField layer). Auto-synced from BoxCollider2D bounds + windAcceleration. Box-only.")]
        [SerializeField] private ParticleSystemForceField snowForceField;

        [Tooltip("Drag inside the field. Higher = particles converge to terminal velocity faster (terminal ≈ |windAcceleration| / drag). 0 = particles keep accelerating in the zone.")]
        [Min(0f)]
        [SerializeField] private float snowDrag = 1.5f;

        [Header("Visual — Side Fields (Soft Transition)")]
        [Tooltip("Optional weaker ForceField on the left of the main collider, smooths the wind boundary so snow next to the zone isn't a hard cut between vertical fall and full diagonal drift.")]
        [SerializeField] private ParticleSystemForceField sideForceFieldLeft;

        [Tooltip("Optional weaker ForceField on the right of the main collider, mirror of left.")]
        [SerializeField] private ParticleSystemForceField sideForceFieldRight;

        [Tooltip("Side field width = mainCollider.size.x × this ratio.")]
        [Range(0.1f, 2f)]
        [SerializeField] private float sideFieldWidthRatio = 0.5f;

        [Tooltip("Side field direction force = main windAcceleration × this multiplier. 0 = no push (visual disabled), 1 = same as main zone.")]
        [Range(0f, 1f)]
        [SerializeField] private float sideFieldStrength = 0.3f;

        private SlimeController trackedSlime;

        void Reset()
        {
            GetComponent<Collider2D>().isTrigger = true;
        }

        void OnValidate()
        {
            SyncParticles();
            SyncForceField();
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
            // Local sim space, else stripes stay world-aligned instead of rotating with the zone.
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var shape = windParticles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = new Vector3(-(alongWind * 0.5f - 0.25f), 0f, 0f);
            shape.scale = new Vector3(0.5f, acrossWind, 0.1f);

            var vel = windParticles.velocityOverLifetime;
            vel.enabled = true;
            // windParticles is rotated so its +X is windDir; velocity is in that local frame.
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = particleSpeed;
            vel.y = 0f;
            vel.z = 0f;

            var emis = windParticles.emission;
            emis.rateOverTime = acrossWind * 12f;
        }

        void SyncForceField()
        {
            if (snowForceField == null) return;
            var box = GetComponent<BoxCollider2D>();
            if (box == null) return;

            var ff = snowForceField;
            ff.shape = ParticleSystemForceFieldShape.Box;

            // Box ForceField half-extents = endRange × lossyScale; pin endRange=0.5 or the
            // field is 2× the collider and accelerates particles outside the visible zone.
            ff.endRange = 0.5f;

            var ffT = ff.transform;
            if (ffT.parent == transform)
            {
                ffT.localPosition = new Vector3(box.offset.x, box.offset.y, 0f);
                ffT.localRotation = Quaternion.identity;
                // Z = 100 → half-extent 50: particles drifting in Z can't fall out of the field.
                ffT.localScale = new Vector3(box.size.x, box.size.y, 100f);
            }

            // Directional acceleration in world space — same units as windAcceleration (units/s²).
            ff.directionX = new ParticleSystem.MinMaxCurve(windAcceleration.x);
            ff.directionY = new ParticleSystem.MinMaxCurve(windAcceleration.y);
            ff.directionZ = new ParticleSystem.MinMaxCurve(0f);

            // Linear drag: terminal = accel / drag, so snowDrag maps to a target speed.
            // Quadratic (multiplyDragByParticleVelocity) gives terminal = √(accel/drag) instead.
            ff.multiplyDragByParticleVelocity = false;
            ff.multiplyDragByParticleSize = false;
            ff.drag = new ParticleSystem.MinMaxCurve(snowDrag);

            int layer = LayerMask.NameToLayer("WindForceField");
            if (layer >= 0 && ff.gameObject.layer != layer)
                ff.gameObject.layer = layer;

            SyncSideField(sideForceFieldLeft, -1f, box);
            SyncSideField(sideForceFieldRight, +1f, box);
        }

        void SyncSideField(ParticleSystemForceField sff, float xSign, BoxCollider2D box)
        {
            if (sff == null) return;

            sff.shape = ParticleSystemForceFieldShape.Box;
            sff.endRange = 0.5f;

            float sideWidth = Mathf.Max(box.size.x * sideFieldWidthRatio, 0.01f);
            var sffT = sff.transform;
            if (sffT.parent == transform)
            {
                sffT.localPosition = new Vector3(
                    box.offset.x + xSign * (box.size.x * 0.5f + sideWidth * 0.5f),
                    box.offset.y, 0f);
                sffT.localRotation = Quaternion.identity;
                sffT.localScale = new Vector3(sideWidth, box.size.y, 100f);
            }

            sff.directionX = new ParticleSystem.MinMaxCurve(windAcceleration.x * sideFieldStrength);
            sff.directionY = new ParticleSystem.MinMaxCurve(windAcceleration.y * sideFieldStrength);
            sff.directionZ = new ParticleSystem.MinMaxCurve(0f);
            sff.multiplyDragByParticleVelocity = false;
            sff.multiplyDragByParticleSize = false;
            sff.drag = new ParticleSystem.MinMaxCurve(snowDrag);

            int layer = LayerMask.NameToLayer("WindForceField");
            if (layer >= 0 && sff.gameObject.layer != layer)
                sff.gameObject.layer = layer;
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
            if (slime != null)
                trackedSlime = slime;
        }

        void OnTriggerExit2D(Collider2D other)
        {
            var slime = other.GetComponentInParent<SlimeController>();
            if (slime != null && slime == trackedSlime)
                trackedSlime = null;
        }

        void FixedUpdate()
        {
            if (trackedSlime == null) return;

            float dt = Time.fixedDeltaTime;
            // Local-space acceleration → world, so the push rotates with the transform.
            Vector2 worldWindAcc = transform.TransformDirection(windAcceleration);
            float mag = worldWindAcc.magnitude;
            if (mag < 0.0001f) return;

            Vector2 worldWindDir = worldWindAcc / mag;

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
                float alongWind = Vector2.Dot(trackedSlime.Velocity, worldWindDir);
                if (alongWind > 0f)
                    falloff = Mathf.Clamp01(1f - alongWind / terminalSpeed);
            }

            // AddSustainedFlowImpulse, not AddExternalVelocity: the latter only bumps the
            // single-frame velocity that Attached/Charging overwrite, so wind feels ~50× weaker.
            Vector2 deltaV = worldWindDir * (mag * multiplier * falloff * dt);
            trackedSlime.AddSustainedFlowImpulse(deltaV);
        }

        void OnDrawGizmos()
        {
            if (windAcceleration.sqrMagnitude < 0.0001f) return;

            Vector3 center = transform.position;
            Vector3 dir = transform.TransformDirection(((Vector3)(Vector2)windAcceleration).normalized);
            float len = Mathf.Clamp(windAcceleration.magnitude * 0.1f, 0.5f, 3f);

            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.7f);
            Gizmos.DrawLine(center, center + dir * len);

            Vector3 perp = Vector3.Cross(dir, Vector3.forward).normalized;
            Vector3 tip = center + dir * len;
            Gizmos.DrawLine(tip, tip - dir * 0.25f + perp * 0.12f);
            Gizmos.DrawLine(tip, tip - dir * 0.25f - perp * 0.12f);
        }

        void OnDrawGizmosSelected()
        {
            // Transform rotation doesn't fire OnValidate — re-sync so visuals follow the gizmo.
            if (!Application.isPlaying)
            {
                SyncParticles();
                SyncForceField();
            }

            var col = GetComponent<Collider2D>();
            if (col == null) return;

            Color fill = new Color(0.4f, 0.8f, 1f, 0.14f);
            Color outline = new Color(0.4f, 0.8f, 1f, 0.6f);

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

            Gizmos.color = new Color(0.5f, 0.85f, 1f, 0.45f);
            Vector3 dir = transform.TransformDirection(((Vector3)(Vector2)windAcceleration).normalized);
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
