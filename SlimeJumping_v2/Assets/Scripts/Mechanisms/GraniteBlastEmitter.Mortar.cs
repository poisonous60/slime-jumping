using UnityEngine;
using SlimeJumping.Audio;

namespace SlimeJumping.Mechanisms
{
    // Mortar mode — single circular projectiles ejected radially around the muzzle, each
    // travels to its own orbit anchor (curve-shaped speed), dwells for a hover beat, then
    // accelerates toward the slime (curve-shaped speed, optional homing). Multiple
    // projectiles are released one-at-a-time at spawnInterval seconds across the
    // FireStart + Firing window.
    //
    // Timing per projectile is independent: the i-th projectile's "dwell → strike"
    // transition fires (travelToOrbitDuration + dwellAtOrbitDuration) seconds AFTER its
    // own spawn — so adjacent projectiles overlap their strike phases. Designer tunes the
    // total-fire feel via spawnInterval / travelToOrbitDuration / dwellAtOrbitDuration
    // (all three are independent).
    public partial class GraniteBlastEmitter
    {
        // Counter of mortars released since FireStart. Reset in EnterIdle/EnterCooldown
        // through SpawnFiringWaveIfDue's caller (we hook via EnterIdle).
        private int mortarsSpawned;
        private float mortarBurstStartTime;

        private void PlanMortar()
        {
            mortarsSpawned = 0;
            mortarBurstStartTime = Time.time;
        }

        // Time-driven release: at every Update during FireStart + Firing, spawn one
        // projectile if (spawnInterval * mortarsSpawned) seconds have elapsed since the
        // burst started. Caps at maxProjectiles (when > 0). Spawning HALTS at PostFire
        // entry by design — trailing projectiles arrive within the configured fire
        // window so the burst feels like a single salvo.
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

            // Per-projectile fire SFX so each launch has its own crack (matches Branching).
            if (fireStartSound != null)
            {
                float pitch = fireStartPitchJitter > 0f
                    ? 1f + Random.Range(-fireStartPitchJitter, fireStartPitchJitter)
                    : 1f;
                SoundService.PlayAt(fireStartSound, muzzleOrigin, SoundCategory.SFX_Object,
                    fireStartVolume, pitch);
            }

            // Random ejection direction in XY — biased upward by upwardBias (0..1).
            // bias=0 → uniform 360°, bias=1 → straight up. Intermediate values rotate
            // a random unit vector toward (0,1) by Slerp.
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
