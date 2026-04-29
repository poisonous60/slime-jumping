using UnityEngine;
using SlimeJumping.Audio;

namespace SlimeJumping.Mechanisms
{
    // Impact effects — shared LineRenderer material for projectiles, ground-impact
    // sound + big explosion VFX, and selection gizmos. Slime-hit small spark uses
    // impactPrefab directly from the Tendril/Beam classes; the big chunky explosion
    // here is for ground hits only.
    public partial class GraniteBlastEmitter
    {
        // --- shared material for line projectiles ---
        private Material coreMatCached;

        public Material GetCoreMaterial() => GetOrCreateMat(ref coreMatCached);

        // Called by tendril / beam / burst-flash on ground impact. Each spawned
        // projectile is responsible for invoking this once when its line first
        // reaches the ground (or completes its draw to a ground-snapped endpoint).
        public void PlayGroundImpactSound(Vector3 worldPos)
        {
            if (groundImpactSound == null) return;
            float pitch = 1f;
            if (groundImpactPitchJitter > 0f)
                pitch = 1f + Random.Range(-groundImpactPitchJitter, groundImpactPitchJitter);
            SoundService.PlayAt(groundImpactSound, worldPos, SoundCategory.SFX_Object, groundImpactVolume, pitch);
        }

        // Spawn the big chunky explosion VFX at a ground hit point. Slime hits do NOT
        // call this — they just play impactPrefab (small spark). Caller should already
        // have classified the hit (slime vs ground).
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

            // Hit-volume preview: a swept "tube" from muzzle along the aim direction
            // shows the actual CircleCast radius used by all three modes.
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
        }
    }
}
