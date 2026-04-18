using UnityEngine;
using SlimeJumping.Platforms;

namespace SlimeJumping.Mechanisms
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WaypointTiltPlatform))]
    public class WaypointTiltRiderGate : MonoBehaviour
    {
        [SerializeField] private PlatformRiderSensor sensor;

        private WaypointTiltPlatform platform;

        void Awake()
        {
            platform = GetComponent<WaypointTiltPlatform>();
            if (sensor == null) sensor = GetComponentInChildren<PlatformRiderSensor>();

            if (sensor == null)
                Debug.LogError($"[WaypointTiltRiderGate] {name} has no PlatformRiderSensor assigned or in children. Platform will never advance.", this);

            platform.CanAdvance = () => sensor != null && sensor.RiderCount > 0;
        }
    }
}
