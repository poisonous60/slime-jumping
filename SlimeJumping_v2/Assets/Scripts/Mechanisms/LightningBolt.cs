using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;
using SlimeJumping.Effects;
using SlimeJumping.Platforms.Snow;

namespace SlimeJumping.Mechanisms
{
    /// <summary>One falling lightning bolt: a jagged bezier line that draws from sky to ground and halts on its first hit.</summary>
    public class LightningBolt : MonoBehaviour
    {
        // One material for every bolt in the scene — same setup as GraniteBlast's line core.
        private static Material sharedMat;
        private static AudioListener listener;
        // Bolts are one-shot objects, so the slime's immunity window has to live outside them.
        private static float lastSlimeHitTime = float.NegativeInfinity;

        private LightningStrikeConfig cfg;
        private ImpulseEmitter shakeEmitter;
        private Vector3[] points;
        private LineRenderer lr;
        private float baseWidth;
        private float drawTime, holdTime, fadeTime;
        private float age, prevDrawT;
        private bool hitResolved;
        // Every bolt of a burst hits, but only the leading one shows the impact — one strike
        // must read as one explosion with one sound, not N stacked.
        private bool carriesImpact;

        // Tip's prev→curr chord is CircleCast each frame; the bolt freezes at the hit point.
        private bool tipHalted;
        private int haltedLastFullIdx;
        private Vector3 haltedTipWorld;
        private float haltedAge = -1f;

        public void Init(LightningStrikeConfig config, ImpulseEmitter shake, Vector3 from, Vector3 to, bool carriesImpact = true)
        {
            cfg = config;
            shakeEmitter = shake;
            this.carriesImpact = carriesImpact;
            baseWidth = Mathf.Max(0.001f, cfg.coreWidth);
            holdTime = Mathf.Max(0f, cfg.holdTime);
            fadeTime = Mathf.Max(0.001f, cfg.fadeTime);

            // Chord, not arc length, so tip speed along the fall line matches travelSpeed
            // however far the sway pushes the middle out.
            float chord = Vector3.Distance(from, to);
            drawTime = cfg.travelSpeed > 0f
                ? Mathf.Max(0.001f, chord / cfg.travelSpeed)
                : Mathf.Max(0.001f, cfg.drawTime);

            BuildPath(from, to);
            CreateLine();
            ApplyDrawProgress(0f);
        }

        private void BuildPath(Vector3 p0, Vector3 p3)
        {
            Vector3 fall = p3 - p0;
            Vector3 axis = fall.sqrMagnitude > 0.0001f ? fall.normalized : Vector3.down;
            Vector3 perp = new Vector3(-axis.y, axis.x, 0f);

            // Mid control points sway sideways and in depth; endpoints keep the anchor's Z.
            Vector3 p1 = Vector3.Lerp(p0, p3, 0.33f)
                       + perp * Random.Range(-cfg.lateralSway, cfg.lateralSway)
                       + Vector3.forward * Random.Range(-cfg.depthSway, cfg.depthSway);
            Vector3 p2 = Vector3.Lerp(p0, p3, 0.67f)
                       + perp * Random.Range(-cfg.lateralSway, cfg.lateralSway)
                       + Vector3.forward * Random.Range(-cfg.depthSway, cfg.depthSway);

            // Control polygon length is an upper bound on arc length — denser segments for longer bolts.
            float segLen = Mathf.Max(0.1f, cfg.segmentLength);
            float polyLen = Vector3.Distance(p0, p1) + Vector3.Distance(p1, p2) + Vector3.Distance(p2, p3);
            int segments = Mathf.Clamp(Mathf.RoundToInt(polyLen / segLen), 6, 48);

            int vertCount = segments + 1;
            points = new Vector3[vertCount];

            Vector3[] baseSamples = new Vector3[vertCount];
            for (int i = 0; i < vertCount; i++)
                baseSamples[i] = CubicBezier(p0, p1, p2, p3, (float)i / (vertCount - 1));

            float totalLen = 0f;
            for (int i = 0; i < vertCount - 1; i++)
                totalLen += (baseSamples[i + 1] - baseSamples[i]).magnitude;
            float maxOff = cfg.jaggedness * (totalLen / Mathf.Max(1, vertCount - 1)) * 1.5f;

            float zOffset = cfg.lineZOffset;
            points[0] = baseSamples[0]; points[0].z += zOffset;
            points[vertCount - 1] = baseSamples[vertCount - 1]; points[vertCount - 1].z += zOffset;

            for (int i = 1; i < vertCount - 1; i++)
            {
                Vector3 tangent = baseSamples[i + 1] - baseSamples[i - 1];
                if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.down;
                Vector3 a = tangent.normalized;
                // XY-only perp jitter — Z noise would just smear the bezier's own depth swerve.
                Vector3 n = new Vector3(-a.y, a.x, 0f);

                float t = (float)i / (vertCount - 1);
                // sin taper keeps both ends smooth, so only the middle kinks.
                float off = Random.Range(-maxOff, maxOff) * Mathf.Sin(t * Mathf.PI);

                Vector3 p = baseSamples[i] + n * off;
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
            lr.positionCount = points.Length;
            lr.material = GetSharedMaterial();
            lr.startColor = cfg.coreColor;
            lr.endColor = cfg.coreColor;

            // Seed every position at the start point, else LineRenderer draws a stray line from world origin.
            for (int i = 0; i < points.Length; i++) lr.SetPosition(i, points[0]);
        }

        // Telegraph arcs draw with the same material so the scene keeps one instance.
        internal static Material SharedMaterial => GetSharedMaterial();

        private static Material GetSharedMaterial()
        {
            if (sharedMat != null) return sharedMat;
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("UI/Default");
            sharedMat = new Material(sh);
            sharedMat.mainTexture = Texture2D.whiteTexture;
            return sharedMat;
        }

        void Update()
        {
            if (WorldTimeFreezeService.IsFrozen) return;
            age += Time.deltaTime;
            float drawT = Mathf.Clamp01(age / drawTime);

            if (!tipHalted)
            {
                CheckCollisionStep(prevDrawT, drawT);
                prevDrawT = drawT;
            }

            if (tipHalted) ApplyHaltedDraw();
            else ApplyDrawProgress(drawT);

            // Hold/fade anchor at the moment the bolt stopped advancing, else a bolt that
            // landed early waits out the would-have-been full fall first.
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
                float alpha = 1f - Mathf.Clamp01((age - holdEnd) / fadeTime);
                Color c = cfg.coreColor;
                c.a *= alpha;
                lr.startColor = c;
                lr.endColor = c;
            }
        }

        private void CheckCollisionStep(float prevT, float currT)
        {
            LayerMask mask = cfg.hitMask;
            if (mask == 0) return;

            Vector3 prevTip = TipAt(prevT);
            Vector3 currTip = TipAt(currT);
            Vector2 a = prevTip;
            Vector2 delta = (Vector2)currTip - a;
            float dist = delta.magnitude;
            if (dist < 0.0001f) return;

            var hit = Cast(a, delta / dist, dist, mask);
            if (hit.collider == null) return;

            tipHalted = true;
            haltedAge = age;
            haltedTipWorld = new Vector3(hit.point.x, hit.point.y, prevTip.z);

            int segs = points.Length - 1;
            haltedLastFullIdx = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(currT) * segs), 0, points.Length - 1);

            var slime = hit.collider.GetComponentInParent<SlimeController>();
            bool isSlime = slime != null;
            // The bolt still halts on a slime inside the window; only the punt is withheld.
            if (isSlime && !hitResolved && Time.time - lastSlimeHitTime >= Mathf.Max(0f, cfg.slimeHitCooldown))
            {
                hitResolved = true;
                lastSlimeHitTime = Time.time;
                // Flung sideways, away from the strike column — a downward push would only
                // pin a grounded slime into the floor. Overwrite-slide, not the plain push channel:
                // a grounded slime rebuilds velocity from crawlVelocity every frame, so a one-shot
                // deltaV survives a single FixedUpdate and barely moves it.
                float side = slime.Position.x >= points[0].x ? 1f : -1f;
                slime.ApplyHazardPushOverwriteSlide(new Vector2(side * cfg.knockbackImpulse, 0f), hit.point);
                HitStopService.Freeze(cfg.hitStopDuration, shakeEmitter, cfg.hitShakeForce, cfg.hitStopFrozenScale);
            }

            if (!carriesImpact) return;

            SpawnImpact(cfg.impactPrefab, hit.point, hit.normal, 1f);
            SpawnImpact(cfg.groundExplosionPrefab, hit.point, hit.normal, cfg.groundExplosionScale);
            if (isSlime) CarveSnowNearSlime(slime, hit.point);
            else CarveSnow(hit.collider, hit.point);
            PlayImpactSound(hit.point);
        }

        // Zone triggers (ColdZone, snow areas) enclose the sky anchors; a start-overlap hit reports
        // distance 0 with the zone's own edge as the point, which would bend the bolt onto it.
        private RaycastHit2D Cast(Vector2 origin, Vector2 dir, float maxDist, LayerMask mask)
        {
            var hits = Physics2D.CircleCastAll(origin, Mathf.Max(0.01f, cfg.hitRadius), dir, maxDist, mask);
            RaycastHit2D best = default;
            float bestDist = float.PositiveInfinity;
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null) continue;
                if (h.distance <= 0f) continue;
                if (h.collider.isTrigger && h.collider.GetComponentInParent<SlimeController>() == null) continue;
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    best = h;
                }
            }
            return best;
        }

        private Vector3 TipAt(float t)
        {
            int n = points.Length;
            int segs = n - 1;
            float exact = Mathf.Clamp01(t) * segs;
            int lastFull = Mathf.FloorToInt(exact);
            if (lastFull >= segs) return points[n - 1];
            return Vector3.Lerp(points[lastFull], points[lastFull + 1], exact - lastFull);
        }

        private void ApplyHaltedDraw()
        {
            int visibleVerts = Mathf.Max(2, Mathf.Min(points.Length, haltedLastFullIdx + 2));
            lr.positionCount = visibleVerts;
            for (int i = 0; i < visibleVerts - 1; i++) lr.SetPosition(i, points[i]);
            lr.SetPosition(visibleVerts - 1, haltedTipWorld);
        }

        private void ApplyDrawProgress(float t)
        {
            int n = points.Length;
            int segs = n - 1;
            float exact = Mathf.Clamp01(t) * segs;
            int lastFull = Mathf.FloorToInt(exact);

            int visibleVerts;
            Vector3 tipOverride;
            bool overrideTip;
            if (lastFull >= segs)
            {
                visibleVerts = n;
                tipOverride = points[n - 1];
                overrideTip = false;
            }
            else
            {
                visibleVerts = lastFull + 2;
                tipOverride = Vector3.Lerp(points[lastFull], points[lastFull + 1], exact - lastFull);
                overrideTip = true;
            }

            lr.positionCount = visibleVerts;
            int last = visibleVerts - 1;
            for (int i = 0; i < last; i++) lr.SetPosition(i, points[i]);
            lr.SetPosition(last, overrideTip ? tipOverride : points[last]);
        }

        private void SpawnImpact(ParticleSystem prefab, Vector3 point, Vector3 normal, float scale)
        {
            if (prefab == null) return;
            Vector3 n = normal.sqrMagnitude > 0.0001f ? normal : Vector3.up;
            Quaternion rot = Quaternion.LookRotation(Vector3.forward, n);
            var inst = Instantiate(prefab, new Vector3(point.x, point.y, point.z + cfg.lineZOffset), rot);
            // Templates run in Local scaling mode, so the transform scales size, speed and shape together.
            if (scale > 0f && scale != 1f) inst.transform.localScale *= scale;
            if (!inst.gameObject.activeSelf) inst.gameObject.SetActive(true);
            inst.Play(true);
            var main = inst.main;
            Destroy(inst.gameObject, main.duration + main.startLifetime.constantMax + 0.3f);
        }

        // Routes through the same channel as slime landing so the bite looks identical.
        private void CarveSnow(Collider2D hostCollider, Vector2 contactWorld)
        {
            if (!cfg.snowCarveOnImpact) return;
            if (cfg.snowCarveDepth <= 0f || cfg.snowCarveRadius <= 0f) return;
            if (hostCollider == null) return;
            if (!SnowFieldRegistry.TryGet(hostCollider, out var field)) return;
            if (field == null) return;
            field.Carve(contactWorld, cfg.snowCarveDepth, cfg.snowCarveRadius);
        }

        // A bolt that stops on the slime body never reaches the snow underneath, so carve here:
        // SurfaceCollider first, then an OverlapCircle scan for airborne hits over snow.
        private void CarveSnowNearSlime(SlimeController slime, Vector2 contactWorld)
        {
            if (!cfg.snowCarveOnImpact) return;
            if (cfg.snowCarveDepth <= 0f || cfg.snowCarveRadius <= 0f) return;

            Collider2D host = slime.SurfaceCollider;
            if (host != null && SnowFieldRegistry.TryGet(host, out var field) && field != null)
            {
                field.Carve(contactWorld, cfg.snowCarveDepth, cfg.snowCarveRadius);
                return;
            }

            // Scan radius covers the carve footprint; first registered SnowField wins.
            var hits = Physics2D.OverlapCircleAll(contactWorld, Mathf.Max(cfg.snowCarveRadius, 1f));
            for (int i = 0; i < hits.Length; i++)
            {
                if (SnowFieldRegistry.TryGet(hits[i], out var f) && f != null)
                {
                    f.Carve(contactWorld, cfg.snowCarveDepth, cfg.snowCarveRadius);
                    return;
                }
            }
        }

        private void PlayImpactSound(Vector3 worldPos)
        {
            float falloff = DistanceFalloff(worldPos);
            if (falloff <= 0.001f) return;
            SoundService.Play(cfg.impactSound, worldPos, falloff);
        }

        // The listener rides the camera, a fixed ~12 units off the play plane, and that constant
        // flattens any rolloff — measure XY only so the knobs read as on-screen distance.
        private float DistanceFalloff(Vector3 worldPos)
        {
            if (listener == null) listener = UnityEngine.Object.FindFirstObjectByType<AudioListener>();
            if (listener == null) return 1f;

            Vector3 lp = listener.transform.position;
            float d = Vector2.Distance(new Vector2(worldPos.x, worldPos.y), new Vector2(lp.x, lp.y));

            float full = Mathf.Max(0f, cfg.impactAudioFullDistance);
            float cut = Mathf.Max(full + 0.01f, cfg.impactAudioCutoffDistance);
            return Mathf.Pow(Mathf.InverseLerp(cut, full, d), cfg.impactAudioFalloffExponent);
        }
    }
}
