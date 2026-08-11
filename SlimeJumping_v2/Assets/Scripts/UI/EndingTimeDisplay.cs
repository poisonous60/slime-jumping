using TMPro;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.UI
{
    /// 엔딩 방 도착 시각을 굳혀 라벨에 쓴다. 슬라임이 이 오브젝트(= 방 바닥 윗면) 높이를
    /// 처음 넘는 순간의 Time.timeSinceLevelLoad 가 곧 플레이 타임 — MainScene 은 한 판
    /// 도중 재로드가 없다. 트리거 콜라이더를 안 쓰는 건 슬라임 표면 탐색이 그걸 집어서다.
    public class EndingTimeDisplay : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private string prefix = "TIME  ";

        private const float SlimeSearchInterval = 1f;

        private SlimeController slime;
        private float nextSlimeSearch;
        private bool captured;

        void Update()
        {
            if (captured || label == null) return;
            if (slime == null)
            {
                if (Time.time < nextSlimeSearch) return;
                nextSlimeSearch = Time.time + SlimeSearchInterval;
                slime = FindFirstObjectByType<SlimeController>();
                if (slime == null) return;
            }
            if (slime.Position.y < transform.position.y) return;

            captured = true;
            int total = Mathf.FloorToInt(Time.timeSinceLevelLoad);
            label.text = $"{prefix}{total / 60:00}:{total % 60:00}";
        }
    }
}
