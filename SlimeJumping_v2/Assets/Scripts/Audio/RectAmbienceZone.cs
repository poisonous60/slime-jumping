using UnityEngine;
using UnityCamera = UnityEngine.Camera;

namespace SlimeJumping.Audio
{
    /// <summary>
    /// Looping ambience audible inside a rectangle instead of a sphere. A 3D AudioSource can only fall
    /// off radially, which cannot express a region that is wide and short, so the loop plays 2D and this
    /// drives its level from the listener's XY position: full inside the rect, ramping to silence across
    /// falloff. Both rectangles are drawn as gizmos, so the audible region is what you see in the scene.
    /// </summary>
    [AddComponentMenu("SlimeJumping/Audio/Rect Ambience Zone")]
    public sealed class RectAmbienceZone : MonoBehaviour
    {
        [SerializeField] private SoundDef sound;

        [Tooltip("Full volume region, centred on this object.")]
        [SerializeField] private Vector2 size = new Vector2(96f, 112f);

        [Tooltip("World units the volume takes to reach silence past the left and right edges.")]
        [Min(0.01f)][SerializeField] private float falloffSides = 20f;

        [Tooltip("World units the volume takes to reach silence above the top edge.")]
        [Min(0.01f)][SerializeField] private float falloffUp = 20f;

        [Tooltip("World units the volume takes to reach silence below the bottom edge.")]
        [Min(0.01f)][SerializeField] private float falloffDown = 20f;

        [Tooltip("Listener to measure against. Falls back to Camera.main.")]
        [SerializeField] private UnityCamera listener;

        private SoundHandle handle;
        private float volumeRoll;

        // Keep the roll, not the volume: re-rolling per frame would make a randomised range flicker.
        private void OnEnable()
        {
            if (sound == null) return;
            volumeRoll = Random.value;
            handle = SoundService.PlayLooping(sound, transform);
            handle.SetVolume(0f);
        }

        private void OnDisable()
        {
            SoundService.StopLoop(handle);
            handle = default;
        }

        private void Update()
        {
            if (!handle.IsValid) return;
            if (listener == null) listener = UnityCamera.main;
            if (listener == null || sound == null) return;
            float defVolume = Mathf.Lerp(sound.volumeRange.x, sound.volumeRange.y, volumeRoll);
            handle.SetVolume(defVolume * Attenuation(listener.transform.position));
        }

        // Each axis normalises by its own falloff before combining, else the longest ramp drags the corners.
        private float Attenuation(Vector3 listenerPos)
        {
            Vector3 c = transform.position;
            float outX = Mathf.Max(Mathf.Abs(listenerPos.x - c.x) - size.x * 0.5f, 0f) / falloffSides;
            float dy = listenerPos.y - c.y;
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
