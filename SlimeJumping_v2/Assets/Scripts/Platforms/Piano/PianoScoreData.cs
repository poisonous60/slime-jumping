namespace SlimeJumping.Platforms.Piano
{
    /// One transcribed note of an auto-play score: when it is struck, which key, how hard, how long
    /// the key stays down. Times are seconds from the start of the audio clip, so the clip's own
    /// playhead can drive them and the keys can never drift out of sync with the recording.
    [System.Serializable]
    public class ScoreNote
    {
        public float t;
        public int midi;
        public float vel;    // 0..1
        public float dur;    // seconds the note is held
    }

    /// A span where the sustain pedal was down in the source performance.
    [System.Serializable]
    public class ScorePedalSpan
    {
        public float t;
        public float dur;
    }

    /// The key the transcription was estimated to be in. Not applied automatically — it is logged
    /// once so the designer can set HarmonizeSettings.tonicMidi / scale to match, which is what makes
    /// the slime's own playing sound like it belongs to the same piece.
    [System.Serializable]
    public class ScoreKey
    {
        public int tonicPc;        // 0 = C
        public string tonic;
        public string mode;        // "major" / "minor"
        public float confidence;
    }

    /// A whole auto-play score, deserialized from the JSON TextAsset produced by the offline
    /// transcription step (see docs). Unknown fields in the JSON — the chord estimate, the source
    /// filename — are simply ignored by JsonUtility.
    [System.Serializable]
    public class PianoScoreData
    {
        public string source;
        public float duration;
        public float tempo;
        public ScoreKey key;
        public ScorePedalSpan[] pedal;
        public ScoreNote[] notes;
    }
}
