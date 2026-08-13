using UnityEngine;

namespace SlimeJumping.VFX
{
    /// <summary>
    /// World rectangle where it rains, centred on this object and edited the same way as RectAmbienceZone:
    /// drag to place, size and per-edge falloff in the inspector, both rectangles drawn as gizmos.
    /// RainCameraEmitter samples Keep over its spawn box, so emission, splashes and ground wetness all
    /// follow this one rect.
    /// </summary>
    [AddComponentMenu("SlimeJumping/VFX/Rain Zone")]
    public sealed class RainZone : MonoBehaviour
    {
        [Tooltip("Full rain region, centred on this object.")]
        [SerializeField] private Vector2 size = new Vector2(88f, 74f);

        [Tooltip("World units the rain takes to die out past the left and right edges.")]
        [Min(0.01f)][SerializeField] private float falloffSides = 20f;

        [Tooltip("World units the rain takes to die out above the top edge.")]
        [Min(0.01f)][SerializeField] private float falloffUp = 20f;

        [Tooltip("World units the rain takes to die out below the bottom edge.")]
        [Min(0.01f)][SerializeField] private float falloffDown = 26f;

        // Each axis normalises by its own falloff before combining, else the longest ramp drags the corners.
        public float Keep(Vector2 p)
        {
            Vector3 c = transform.position;
            float outX = Mathf.Max(Mathf.Abs(p.x - c.x) - size.x * 0.5f, 0f) / falloffSides;
            float dy = p.y - c.y;
            float outY = Mathf.Max(Mathf.Abs(dy) - size.y * 0.5f, 0f) / (dy >= 0f ? falloffUp : falloffDown);
            return 1f - Mathf.Clamp01(new Vector2(outX, outY).magnitude);
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 c = transform.position;
            float hx = size.x * 0.5f, hy = size.y * 0.5f;

            Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.9f);
            DrawRect(c.x - hx, c.y - hy, c.x + hx, c.y + hy);

            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.7f);
            DrawRect(c.x - hx - falloffSides, c.y - hy - falloffDown, c.x + hx + falloffSides, c.y + hy + falloffUp);
        }

        private static void DrawRect(float minX, float minY, float maxX, float maxY)
        {
            Vector3 p0 = new Vector3(minX, minY, 0f);
            Vector3 p1 = new Vector3(maxX, minY, 0f);
            Vector3 p2 = new Vector3(maxX, maxY, 0f);
            Vector3 p3 = new Vector3(minX, maxY, 0f);
            Gizmos.DrawLine(p0, p1);
            Gizmos.DrawLine(p1, p2);
            Gizmos.DrawLine(p2, p3);
            Gizmos.DrawLine(p3, p0);
        }
    }
}
