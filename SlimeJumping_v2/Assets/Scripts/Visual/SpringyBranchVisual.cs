using UnityEngine;
using SlimeJumping.Platforms;

namespace SlimeJumping.Visual
{
    // Bends a bone chain in sync with SpringyBranchPlatform's deflection.
    // Asset-agnostic: drop any sprite/mesh/SpriteSkin rig under the bones.
    // Writes only bone.localRotation — never feeds back into physics.
    [DefaultExecutionOrder(100)]
    public class SpringyBranchVisual : MonoBehaviour
    {
        public enum BendAxis { X, Y, Z }

        [SerializeField] SpringyBranchPlatform motor;
        [SerializeField] Transform[] bones;
        [SerializeField] AnimationCurve bendWeight = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField, Min(0f)] float visualLag = 0.05f;

        [Header("Bend Axis")]
        [Tooltip("Which local axis of each bone to rotate around. Z = Unity-native 2D bending. " +
                 "FBX imported from Blender (Z-up) often needs Y due to axis conversion. " +
                 "If bend direction is flipped, toggle Invert Bend.")]
        [SerializeField] BendAxis bendAxis = BendAxis.Z;
        [SerializeField] bool invertBend = false;

        float filteredDeflection;
        Quaternion[] restRotations;

        void Awake()
        {
            CaptureRestPose();
        }

        void CaptureRestPose()
        {
            if (bones == null) { restRotations = null; return; }
            restRotations = new Quaternion[bones.Length];
            for (int i = 0; i < bones.Length; i++)
                restRotations[i] = bones[i] != null ? bones[i].localRotation : Quaternion.identity;
        }

        void LateUpdate()
        {
            if (motor == null || bones == null || bones.Length == 0) return;
            if (restRotations == null || restRotations.Length != bones.Length) CaptureRestPose();

            float target = motor.CurrentDeflectionDeg;
            float lag = Mathf.Max(visualLag, 1e-4f);
            filteredDeflection = Mathf.Lerp(filteredDeflection, target, Time.deltaTime / lag);

            float sign = invertBend ? -1f : 1f;
            int n = bones.Length;
            float prevW = bendWeight.Evaluate(0f);
            for (int i = 0; i < n; i++)
            {
                float currT = (i + 1f) / n;
                float currW = bendWeight.Evaluate(currT);
                float boneDelta = filteredDeflection * (currW - prevW) * sign;
                if (bones[i] != null)
                    bones[i].localRotation = restRotations[i] * BuildRotation(boneDelta);
                prevW = currW;
            }
        }

        Quaternion BuildRotation(float delta)
        {
            switch (bendAxis)
            {
                case BendAxis.X: return Quaternion.Euler(delta, 0f, 0f);
                case BendAxis.Y: return Quaternion.Euler(0f, delta, 0f);
                default:         return Quaternion.Euler(0f, 0f, delta);
            }
        }
    }
}
