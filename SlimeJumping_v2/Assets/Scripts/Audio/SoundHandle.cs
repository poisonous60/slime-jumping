using UnityEngine;

namespace SlimeJumping.Audio
{
    public struct SoundHandle
    {
        internal AudioSource source;
        internal int generation;

        public bool IsValid => source != null && source.gameObject != null;
        public bool IsPlaying => IsValid && source.isPlaying;

        public void Stop()
        {
            if (!IsValid) return;
            source.Stop();
        }

        public void SetVolume(float volume)
        {
            if (!IsValid) return;
            source.volume = volume;
        }

        public void SetPosition(Vector3 worldPos)
        {
            if (!IsValid) return;
            source.transform.position = worldPos;
        }
    }
}
