using UnityEngine;

namespace SlimeJumping.UI.Framework
{
    /// Tag component. Attach on a child GameObject inside a panel prefab and set
    /// its SlotType so the panel's controller can bind the child without a
    /// hardcoded hierarchy path. Mirrors the MemoSlot pattern already proven in
    /// the Memo prefabs.
    [DisallowMultipleComponent]
    public class UISlot : MonoBehaviour
    {
        public UISlotType slotType;
    }
}
