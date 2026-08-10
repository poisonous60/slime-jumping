using UnityEngine;
using SlimeJumping.Audio;

namespace SlimeJumping.Mechanisms
{
    // Mortar mode — projectiles ejected radially, each travels to its own orbit anchor, dwells,
    // then strikes the slime. Timing is per-projectile: the i-th transitions travelToOrbit+dwell
    // seconds after its OWN spawn, so adjacent projectiles overlap their strike phases.
    public partial class GraniteBlastEmitter
    {
        private int mortarsSpawned;
        private float mortarBurstStartTime;

        private void PlanMortar()
        {
            mortarsSpawned = 0;
            mortarBurstStartTime = Time.time;
        }

        // One projectile per spawnInterval across FireStart + Firing. Spawning halts at PostFire
        // entry by design, so the whole salvo lands inside the configured fire window.
        private void SpawnNextMortarIfDue()
        {
            var cfg = mortarConfig;
            if (cfg == null) return;

            float interval = Mathf.Max(0.001f, cfg.spawnInterval);
            int maxN = cfg.maxProjectiles;

            float elapsed = Time.time - mortarBurstStartTime;
            // The first projectile fires at FireStart entry (mortarsSpawned=0, due at t=0).
            int desired = 1 + Mathf.FloorToInt(elapsed / interval);
            if (maxN > 0) desired = Mathf.Min(desired, maxN);

            while (mortarsSpawned < desired)
            {
                SpawnOneMortar(cfg);
                mortarsSpawned++;
            }
        }

        private void SpawnOneMortar(GraniteBlastMortarConfig cfg)
        {
            // Live muzzle position so a moving emitter still spawns correctly.
            Vector3 muzzleOrigin = AimOrigin();

            // Per-projectile fire SFX so each launch has its own crack.
            if (fireStartSound != null)
            {
                SoundService.Play(fireStartSound, muzzleOrigin);
            }

            // upwardBias 0 = uniform 360° ejection, 1 = straight up.
            float ang = Random.Range(0f, Mathf.PI * 2f);
            Vector2 randomDir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            Vector2 dir = Vector2.Lerp(randomDir, Vector2.up, Mathf.Clamp01(cfg.upwardBias));
            if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;
            dir.Normalize();

            var go = new GameObject("GraniteBlast_Mortar");
            go.transform.position = new Vector3(muzzleOrigin.x, muzzleOrigin.y,
                muzzleOrigin.z + cfg.zOffset);
            var mortar = go.AddComponent<GraniteBlastMortar>();
            mortar.Init(this, cfg, dir);
        }
    }
}
