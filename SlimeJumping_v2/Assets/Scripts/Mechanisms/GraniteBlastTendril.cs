using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    /// <summary>One curved lightning tendril (bezier or nav polyline) for GraniteBlastEmitter's Branching mode.</summary>
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

        // Tip's prev→curr chord is CircleCast each frame; the bolt freezes at the hit point.
        // Slime that left the line since fire time no longer halts it.
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

        // Polyline-init variant for NavGraph A* paths; everything downstream only reads points[].
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

            // Endpoints keep each bezier sample's own Z, so muzzle Z != 0 and Z-bowed control points survive.
            points[0] = baseSamples[0]; points[0].z += zOffset;
            points[vertCount - 1] = baseSamples[vertCount - 1]; points[vertCount - 1].z += zOffset;

            for (int i = 1; i < vertCount - 1; i++)
            {
                Vector3 a = baseSamples[i - 1];
                Vector3 b = baseSamples[i + 1];
                Vector3 tangent = b - a;
                if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.right;
                Vector3 axis = tangent.normalized;
                // XY-only perp jitter — adding Z noise would just smear the bezier's own Z swerve.
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

            // Per-tendril seed, else a fan of bolts on the same nav-path sits on top of itself.
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

                float t = (float)i / (vertCount - 1);
                float taper = Mathf.Sin(t * Mathf.PI);

                // Per-vertex high-freq jaggedness (lightning crackle).
                float off = Random.Range(-maxOff, maxOff) * taper;

                // Low-freq wander along arc length, independent of jaggedness — the bolt
                // drifts off the waypoints between corners instead of only spiking.
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

            // Seed every position at the start point, else LineRenderer draws a stray line from world origin.
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

            // Hold/fade anchor at the moment the bolt stopped advancing, else an early-halted
            // bolt waits out the would-have-been full draw first.
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

            tipHalted = true;
            haltedAge = age;
            haltedTipWorld = new Vector3(hit.point.x, hit.point.y, prevTip.z);

            int n = points.Length;
            int segs = n - 1;
            float exact = Mathf.Clamp01(currT) * segs;
            haltedLastFullIdx = Mathf.Clamp(Mathf.FloorToInt(exact), 0, n - 1);

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
                // Bolt halts at the body instead of passing through, so carve around it or no bite appears.
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
