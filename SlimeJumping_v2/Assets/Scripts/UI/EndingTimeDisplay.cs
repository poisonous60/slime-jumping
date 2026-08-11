using TMPro;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.UI
{
    /// 플레이 타임을 굳혀 라벨에 쓴다. 재는 구간은 시작 연출에서 깨어난 순간부터
    /// 슬라임이 captureZone 안에 처음 들어온 순간까지 — 잠들어 있는 동안은 안 센다.
    /// 깨어남은 시작 오버라이드(BoxSleep 등)가 풀리는 시점으로 잡고, 오버라이드를 한 번도
    /// 못 봤으면 씬 로드 시점부터 잰다(에디터에서 MainScene 을 바로 재생한 경우).
    /// 확정 전에는 라벨을 아예 끈다 — 숫자가 바뀌는 과정이 보이면 안 되니 도착 판정 존은
    /// 라벨이 화면에 들어오기 전 위치에 둬야 한다.
    /// 존은 폴링으로만 읽는다(OnTrigger 미사용). 레이어도 Environment 밖에 둬서
    /// 슬라임 표면 탐색이 이 콜라이더를 지면으로 집지 않게 한다.
    public class EndingTimeDisplay : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private string prefix = "";
        [Tooltip("슬라임이 이 안에 들어오면 기록 확정. 씬에서 크기·위치를 직접 잡는다.")]
        [SerializeField] private Collider2D captureZone;
        [Tooltip("Capture Now 로 찍을 초. -1 이면 실제 경과.")]
        [SerializeField] private int previewSeconds = -1;

        private const float SlimeSearchInterval = 1f;
        private const int OverflowSeconds = 100 * 3600;

        private SlimeController slime;
        private float nextSlimeSearch;
        private bool sawStartOverride;
        private float startTime;
        private bool startLocked;

        void Awake()
        {
            if (label != null) label.enabled = false;
            if (captureZone == null)
            {
                Debug.LogWarning("[EndingTime] captureZone 이 비었다 — 기록을 확정할 방법이 없다.", this);
                enabled = false;
            }
        }

        void Update()
        {
            if (label == null) return;
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

            if (!captureZone.OverlapPoint(slime.Position)) return;
            Capture(Mathf.FloorToInt(Time.timeSinceLevelLoad - startTime));
        }

        private void Capture(int seconds)
        {
            label.text = prefix + Format(seconds);
            label.enabled = true;
            enabled = false;
        }

        /// 인스펙터 우클릭 → 존까지 안 가고 지금 확정. previewSeconds 로 자릿수도 확인한다.
        [ContextMenu("Capture Now")]
        private void CaptureNow()
        {
            if (label == null) return;
            Capture(previewSeconds >= 0 ? previewSeconds : Mathf.FloorToInt(Time.timeSinceLevelLoad - startTime));
        }

        static string Format(int seconds)
        {
            if (seconds >= OverflowSeconds) return "99:99:99";
            return seconds >= 3600
                ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}"
                : $"{seconds / 60:00}:{seconds % 60:00}";
        }
    }
}
