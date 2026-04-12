using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    public class CCTVTracker : MonoBehaviour
    {
        public enum LensAxis { PositiveZ, NegativeZ, PositiveX, NegativeX, PositiveY, NegativeY }

        [Header("Head")]
        [Tooltip("Transform that will be rotated to face the target (the rotating camera head node).")]
        [SerializeField] private Transform headTransform;
        [Tooltip("Which local axis of headTransform points toward the lens. Pick the one that matches the red(X) / green(Y) / blue(Z) gizmo arrow emerging from the lens.")]
        [SerializeField] private LensAxis lensAxis = LensAxis.PositiveZ;

        [Header("Target")]
        [Tooltip("If null, auto-finds SlimeController in scene at Start.")]
        [SerializeField] private Transform target;

        [Header("Tracking")]
        [SerializeField] private float trackingRange = 20f;
        [SerializeField] private float angularSpeed = 120f;

        [Header("Angle Limits (degrees, relative to initial lens direction)")]
        [Range(0f, 180f)] [SerializeField] private float maxLeftDeg = 90f;
        [Range(0f, 180f)] [SerializeField] private float maxRightDeg = 90f;
        [Range(0f, 90f)]  [SerializeField] private float maxUpDeg = 60f;
        [Range(0f, 90f)]  [SerializeField] private float maxDownDeg = 60f;

        private Quaternion initialLocalRotation;
        private bool captured;

        private void Start()
        {
            if (headTransform != null)
            {
                initialLocalRotation = headTransform.localRotation;
                captured = true;
            }
            if (target == null)
            {
                var slime = FindFirstObjectByType<SlimeController>(FindObjectsInactive.Exclude);
                if (slime != null) target = slime.transform;
            }
        }

        private void Update()
        {
            if (headTransform == null || !captured) return;

            Quaternion parentRot = headTransform.parent != null ? headTransform.parent.rotation : Quaternion.identity;
            Quaternion idleWorld = parentRot * initialLocalRotation;
            Quaternion desired = idleWorld;

            if (target != null)
            {
                Vector3 dir = target.position - headTransform.position;
                float sqr = dir.sqrMagnitude;
                if (sqr <= trackingRange * trackingRange && sqr > 0.0001f)
                {
                    Vector3 lensLocal = AxisToVec(lensAxis);
                    BuildBasis(idleWorld * lensLocal, out Vector3 fwd, out Vector3 right, out Vector3 up);
                    Vector3 clampedDir = ClampDirToBox(dir, fwd, right, up,
                        maxLeftDeg, maxRightDeg, maxUpDeg, maxDownDeg);
                    desired = Quaternion.LookRotation(clampedDir, Vector3.up)
                            * Quaternion.FromToRotation(lensLocal, Vector3.forward);
                }
            }

            headTransform.rotation = Quaternion.RotateTowards(
                headTransform.rotation, desired, angularSpeed * Time.deltaTime);
        }

        private static Vector3 AxisToVec(LensAxis a)
        {
            switch (a)
            {
                case LensAxis.PositiveZ: return Vector3.forward;
                case LensAxis.NegativeZ: return Vector3.back;
                case LensAxis.PositiveX: return Vector3.right;
                case LensAxis.NegativeX: return Vector3.left;
                case LensAxis.PositiveY: return Vector3.up;
                case LensAxis.NegativeY: return Vector3.down;
            }
            return Vector3.forward;
        }

        // Orthonormal basis around initialForward. World up is the preferred "camera up"
        // reference; falls back when forward is parallel to world up (e.g. looking straight down).
        private static void BuildBasis(Vector3 initialForward, out Vector3 fwd, out Vector3 right, out Vector3 up)
        {
            fwd = initialForward.sqrMagnitude > 0.0001f ? initialForward.normalized : Vector3.forward;
            Vector3 refUp = Vector3.up;
            right = Vector3.Cross(refUp, fwd);
            if (right.sqrMagnitude < 0.0001f)
            {
                right = Vector3.Cross(Vector3.forward, fwd);
                if (right.sqrMagnitude < 0.0001f) right = Vector3.Cross(Vector3.right, fwd);
            }
            right.Normalize();
            up = Vector3.Cross(fwd, right).normalized;
        }

        // Clamp dir into the yaw/pitch rectangle. Yaw+ = right, Pitch+ = up.
        private static Vector3 ClampDirToBox(Vector3 dir, Vector3 fwd, Vector3 right, Vector3 up,
            float maxLeft, float maxRight, float maxUp, float maxDown)
        {
            Vector3 d = dir.normalized;
            float dotF = Vector3.Dot(d, fwd);
            float dotR = Vector3.Dot(d, right);
            float dotU = Vector3.Dot(d, up);

            float yaw = Mathf.Atan2(dotR, dotF); // + right, - left (radians)
            float pitch = Mathf.Atan2(dotU, Mathf.Sqrt(dotF * dotF + dotR * dotR)); // + up, - down

            // Outside forward hemisphere: yaw saturates to nearest side before clamp.
            if (dotF < 0f)
            {
                yaw = dotR >= 0f ? Mathf.PI * 0.5f : -Mathf.PI * 0.5f;
            }

            float yawClamped = Mathf.Clamp(yaw, -maxLeft * Mathf.Deg2Rad, maxRight * Mathf.Deg2Rad);
            float pitchClamped = Mathf.Clamp(pitch, -maxDown * Mathf.Deg2Rad, maxUp * Mathf.Deg2Rad);

            float cy = Mathf.Cos(yawClamped), sy = Mathf.Sin(yawClamped);
            float cp = Mathf.Cos(pitchClamped), sp = Mathf.Sin(pitchClamped);
            return cp * (cy * fwd + sy * right) + sp * up;
        }

        private void OnDrawGizmosSelected()
        {
            if (headTransform == null) return;

            Vector3 origin = headTransform.position;
            Vector3 lensLocal = AxisToVec(lensAxis);

            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.25f);
            Gizmos.DrawWireSphere(origin, trackingRange);

            // Live lens direction (yellow, updates with inspector changes)
            Vector3 lensWorldNow = headTransform.rotation * lensLocal;
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(origin, origin + lensWorldNow * 2f);

            // Initial (rest) basis
            Quaternion idleWorld = Application.isPlaying && captured
                ? (headTransform.parent != null ? headTransform.parent.rotation : Quaternion.identity) * initialLocalRotation
                : headTransform.rotation;
            Vector3 initialForward = idleWorld * lensLocal;
            BuildBasis(initialForward, out Vector3 fwd, out Vector3 right, out Vector3 up);

            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.9f);
            Gizmos.DrawLine(origin, origin + fwd * trackingRange);

            // Draw the clamp box as 4 extreme edge directions + connecting arcs
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.5f);
            Vector3 dL = DirFromYawPitch(fwd, right, up, -maxLeftDeg, 0f);
            Vector3 dR = DirFromYawPitch(fwd, right, up,  maxRightDeg, 0f);
            Vector3 dU = DirFromYawPitch(fwd, right, up, 0f,  maxUpDeg);
            Vector3 dD = DirFromYawPitch(fwd, right, up, 0f, -maxDownDeg);
            Gizmos.DrawLine(origin, origin + dL * trackingRange);
            Gizmos.DrawLine(origin, origin + dR * trackingRange);
            Gizmos.DrawLine(origin, origin + dU * trackingRange);
            Gizmos.DrawLine(origin, origin + dD * trackingRange);

            // 4 corner directions
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.3f);
            Vector3 cTL = DirFromYawPitch(fwd, right, up, -maxLeftDeg,  maxUpDeg);
            Vector3 cTR = DirFromYawPitch(fwd, right, up,  maxRightDeg, maxUpDeg);
            Vector3 cBL = DirFromYawPitch(fwd, right, up, -maxLeftDeg, -maxDownDeg);
            Vector3 cBR = DirFromYawPitch(fwd, right, up,  maxRightDeg,-maxDownDeg);
            Gizmos.DrawLine(origin + cTL * trackingRange, origin + cTR * trackingRange);
            Gizmos.DrawLine(origin + cTR * trackingRange, origin + cBR * trackingRange);
            Gizmos.DrawLine(origin + cBR * trackingRange, origin + cBL * trackingRange);
            Gizmos.DrawLine(origin + cBL * trackingRange, origin + cTL * trackingRange);
        }

        private static Vector3 DirFromYawPitch(Vector3 fwd, Vector3 right, Vector3 up, float yawDeg, float pitchDeg)
        {
            float y = yawDeg * Mathf.Deg2Rad;
            float p = pitchDeg * Mathf.Deg2Rad;
            float cy = Mathf.Cos(y), sy = Mathf.Sin(y);
            float cp = Mathf.Cos(p), sp = Mathf.Sin(p);
            return cp * (cy * fwd + sy * right) + sp * up;
        }
    }
}
