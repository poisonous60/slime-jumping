using UnityEngine;
using SlimeJumping.Settings;

namespace SlimeJumping.Physics
{
    public class SurfaceTag : MonoBehaviour
    {
        public SurfaceMaterial material;

        public SurfaceModifiers GetModifiers() => material != null
            ? material.ToModifiers()
            : SurfaceModifiers.Default;

        // First enabled SurfaceTag up the parent chain wins; no tag / no override falls back to SlimeSettings.
        public static void ResolveCeilingThresholds(Collider2D col, SlimeSettings settings,
            out float ceilingThreshold, out float ceilingReattachThreshold)
        {
            if (col != null)
            {
                var tags = col.GetComponentsInParent<SurfaceTag>();
                for (int i = 0; i < tags.Length; i++)
                {
                    if (!tags[i].enabled) continue;
                    var mat = tags[i].material;
                    if (mat != null && mat.overrideCeilingAngle)
                    {
                        ceilingThreshold = Mathf.Cos(mat.ceilingAngle * Mathf.Deg2Rad);
                        ceilingReattachThreshold = Mathf.Cos(mat.ceilingReattachAngle * Mathf.Deg2Rad);
                        return;
                    }
                    break;
                }
            }
            ceilingThreshold = settings.ceilingThreshold;
            ceilingReattachThreshold = settings.ceilingReattachThreshold;
        }
    }
}
