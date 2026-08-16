using TMPro;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.UI
{
    /// 슬라임이 captureZone 에 처음 들어온 순간 RunClock 을 굳혀 라벨에 쓴다. 재는 구간과
    /// 시계는 전부 RunClock 소관이라 신전 비석과 EndScene 크레딧 방이 같은 숫자를 보여준다
    /// (뒤에 오는 쪽은 이미 굳은 값을 되받는다).
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

        private SlimeController slime;
        private float nextSlimeSearch;

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

            if (!captureZone.OverlapPoint(slime.Position)) return;
            Capture(RunClock.Capture());
        }

        private void Capture(int seconds)
        {
            label.text = prefix + RunClock.Format(seconds);
            label.enabled = true;
            enabled = false;
        }

        /// 인스펙터 우클릭 → 존까지 안 가고 지금 확정. previewSeconds 로 자릿수도 확인한다.
        [ContextMenu("Capture Now")]
        private void CaptureNow()
        {
            if (label == null) return;
            Capture(previewSeconds >= 0 ? previewSeconds : RunClock.Capture());
        }
    }
}
