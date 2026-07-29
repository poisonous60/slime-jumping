using UnityEngine;

namespace SlimeJumping.MeshTools
{
    // SelectionBase marker for MeshToCollider2DGenerator wrappers: scene-view clicks on the child
    // mesh select this wrapper (owner of the PolygonCollider2D) instead of the child.
    [SelectionBase]
    [DisallowMultipleComponent]
    public class MeshCollider2DWrapper : MonoBehaviour { }
}
