using UnityEngine;
using SlimeJumping.Audio;

namespace SlimeJumping.VFX
{
    // 팡파레 색종이 공유 튜닝. canonical asset = Assets/Resources/ConfettiFanfareConfig.asset (자동 로드).
    // 값은 유저 지정 튜토리얼 영상(youtu.be/Zl-oZLLcK9g) 레시피 그대로, 3D화(쿼드 메시)만 추가.
    [CreateAssetMenu(fileName = "ConfettiFanfareConfig",
        menuName = "SlimeJumping/VFX/Confetti Fanfare Config", order = 0)]
    public sealed class ConfettiFanfareConfig : ScriptableObject
    {
        [Header("Burst")]
        [Tooltip("포퍼 하나가 터뜨리는 조각 수.")]
        [Min(1)] public int pieceCountPerPopper = 60;

        [Tooltip("초기 발사 속도 최소.")]
        [Min(0f)] public float speedMin = 6f;

        [Tooltip("초기 발사 속도 최대.")]
        [Min(0f)] public float speedMax = 15f;

        [Tooltip("발사 원뿔 반각 (도).")]
        [Range(0f, 90f)] public float coneAngle = 25f;

        [Tooltip("발사 노즐 반경.")]
        [Min(0f)] public float nozzleRadius = 0.1f;

        [Header("Piece")]
        [Tooltip("조각 가로 범위.")]
        [Min(0.01f)] public float sizeXMin = 0.1f;
        [Min(0.01f)] public float sizeXMax = 0.3f;

        [Tooltip("조각 세로 범위.")]
        [Min(0.01f)] public float sizeYMin = 0.1f;
        [Min(0.01f)] public float sizeYMax = 0.5f;

        [Tooltip("시작 회전 랜덤 상한 (도, 3축 각각).")]
        [Range(0f, 360f)] public float startRotationMaxDeg = 180f;

        [Tooltip("조각 팔레트. Fixed 그라디언트라 섞이지 않고 하나씩 뽑힌다 (최대 8색).")]
        public Color[] palette =
        {
            Color.red,
            Color.green,
            Color.yellow,
            new Color(1f, 0.4f, 0.7f),
            Color.blue,
        };

        [Header("Motion")]
        [Tooltip("중력 배율.")]
        [Min(0f)] public float gravityScale = 1f;

        [Tooltip("수명에 걸친 속도 상한 커브 (limit velocity).")]
        public AnimationCurve slowdownCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);

        [Tooltip("상한 초과 속도를 깎는 세기 0..1.")]
        [Range(0f, 1f)] public float slowdownDampen = 1f;

        [Tooltip("수명에 걸친 크기 커브 (size over lifetime).")]
        public AnimationCurve shrinkCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);

        [Header("Sound")]
        [Tooltip("왼쪽 포퍼 터짐 소리. 볼륨/피치는 SoundDef 소유.")]
        public SoundDef popSoundLeft;

        [Tooltip("오른쪽 포퍼 터짐 소리.")]
        public SoundDef popSoundRight;

        [Tooltip("추가 레이어 — 좌우 팝과 동시에 중앙에서 한 발 (환호/짠 등).")]
        public SoundDef fanfareSound;

        [Header("Life")]
        [Tooltip("조각 수명 (초).")]
        [Min(0.1f)] public float lifetime = 5f;

        [Tooltip("시스템 duration (초).")]
        [Min(0.1f)] public float duration = 5f;

#if UNITY_EDITOR
        // 인스펙터에서 값을 고치면 씬 포퍼에 바로 굽는다 — Apply 클릭 불필요.
        void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall -= ApplyToSceneInstances;
            UnityEditor.EditorApplication.delayCall += ApplyToSceneInstances;
        }

        void ApplyToSceneInstances()
        {
            if (this == null) return;
            var fanfares = FindObjectsByType<ConfettiFanfare>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var fanfare in fanfares)
                fanfare.ApplyConfigToPoppers();
        }
#endif

        const string ResourcesPath = "ConfettiFanfareConfig";
        static ConfettiFanfareConfig fallback;

        public static ConfettiFanfareConfig Resolve(ConfettiFanfareConfig @override)
        {
            if (@override != null) return @override;

            var asset = Resources.Load<ConfettiFanfareConfig>(ResourcesPath);
            if (asset != null) return asset;

            if (fallback == null)
            {
                fallback = CreateInstance<ConfettiFanfareConfig>();
                Debug.LogWarning("ConfettiFanfareConfig: no Assets/Resources/ConfettiFanfareConfig.asset found — " +
                                 "running on built-in defaults. Create one via Assets > Create > SlimeJumping > VFX > Confetti Fanfare Config.");
            }
            return fallback;
        }
    }
}
