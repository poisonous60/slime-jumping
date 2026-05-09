using UnityEngine;

namespace SlimeJumping.MeshTools
{
    // SelectionBase marker for MeshToCollider2DGenerator wrappers. Scene-view clicks on the
    // child mesh route the selection up to this wrapper (which owns the PolygonCollider2D);
    // Hierarchy clicks still pick the child directly when needed.
    [SelectionBase]
    [DisallowMultipleComponent]
    public class MeshCollider2DWrapper : MonoBehaviour { }
}
