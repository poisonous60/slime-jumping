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
        [Tooltip("전체 음량 노브(클립 없이 볼륨만 씀). Sound Inventory 에서 조절, 곡별 volume 에 곱해진다.")]
        [SerializeField] SoundDef masterVolume;
        [Min(0f)] [SerializeField] float fadeSeconds = 1.5f;
        [Tooltip("메뉴 진입 첫 곡만 이 시간으로 페이드인.")]
        [Min(0f)] [SerializeField] float firstFadeInSeconds = 2f;

        int lastPick = -1;

        void Start()
        {
            StartCoroutine(Rotate());
        }

        // Scene unload (game start) fades the menu song out; BGM otherwise persists across scenes.
        void OnDestroy()
        {
            SoundService.StopBgm(fadeSeconds);
        }

        IEnumerator Rotate()
        {
            bool first = true;
            while (true)
            {
                var song = PickRandom();
                if (song == null) yield break;
                float master = masterVolume != null ? masterVolume.PickVolume() : 1f;
                SoundService.PlayBgm(song.clip, first ? firstFadeInSeconds : fadeSeconds, song.volume * master);
                first = false;
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
