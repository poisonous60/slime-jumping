using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    // Beam mode — single thick laser from the muzzle along the locked direction.
    // Rendering + sweep + sustainedHit live on GraniteBlastBeam; this partial only spawns it.
    public partial class GraniteBlastEmitter
    {
        private void SpawnBeam()
        {
            // Beam lives across all three firing phases so it stays committed.
            float beamLife = fireStartDuration + firingDuration + postFireDuration;
            var go = new GameObject("GraniteBlast_Beam");
            go.transform.position = lockedOrigin;
            var beam = go.AddComponent<GraniteBlastBeam>();
            beam.Init(this, muzzle != null ? muzzle : transform, lockedDir,
                duration: beamLife,
                fadeTime: beamFadeTime,
                coreWidth: beamCoreWidth,
                travelSpeed: beamTravelSpeed,
                trackAngularSpeed: beamTrackAngularSpeed,
                trackLag: beamTrackLag);
            RegisterActiveBeam(beam);
        }
    }
}
