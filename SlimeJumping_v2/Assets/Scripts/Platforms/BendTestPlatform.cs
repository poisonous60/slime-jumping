using UnityEngine;
using MegaFiers;

namespace SlimeJumping.Platforms
{
    // MegaFiers Bend + LocalTrail RT harness: collider stays rigid, only the visual mesh bends.
    // Drive the angle to check trail marks painted at θ=0 stay glued to the deformed surface.
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
            // Must write in Update: MegaModifyObject reads the angle in its own LateUpdate deform job.
            if (bend == null) return;
            bend.axis = bendAxis;
            bend.angle = manualAngle;
        }
    }
}
