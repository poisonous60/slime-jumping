using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// One curved lightning tendril for <see cref="GraniteBlastEmitter"/>'s Branching mode.
    /// Path is a cubic Bezier (P0=burst, P1=lateral kick, P2=above target, P3=ground point near slime),
    /// sampled into N vertices, with perpendicular jaggedness noise per interior vertex
    /// to read as lightning. Single LineRenderer (Sprites/Default + whiteTexture) — same setup
    /// as TurretHazard's tracer, which is verifiably visible. On full-draw, runs Linecasts along
    /// each segment and applies the hazard once if the slime is intercepted.
    /// </summary>
    public class GraniteBlastTendril : MonoBehaviour
    {
        private GraniteBlastEmitter owner;
        private Vector3[] points;
        private LineRenderer lr;
        private float baseWidth;

        private float drawTime, holdTime, fadeTime;
        private float age;
        private bool hitResolved;
        private bool canHit;

        // Projectile-style halt tracking. Each frame the tip's previous→current chord
        // is CircleCast against the world; on hit the bolt freezes at that point.
        // Slime that was on the line at fire time but moved away by the time the tip
        // arrives no longer halts the bolt — it just keeps going.
        private bool tipHalted;
        private int haltedLastFullIdx;
        private Vector3 haltedTipWorld;
        private float haltedAge = -1f;
        private float prevDrawT;

        public void Init(GraniteBlastEmitter emitter,
                         Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
                         int segments, float jaggedness, float coreWidth,
                         float drawTime, float holdTime, float fadeTime,
                         bool canHit)
        {
            this.owner = emitter;
            this.drawTime = Mathf.Max(0.001f, drawTime);
            this.holdTime = Mathf.Max(0f, holdTime);
            this.fadeTime = Mathf.Max(0.001f, fadeTime);
            this.canHit = canHit;
            this.baseWidth = Mathf.Max(0.001f, coreWidth);

            BuildCurvedJaggedPath(p0, p1, p2, p3, Mathf.Max(2, segments), jaggedness);
            CreateLine();
            ApplyDrawProgress(0f);
        }

        // Polyline-init variant for NavGraph A* paths. Walks the world-space waypoint
        // list, samples each segment at segmentLength resolution, and applies the same
        // perp jaggedness noise as the bezier path. The rest of the runtime pipeline
        // (collision sweep, halt, draw) is path-agnostic — it just consumes points[].
        public void InitPath(GraniteBlastEmitter emitter,
                             System.Collections.Generic.List<Vector3> worldPath,
                             float segmentLength, float jaggedness, float coreWidth,
                             float drawTime, float holdTime, float fadeTime,
                             bool canHit)
        {
            this.owner = emitter;
            this.drawTime = Mathf.Max(0.001f, drawTime);
            this.holdTime = Mathf.Max(0f, holdTime);
            this.fadeTime = Mathf.Max(0.001f, fadeTime);
            this.canHit = canHit;
            this.baseWidth = Mathf.Max(0.001f, coreWidth);

            BuildPolylineJaggedPath(worldPath, Mathf.Max(0.05f, segmentLength), jaggedness);
            CreateLine();
            ApplyDrawProgress(0f);
        }

        private void BuildCurvedJaggedPath(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
                                           int segments, float jaggedness)
        {
            int vertCount = segments + 1;
            points = new Vector3[vertCount];

            float zOffset = owner != null ? owner.LineZOffset : -0.5f;

            Vector3[] baseSamples = new Vector3[vertCount];
            for (int i = 0; i < vertCount; i++)
            {
                float t = (float)i / (vertCount - 1);
                baseSamples[i] = CubicBezier(p0, p1, p2, p3, t);
            }
            float totalLen = 0f;
            for (int i = 0; i < vertCount - 1; i++)
                totalLen += (baseSamples[i + 1] - baseSamples[i]).magnitude;
            float avgSeg = totalLen / Mathf.Max(1, vertCount - 1);
            float maxOff = jaggedness * avgSeg * 1.5f;

            // Endpoints inherit each bezier sample's own Z (so muzzle Z != 0 setups
            // and Z-bowed control points produce a curve that actually leaves the
            // muzzle plane in the middle and returns at the end).
            points[0] = baseSamples[0]; points[0].z += zOffset;
            points[vertCount - 1] = baseSamples[vertCount - 1]; points[vertCount - 1].z += zOffset;

            for (int i = 1; i < vertCount - 1; i++)
            {
                Vector3 a = baseSamples[i - 1];
                Vector3 b = baseSamples[i + 1];
                Vector3 tangent = b - a;
                if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.right;
                Vector3 axis = tangent.normalized;
                // Perp jitter is XY-only — Z swerve already comes from the bezier
                // control polygon, so layering perp Z noise on top would just smear it.
                Vector3 perp = new Vector3(-axis.y, axis.x, 0f);

                float t = (float)i / (vertCount - 1);
                float taper = Mathf.Sin(t * Mathf.PI);
                float off = Random.Range(-maxOff, maxOff) * taper;

                Vector3 p = baseSamples[i] + perp * off;
                p.z += zOffset;
                points[i] = p;
            }
        }

        private static Vector3 CubicBezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            float u = 1f - t;
            float uu = u * u, uuu = uu * u;
            float tt = t * t, ttt = tt * t;
            return uuu * a + 3f * uu * t * b + 3f * u * tt * c + ttt * d;
        }

        private void BuildPolylineJaggedPath(System.Collections.Generic.List<Vector3> waypoints,
                                             float segmentLength, float jaggedness)
        {
            float zOffset = owner != null ? owner.LineZOffset : -0.5f;

            // Walk waypoints, sub-sampling each leg by segmentLength so the resulting
            // vertex count matches the bezier path's density. baseSamples preserves
            // the polyline's own Z (so navgraph nodes with non-zero Z keep their depth).
            var samples = new System.Collections.Generic.List<Vector3>(64);
            if (waypoints == null || waypoints.Count < 2)
            {
                // Degenerate input — make a 2-vertex line at whatever we got.
                Vector3 a = waypoints != null && waypoints.Count > 0 ? waypoints[0] : Vector3.zero;
                samples.Add(a);
                samples.Add(a + Vector3.right * 0.01f);
            }
            else
            {
                samples.Add(waypoints[0]);
                for (int i = 0; i < waypoints.Count - 1; i++)
                {
                    Vector3 a = waypoints[i];
                    Vector3 b = waypoints[i + 1];
                    float legLen = Vector3.Distance(a, b);
                    int divs = Mathf.Max(1, Mathf.CeilToInt(legLen / segmentLength));
                    for (int j = 1; j <= divs; j++)
                    {
                        float t = (float)j / divs;
                        samples.Add(Vector3.Lerp(a, b, t));
                    }
                }
            }

            int vertCount = samples.Count;
            points = new Vector3[vertCount];

            // Compute perp jitter scale from average sample length, plus the
            // cumulative arc length of the polyline (used to drive smooth
            // low-frequency Perlin wander along the path).
            float totalLen = 0f;
            float[] arc = new float[vertCount];
            arc[0] = 0f;
            for (int i = 0; i < vertCount - 1; i++)
            {
                float seg = (samples[i + 1] - samples[i]).magnitude;
                totalLen += seg;
                arc[i + 1] = totalLen;
            }
            float avgSeg = totalLen / Mathf.Max(1, vertCount - 1);
            float maxOff = jaggedness * avgSeg * 1.5f;

            // Smooth path-noise params from owner. Per-tendril seed makes each
            // bolt drift independently so a fan of tendrils all rendering the
            // same nav-path don't sit on top of each other.
            float noiseAmp = owner != null ? owner.TendrilPathNoiseAmplitude : 0f;
            float noiseFreq = owner != null ? owner.TendrilPathNoiseFrequency : 0f;
            float noiseSeed = Random.Range(0f, 1000f);

            points[0] = samples[0]; points[0].z += zOffset;
            points[vertCount - 1] = samples[vertCount - 1]; points[vertCount - 1].z += zOffset;

            for (int i = 1; i < vertCount - 1; i++)
            {
                Vector3 a = samples[i - 1];
                Vector3 b = samples[i + 1];
                Vector3 tangent = b - a;
                if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.right;
                Vector3 axis = tangent.normalized;
                Vector3 perp = new Vector3(-axis.y, axis.x, 0f);

                // Taper: zero at endpoints, peak at the middle. For polylines with sharp
                // corners, we still want the noise to dampen at the very ends.
                float t = (float)i / (vertCount - 1);
                float taper = Mathf.Sin(t * Mathf.PI);

                // Per-vertex high-freq jaggedness (lightning crackle).
                float off = Random.Range(-maxOff, maxOff) * taper;

                // Smooth low-freq wander along arc length — Perlin sampled at
                // arc[i] * freq remaps -1..1, scaled by noiseAmp and tapered the
                // same way. Independent from jaggedness — the bolt drifts off
                // the waypoints between corners instead of just spiking around.
                if (noiseAmp > 0f)
                {
                    float u = arc[i] * noiseFreq + noiseSeed;
                    float n = Mathf.PerlinNoise(u, noiseSeed * 0.37f) * 2f - 1f;
                    off += n * noiseAmp * taper;
                }

                Vector3 p = samples[i] + perp * off;
                p.z += zOffset;
                points[i] = p;
            }
        }

        private void CreateLine()
        {
            lr = gameObject.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 2;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Stretch;
            lr.sortingOrder = 100;
            lr.widthMultiplier = baseWidth;
            lr.positionCount = points != null ? points.Length : 2;

            Material mat = owner != null ? owner.GetCoreMaterial() : null;
            if (mat == null)
            {
                Shader sh = Shader.Find("Sprites/Default");
                if (sh == null) sh = Shader.Find("UI/Default");
                mat = new Material(sh);
                mat.mainTexture = Texture2D.whiteTexture;
            }
            lr.material = mat;

            Color c = owner != null ? owner.CoreColor : new Color(0.85f, 0.96f, 1f, 1f);
            lr.startColor = c;
            lr.endColor = c;

            // Initialize all positions to the start point so we never render a stray line at world origin.
            if (points != null)
            {
                for (int i = 0; i < points.Length; i++) lr.SetPosition(i, points[0]);
            }
        }

        void Update()
        {
            if (SlimeJumping.Effects.WorldTimeFreezeService.IsFrozen) return;
            age += Time.deltaTime;
            float drawT = Mathf.Clamp01(age / drawTime);

            if (!tipHalted)
            {
                CheckCollisionStep(prevDrawT, drawT);
                prevDrawT = drawT;
            }

            if (tipHalted) ApplyHaltedDraw();
            else ApplyDrawProgress(drawT);

            // Hold + fade timing is anchored at the moment the bolt actually stopped
            // advancing — either on collision (haltedAge) or on full-range completion
            // (drawTime). This way an early-halted bolt doesn't "wait" for the
            // would-have-been full draw before starting its hold/fade.
            float effectiveDrawEnd = haltedAge >= 0f ? haltedAge : drawTime;
            float holdEnd = effectiveDrawEnd + holdTime;
            float fadeEnd = holdEnd + fadeTime;

            if (age >= fadeEnd)
            {
                Destroy(gameObject);
                return;
            }

            if (age >= effectiveDrawEnd)
            {
                float pulse = 1f + 0.18f * (Mathf.PerlinNoise(age * 14f, 0f) - 0.5f);
                lr.widthMultiplier = baseWidth * pulse;
            }

            if (age >= holdEnd)
            {
                float fadeT = Mathf.Clamp01((age - holdEnd) / fadeTime);
                float alpha = 1f - fadeT;
                Color c = owner != null ? owner.CoreColor : Color.white;
                c.a *= alpha;
                lr.startColor = c;
                lr.endColor = c;
            }
        }

        private void CheckCollisionStep(float prevT, float currT)
        {
            if (points == null || points.Length < 2 || owner == null) return;
            LayerMask mask = owner.BulletHitMask;
            if (mask == 0) return;

            Vector3 prevTip = TipAt(prevT);
            Vector3 currTip = TipAt(currT);
            Vector2 a = prevTip;
            Vector2 delta = (Vector2)currTip - a;
            float dist = delta.magnitude;
            if (dist < 0.0001f) return;

            Vector2 dir = delta / dist;
            float radius = Mathf.Max(0.01f, owner.HitRadius);
            var hit = owner.CircleCastExcludingSelf(a, radius, dir, dist, mask);
            if (hit.collider == null) return;

            // Halt the bolt right at the hit point.
            tipHalted = true;
            haltedAge = age;
            haltedTipWorld = new Vector3(hit.point.x, hit.point.y, prevTip.z);

            int n = points.Length;
            int segs = n - 1;
            float exact = Mathf.Clamp01(currT) * segs;
            haltedLastFullIdx = Mathf.Clamp(Mathf.FloorToInt(exact), 0, n - 1);

            // Slime push if the hit is a slime — one impulse per tendril, no stun.
            // Direction is muzzle→slime so the slime is shoved away from the cannon.
            var slime = hit.collider.GetComponentInParent<SlimeController>();
            bool isSlime = slime != null;
            if (isSlime && canHit && !hitResolved)
            {
                hitResolved = true;
                Vector3 emitterOrigin = owner.AimOrigin();
                Vector2 away = (Vector2)slime.transform.position - new Vector2(emitterOrigin.x, emitterOrigin.y);
                Vector2 pushDir = away.sqrMagnitude > 0.0001f ? away.normalized : dir;
                if (!owner.TryApplyStunOnHit(slime, pushDir, owner.KnockbackImpulse, hit.point))
                {
                    slime.ApplyHazardPush(pushDir * owner.KnockbackImpulse, hit.point);
                }
                owner.TryFireHitStopFeedback();
            }

            SpawnImpact(hit.point, hit.normal);
            owner.SpawnGroundExplosion(hit.point, hit.normal);
            if (isSlime)
            {
                // Tendril doesn't pass through the slime — carve snow around the slime
                // body so the visible bite still appears even though the bolt halts here.
                owner.CarveSnowNearSlime(slime, hit.point);
            }
            else
            {
                owner.CarveSnowAtImpact(hit.collider, hit.point);
            }
            owner.PlayGroundImpactSound(hit.point);
        }

        private Vector3 TipAt(float t)
        {
            int n = points.Length;
            int segs = n - 1;
            float exact = Mathf.Clamp01(t) * segs;
            int lastFull = Mathf.FloorToInt(exact);
            if (lastFull >= segs) return points[n - 1];
            float frac = exact - lastFull;
            return Vector3.Lerp(points[lastFull], points[lastFull + 1], frac);
        }

        private void ApplyHaltedDraw()
        {
            if (lr == null || points == null) return;
            int visibleVerts = Mathf.Min(points.Length, haltedLastFullIdx + 2);
            if (visibleVerts < 2) visibleVerts = 2;
            lr.positionCount = visibleVerts;
            for (int i = 0; i < visibleVerts - 1; i++) lr.SetPosition(i, points[i]);
            lr.SetPosition(visibleVerts - 1, haltedTipWorld);
        }

        private void ApplyDrawProgress(float t)
        {
            if (lr == null || points == null || points.Length < 2) return;
            int n = points.Length;
            int segs = n - 1;
            float exact = Mathf.Clamp01(t) * segs;
            int lastFull = Mathf.FloorToInt(exact);
            float frac = exact - lastFull;

            int visibleVerts;
            Vector3 tipOverride = points[n - 1];
            bool overrideTip;
            if (lastFull >= segs)
            {
                visibleVerts = n;
                overrideTip = false;
            }
            else
            {
                visibleVerts = lastFull + 2;
                tipOverride = Vector3.Lerp(points[lastFull], points[lastFull + 1], frac);
                overrideTip = true;
            }

            lr.positionCount = visibleVerts;
            int last = visibleVerts - 1;
            for (int i = 0; i < last; i++) lr.SetPosition(i, points[i]);
            lr.SetPosition(last, overrideTip ? tipOverride : points[last]);
        }

        private void SpawnImpact(Vector3 point, Vector3 normal)
        {
            if (owner == null) return;
            var prefab = owner.ImpactPrefab;
            if (prefab == null) return;

            Vector3 n = normal.sqrMagnitude > 0.0001f ? normal : Vector3.up;
            Quaternion rot = Quaternion.LookRotation(Vector3.forward, n);
            Vector3 spawnPos = new Vector3(point.x, point.y, point.z + owner.LineZOffset);
            var inst = Instantiate(prefab, spawnPos, rot);
            if (!inst.gameObject.activeSelf) inst.gameObject.SetActive(true);
            inst.Play(true);
            var main = inst.main;
            float life = main.duration + main.startLifetime.constantMax + 0.2f;
            Destroy(inst.gameObject, life);
        }
    }
}
