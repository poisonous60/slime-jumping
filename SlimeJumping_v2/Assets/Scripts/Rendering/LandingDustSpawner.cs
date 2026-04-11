using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Rendering
{
    /// <summary>
    /// Spawns landing dust VFX when slime lands on a surface.
    /// The dust prefab should emit along local up — this spawner handles
    /// world rotation via Quaternion.FromToRotation(Vector3.up, surfaceNormal).
    /// </summary>
    public class LandingDustSpawner : MonoBehaviour
    {
        [SerializeField] private SlimeController slimeController;
        [SerializeField] private GameObject dustPrefab;
        [SerializeField] private float cooldown = 0.15f;

        private float cooldownTimer;

        private void OnEnable()
        {
            if (slimeController != null)
                slimeController.OnLanded += HandleLanded;
        }

        private void OnDisable()
        {
            if (slimeController != null)
                slimeController.OnLanded -= HandleLanded;
        }

        private void Update()
        {
            if (cooldownTimer > 0f)
                cooldownTimer -= Time.deltaTime;
        }

        private void HandleLanded(Vector2 position, Vector2 surfaceNormal)
        {
            if (dustPrefab == null) return;
            if (cooldownTimer > 0f) return;

            cooldownTimer = cooldown;
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, (Vector3)surfaceNormal);
            var go = Instantiate(dustPrefab, (Vector3)position, rotation);
            // playOnAwake must be OFF on the prefab. Calling Play() explicitly after
            // Instantiate avoids the one-frame flash where burst fires in Awake()
            // before the render frame reconciles the position (FixedUpdate timing issue).
            var ps = go.GetComponent<ParticleSystem>();
            if (ps != null) ps.Play();
        }
    }
}
