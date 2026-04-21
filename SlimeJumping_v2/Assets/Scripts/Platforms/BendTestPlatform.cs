using UnityEngine;
using MegaFiers;

namespace SlimeJumping.Platforms
{
    // Validation harness for MegaFiers Bend + LocalTrail RT combo.
    // Collider stays rigid; only the visual mesh deforms. Drive bend.angle from the
    // inspector to verify trail marks painted at θ=0 stay glued to the deforming
    // surface at other angles (i.e. the UV-channel trail sampling is correct).
    [AddComponentMenu("SlimeJumping/Debug/Bend Test Platform")]
    public class BendTestPlatform : MonoBehaviour
    {
        [SerializeField] MegaBend bend;
        [Range(-60f, 60f)] public float manualAngle = 0f;
        [SerializeField] MegaAxis bendAxis = MegaAxis.X;

        void Reset()
        {
            bend = GetComponentInChildren<MegaBend>();
        }

        void Update()
        {
            // Update in Update (not LateUpdate) so MegaModifyObject's LateUpdate sees
            // the new angle before it issues the deform job. Writing in LateUpdate
            // would race against the modifier's own LateUpdate read.
            if (bend == null) return;
            bend.axis = bendAxis;
            bend.angle = manualAngle;
        }
    }
}
