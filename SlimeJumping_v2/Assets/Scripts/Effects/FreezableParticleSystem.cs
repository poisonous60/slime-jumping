using UnityEngine;

namespace SlimeJumping.Effects
{
    /// Pauses its ParticleSystem while <see cref="WorldTimeFreezeService"/> is frozen
    /// and resumes it on freeze end. Drop on snow/dust/ambient particle GOs that
    /// should respect the cooperative freeze without changing global Time.timeScale.
    [RequireComponent(typeof(ParticleSystem))]
    [AddComponentMenu("SlimeJumping/Effects/Freezable Particle System")]
    public class FreezableParticleSystem : MonoBehaviour
    {
        private ParticleSystem ps;

        private void Awake()
        {
            ps = GetComponent<ParticleSystem>();
        }

        private void OnEnable()
        {
            WorldTimeFreezeService.OnFreezeStarted += Pause;
            WorldTimeFreezeService.OnFreezeEnded += Resume;
            if (WorldTimeFreezeService.IsFrozen) Pause();
        }

        private void OnDisable()
        {
            WorldTimeFreezeService.OnFreezeStarted -= Pause;
            WorldTimeFreezeService.OnFreezeEnded -= Resume;
        }

        private void Pause()
        {
            if (ps != null) ps.Pause(true);
        }

        private void Resume()
        {
            if (ps != null) ps.Play(true);
        }
    }
}
