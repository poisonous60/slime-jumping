using UnityEngine;
using SlimeJumping.UI;

namespace SlimeJumping.Controller
{
    /// <summary>
    /// Web 배포본은 1스테이지까지만 담는다. 2스테이지로 올라가는 높이에 닿으면 그대로 EndScene 으로 넘긴다.
    /// 낙하 회수와 같은 이유로 판정은 높이 하나면 충분하다 — 옆으로 돌아가는 경로가 없다.
    /// 페이드는 SceneTransitionController 가 이미 갖고 있어 여기서 따로 그리지 않는다.
    /// </summary>
    public class WebStageExit : MonoBehaviour
    {
        [Tooltip("이 높이를 넘으면 EndScene 으로 전환한다. 1스테이지 최상단보다 조금 위로.")]
        [SerializeField] private float exitY = 375f;

        private const float GizmoLineHalfWidth = 60f;

        private SlimeController slime;
        private bool fired;

        private void Awake()
        {
            slime = GetComponent<SlimeController>();
        }

        private void Update()
        {
            if (fired || slime == null) return;
            if (slime.Position.y < exitY) return;

            fired = true;
            SceneTransitionController.Play(GameFlow.EndScene);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3 c = transform.position;
            Gizmos.DrawLine(new Vector3(c.x - GizmoLineHalfWidth, exitY, c.z),
                            new Vector3(c.x + GizmoLineHalfWidth, exitY, c.z));
        }
    }
}
