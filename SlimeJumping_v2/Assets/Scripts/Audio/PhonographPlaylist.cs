using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.Audio
{
    /// Selectable BGM tracks for a gramophone. Data-only — keep runtime state out.
    [CreateAssetMenu(menuName = "SlimeJumping/Audio/Phonograph Playlist", fileName = "PhonographPlaylist")]
    public class PhonographPlaylist : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("Shown in the track selection UI.")]
            public string displayName;
            public AudioClip clip;
            [Range(0f, 1f)] public float volume = 1f;
        }

        [SerializeField] private List<Entry> tracks = new List<Entry>();

        public IReadOnlyList<Entry> Tracks => tracks;
        public int Count => tracks != null ? tracks.Count : 0;

        public Entry GetTrack(int index)
        {
            if (tracks == null || index < 0 || index >= tracks.Count) return null;
            return tracks[index];
        }
    }
}
