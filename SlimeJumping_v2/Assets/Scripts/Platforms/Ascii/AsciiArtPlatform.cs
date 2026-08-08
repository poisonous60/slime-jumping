using TMPro;
using UnityEngine;

namespace SlimeJumping.Platforms
{
    /// <summary>
    /// 아스키아트 발판: .txt 원본을 world-space TMP 한 장으로 그리고, 공백 아닌 글자의
    /// 점유 그리드 외곽선을 PolygonCollider2D 로 쓴다. 생성과 콜라이더 추출은 에디터
    /// 빌더(AsciiArtPlatformBuilder) 몫이고, 이 컴포넌트는 빌드 시점 실측 셀 메트릭을
    /// 보관한다 — 이후 착지 출렁임이 이 메트릭으로 착지점 주변 글자를 찾는다.
    /// </summary>
    public class AsciiArtPlatform : MonoBehaviour
    {
        [Tooltip("비우면 Assets/Resources/AsciiArtPlatformConfig.asset 공유 튜닝.")]
        [SerializeField] AsciiArtPlatformConfig configOverride;
        [SerializeField] TextAsset sourceArt;
        [SerializeField] TMP_Text visualText;

        [Header("빌드 실측 (빌더가 기록)")]
        [SerializeField] Vector2 gridTopLeft;   // (0,0) 셀 좌상단, 로컬 기준.
        [SerializeField] float cellWidth;
        [SerializeField] float cellHeight;
        [SerializeField] int columns;
        [SerializeField] int rows;

        public AsciiArtPlatformConfig Config => AsciiArtPlatformConfig.Resolve(configOverride);
        public TextAsset SourceArt => sourceArt;
        public TMP_Text VisualText => visualText;
        public Vector2 GridTopLeft => gridTopLeft;
        public float CellWidth => cellWidth;
        public float CellHeight => cellHeight;
        public int Columns => columns;
        public int Rows => rows;
    }
}
