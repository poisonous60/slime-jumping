using UnityEngine;
using UnityEngine.InputSystem;
using SlimeJumping.Controller;

namespace SlimeJumping.Debugging
{
    /// <summary>On the Slime root: logs per-frame deltas of trembling-prone transforms for a capture window started by the trigger key.</summary>
    public class SlimeJitterProbe : MonoBehaviour
    {
        [SerializeField] SlimeController controller;
        [SerializeField] Transform slimeVisual;
        [SerializeField] Transform body;
        [SerializeField] Transform body3d;
        [Tooltip("Optional: a reference platform transform for relative offset logging")]
        [SerializeField] Transform platform;
        [Tooltip("Number of frames to capture once started")]
        [SerializeField] int captureFrames = 60;
        [Tooltip("Key to start a capture window")]
        [SerializeField] Key triggerKey = Key.G;

        Rigidbody2D rb;

        Vector3 prevRootPos;
        Vector3 prevVisualPos;
        Vector3 prevBodyLocalPos;
        Vector3 prevBodyLocalScale;
        Vector3 prevBody3dLocalPos;
        Vector3 prevBody3dLocalScale;
        Quaternion prevBodyLocalRot;
        Vector2 prevPlatformOffset;
        int framesLeft;
        int frameIdx;

        void Awake()
        {
            if (controller == null) controller = GetComponent<SlimeController>();
            rb = GetComponent<Rigidbody2D>();
        }

        void LateUpdate()
        {
            if (Keyboard.current != null && Keyboard.current[triggerKey].wasPressedThisFrame)
            {
                framesLeft = captureFrames;
                frameIdx = 0;
                prevRootPos = transform.position;
                prevVisualPos = slimeVisual != null ? slimeVisual.position : Vector3.zero;
                prevBodyLocalPos = body != null ? body.localPosition : Vector3.zero;
                prevBodyLocalScale = body != null ? body.localScale : Vector3.one;
                prevBodyLocalRot = body != null ? body.localRotation : Quaternion.identity;
                prevBody3dLocalPos = body3d != null ? body3d.localPosition : Vector3.zero;
                prevBody3dLocalScale = body3d != null ? body3d.localScale : Vector3.one;
                prevPlatformOffset = platform != null
                    ? (Vector2)transform.position - (Vector2)platform.position
                    : Vector2.zero;
                Debug.Log($"[JitterProbe] capture start frames={captureFrames}");
                return;
            }

            if (framesLeft <= 0) return;

            Vector3 rootPos = transform.position;
            Vector3 visualPos = slimeVisual != null ? slimeVisual.position : Vector3.zero;
            Vector3 bodyLocalPos = body != null ? body.localPosition : Vector3.zero;
            Vector3 bodyLocalScale = body != null ? body.localScale : Vector3.one;
            Quaternion bodyLocalRot = body != null ? body.localRotation : Quaternion.identity;
            Vector3 body3dLocalPos = body3d != null ? body3d.localPosition : Vector3.zero;
            Vector3 body3dLocalScale = body3d != null ? body3d.localScale : Vector3.one;
            Vector2 platformOffset = platform != null
                ? (Vector2)transform.position - (Vector2)platform.position
                : Vector2.zero;

            Vector3 dRoot = rootPos - prevRootPos;
            Vector3 dVisual = visualPos - prevVisualPos;
            Vector3 dBodyPos = bodyLocalPos - prevBodyLocalPos;
            Vector3 dBodyScale = bodyLocalScale - prevBodyLocalScale;
            float dBodyRotDeg = Quaternion.Angle(prevBodyLocalRot, bodyLocalRot);
            Vector3 dBody3dPos = body3dLocalPos - prevBody3dLocalPos;
            Vector3 dBody3dScale = body3dLocalScale - prevBody3dLocalScale;
            Vector2 dRelOffset = platformOffset - prevPlatformOffset;

            string stateStr = controller != null ? controller.CurrentState.ToString() : "?";
            Vector2 rbVel = rb != null ? rb.linearVelocity : Vector2.zero;
            Vector2 ctrlVel = controller != null ? controller.Velocity : Vector2.zero;
            Debug.Log(
                $"[JitterProbe f{frameIdx}] " +
                $"rootΔ=({dRoot.x:F4},{dRoot.y:F4}) " +
                $"visualΔ=({dVisual.x:F4},{dVisual.y:F4}) " +
                $"bodyLocalPosΔ=({dBodyPos.x:F4},{dBodyPos.y:F4}) " +
                $"bodyLocalScaleΔ=({dBodyScale.x:F4},{dBodyScale.y:F4}) " +
                $"bodyRotΔ={dBodyRotDeg:F3}deg " +
                $"body3dPosΔ=({dBody3dPos.x:F4},{dBody3dPos.y:F4}) " +
                $"body3dScaleΔ=({dBody3dScale.x:F4},{dBody3dScale.y:F4}) " +
                $"slime-platformΔ=({dRelOffset.x:F4},{dRelOffset.y:F4}) " +
                $"vel={rbVel} ctrlVel={ctrlVel} state={stateStr}"
            );

            prevRootPos = rootPos;
            prevVisualPos = visualPos;
            prevBodyLocalPos = bodyLocalPos;
            prevBodyLocalScale = bodyLocalScale;
            prevBodyLocalRot = bodyLocalRot;
            prevBody3dLocalPos = body3dLocalPos;
            prevBody3dLocalScale = body3dLocalScale;
            prevPlatformOffset = platformOffset;

            frameIdx++;
            framesLeft--;
            if (framesLeft == 0)
                Debug.Log($"[JitterProbe] capture done");
        }
    }
}
