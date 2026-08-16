using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Platforms.Piano;

namespace SlimeJumping.Audio
{
    /// Menu BGM drawn from the piano auto-play rotation: a random enabled song plays through
    /// SoundService with its hand-tuned per-song volume, then crossfades into the next pick.
    /// Reuses PianoConfig's song list so the menu never needs its own clip/volume bookkeeping.
    [AddComponentMenu("SlimeJumping/Audio/Menu Piano BGM")]
    public class MenuPianoBgm : MonoBehaviour
    {
        [SerializeField] PianoConfig config;
        [Range(0f, 1f)] [SerializeField] float volume = 0.8f;
        [Min(0f)] [SerializeField] float fadeSeconds = 1.5f;

        int lastPick = -1;

        void Start()
        {
            StartCoroutine(Rotate());
        }

        IEnumerator Rotate()
        {
            while (true)
            {
                var song = PickRandom();
                if (song == null) yield break;
                SoundService.PlayBgm(song.clip, fadeSeconds, song.volume * volume);
                // Fade completes at song end, before the looping BGM source wraps to the intro.
                yield return new WaitForSeconds(Mathf.Max(1f, song.clip.length - fadeSeconds));
            }
        }

        PianoSong PickRandom()
        {
            if (config == null) return null;
            var pool = new List<int>();
            var songs = config.autoPlay.songs;
            for (int i = 0; i < songs.Count; i++)
                if (songs[i].enabled && songs[i].clip != null && i != lastPick) pool.Add(i);
            if (pool.Count == 0) return lastPick >= 0 ? songs[lastPick] : null;
            lastPick = pool[Random.Range(0, pool.Count)];
            return songs[lastPick];
        }
    }
}
