using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;
using SlimeJumping.Platforms.Snow;

namespace SlimeJumping.Mechanisms
{
    // Impact effects: shared line material, ground-impact sound, explosion VFX, gizmos.
    // Tendril/Beam own the small spark via impactPrefab and gate the explosion once per hit.
    public partial class GraniteBlastEmitter
    {
        private Material coreMatCached;

        public Material GetCoreMaterial() => GetOrCreateMat(ref coreMatCached);

        // Caller contract: each projectile invokes this once, when its line first reaches ground.
        public void PlayGroundImpactSound(Vector3 worldPos)
        {
            if (groundImpactSound == null) return;
            float pitch = 1f;
            if (groundImpactPitchJitter > 0f)
                pitch = 1f + Random.Range(-groundImpactPitchJitter, groundImpactPitchJitter);
            SoundService.PlayAt(groundImpactSound, worldPos, SoundCategory.SFX_Object, groundImpactVolume, pitch);
        }

        // Routes through the same channel as slime landing so the bite looks identical.
        public void CarveSnowAtImpact(Collider2D hostCollider, Vector2 contactWorld)
        {
            if (!snowCarveOnImpact) return;
            if (snowCarveDepth <= 0f || snowCarveRadius <= 0f) return;
            if (hostCollider == null) return;
            if (!SnowFieldRegistry.TryGet(hostCollider, out var field)) return;
            if (field == null) return;
            field.Carve(contactWorld, snowCarveDepth, snowCarveRadius);
        }

        // A bolt that stops on the slime body never reaches the snow underneath, so carve here:
        // SurfaceCollider first, then an OverlapCircle scan for airborne hits over snow.
        public void CarveSnowNearSlime(SlimeController slime, Vector2 contactWorld)
        {
            if (!snowCarveOnImpact) return;
            if (snowCarveDepth <= 0f || snowCarveRadius <= 0f) return;
            if (slime == null) return;

            Collider2D host = slime.SurfaceCollider;
            if (host != null && SnowFieldRegistry.TryGet(host, out var field) && field != null)
            {
                field.Carve(contactWorld, snowCarveDepth, snowCarveRadius);
                return;
            }

            // Scan radius covers the carve footprint; first registered SnowField wins.
            float scanR = Mathf.Max(snowCarveRadius, 1f);
            var hits = Physics2D.OverlapCircleAll(contactWorld, scanR);
            for (int i = 0; i < hits.Length; i++)
            {
                if (SnowFieldRegistry.TryGet(hits[i], out var f) && f != null)
                {
                    f.Carve(contactWorld, snowCarveDepth, snowCarveRadius);
                    return;
                }
            }
        }

        // Caller must gate to once per hit, else a sustained beam restages the explosion every frame.
        public void SpawnGroundExplosion(Vector3 point, Vector3 normal)
        {
            if (groundExplosionPrefab == null) return;
            Vector3 n = normal.sqrMagnitude > 0.0001f ? normal : Vector3.up;
            Quaternion rot = Quaternion.LookRotation(Vector3.forward, n);
            Vector3 spawnPos = new Vector3(point.x, point.y, point.z + lineZOffset);
            var inst = Instantiate(groundExplosionPrefab, spawnPos, rot);
            if (!inst.gameObject.activeSelf) inst.gameObject.SetActive(true);
            inst.Play(true);
            var main = inst.main;
            float life = main.duration + main.startLifetime.constantMax + 0.3f;
            Destroy(inst.gameObject, life);
        }

        private Material GetOrCreateMat(ref Material slot)
        {
            if (slot != null) return slot;
            // Same setup as TurretHazard's bulletTracer — known to render visibly in this project.
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("UI/Default");
            slot = new Material(sh);
            slot.mainTexture = Texture2D.whiteTexture;
            return slot;
        }

        // --- Gizmos ---

        void OnDrawGizmosSelected()
        {
            Vector3 origin = AimOrigin();
            Gizmos.color = new Color(0.5f, 0.85f, 1f, 0.35f);
            Gizmos.DrawWireSphere(origin, detectionRange);

            Vector2 d = AimDirection2D();
            Vector3 d3 = new Vector3(d.x, d.y, 0f);
            Gizmos.color = new Color(0.7f, 0.95f, 1f, 0.9f);
            Gizmos.DrawLine(origin, origin + d3 * Mathf.Min(maxRange, detectionRange));

            // Swept tube previewing the CircleCast radius all three modes use.
            float r = Mathf.Max(0.01f, hitRadius);
            float previewLen = Mathf.Min(maxRange, detectionRange);
            Vector3 perp = new Vector3(-d.y, d.x, 0f) * r;
            Vector3 endP = origin + d3 * previewLen;
            Gizmos.color = new Color(1f, 0.4f, 0.4f, 0.5f);
            Gizmos.DrawWireSphere(origin, r);
            Gizmos.DrawWireSphere(endP, r);
            Gizmos.DrawLine(origin + perp, endP + perp);
            Gizmos.DrawLine(origin - perp, endP - perp);

            if (mode == FireMode.Branching && target != null)
            {
                Gizmos.color = new Color(1f, 0.7f, 0.3f, 0.5f);
                Gizmos.DrawWireSphere(target.position, targetRadius);
            }

            // Close / Mid band boundaries for distance-based mode switching.
            if (distanceBasedMode)
            {
                Gizmos.color = new Color(1f, 0.4f, 0.4f, 0.85f);
                Gizmos.DrawWireSphere(origin, Mathf.Max(0f, closeRangeDistance));

                if (midRangeDistance > closeRangeDistance)
                {
                    Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.85f);
                    Gizmos.DrawWireSphere(origin, midRangeDistance);
                }
            }
        }
    }
}
