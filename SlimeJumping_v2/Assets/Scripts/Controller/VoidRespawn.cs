using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;

namespace SlimeJumping.Controller
{
    /// <summary>
    /// 맵 밖으로 떨어진 슬라임 회수. Kill Y 아래로 내려가면 화면을 검게 덮고, 검은 동안 Respawn Point 로
    /// 옮긴 뒤 다시 걷는다. 낙하는 끝이 없으므로 판정은 높이 하나로 충분하다.
    /// 페이드 캔버스는 런타임 생성 — UIRoot 배선 없이 어느 씬에서도 같은 동작.
    /// </summary>
    public class VoidRespawn : MonoBehaviour
    {
        [Tooltip("이 높이 아래로 내려가면 페이드아웃이 시작된다. 맵 최하단보다 충분히 아래로.")]
        [SerializeField] private float killY = -60f;
        [Tooltip("검은 화면 동안 슬라임이 놓일 지점. 씬에 빈 오브젝트를 두고 드래그해서 맞춘다.")]
        [SerializeField] private Transform respawnPoint;
        [SerializeField] private VoidRespawnConfig configOverride;

        private const float GizmoLineHalfWidth = 60f;

        private SlimeController slime;
        private Image fadeImage;
        private bool respawning;

        private void Awake()
        {
            slime = GetComponent<SlimeController>();
        }

        private void Update()
        {
            if (respawning || slime == null || respawnPoint == null) return;
            if (slime.Position.y < killY) StartCoroutine(RespawnRoutine());
        }

        private IEnumerator RespawnRoutine()
        {
            respawning = true;

            VoidRespawnConfig config = VoidRespawnConfig.Resolve(configOverride);
            float fadeOut = config != null ? config.fadeOutSeconds : 1.2f;
            float fadeIn = config != null ? config.fadeInSeconds : 0.8f;

            EnsureFadeImage();
            yield return Fade(0f, 1f, fadeOut, config != null ? config.fadeOutCurve : null);

            Vector2 before = slime.Position;
            slime.TeleportTo(respawnPoint.position, Vector2.zero);
            // 카메라도 즉시 워프 — 없으면 심연에서 복귀 지점까지 팬이 페이드인 뒤에도 이어진다.
            CinemachineCore.OnTargetObjectWarped(slime.transform, (Vector3)(slime.Position - before));

            yield return null;
            yield return Fade(1f, 0f, fadeIn, config != null ? config.fadeInCurve : null);

            respawning = false;
        }

        // 곡선 y = 검은 정도 그 자체. 비어 있으면 from→to 직선으로 떨어진다.
        private IEnumerator Fade(float from, float to, float seconds, AnimationCurve curve)
        {
            if (seconds <= 0f)
            {
                SetAlpha(to);
                yield break;
            }

            bool shaped = curve != null && curve.length > 0;
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / seconds);
                SetAlpha(Mathf.Clamp01(shaped ? curve.Evaluate(u) : Mathf.Lerp(from, to, u)));
                yield return null;
            }
            SetAlpha(to);
        }

        private void SetAlpha(float a)
        {
            if (fadeImage != null) fadeImage.color = new Color(0f, 0f, 0f, a);
        }

        private void EnsureFadeImage()
        {
            if (fadeImage != null) return;

            var canvasGo = new GameObject("VoidRespawnFade");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;

            var imageGo = new GameObject("Black");
            imageGo.transform.SetParent(canvasGo.transform, false);
            fadeImage = imageGo.AddComponent<Image>();
            fadeImage.raycastTarget = false;
            fadeImage.color = new Color(0f, 0f, 0f, 0f);

            RectTransform rt = fadeImage.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private void OnDrawGizmos()
        {
            float x = respawnPoint != null ? respawnPoint.position.x : transform.position.x;
            Gizmos.color = new Color(1f, 0.25f, 0.25f, 0.9f);
            Gizmos.DrawLine(new Vector3(x - GizmoLineHalfWidth, killY, 0f),
                            new Vector3(x + GizmoLineHalfWidth, killY, 0f));

            if (respawnPoint == null) return;
            Vector3 p = respawnPoint.position;
            Gizmos.color = new Color(0.3f, 1f, 0.45f, 0.9f);
            Gizmos.DrawWireSphere(p, 1f);
            Gizmos.DrawLine(p, p + Vector3.down * 2f);
            Gizmos.DrawLine(p + Vector3.down * 2f, p + new Vector3(-0.6f, -1.2f, 0f));
            Gizmos.DrawLine(p + Vector3.down * 2f, p + new Vector3(0.6f, -1.2f, 0f));
        }
    }
}
