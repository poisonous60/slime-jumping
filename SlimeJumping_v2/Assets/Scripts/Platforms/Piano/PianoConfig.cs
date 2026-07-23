using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.Platforms.Piano
{
    /// Single tuning asset for a whole piano — keys, keyboard detection, and pedals. Referenced by
    /// PianoKeyboard and the PianoPedal motors so one .asset drives the entire instrument (mirrors
    /// the project's other platform config assets, e.g. BambooV2Settings / SoundSettings).
    [CreateAssetMenu(fileName = "PianoConfig", menuName = "SlimeJumping/Piano Config")]
    public class PianoConfig : ScriptableObject
    {
        [Header("Harmonize (opt-in progression mode)")]
        [Tooltip("꺼면 밟은 건반의 원음만 재생(현행). 켜면 착지마다 코드 진행이 전진하며 화음을 낸다.")]
        public HarmonizeSettings harmonize = new HarmonizeSettings();

        [Header("Keys — dynamics & feel")]
        public PianoSettings keys = new PianoSettings();

        [Header("Keyboard — multi-key detection")]
        [Tooltip("Min horizontal overlap (world units) between slime and a key to press it. Lower = easier multi-key.")]
        public float overlapThreshold = 0.06f;
        [Tooltip("Max vertical gap (world units) between the slime's underside and a key top to count as resting.")]
        public float verticalTolerance = 0.14f;

        [Header("Pedals — press feel")]
        [Tooltip("Downward sink at full press (world units). This carries the slime down.")]
        public float pedalMaxDip = 0.1f;
        [Tooltip("Cosmetic X tilt at full press (degrees, into the screen).")]
        public float pedalMaxAngle = 7f;
        [Min(0f)] public float pedalSpringStiffness = 120f;
        [Min(0f)] public float pedalSpringDamping = 11f;

        [Header("Pedals — sound")]
        [Tooltip("Played once when the slime steps onto a pedal.")]
        public AudioClip pedalPressSound;
        [Tooltip("Master volume multiplier on top of the velocity curve.")]
        [Range(0f, 1f)] public float pedalSoundVolume = 1f;
        [Tooltip("Slime impact-speed range (world u/s) mapped to the volume curve's 0..1 input.")]
        public float pedalMinImpactSpeed = 1f;
        public float pedalMaxImpactSpeed = 25f;
        [Tooltip("Press speed (normalized 0..1) -> sound volume (0..1). 밟는 속도별 소리 크기.")]
        public AnimationCurve pedalVelocityToVolume = AnimationCurve.Linear(0f, 0.3f, 1f, 1f);

        /// 0..1 position of a pedal impact speed inside [pedalMinImpactSpeed, pedalMaxImpactSpeed].
        public float PedalNormalizedImpact(float speed)
            => Mathf.Clamp01(Mathf.InverseLerp(pedalMinImpactSpeed, pedalMaxImpactSpeed, speed));
    }

    public enum ScaleType { Major, Minor }
    public enum MelodySnapMode { Pentatonic, ChordTones, ScaleFull }
    public enum LandingZone { Left, Center, Right }

    /// One authored candidate for "what chord comes next", with a relative weight. The dice only ever
    /// pick from this list, so every outcome is one the designer already approved.
    [System.Serializable]
    public class NextOption
    {
        [Tooltip("다음 스텝 인덱스(progression 리스트 기준).")]
        public int stepIndex;
        [Tooltip("상대 가중치. 같은 스텝의 후보들 합으로 정규화된다. 0 이하면 후보에서 제외.")]
        [Min(0f)] public float weight = 1f;
    }

    /// One step of the chord progression. Voicing is authored as semitone offsets from the tonic
    /// (so changing HarmonizeSettings.tonicMidi transposes the whole progression). Low offsets are
    /// the bass — keep them wide (octave/5th) and put 3rds/2nds high (Low Interval Limit).
    [System.Serializable]
    public class ChordStep
    {
        public string label = "I";
        [Tooltip("Tonic 기준 반음 오프셋. 음수=저역. 넓은 저역 + 위쪽 3화음 권장.")]
        public int[] offsetsFromTonic = { -12, 0, 4, 7, 12 };
        [Tooltip("이 코드의 릴리즈 테일(초). 0이면 defaultReleaseSeconds 사용.")]
        public float releaseSeconds = 0f;
        [Tooltip("착지 zone별 다음 스텝 인덱스. -1 = 선형(다음 = i+1). branchingEnabled 시에만 사용. zone 분기가 최우선.")]
        public int leftNext = -1, centerNext = -1, rightNext = -1;
        [Tooltip("zone 분기가 없을 때 쓰는 가중치 랜덤 후보들. 비어 있으면 선형(i+1).")]
        public NextOption[] nextOptions = new NextOption[0];

        [Tooltip("이 코드에서만 멜로디 스냅 방식을 바꾼다. 차용화음(♭VI/♭VII)처럼 조성 밖 코드는 ChordTones로 강제해야 펜타토닉 멜로디와 반음 충돌하지 않는다.")]
        public bool overrideSnapMode = false;
        public MelodySnapMode snapMode = MelodySnapMode.ChordTones;
    }

    /// Opt-in "progression harmonize" tuning. When disabled the keyboard plays each key's literal
    /// note (current behavior). When enabled, every landing advances the progression one step, plays
    /// that chord's voicing through a voice pool, snaps the landed key(s) to a melody note on top,
    /// and visually sinks the chord's keys. All parameters are designer-editable on the PianoConfig.
    [System.Serializable]
    public class HarmonizeSettings
    {
        [Tooltip("★ 온/오프. 꺼면 밟은 건반의 원음만(현행). 런타임에 인스펙터로 토글 가능.")]
        public bool enabled = false;

        [Header("Key / scale")]
        [Tooltip("으뜸음 MIDI. 60 = C4. 바꾸면 진행 전체가 전조된다.")]
        public int tonicMidi = 60;
        public ScaleType scale = ScaleType.Major;
        [Tooltip("착지 건반 → 멜로디 음 스냅 방식. Pentatonic = 가장 안전(안 틀림).")]
        public MelodySnapMode snapMode = MelodySnapMode.Pentatonic;

        [Header("Progression = 4 authored schemas x 4 chords")]
        [Tooltip("루프 '안'은 항상 순서대로(기능화성 T→S→D→T 유지 = 곡처럼 들리는 이유). 확률은 각 루프의 4번째 코드에서만 굴려 다음 스키마를 고른다. 스키마 4종 모두 코퍼스에 이름이 붙어 있는 실제 진행.")]
        public List<ChordStep> progression = new List<ChordStep>
        {
            // ── S1 (0-3) 싱어송라이터 회전형 I–V–vi–IV (U2 "With or Without You" 형) ──
            new ChordStep { label = "S1 I  C6/9",   offsetsFromTonic = ChordI },
            new ChordStep { label = "S1 V  G(add9)",offsetsFromTonic = ChordV },
            new ChordStep { label = "S1 vi Am7",    offsetsFromTonic = ChordVi },
            new ChordStep { label = "S1 IV Fmaj9",  offsetsFromTonic = ChordIV,   // 루프 끝 → 다음 스키마 추첨. IV→I = 플라갈
                nextOptions = new[] { Opt(0, 0.24f), Opt(4, 0.08f), Opt(8, 0.08f), Opt(12, 0.08f),
                                      Opt(20, 0.23f), Opt(24, 0.23f), Opt(16, 0.06f) } },

            // ── S2 (4-7) 두왑 I–vi–IV–V (1960s pop) ──
            new ChordStep { label = "S2 I  C6/9",   offsetsFromTonic = ChordI },
            new ChordStep { label = "S2 vi Am7",    offsetsFromTonic = ChordVi },
            new ChordStep { label = "S2 IV Fmaj9",  offsetsFromTonic = ChordIV },
            new ChordStep { label = "S2 V  G(add9)",offsetsFromTonic = ChordV,    // V→I 정격 / V→vi 위종지
                nextOptions = new[] { Opt(4, 0.24f), Opt(0, 0.08f), Opt(8, 0.08f), Opt(12, 0.08f),
                                      Opt(20, 0.23f), Opt(24, 0.23f), Opt(16, 0.06f) } },

            // ── S3 (8-11) 싱어송라이터 원형 vi–IV–I–V ──
            new ChordStep { label = "S3 vi Am7",    offsetsFromTonic = ChordVi },
            new ChordStep { label = "S3 IV Fmaj9",  offsetsFromTonic = ChordIV },
            new ChordStep { label = "S3 I  C6/9",   offsetsFromTonic = ChordI },
            new ChordStep { label = "S3 V  G(add9)",offsetsFromTonic = ChordV,
                nextOptions = new[] { Opt(8, 0.24f), Opt(0, 0.08f), Opt(4, 0.08f), Opt(12, 0.08f),
                                      Opt(20, 0.23f), Opt(24, 0.23f), Opt(16, 0.06f) } },

            // ── S4 (12-15) 프레이즈 모델 확장형 I–vi–ii–V ("I VI II V I") ──
            new ChordStep { label = "S4 I  C6/9",   offsetsFromTonic = ChordI },
            new ChordStep { label = "S4 vi Am7",    offsetsFromTonic = ChordVi },
            new ChordStep { label = "S4 ii Dm7",    offsetsFromTonic = ChordIi },
            new ChordStep { label = "S4 V  G(add9)",offsetsFromTonic = ChordV,
                nextOptions = new[] { Opt(12, 0.24f), Opt(0, 0.08f), Opt(4, 0.08f), Opt(8, 0.08f),
                                      Opt(20, 0.23f), Opt(24, 0.23f), Opt(16, 0.06f) } },

            // ── S5 (16-19) 희귀 색채: IV–V–♭VI–♭VII (병행단조 차용, "승리 테마" 관용구) ──
            // 각 루프 끝에서 6% 확률로만 진입 → 평균 12~13바퀴에 한 번. 리서치: 차용화음은 드물게,
            // 그리고 반드시 해결. 자기 반복 없음(끝나면 항상 일반 스키마로 복귀).
            // ♭VI/♭VII는 조성 밖이라 멜로디 스냅을 코드톤으로 강제 — 안 그러면 라♮/미♮이 반음으로 부딪침.
            new ChordStep { label = "S5 IV Fmaj9",  offsetsFromTonic = ChordIV },
            new ChordStep { label = "S5 V  G(add9)",offsetsFromTonic = ChordV },
            new ChordStep { label = "S5 bVI Ab",    offsetsFromTonic = ChordFlatVI,
                overrideSnapMode = true, snapMode = MelodySnapMode.ChordTones },
            new ChordStep { label = "S5 bVII Bb",   offsetsFromTonic = ChordFlatVII,   // ♭VII→I = 모달 종지
                overrideSnapMode = true, snapMode = MelodySnapMode.ChordTones,
                nextOptions = new[] { Opt(0, 0.20f), Opt(4, 0.20f), Opt(8, 0.15f), Opt(12, 0.15f), Opt(20, 0.15f), Opt(24, 0.15f) } },

            // ── M1 (20-23) 나란한단조 A aeolian: i–VI–VII–i ──
            // 흰건반만 사용(임시표 없음). C장조 펜타토닉 = A단조 펜타토닉이라 멜로디 스냅도 그대로 안전.
            // Am으로 시작하고 Am으로 끝나야 귀의 중심이 실제로 A로 옮겨간다.
            new ChordStep { label = "M1 i   Am",    offsetsFromTonic = ChordAmTonic },
            new ChordStep { label = "M1 VI  Fmaj9", offsetsFromTonic = ChordIV },
            new ChordStep { label = "M1 VII G",     offsetsFromTonic = ChordV },
            new ChordStep { label = "M1 i   Am",    offsetsFromTonic = ChordAmTonic,
                nextOptions = new[] { Opt(0, 0.13f), Opt(4, 0.13f), Opt(8, 0.13f), Opt(12, 0.13f),
                                      Opt(20, 0.21f), Opt(24, 0.21f), Opt(16, 0.06f) } },

            // ── M2 (24-27) A aeolian: i–v–VI–VII (단조 도미넌트 = 화성단조 안 씀 → 임시표 없음) ──
            new ChordStep { label = "M2 i   Am",    offsetsFromTonic = ChordAmTonic },
            new ChordStep { label = "M2 v   Em",    offsetsFromTonic = ChordMinorV },
            new ChordStep { label = "M2 VI  Fmaj9", offsetsFromTonic = ChordIV },
            new ChordStep { label = "M2 VII G",     offsetsFromTonic = ChordV,
                nextOptions = new[] { Opt(0, 0.13f), Opt(4, 0.13f), Opt(8, 0.13f), Opt(12, 0.13f),
                                      Opt(24, 0.21f), Opt(20, 0.21f), Opt(16, 0.06f) } },
        };

        // Shared voicings. Bass never below F2 (MIDI 41) — lower than that muddies on pitch-shifted
        // samples. Wide low spacing, extensions on top (Low Interval Limit).
        static readonly int[] ChordI  = { -12, -5, 4, 9 };    // C3 G3 E4 A4  — C6/9
        static readonly int[] ChordV  = { -17, -10, -1, 2 };  // G2 D3 B3 D4  — G(add9)
        static readonly int[] ChordVi = { -15, -8, -5, 0 };   // A2 E3 G3 C4  — Am7
        static readonly int[] ChordIV = { -19, -12, -3, 7 };  // F2 C3 A3 G4  — Fmaj9
        static readonly int[] ChordIi = { -10, -3, 0, 5 };    // D3 A3 C4 F4  — Dm7
        static readonly int[] ChordFlatVI  = { -16, -9, -4, 0 };  // Ab2 Eb3 Ab3 C4 — bVI (borrowed)
        static readonly int[] ChordFlatVII = { -14, -7, -2, 2 };  // Bb2 F3 Bb3 D4  — bVII (borrowed)
        static readonly int[] ChordAmTonic = { -15, -8, -3, 0 };  // A2 E3 A3 C4  — Am (relative-minor tonic)
        static readonly int[] ChordMinorV  = { -8, -1, 4, 7 };    // E3 B3 E4 G4  — Em (aeolian v)

        static NextOption Opt(int index, float weight) => new NextOption { stepIndex = index, weight = weight };

        [Header("Reset")]
        [Tooltip("이 시간 동안 새 착지가 없으면(쉬거나 발판 이탈) 진행을 으뜸으로 리셋.")]
        [Min(0.1f)] public float resetIdleSeconds = 1.6f;

        [Header("Dynamics / audio")]
        [Tooltip("착지 속도(0..1) → 화음 음량.")]
        public AnimationCurve velocityToVolume = AnimationCurve.Linear(0f, 0.4f, 1f, 1f);
        [Range(0f, 1f)] public float masterVolume = 0.8f;
        [Tooltip("동시 발음 보이스 풀 크기. 겹치는 화음 + 릴리즈 테일을 감당할 만큼.")]
        [Min(1)] public int voicePoolSize = 24;
        [Tooltip("ChordStep.releaseSeconds가 0일 때 쓰는 기본 릴리즈 테일(초).")]
        public float defaultReleaseSeconds = 1.2f;
        [Tooltip("멜로디 음을 보이싱 최상단보다 최소 이 반음 위로 띄운다.")]
        public float minMelodyGap = 3f;

        [Header("Seam (2-key) dyad")]
        [Tooltip("이음새 2건반 착지 시 두 멜로디 음의 음량 배수(살짝 두껍게).")]
        [Range(1f, 1.5f)] public float dyadVolumeScale = 1.1f;

        [Header("Visual sink (chord keys)")]
        [Tooltip("코드 구성 흰 건반이 시각적으로 눌리는 깊이(월드).")]
        public float visualSinkDepth = 0.09f;
        [Tooltip("시각 눌림 유지 시간(초).")]
        public float visualHoldSeconds = 0.25f;
        [Tooltip("강한 착지일수록 시각 눌림 깊이 배수(1..this).")]
        public float visualDepthVelocityScale = 1.5f;

        [Header("Branching (landing zone → next chord)")]
        [Tooltip("켜면 착지 X 위치(좌/중/우)가 스텝의 leftNext/centerNext/rightNext로 다음 코드를 고른다.")]
        public bool branchingEnabled = true;
        [Tooltip("켜면 zone 분기가 없는 스텝에서 nextOptions 가중치 랜덤으로 다음 코드를 고른다. 꺼면 항상 선형(i+1).")]
        public bool randomBranching = true;
        [Range(0f, 0.5f)] public float zoneLeftMax = 0.33f;
        [Range(0.5f, 1f)] public float zoneRightMin = 0.66f;

        [Header("Velocity density")]
        [Tooltip("켜면 강한 착지에 옥타브 더블링으로 두껍게.")]
        public bool velocityDensity = true;
        [Tooltip("정규화 착지 속도가 이 값 이상이면 최상단 음을 한 옥타브 위로 더블링.")]
        [Range(0f, 1f)] public float octaveDoubleVelocity = 0.7f;

        [Header("Reverb (requires manual mixer wiring)")]
        [Tooltip("켜고 믹서에 Reverb 이펙트 + 노출 파라미터가 있어야 동작. 없으면 무시.")]
        public bool reverbEnabled = false;
        [Tooltip("믹서에 노출한 wet(dB) 파라미터 이름.")]
        public string reverbWetParam = "PianoReverbWet";
        [Tooltip("착지 속도(0..1) → wet(dB). 믹서 파라미터로 전달.")]
        public AnimationCurve velocityToReverb = AnimationCurve.Linear(0f, -20f, 1f, 0f);
    }
}
