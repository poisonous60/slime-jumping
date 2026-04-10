using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    public class PortalSwapper : MonoBehaviour
    {
        [SerializeField] private Portal target;
        [SerializeField] private Transform slotA;
        [SerializeField] private Transform slotB;
        [SerializeField] private float swapDelay = 1f;

        private bool atSlotA = true;

        public void Toggle()
        {
            Invoke(nameof(DoSwap), swapDelay);
        }

        void DoSwap()
        {
            Transform dest = atSlotA ? slotB : slotA;
            target.transform.SetPositionAndRotation(dest.position, dest.rotation);
            atSlotA = !atSlotA;
        }
    }
}
