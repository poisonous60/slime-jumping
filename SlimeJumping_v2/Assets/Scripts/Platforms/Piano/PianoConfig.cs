using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.Platforms.Piano
{
    /// Single tuning asset for a whole piano — keys, keyboard detection, pedals.
    [CreateAssetMenu(fileName = "PianoConfig", menuName = "SlimeJumping/Piano Config")]
    public class PianoConfig : ScriptableObject
    {
        [Header("Harmonize (opt-in progression mode)")]
        [Tooltip("꺼면 밟은 건반의 원음만 재생(현행). 켜면 착지마다 코드 진행이 전진하며 화음을 낸다.")]
        public HarmonizeSettings harmonize = new HarmonizeSettings();

        [Header("Auto-play (idle → the piano plays a recorded piece by itself)")]
        [Tooltip("슬라임이 건반을 몇 번 밟은 뒤 피아노를 떠나면, 녹음된 곡이 재생되고 그 곡의 노트대로 건반이 스스로 눌린다.")]
        public AutoPlaySettings autoPlay = new AutoPlaySettings();

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

    /// One weighted candidate for "what chord comes next". The dice only ever pick from this list.
    [System.Serializable]
    public class NextOption
    {
        [Tooltip("다음 스텝 인덱스(progression 리스트 기준).")]
        public int stepIndex;
        [Tooltip("상대 가중치. 같은 스텝의 후보들 합으로 정규화된다. 0 이하면 후보에서 제외.")]
        [Min(0f)] public float weight = 1f;
    }

    /// One progression step, voiced as semitone offsets from the tonic (so tonicMidi transposes all).
    /// Low offsets are bass — keep them wide, 3rds/2nds go high (Low Interval Limit).
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

    /// Per-landing ornaments. All fire FROM the landing (first note always instant, never clocked) so
    /// nothing can drift out of sync. Chord-figure precedence: chiptune > Alberti > rolled > block.
    [System.Serializable]
    public class OrnamentSettings
    {
        [Header("① 롤드 코드 (아르페지아토)")]
        [Tooltip("화음을 동시타 대신 낮은음→높은음으로 흩뿌린다. 첫 음은 착지 순간 그대로.")]
        public bool rolledChord = false;
        [Tooltip("음 사이 간격(초). 0.02~0.04 권장. 크면 뚜렷한 아르페지오, 작으면 '손으로 친' 뉘앙스.")]
        [Min(0f)] public float rollStepSeconds = 0.03f;
        [Tooltip("끄면 높은음→낮은음(하행 롤).")]
        public bool rollLowToHigh = true;

        [Header("② 보이스리딩 (전위 자동 선택)")]
        [Tooltip("직전 화음에서 가장 적게 움직이는 옥타브로 윗성부를 재배치. 화음 연결이 매끄러워진다.")]
        public bool voiceLeading = false;
        [Tooltip("아래 N개 음은 authored 보이싱 그대로 둔다(저역 간격 규칙 보존).")]
        [Min(0)] public int voiceLeadingKeepLow = 2;

        [Header("③ 페달 포인트")]
        [Tooltip("스키마의 첫 코드에서 낮은 으뜸음 하나를 긴 테일로 깐다. 클럭 아님(착지에 물림).")]
        public bool pedalPoint = false;
        [Tooltip("으뜸 기준 오프셋. -24 = 2옥타브 아래.")]
        public int pedalOffsetFromTonic = -24;
        [Range(0f, 1f)] public float pedalVolume = 0.35f;
        [Min(0.1f)] public float pedalReleaseSeconds = 4f;

        [Header("④ 알베르티 (강한 착지에만)")]
        [Tooltip("화음을 low-high-middle-high 순서로 펼친다(알베르티 베이스). 강착지에서만 발동.")]
        public bool alberti = false;
        [Tooltip("정규화 착지 속도가 이 값 이상일 때만.")]
        [Range(0f, 1f)] public float albertiVelocity = 0.6f;
        [Min(0.01f)] public float albertiStepSeconds = 0.09f;

        [Header("⑤ 칩튠 아르페지오 버스트")]
        [Tooltip("코드톤을 아주 빠르게 순환시켜 초기 게임기의 '가짜 화음'을 흉내낸다.")]
        public bool chiptuneArp = false;
        [Min(2)] public int chiptuneNotes = 6;
        [Min(0.005f)] public float chiptuneStepSeconds = 0.04f;
        [Tooltip("버스트를 올릴 옥타브 수.")]
        public int chiptuneOctaveUp = 1;
        [Range(0f, 1f)] public float chiptuneVolume = 0.5f;

        [Header("⑥ 아포자투라 (기대는 꾸밈음)")]
        [Tooltip("착지 순간엔 한 음 위를 치고, 잠시 뒤 진짜 멜로디 음으로 미끄러진다. 첫 음은 여전히 즉시.")]
        public bool appoggiatura = false;
        [Tooltip("꾸밈음이 본음으로 해결되기까지(초). 0.08~0.15 권장.")]
        [Min(0.01f)] public float appoggiaturaSeconds = 0.11f;

        [Header("⑦ 서스펜션 (4-3)")]
        [Tooltip("직전 화음의 음을 새 화음 위에 붙들고 있다가 한 음 아래로 해결. 화음이 바뀌는 순간에만 발동.")]
        public bool suspension = false;
        [Tooltip("붙든 음이 해결되기까지(초). 0.15~0.3 권장.")]
        [Min(0.02f)] public float suspensionResolveSeconds = 0.18f;

        [Header("⑨ Drop-2 오픈 보이싱")]
        [Tooltip("위에서 두 번째 음을 한 옥타브 내려 화음 간격을 벌린다. 닫힌 보이싱의 탁함을 줄임.")]
        public bool dropTwo = false;

        [Header("⑩ 서스테인 페달 연동")]
        [Tooltip("슬라임이 피아노 페달을 밟고 있으면 모든 음의 테일이 길어진다(댐퍼가 올라간 상태).")]
        public bool sustainPedal = false;
        [Tooltip("페달을 밟는 동안 릴리즈 테일 배수.")]
        [Min(1f)] public float pedalReleaseMultiplier = 3.5f;

        [Header("⑪ 심퍼세틱 레조넌스")]
        [Tooltip("화음 최저음의 옥타브·12도 위를 아주 작게 같이 울려 울림통을 흉내낸다.")]
        public bool resonance = false;
        [Range(0f, 0.5f)] public float resonanceVolume = 0.12f;

        [Header("⑬ 글리산도 런 (점프 거리 → 음악)")]
        [Tooltip("직전 착지에서 멀리 건너뛰었을 때, 진행 방향으로 짧은 런을 흘린다.")]
        public bool glissando = false;
        [Tooltip("이 반음 거리 이상 건너뛰어야 발동.")]
        [Min(1)] public int glissMinSemitones = 10;
        [Min(1)] public int glissMaxNotes = 5;
        [Min(0.005f)] public float glissStepSeconds = 0.035f;
        [Range(0f, 1f)] public float glissVolume = 0.4f;
    }

    /// Opt-in "progression harmonize" tuning. Off = each key's literal note; on = every landing
    /// advances the progression, voices the chord, and snaps the landed key to a melody note on top.
    [System.Serializable]
    public class HarmonizeSettings
    {
        [Tooltip("★ 온/오프. 꺼면 밟은 건반의 원음만(현행). 런타임에 인스펙터로 토글 가능.")]
        public bool enabled = false;

        [Header("Key / scale")]
        [Tooltip("으뜸음 MIDI. 60 = C4. 바꾸면 진행 전체가 전조된다. ※ 자동 연주 곡이 선택되면 그 곡의 조성이 런타임에 덮어쓴다(에셋은 안 바뀜) — 실효값은 TonicMidi.")]
        public int tonicMidi = 60;
        public ScaleType scale = ScaleType.Major;

        // ── Runtime-only key / ornament override ────────────────────────────────────────────────
        // The rotation song sets the key at runtime. Must be [NonSerialized]: writing tonicMidi /
        // scale / ornaments on the ScriptableObject persists into the .asset in the editor.
        [System.NonSerialized] private bool hasKeyOverride;
        [System.NonSerialized] private int overrideTonicMidi;
        [System.NonSerialized] private ScaleType overrideScale;
        [System.NonSerialized] private OrnamentSettings overrideOrnaments;
        [System.NonSerialized] private List<ChordStep> overrideProgression;
        [System.NonSerialized] private ScorePreviewStep[] overridePreviewSteps;

        /// The key everything harmonic must actually use — the song's if one is in rotation.
        public int TonicMidi => hasKeyOverride ? overrideTonicMidi : tonicMidi;
        public ScaleType Scale => hasKeyOverride ? overrideScale : scale;

        /// The ornament preset actually in force — the song's if it authored one.
        public OrnamentSettings Ornaments => overrideOrnaments != null ? overrideOrnaments : ornaments;

        /// The progression in force (song's loop, else the authored schemas). Landing code must read
        /// THIS, never the `progression` field, or the shared asset gets mutated.
        public List<ChordStep> Progression =>
            overrideProgression != null && overrideProgression.Count > 0 ? overrideProgression : progression;

        /// Song's measured strikes, index-aligned with Progression; null = no song, rebuild the chord.
        public ScorePreviewStep[] PreviewSteps => overridePreviewSteps;

        public void SetKeyOverride(int midi, ScaleType s)
        {
            hasKeyOverride = true; overrideTonicMidi = midi; overrideScale = s;
        }

        public void SetOrnamentOverride(OrnamentSettings o) => overrideOrnaments = o;

        public void SetProgressionOverride(List<ChordStep> p) => overrideProgression = p;

        public void SetPreviewStepsOverride(ScorePreviewStep[] s) => overridePreviewSteps = s;

        public void ClearOverrides()
        {
            hasKeyOverride = false; overrideOrnaments = null;
            overrideProgression = null; overridePreviewSteps = null;
        }
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

            // ── S5 (16-19) 희귀 색채: IV–V–♭VI–♭VII (병행단조 차용) ──
            // ♭VI/♭VII는 조성 밖이라 멜로디 스냅을 코드톤으로 강제 — 안 그러면 라♮/미♮이 반음으로 부딪침.
            new ChordStep { label = "S5 IV Fmaj9",  offsetsFromTonic = ChordIV },
            new ChordStep { label = "S5 V  G(add9)",offsetsFromTonic = ChordV },
            new ChordStep { label = "S5 bVI Ab",    offsetsFromTonic = ChordFlatVI,
                overrideSnapMode = true, snapMode = MelodySnapMode.ChordTones },
            new ChordStep { label = "S5 bVII Bb",   offsetsFromTonic = ChordFlatVII,   // ♭VII→I = 모달 종지
                overrideSnapMode = true, snapMode = MelodySnapMode.ChordTones,
                nextOptions = new[] { Opt(0, 0.20f), Opt(4, 0.20f), Opt(8, 0.15f), Opt(12, 0.15f), Opt(20, 0.15f), Opt(24, 0.15f) } },

            // ── M1 (20-23) 나란한단조 A aeolian: i–VI–VII–i ──
            // 흰건반만(임시표 없음). C장조 펜타토닉 = A단조 펜타토닉이라 멜로디 스냅 그대로 안전.
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
        [Tooltip("이 시간 동안 새 착지가 없으면(쉬거나 발판 이탈) 현재 스키마의 첫 코드로 리셋. 높이 점프했다 내려오는 동안 끊기지 않게 넉넉히.")]
        [Min(0.1f)] public float resetIdleSeconds = 10f;

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

        [Header("Performance expression")]
        [Tooltip("★ 곡의 실측 타건 재현. 끄면 기존 화음 재구성 + 장식 경로(스키마 폴백과 동일).")]
        public bool previewGesture = true;
        [Tooltip("한 착지의 제스처 예산(초). 빠른 곡은 이 안에 곡의 연속 타건이 실제 간격 그대로 들어와 '띠리링'이 되고, 느린 곡은 안에 들어올 게 없어서 한 타건만 난다. 추출 쪽 GESTURE_WINDOW와 같은 값으로 두 것. 0.08로 낮추면 착지당 한 타건(2026-07-27 이전 동작).")]
        [Min(0f)] public float gestureMaxSpreadSeconds = 0.35f;
        [Tooltip("한 음이 울릴 수 있는 최대 시간(초). 곡의 실측 울림이 이보다 길면 잘린다.")]
        [Min(0.1f)] public float gestureMaxRingSeconds = 6f;
        [Tooltip("멜로디 음과 이 반음 이내인 지지음은 뺀다. 밟은 건반이 곡 바깥 음일 때 화음을 억지로 유지하지 않고 줄이는 쪽이 곡답게 들린다.")]
        [Range(0, 3)] public int melodyClashSemitones = 1;
        [Tooltip("★ 밟은 건반을 곡의 주 멜로디 자리에 **대신 꽂는다**(켬) vs 곡이 친 음은 노래하는 음까지 전부 울리고 밟은 건반은 그 텍스처 안의 한 성부로 **섞는다**(끔, 기본). 켜면 곡의 선율선이 빠지고 임의 음이 제일 앞에 나와 겉돈다.")]
        public bool previewPlayerAsLead = false;
        [Tooltip("★ 곡의 음을 **순서대로 따라 치지 않고** 그 구간 음들을 화음으로 뭉쳐서 낸다(기본 켬). 전사를 문자 그대로 재생하면 곡을 어설프게 흉내내는 것처럼 들려서, 화음 인상만 두루뭉실하게 전한다. 세기·길이·잔향·음역은 그대로 실측값.")]
        public bool previewChordBlur = true;
        [Tooltip("뭉친 화음을 굴리는 폭(초). 0이면 완전 동시타. 화음이 한 덩어리로 뭉개지지 않을 만큼만.")]
        [Min(0f)] public float blurRollSeconds = 0.03f;
        [Tooltip("lead 모드에서만: 멜로디를 항상 지지음 최상단 위로 올린다(곡이 실제로 그렇게 친다). 끄면 밟은 건반의 옥타브를 그대로 존중.")]
        public bool melodyAboveSupport = true;
        [Tooltip("착지 속도를 음량·음색에 반영할지. **끔이 기본** — 세기는 곡이 실제로 친 세기로만 정해진다(빠르게 착지했다고 곡보다 크게 치면 재현이 깨짐). 켜면 예전처럼 velocityToVolume 곡선이 곱해진다.")]
        public bool previewUseLandingVelocity = false;
        [Tooltip("★ 음이 끊기는 시점 = 곡의 실측 길이와 밟고 있는 시간 중 **더 긴 쪽**. 실측이 짧거나 오탐인 구간은 점프할 때까지 유지되고, 실측이 더 긴 음은 떠난 뒤에도 남은 길이만큼 울린다. 끄면 실측 길이만 쓴다.")]
        public bool sustainWhileStanding = true;
        [Tooltip("★ 밟은 건반을 그 순간 울리는 **화음의 음으로** 강제한다(밟은 음에서 가장 가까운 화음 음, 최대 온음 이동). 어느 건반을 밟았는지는 소리에 남지만 화성 밖 음은 절대 안 나온다. 곡이 이미 그 음을 치고 있으면 옥타브를 옮겨 따로 들리게 한다.")]
        public bool snapLandedKeyToChord = false;

        [Tooltip("건반을 뗀 뒤 댐퍼가 소리를 죽이는 시간(초). 울리는 동안은 샘플 자신의 감쇠에 맡기고, 끝에서만 이 시간으로 지수 감쇠한다 — 릴리즈 내내 선형으로 줄이면 샘플의 자연 감쇠와 싸워서 죽은 소리가 된다.")]
        [Min(0.02f)] public float damperSeconds = 0.18f;
        [Tooltip("★ 세기 → 음색(밝기). 실제 피아노는 세게 칠수록 밝아지는데 건반당 샘플이 1개라 음량만 변한다. 로우패스로 약한 타건을 어둡게 만들어 근사한다(밝게는 못 만든다 — 뺄셈만 가능).")]
        public bool brightnessEnabled = true;
        [Tooltip("가장 약한 타건의 로우패스 컷오프(Hz).")]
        [Min(200f)] public float brightnessSoftHz = 1600f;
        [Tooltip("가장 센 타건의 로우패스 컷오프(Hz). 22000이면 사실상 필터 없음.")]
        [Min(200f)] public float brightnessHardHz = 22000f;
        [Tooltip("이 세기 이하는 전부 가장 어둡게. 전사 세기가 실제로 쓰는 구간의 하단(로테이션 11곡 실측 0.4~0.8) — 0~1 전 구간에 매핑하면 실제 데이터가 중간에만 몰려서 여리게 쳐도 안 어두워진다.")]
        [Range(0f, 1f)] public float brightnessVelocityLow = 0.35f;
        [Tooltip("이 세기 이상은 전부 가장 밝게. 위 항목의 상단.")]
        [Range(0f, 1f)] public float brightnessVelocityHigh = 0.85f;
        [Tooltip("컷오프 하한 = 그 음 기음의 이 배수. 높은 음이 약하게 나도 사라지지 않게 한다.")]
        [Min(1f)] public float brightnessHarmonicFloor = 4f;

        [Header("Seam (2-key) dyad")]
        [Tooltip("이음새 2건반 착지 시 두 멜로디 음의 음량 배수(살짝 두껍게).")]
        [Range(1f, 1.5f)] public float dyadVolumeScale = 1.1f;

        [Header("Visual — 울린 음마다 그 건반이 내려간다 (흰/검은 건반 88개 전부)")]
        [Tooltip("건반이 시각적으로 눌리는 기준 깊이(**world 단위**). 흰 건반 메시는 KeyMotor(scale 1) 자식, 검은 건반 메시는 Piano 루트(scale 2) 직속이라 로컬로 쓰면 검은 건반만 2배 내려간다 — PianoKeyVisual이 부모 스케일로 나눠 world로 통일한다.")]
        public float visualSinkDepth = 0.12f;
        [Tooltip("시각 눌림 최소 유지 시간(초). 실제 유지 = clamp(릴리즈 × visualHoldScale, 이 값, visualMaxHoldSeconds).")]
        public float visualHoldSeconds = 0.25f;
        [Tooltip("건반이 완전히 눌린 채 버티는 시간 = 그 음의 릴리즈 × 이 배수. 1이면 소리 길이와 일치.")]
        [Min(0f)] public float visualHoldScale = 1f;
        [Tooltip("유지가 끝난 뒤 원위치로 올라오는 시간(초).")]
        [Min(0.01f)] public float visualReleaseSeconds = 0.15f;
        [Tooltip("긴 테일이 건반을 영원히 붙잡지 않게 하는 상한(초).")]
        [Min(0.1f)] public float visualMaxHoldSeconds = 2f;
        [Tooltip("페달을 밟는 동안의 유지 시간(초). 실제 피아노처럼 건반은 곧 올라오고 소리만 남는다.")]
        [Min(0.02f)] public float visualPedaledHoldSeconds = 0.25f;
        [Tooltip("가장 여린 음의 깊이 배수. 여린 장식음은 살짝, 센 음은 깊게.")]
        [Range(0f, 1f)] public float visualMinDepthScale = 0.35f;
        [Tooltip("음량이 이보다 작으면 건반을 움직이지 않는다. 기본 0 = 울린 음은 전부 움직이고 깊이만 음량에 비례.")]
        [Range(0f, 1f)] public float visualMinVolume = 0f;
        [Tooltip("이미 눌린 건반을 다시 칠 때 튕겨 올라오는 비율(재타격이 보이게). 실제 피아노도 이스케이프먼트까지 올라와야 다시 소리남. 0이면 재타격이 안 보임.")]
        [Range(0f, 1f)] public float visualRearticulateLift = 0.55f;
        [Tooltip("튕겨 올라온 뒤 다시 내려앉는 시간(초). 칩튠 버스트(40ms 간격)에서도 보이려면 짧게.")]
        [Min(0.005f)] public float visualRearticulateSeconds = 0.05f;
        [Tooltip("큰 음량일수록 시각 눌림 깊이 배수(visualMinDepthScale..this).")]
        public float visualDepthVelocityScale = 1.5f;

        [Header("Branching (landing zone → next chord)")]
        [Tooltip("켜면 착지 X 위치(좌/중/우)가 스텝의 leftNext/centerNext/rightNext로 다음 코드를 고른다.")]
        public bool branchingEnabled = true;
        [Tooltip("켜면 zone 분기가 없는 스텝에서 nextOptions 가중치 랜덤으로 다음 코드를 고른다. 꺼면 항상 선형(i+1).")]
        public bool randomBranching = true;
        [Range(0f, 0.5f)] public float zoneLeftMax = 0.33f;
        [Range(0.5f, 1f)] public float zoneRightMin = 0.66f;

        [Header("Ornaments (per-landing performance figures)")]
        public OrnamentSettings ornaments = new OrnamentSettings();

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

    /// One piece in the auto-play rotation. Audio is the recording itself; keys are driven from its
    /// transcription clocked off the clip playhead, so picture and sound cannot drift apart.
    /// Selecting a song is what transposes the whole progression into its key.
    [System.Serializable]
    public class PianoSong
    {
        [Tooltip("끄면 로테이션에서 제외된다. 리스트에서 지우지 않고 잠깐 빼둘 때.")]
        public bool enabled = true;

        [Tooltip("인스펙터 표시용 이름. 비워도 동작.")]
        public string label = "";

        [Tooltip("재생할 녹음. 소리는 전적으로 이 클립에서 나온다(샘플러 아님).")]
        public AudioClip clip;

        [Tooltip("이 곡만의 볼륨. AutoPlay 의 volume 에 곱해진다. 녹음 레벨이 곡마다 달라 크게 튀는 곡을 낮출 때. Tools/Sound Inventory 에서 곡별로 조절.")]
        [Range(0f, 1f)] public float volume = 1f;

        [Tooltip("위 클립을 전사한 노트 이벤트 JSON(TextAsset). 건반은 이 노트대로 눌리고, 조성도 여기서 읽는다.")]
        public TextAsset scoreJson;

        [Tooltip("-1 = scoreJson이 추정한 조성을 그대로 쓴다(권장). 0 이상이면 그 값으로 강제하고 아래 scale도 함께 쓴다. 보이싱 최저음이 F2(41) 밑으로 안 내려가려면 60 이상이어야 한다.")]
        public int tonicMidi = -1;

        [Tooltip("tonicMidi를 직접 지정했을 때만 쓰인다.")]
        public ScaleType scale = ScaleType.Major;

        [Tooltip("이 곡이 선택된 동안 쓸 연주 장식 프리셋. 곡 성격에 맞게 손으로 켠다(잔잔한 곡=롤드+페달포인트, 빠른 곡=알베르티 등).")]
        public OrnamentSettings ornaments = new OrnamentSettings();
    }

    [System.Serializable]
    public class AutoPlaySettings
    {
        [Tooltip("★ 온/오프.")]
        public bool enabled = false;

        [Tooltip("로테이션에 들어가는 곡들. 슬라임이 피아노에 올라탈 때마다 다음 곡으로 넘어가면서 조성·장식이 그 곡에 맞춰진다. 각 항목의 체크박스로 개별 on/off.")]
        public System.Collections.Generic.List<PianoSong> songs = new System.Collections.Generic.List<PianoSong>();

        [Header("Trigger")]
        [Tooltip("자동연주가 걸리려면 그 전에 건반을 이만큼 밟았어야 한다. 지나가기만 한 슬라임에겐 안 걸린다.")]
        [Min(1)] public int minPresses = 3;
        [Tooltip("마지막으로 건반에서 떨어진 뒤 이 시간이 지나면 시작(초).")]
        [Min(0.1f)] public float idleSeconds = 3f;
        [Tooltip("한 번 끝나거나 중단된 뒤 다시 걸리기까지의 최소 대기(초).")]
        [Min(0f)] public float cooldownSeconds = 8f;

        [Header("Audio")]
        [Range(0f, 1f)] public float volume = 0.7f;
        public bool loop = false;
        [Min(0f)] public float fadeInSeconds = 0.6f;
        [Tooltip("슬라임이 건반에 돌아오면 이 시간에 걸쳐 페이드아웃하며 피아노를 돌려준다(초).")]
        [Min(0f)] public float fadeOutSeconds = 0.35f;

        [Header("Visual")]
        [Tooltip("건반 눌림 깊이 배수. 나머지 시각 파라미터는 Harmonize의 Visual 블록을 그대로 쓴다.")]
        [Range(0f, 2f)] public float visualDepthScale = 1f;

        [Tooltip("곡이 바뀔 때 콘솔에 어느 곡·조성·장식으로 넘어갔는지 찍는다. 로테이션이 도는지 눈으로 확인용.")]
        public bool logSongChange = true;
    }
}
