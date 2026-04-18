using UnityEngine;

namespace SlimeJumping.Interaction
{
    public enum MemoSlotType
    {
        PanelRoot,
        Background,
        Title,
        Body,
        Illustration
    }

    public class MemoSlot : MonoBehaviour
    {
        public MemoSlotType slotType;
    }
}
