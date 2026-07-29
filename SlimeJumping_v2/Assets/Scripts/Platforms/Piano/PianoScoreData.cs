namespace SlimeJumping.Platforms.Piano
{
    /// <summary>One transcribed note; t is seconds from clip start so the clip playhead can drive it.</summary>
    [System.Serializable]
    public class ScoreNote
    {
        public float t;
        public int midi;
        public float vel;    // 0..1
        public float dur;    // seconds the note is held
    }

    /// <summary>A span where the sustain pedal was down in the source performance.</summary>
    [System.Serializable]
    public class ScorePedalSpan
    {
        public float t;
        public float dur;
    }

    /// <summary>Estimated key. Logged only — the designer copies it into HarmonizeSettings by hand.</summary>
    [System.Serializable]
    public class ScoreKey
    {
        public int tonicPc;        // 0 = C
        public string tonic;
        public string mode;        // "major" / "minor"
        public float confidence;
    }

    /// <summary>One note of a measured strike. hold/fall stay separate: merging them into a single
    /// "ring" turns a short bounce under a long pedal into a sustained note.</summary>
    [System.Serializable]
    public class ScoreSupportNote
    {
        public int midi;
        public float dt;    // seconds after the strike's first note
        public float vel;   // 0..1, relative to the window's peak
        public float hold;  // seconds the key is down
        public float fall;  // seconds of pedal tail after that; 0 = damper falls right away
        public bool sings;  // the singing voice — dropped only in lead mode
    }

    /// <summary>One step = ONE attack the song actually played, not a chord symbol. `support` is the
    /// whole figure; the landed key joins it snapped into snapPcs, or replaces the sung note in lead mode.</summary>
    [System.Serializable]
    public class ScorePreviewStep
    {
        public string label;
        public int[] offsetsFromTonic;   // tonic-relative offsets (fallback math only)
        public bool chordTones;

        public int[] snapPcs;            // blend mode: where the landed key goes
        public int[] melodyPcs;          // lead mode: the line the song sings over this chord
        public float melodyVel;          // this strike's singing dynamics
        public float melodyHold, melodyFall;   // as ScoreSupportNote
        public bool melodyStruck;        // false = no melody struck here (bass-only attack)

        public ScoreSupportNote[] support;
    }

    /// <summary>Preview steps. Validity is judged by content (PianoKeyboard.AutoPlay.PreviewUsable),
    /// never by null: JsonUtility materializes a default instance when the JSON has no "preview".</summary>
    [System.Serializable]
    public class ScorePreview
    {
        public int tonicMidi;
        public bool minor;
        public ScorePreviewStep[] steps;
    }

    /// <summary>A whole auto-play score, deserialized from the offline transcription JSON TextAsset.</summary>
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
