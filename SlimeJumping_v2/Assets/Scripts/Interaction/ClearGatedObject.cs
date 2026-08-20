using UnityEngine;
using SlimeJumping.Optimization;
using SlimeJumping.UI;

namespace SlimeJumping.Interaction
{
    /// <summary>
    /// 1회 클리어 여부(LoadSaveManager.HasCleared)로 이 오브젝트의 활성을 정하는 게이트.
    /// 판정은 Awake 한 번뿐 — 플래그는 엔딩 진입에서만 바뀌고 그 뒤엔 씬을 새로 로드한다.
    /// 게이트가 껐으면 자신·자식의 OffscreenSleepable 도 지운다. 남겨 두면 슬라임이 다가올 때
    /// 슬리퍼가 SetActive(true) 로 깨워 꺼둔 오브젝트가 도로 나타난다.
    /// 실행 순서는 슬리퍼 매니저(-9999)보다 앞 — 같은 오브젝트의 다른 컴포넌트가 Awake 하기 전에 끈다.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public class ClearGatedObject : MonoBehaviour
    {
        [Tooltip("On = 1회 클리어 이후에만 활성. Off = 클리어 전에만 활성.")]
        [SerializeField] bool activeWhenCleared = true;

        void Awake()
        {
            if (LoadSaveManager.HasCleared == activeWhenCleared) return;

            var sleepers = GetComponentsInChildren<OffscreenSleepable>(true);
            for (int i = 0; i < sleepers.Length; i++) Destroy(sleepers[i]);

            gameObject.SetActive(false);
        }
    }
}
