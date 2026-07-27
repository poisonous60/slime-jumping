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

    /// One note of a measured strike: which key, how long after the strike's first note it enters,
    /// how loud relative to the loudest note in the whole measured window, and its two-part length.
    /// `hold` and `fall` are separate measurements on purpose — a 0.2 s bounce under a 3.4 s pedal is
    /// a short strike with a long tail, not a 3.4 s note, and collapsing them into one "ring" is what
    /// made a staccato piece sound sustained.
    [System.Serializable]
    public class ScoreSupportNote
    {
        public int midi;
        public float dt;    // seconds after the strike's first note (tens of ms at most)
        public float vel;   // 0..1, relative to the window's peak — this is the song's dynamics
        public float hold;  // seconds the key is down: full gain, the recording decays on its own
        public float fall;  // seconds of pedal tail after that; 0 = the damper falls right away
        public bool sings;  // the voice the song sings with — dropped only in lead mode
    }

    /// One step of the preview = ONE attack the song actually played, not a chord symbol. Two songs
    /// on the same progression differ entirely above the chord label — which notes are struck
    /// together, how hard, how long, which one sings — so that is what a step carries.
    /// `support` is the whole figure, singing voice included. The landed key joins it as one more
    /// voice, snapped into `snapPcs` (the pitch classes the song is using right here) and set at the
    /// figure's own level, so it blends into the texture instead of sitting on top of it.
    /// In lead mode (`previewPlayerAsLead`) the sung note is dropped and the landed key takes its
    /// place in `melodyPcs` instead.
    [System.Serializable]
    public class ScorePreviewStep
    {
        public string label;
        public int[] offsetsFromTonic;   // the strike as tonic-relative offsets (fallback math only)
        public bool chordTones;

        public int[] snapPcs;            // blend mode: where the landed key goes
        public int[] melodyPcs;          // lead mode: the line the song sings over this chord
        public float melodyVel;          // this strike's singing dynamics
        public float melodyHold, melodyFall;   // as ScoreSupportNote
        public bool melodyStruck;        // false = the song struck no melody here (bass-only attack)

        public ScoreSupportNote[] support;
    }

    /// The song-as-preview data: the opening as a run of measured attacks, so a run of landings
    /// reproduces THIS piece rather than a shared schema.
    /// Validity is judged by content (see PianoKeyboard.AutoPlay.PreviewUsable), never by null:
    /// JsonUtility may materialize a default instance when the JSON has no "preview" field.
    [System.Serializable]
    public class ScorePreview
    {
        public int tonicMidi;
        public bool minor;
        public ScorePreviewStep[] steps;
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
