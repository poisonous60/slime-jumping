using TMPro;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.UI
{
    /// 플레이 타임을 굳혀 라벨에 쓴다. 재는 구간은 시작 연출에서 깨어난 순간부터
    /// 슬라임이 captureRadius 안에 처음 들어온 순간까지 — 잠들어 있는 동안은 안 센다.
    /// 깨어남은 시작 오버라이드(BoxSleep 등)가 풀리는 시점으로 잡고, 오버라이드를 한 번도
    /// 못 봤으면 씬 로드 시점부터 잰다(에디터에서 MainScene 을 바로 재생한 경우).
    /// 굳힌 뒤 rollDuration 동안 0 부터 굴러 올라가 멈춘다.
    /// 트리거 콜라이더를 안 쓰는 건 슬라임 표면 탐색이 그걸 집어서다.
    public class EndingTimeDisplay : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private string prefix = "";
        [SerializeField] private float captureRadius = 7f;
        [SerializeField] private float rollDuration = 0.9f;

        private const float SlimeSearchInterval = 1f;

        private SlimeController slime;
        private float nextSlimeSearch;
        private bool sawStartOverride;
        private float startTime;
        private bool startLocked;
        private int finalSeconds = -1;
        private float rollTime;

        void Update()
        {
            if (label == null) return;

            if (finalSeconds < 0)
            {
                if (slime == null)
                {
                    if (Time.time < nextSlimeSearch) return;
                    nextSlimeSearch = Time.time + SlimeSearchInterval;
                    slime = FindFirstObjectByType<SlimeController>();
                    if (slime == null) return;
                }
                if (!startLocked)
                {
                    if (slime.HasOverride) sawStartOverride = true;
                    else if (sawStartOverride)
                    {
                        startTime = Time.timeSinceLevelLoad;
                        startLocked = true;
                    }
                }
                Vector2 here = transform.position;
                if ((slime.Position - here).sqrMagnitude > captureRadius * captureRadius) return;
                finalSeconds = Mathf.FloorToInt(Time.timeSinceLevelLoad - startTime);
                rollTime = 0f;
            }

            if (rollTime > rollDuration) return;
            rollTime += Time.deltaTime;
            float k = rollDuration > 0f ? Mathf.Clamp01(rollTime / rollDuration) : 1f;
            int shown = Mathf.RoundToInt(Mathf.SmoothStep(0f, finalSeconds, k));
            label.text = prefix + Format(shown, finalSeconds >= 3600);
        }

        private const int OverflowSeconds = 100 * 3600;

        // 시 자리 유무는 굴러가는 값이 아니라 최종 기록으로 정한다 — 롤업 도중 자릿수가 늘면 글자가 튄다.
        static string Format(int seconds, bool withHours)
        {
            if (seconds >= OverflowSeconds) return "99:99:99";
            return withHours
                ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}"
                : $"{seconds / 60:00}:{seconds % 60:00}";
        }
    }
}
