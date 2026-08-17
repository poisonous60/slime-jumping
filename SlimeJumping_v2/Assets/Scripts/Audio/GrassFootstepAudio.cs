using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Audio
{
    /// Rustle when the slime lands on the ground a grass decoration covers. The patch runs the test
    /// itself because the ground beneath it is one long collider — a SurfaceTag there would retag the
    /// whole floor, not the grassy stretch. Layers over the surface's own landing sound instead of
    /// replacing it, so the thud keeps playing underneath. Put one on each Grass group object; the
    /// covered span is read from its child renderers, so nothing needs measuring by hand.
    [DisallowMultipleComponent]
    [AddComponentMenu("SlimeJumping/Audio/Grass Footstep Audio")]
    public class GrassFootstepAudio : MonoBehaviour
    {
        [Tooltip("비우면 Assets/Resources/GrassFootstepConfig.asset 사용.")]
        [SerializeField] GrassFootstepConfig configOverride;

        GrassFootstepConfig config;
        SlimeController slime;
        Bounds bounds;

        void OnEnable()
        {
            config = GrassFootstepConfig.Resolve(configOverride);
            bounds = MeasureBounds();
            if (slime == null) slime = FindFirstObjectByType<SlimeController>();
            if (slime != null) slime.OnLanded += HandleLanded;
        }

        void OnDisable()
        {
            if (slime != null) slime.OnLanded -= HandleLanded;
        }

        // Inactive LOD levels included, so the span does not depend on which LOD is showing.
        Bounds MeasureBounds()
        {
            var b = new Bounds(transform.position, Vector3.zero);
            var renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        void HandleLanded(Vector2 position, Vector2 surfaceNormal)
        {
            if (Mathf.Abs(position.x - bounds.center.x) > bounds.extents.x + config.padX) return;
            if (Mathf.Abs(position.y - bounds.center.y) > bounds.extents.y + config.padY) return;
            SoundService.Play(config.landSound, position, config.volumeScale);
        }

        void OnDrawGizmosSelected()
        {
            var cfg = GrassFootstepConfig.Resolve(configOverride);
            var b = MeasureBounds();
            Gizmos.color = new Color(0.4f, 0.9f, 0.4f, 0.7f);
            Gizmos.DrawWireCube(
                new Vector3(b.center.x, b.center.y, transform.position.z),
                new Vector3(b.size.x + cfg.padX * 2f, b.size.y + cfg.padY * 2f, 0.1f));
        }
    }
}
