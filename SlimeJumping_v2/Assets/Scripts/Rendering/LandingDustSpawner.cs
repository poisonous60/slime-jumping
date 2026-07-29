using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Rendering
{
    /// <summary>Spawns landing dust; the prefab must emit along local up, this spawner supplies the surface-normal rotation.</summary>
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
            // playOnAwake must be OFF on the prefab: an Awake() burst fires before the render
            // frame reconciles position, giving a one-frame flash at the wrong spot.
            var ps = go.GetComponent<ParticleSystem>();
            if (ps != null) ps.Play();
        }
    }
}
