using UnityEngine;

namespace SlimeJumping.UI.Framework
{
    /// Tag on a panel-prefab child so its controller binds by slotType instead of a hierarchy path.
    [DisallowMultipleComponent]
    public class UISlot : MonoBehaviour
    {
        public UISlotType slotType;
    }
}
