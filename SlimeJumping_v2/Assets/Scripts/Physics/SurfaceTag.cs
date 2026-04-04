using UnityEngine;

namespace SlimeJumping.Physics
{
    public class SurfaceTag : MonoBehaviour
    {
        public SurfaceMaterial material;

        public SurfaceModifiers GetModifiers() => material != null
            ? material.ToModifiers()
            : SurfaceModifiers.Default;
    }
}
