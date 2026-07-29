using UnityEngine;

namespace SlimeJumping.Effects
{
    /// Pauses/resumes its ParticleSystem with <see cref="WorldTimeFreezeService"/>, so the freeze
    /// stays cooperative instead of touching global Time.timeScale.
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
