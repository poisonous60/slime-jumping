using UnityEngine;
using SlimeJumping.Physics;

namespace SlimeJumping.Camera
{
    /// <summary>
    /// Provides a smoothed follow position for Cinemachine.
    /// Tracks the kinematic center node with velocity-based lookahead.
    /// </summary>
    public class SlimeCameraTarget : MonoBehaviour
    {
        [SerializeField] private SlimeSoftBody softBody;
        [SerializeField] private float smoothTime = 0.15f;
        [SerializeField] private float lookaheadFactor = 0.3f;
        [SerializeField] private float lookaheadSmooth = 5f;

        private Vector2 currentVelocity;
        private Vector2 currentLookahead;

        private void LateUpdate()
        {
            if (softBody == null || softBody.Nodes == null) return;

            // v3: track kinematic center node directly (more stable than CoM)
            Vector2 targetPos = softBody.CenterNode.Position;

            Vector2 velocity = softBody.CenterNode.Velocity;
            Vector2 desiredLookahead = velocity * lookaheadFactor;
            currentLookahead = Vector2.Lerp(currentLookahead, desiredLookahead, Time.deltaTime * lookaheadSmooth);

            Vector2 finalTarget = targetPos + currentLookahead;

            Vector2 smoothed = Vector2.SmoothDamp(
                (Vector2)transform.position,
                finalTarget,
                ref currentVelocity,
                smoothTime
            );

            transform.position = new Vector3(smoothed.x, smoothed.y, transform.position.z);
        }
    }
}
