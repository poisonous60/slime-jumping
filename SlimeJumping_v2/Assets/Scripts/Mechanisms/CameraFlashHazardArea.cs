using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    [RequireComponent(typeof(Collider2D))]
    public class CameraFlashHazardArea : MonoBehaviour
    {
        [SerializeField] private CameraFlashHazard owner;

        void Reset()
        {
            var col = GetComponent<Collider2D>();
            if (col != null) col.isTrigger = true;
        }

        void OnTriggerEnter2D(Collider2D c)
        {
            if (owner != null) owner.NotifyEnter(c);
        }

        void OnTriggerExit2D(Collider2D c)
        {
            if (owner != null) owner.NotifyExit(c);
        }
    }
}
