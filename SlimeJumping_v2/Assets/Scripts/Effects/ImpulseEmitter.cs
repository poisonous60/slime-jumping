using UnityEngine;
using Unity.Cinemachine;

namespace SlimeJumping.Effects
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CinemachineImpulseSource))]
    public class ImpulseEmitter : MonoBehaviour
    {
        private CinemachineImpulseSource source;

        void Awake() => source = GetComponent<CinemachineImpulseSource>();

        public void Emit(float force = 1f)
        {
            if (source == null) return;
            source.GenerateImpulseWithForce(force);
        }
    }
}
