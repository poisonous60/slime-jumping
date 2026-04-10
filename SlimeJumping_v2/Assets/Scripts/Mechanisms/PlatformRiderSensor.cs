using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    [RequireComponent(typeof(Collider2D))]
    public class PlatformRiderSensor : MonoBehaviour
    {
        public int RiderCount { get; private set; }

        void Reset()
        {
            GetComponent<Collider2D>().isTrigger = true;
        }

        void OnTriggerEnter2D(Collider2D c)
        {
            if (c.GetComponentInParent<SlimeController>() != null)
                RiderCount++;
        }

        void OnTriggerExit2D(Collider2D c)
        {
            if (c.GetComponentInParent<SlimeController>() != null)
                RiderCount = Mathf.Max(0, RiderCount - 1);
        }
    }
}
