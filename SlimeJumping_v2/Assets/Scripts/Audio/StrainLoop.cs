using UnityEngine;

namespace SlimeJumping.Audio
{
    /// A loop whose level tracks how hard something is being strained right now — a rope pulled
    /// down, a platform still ringing after a landing. Drive it every tick with a 0..1 figure;
    /// at 0 it releases the pooled source, so a settled platform holds nothing. The pool is 8
    /// sources per category, so a loop that keeps its slot while inaudible starves the others.
    public class StrainLoop
    {
        SoundHandle handle;
        SoundDef playing;
        float defVolume;

        public void Drive(SoundDef def, Vector3 worldPos, float strain01,
                          float pitchAtRest = 1f, float pitchAtFull = 1f)
        {
            if (def == null) { Stop(); return; }

            strain01 = Mathf.Clamp01(strain01);
            if (strain01 <= 0f) { Stop(); return; }

            // A loop that ended on its own still owns its pool entry until StopLoop releases it.
            if (!handle.IsPlaying || playing != def)
            {
                Stop();
                handle = SoundService.PlayLooping(def, null);
                playing = def;
                defVolume = handle.Volume;
            }

            handle.SetPosition(worldPos);
            handle.SetVolume(defVolume * strain01);
            handle.SetPitch(Mathf.Lerp(pitchAtRest, pitchAtFull, strain01));
        }

        public void Stop(float fadeSeconds = 0f)
        {
            SoundService.StopLoop(handle, fadeSeconds);
            handle = default;
            playing = null;
        }
    }
}
