using UnityEngine;

namespace SlimeJumping.Audio
{
    public struct SoundHandle
    {
        internal AudioSource source;
        internal int generation;

        public bool IsValid => source != null && source.gameObject != null;
        public bool IsPlaying => IsValid && source.isPlaying;

        /// Level actually on the source — the SoundDef's own volume times whatever scale was passed.
        /// A fade has to ramp from this, not from the caller's scale alone.
        public float Volume => IsValid ? source.volume : 0f;

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

        /// A sound borrowed through PlayLooping but meant to run once; the owner still has to
        /// StopLoop it when it ends, or the pool entry stays locked.
        public void SetLoop(bool loop)
        {
            if (!IsValid) return;
            source.loop = loop;
        }

        public void SetPitch(float pitch)
        {
            if (!IsValid) return;
            source.pitch = pitch;
        }

        public void SetPosition(Vector3 worldPos)
        {
            if (!IsValid) return;
            source.transform.position = worldPos;
        }
    }
}
