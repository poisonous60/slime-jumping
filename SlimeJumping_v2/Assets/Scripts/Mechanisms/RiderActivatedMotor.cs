using UnityEngine;
using SlimeJumping.Platforms;

namespace SlimeJumping.Mechanisms
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WaypointPlatform))]
    public class RiderActivatedMotor : MonoBehaviour
    {
        [SerializeField] private PlatformRiderSensor sensor;

        private WaypointPlatform platform;

        void Awake()
        {
            platform = GetComponent<WaypointPlatform>();
            if (sensor == null) sensor = GetComponentInChildren<PlatformRiderSensor>();

            if (sensor == null)
                Debug.LogError($"[RiderActivatedMotor] {name} has no PlatformRiderSensor assigned or in children. Platform will never advance.", this);

            platform.CanAdvance = () => sensor != null && sensor.RiderCount > 0;
        }
    }
}
