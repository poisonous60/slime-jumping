using UnityEngine;
using UnityEngine.Events;

namespace SlimeJumping.Mechanisms
{
    public class SignalSource : MonoBehaviour
    {
        public UnityEvent onActivate;
        public UnityEvent onDeactivate;

        protected void Activate() => onActivate?.Invoke();
        protected void Deactivate() => onDeactivate?.Invoke();
    }
}
