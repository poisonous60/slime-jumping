using UnityEngine;

namespace SlimeJumping.Platforms
{
    public class RotatingPlatform : PlatformMotor
    {
        public enum RotationAxis { X, Y, Z }

        [SerializeField] RotationAxis axis = RotationAxis.Z;
        // X/Y는 2D 물리에 존재하지 않는 축이라 이 Transform만 돌리고 콜라이더는 그대로 둔다.
        [SerializeField] Transform visualRoot;
        [SerializeField] float degreesPerSecond = 45f;
        [SerializeField] bool oscillate = false;
        [SerializeField] float oscillateRange = 90f;

        float baseRotation;
        Vector3 baseVisualEuler;
        float elapsed;
        float angle;

        protected override void Awake()
        {
            base.Awake();
            baseRotation = rb.rotation;

            if (visualRoot != null) baseVisualEuler = visualRoot.localEulerAngles;
            else if (axis != RotationAxis.Z)
                Debug.LogWarning($"{name}: axis={axis} needs visualRoot assigned.", this);
        }

        protected override (Vector2, float) ComputeMotion(float dt)
        {
            elapsed += dt;

            angle = oscillate
                ? Mathf.Sin(elapsed * degreesPerSecond * Mathf.Deg2Rad) * oscillateRange
                : elapsed * degreesPerSecond;

            return (rb.position, axis == RotationAxis.Z ? baseRotation + angle : baseRotation);
        }

        protected override void OnAfterMotion(float dt)
        {
            if (axis == RotationAxis.Z || visualRoot == null) return;

            Vector3 e = baseVisualEuler;
            if (axis == RotationAxis.X) e.x += angle;
            else e.y += angle;
            visualRoot.localEulerAngles = e;
        }
    }
}
