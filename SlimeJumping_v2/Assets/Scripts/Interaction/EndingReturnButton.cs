using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using SlimeJumping.Controller;
using SlimeJumping.Platforms;
using SlimeJumping.UI;

namespace SlimeJumping.Interaction
{
    /// EndScene 전용 Return 버튼 발판. 슬라임이 윗면을 밟거나(피아노 건반식 x 겹침 +
    /// 바닥-윗면 근접 판정) 마우스로 클릭하면 비주얼만 눌림 연출 후 메인 메뉴로 돌아간다.
    /// 콜라이더는 움직이지 않는다. AsciiArtPlatform 빌드가 끝난 오브젝트에 부착한다.
    public class EndingReturnButton : MonoBehaviour
    {
        [Header("밟기 판정")]
        [SerializeField] private float stompXOverlap = 0.05f;
        [SerializeField] private float stompYTolerance = 0.15f;

        [Header("눌림 연출")]
        [SerializeField] private float pressDepth = 0.18f;
        [SerializeField] private float pressDuration = 0.12f;
        [SerializeField] private float returnDelay = 0.5f;

        private Collider2D buttonCollider;
        private Transform visual;
        private SlimeController slime;
        private float nextSlimeSearch;
        private bool fired;

        void Awake()
        {
            buttonCollider = GetComponent<Collider2D>();
            var platform = GetComponent<AsciiArtPlatform>();
            if (platform != null && platform.VisualText != null)
                visual = platform.VisualText.transform;
        }

        void FixedUpdate()
        {
            if (fired || buttonCollider == null) return;
            if (slime == null)
            {
                if (Time.time < nextSlimeSearch) return;
                nextSlimeSearch = Time.time + 1f;
                slime = FindFirstObjectByType<SlimeController>();
                if (slime == null) return;
            }

            Bounds b = buttonCollider.bounds;
            Vector2 p = slime.Position;
            float r = slime.SlimeRadius;
            float overlap = Mathf.Min(p.x + r, b.max.x) - Mathf.Max(p.x - r, b.min.x);
            if (overlap > stompXOverlap && Mathf.Abs(p.y - r - b.max.y) < stompYTolerance)
                Fire();
        }

        void Update()
        {
            if (fired || buttonCollider == null) return;
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            var cam = UnityEngine.Camera.main;
            if (cam == null) return;
            Vector2 world = cam.ScreenToWorldPoint(mouse.position.ReadValue());
            if (buttonCollider.OverlapPoint(world)) Fire();
        }

        private void Fire()
        {
            fired = true;
            StartCoroutine(PressAndReturn());
        }

        private IEnumerator PressAndReturn()
        {
            Vector3 rest = visual != null ? visual.localPosition : Vector3.zero;
            Vector3 pressed = rest + Vector3.down * pressDepth;
            float t = 0f;
            while (visual != null && t < pressDuration)
            {
                t += Time.deltaTime;
                visual.localPosition = Vector3.Lerp(rest, pressed, Mathf.Clamp01(t / pressDuration));
                yield return null;
            }
            if (returnDelay > 0f) yield return new WaitForSeconds(returnDelay);
            GameFlow.LoadMainMenu();
        }
    }
}
