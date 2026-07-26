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

    /// One chord of the song's own opening progression, voiced offline (tools/extract_preview.py)
    /// as tonic-relative semitone offsets — exactly what ChordStep.offsetsFromTonic wants, so the
    /// runtime does no music math. chordTones marks out-of-key chords whose melody snap must be
    /// forced to chord tones (same reason as the authored S5 borrowed chords).
    [System.Serializable]
    public class ScorePreviewStep
    {
        public string label;
        public int[] offsetsFromTonic;
        public bool chordTones;
    }

    /// The measured shape of how the song's opening actually plays its chords — every field is a
    /// continuous value from the transcription, never a toggle from an ornament catalog:
    /// how far apart (and in which direction) chord notes unfold, how long chords ring, whether a
    /// low drone is held (and for how long / how loud), and how loud the accompaniment sits under
    /// the top voice. Deliberately NO inter-attack pattern: one landing = one sounding gesture is
    /// a user-set contract — a landing must never re-strike keys a beat later, however faithfully
    /// that would copy the song's ostinato.
    [System.Serializable]
    public class ScoreTexture
    {
        public float spreadSeconds;
        public bool rollUp;
        public float releaseSeconds;
        public float bassReleaseSeconds;   // 0 = the song holds no drone
        public float bassVolumeScale;
        public float chordVolumeScale;
    }

    /// The song-as-preview data: the piece's opening chord loop plus the measured texture above,
    /// so the slime's landings play THIS piece, not the shared schemas.
    /// Validity is judged by content (see PianoKeyboard.AutoPlay.PreviewUsable), never by null:
    /// JsonUtility may materialize a default instance when the JSON has no "preview" field.
    [System.Serializable]
    public class ScorePreview
    {
        public int tonicMidi;
        public bool minor;
        public ScorePreviewStep[] steps;
        public ScoreTexture texture;
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
        public ScorePreview preview;
    }
}
