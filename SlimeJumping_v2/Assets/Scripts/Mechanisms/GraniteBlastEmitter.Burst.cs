using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    // Burst mode — a fan of short-lived beams across a spread cone. Each "burst flash"
    // is just a slim, brief GraniteBlastBeam.
    public partial class GraniteBlastEmitter
    {
        // Burst scheduling: precomputed bullet directions, consumed across FireStart + Firing.
        private readonly List<Vector2> pendingBullets = new List<Vector2>(16);
        private int bulletsSpawned;
        private int bulletsAtFireStart;
        // Time the active burst window started (FireStart entry). Used by interval mode
        // to spawn one flash every burstFlashInterval seconds.
        private float burstStartTime;

        private void PlanBurst()
        {
            pendingBullets.Clear();
            bulletsSpawned = 0;
            int n = Mathf.Max(1, burstFlashCount);
            bulletsAtFireStart = Mathf.Clamp(Mathf.RoundToInt(n * burstFireStartFraction), 1, n);

            float halfSpread = burstSpreadDegrees * 0.5f;
            float baseAngle = Mathf.Atan2(lockedDir.y, lockedDir.x);
            for (int i = 0; i < n; i++)
            {
                float t = n == 1 ? 0.5f : (float)i / (n - 1);
                float angleDeg = Mathf.Lerp(-halfSpread, halfSpread, t);
                float jitter = Random.Range(-2f, 2f);
                float a = baseAngle + (angleDeg + jitter) * Mathf.Deg2Rad;
                pendingBullets.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
            }
        }

        private void SpawnBulletsUpTo(int targetCount)
        {
            int target = Mathf.Min(targetCount, pendingBullets.Count);
            while (bulletsSpawned < target)
            {
                Vector2 v = pendingBullets[bulletsSpawned++];
                var go = new GameObject("GraniteBlast_BurstFlash");
                go.transform.position = lockedOrigin;
                var beam = go.AddComponent<GraniteBlastBeam>();
                beam.Init(this, muzzle != null ? muzzle : transform, v,
                    duration: burstFlashDuration,
                    fadeTime: burstFlashFadeTime,
                    coreWidth: burstFlashCoreWidth,
                    travelSpeed: burstFlashTravelSpeed);
            }
        }
    }
}
