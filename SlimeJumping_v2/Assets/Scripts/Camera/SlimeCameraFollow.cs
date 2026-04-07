using UnityEngine;

namespace SlimeJumping.Camera
{
    public class SlimeCameraFollow : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private float smoothTime = 0.0f;
        [SerializeField] private Vector3 offset = new Vector3(0f, 1f, -6f);

        private Vector3 velocity;

        private void LateUpdate()
        {
            if (target == null) return;

            Vector3 desired = target.position + offset;
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref velocity, smoothTime);
        }
    }
}
