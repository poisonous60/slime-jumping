using UnityEngine;
using SlimeJumping.Platforms;

namespace SlimeJumping.Mechanisms
{
    [RequireComponent(typeof(WaypointPlatform))]
    public class ElevatorJoltVisual : MonoBehaviour
    {
        [SerializeField] private Transform visualTransform;
        [SerializeField] private float offsetMagnitude = 0.05f;
        [SerializeField] private float duration = 0.12f;

        private WaypointPlatform platform;
        private Vector3 baseLocal;
        private float timer = -1f;
        private Vector2 joltDir;

        void Awake()
        {
            platform = GetComponent<WaypointPlatform>();
            if (visualTransform != null) baseLocal = visualTransform.localPosition;
        }

        void OnEnable()
        {
            if (platform != null) platform.OnSegmentArrived += HandleArrived;
        }

        void OnDisable()
        {
            if (platform != null) platform.OnSegmentArrived -= HandleArrived;
        }

        void HandleArrived()
        {
            joltDir = -platform.LastDirection;
            timer = 0f;
        }

        void LateUpdate()
        {
            if (visualTransform == null || timer < 0f) return;

            timer += Time.deltaTime;
            float t = Mathf.Clamp01(duration > 0f ? timer / duration : 1f);
            float ease = 1f - (1f - t) * (1f - t);
            float amt = offsetMagnitude * (1f - ease);
            visualTransform.localPosition = baseLocal + (Vector3)(joltDir * amt);

            if (t >= 1f)
            {
                visualTransform.localPosition = baseLocal;
                timer = -1f;
            }
        }
    }
}
