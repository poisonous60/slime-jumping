using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// World-space dotted line that visualizes pipe traversal. At Awake, spawns N
    /// SpriteRenderer dots along the path at uniform arc-length spacing. Idle, all
    /// dots sit at idleColor (very dim or black). During traversal,
    /// SetProgress(0..1) brightens a sliding window of dots near the slime's
    /// current position so the player sees the slime moving inside the wall.
    /// </summary>
    [RequireComponent(typeof(PipePath))]
    public class PipeDotTrail : MonoBehaviour
    {
        [Tooltip("World-space spacing between dots. Lower = denser line.")]
        [SerializeField] private float dotSpacing = 0.4f;

        [Tooltip("Sprite for each dot. Falls back to Unity's built-in Knob.psd if null.")]
        [SerializeField] private Sprite dotSprite;

        [Tooltip("World-space scale per dot.")]
        [SerializeField] private float dotScale = 0.18f;

        [Tooltip("Tint for the active (brightest) dot. Use HDR-intensity color for Bloom glow.")]
        [ColorUsage(true, true)]
        [SerializeField] private Color activeColor = new Color(1.5f, 2.0f, 3.0f, 1f);

        [Tooltip("Tint for idle dots (when no traversal is active). Should be visible but NOT HDR — pre-traversal preview shouldn't compete with the active glow.")]
        [SerializeField] private Color idleColor = new Color(0.55f, 0.58f, 0.62f, 0.9f);

        [Tooltip("Window radius in arc-length units. Dots within this distance of the slime brighten.")]
        [SerializeField] private float windowRadius = 1.2f;

        [Tooltip("Sorting layer for the dots. Should be above Background_VFX so the line reads inside walls.")]
        [SerializeField] private string sortingLayer = "Default";

        [Tooltip("Sorting order for the dots.")]
        [SerializeField] private int sortingOrder = 100;

        private PipePath path;
        private SpriteRenderer[] dots;
        private float[] dotDistances;
        private float pathLength;

        private void Awake()
        {
            path = GetComponent<PipePath>();
            BuildDots();
            ResetIdle();
        }

        private void BuildDots()
        {
            // Tear down any prior dots (in case the trail is re-built at edit time).
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var ch = transform.GetChild(i);
                if (ch.name.StartsWith("Dot_"))
                {
                    if (Application.isPlaying) Destroy(ch.gameObject);
                    else DestroyImmediate(ch.gameObject);
                }
            }

            pathLength = path.TotalLength;
            if (pathLength < 0.001f || dotSpacing < 0.01f)
            {
                dots = new SpriteRenderer[0];
                dotDistances = new float[0];
                return;
            }

            int count = Mathf.Max(2, Mathf.CeilToInt(pathLength / dotSpacing) + 1);
            dots = new SpriteRenderer[count];
            dotDistances = new float[count];

            Sprite sprite = dotSprite != null ? dotSprite : GetOrCreateFallbackSprite();

            for (int i = 0; i < count; i++)
            {
                float d = pathLength * (i / (float)(count - 1));
                Vector2 pos = path.SampleAtDistance(d, out _);

                var go = new GameObject("Dot_" + i);
                go.transform.SetParent(transform, worldPositionStays: false);
                go.transform.position = new Vector3(pos.x, pos.y, 0f);
                go.transform.localScale = new Vector3(dotScale, dotScale, 1f);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.color = idleColor;
                sr.sortingLayerName = sortingLayer;
                sr.sortingOrder = sortingOrder;

                dots[i] = sr;
                dotDistances[i] = d;
            }
        }

        public void ResetIdle()
        {
            if (dots == null) return;
            for (int i = 0; i < dots.Length; i++)
                if (dots[i] != null) dots[i].color = idleColor;
        }

        /// <summary>
        /// Brighten dots near the slime's current arc-length position. Progress is
        /// 0..1, mapped onto the path. Falloff is linear in arc-length within
        /// windowRadius.
        /// </summary>
        public void SetProgress(float progress01)
        {
            if (dots == null || dots.Length == 0 || pathLength < 0.001f) return;
            float center = Mathf.Clamp01(progress01) * pathLength;
            float r = Mathf.Max(0.001f, windowRadius);

            for (int i = 0; i < dots.Length; i++)
            {
                if (dots[i] == null) continue;
                float dist = Mathf.Abs(dotDistances[i] - center);
                float t = Mathf.Clamp01(1f - dist / r);
                dots[i].color = Color.Lerp(idleColor, activeColor, t);
            }
        }

        /// <summary>
        /// Editor-time helper for the test setup util to (re)build the dot chain
        /// after the path has been wired.
        /// </summary>
        public void RebuildEditor()
        {
            if (path == null) path = GetComponent<PipePath>();
            BuildDots();
            ResetIdle();
        }

        private static Sprite fallbackSprite;
        private static Sprite GetOrCreateFallbackSprite()
        {
            if (fallbackSprite != null) return fallbackSprite;
            // 32×32 soft circle so dots show as round glow blobs at any scale.
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            float c = (size - 1) * 0.5f;
            float r = c;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / r;
                float a = Mathf.Clamp01(1f - d);
                a *= a; // sharper falloff
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
            tex.SetPixels(pixels);
            tex.Apply(false, true);
            fallbackSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            fallbackSprite.name = "PipeDot_Fallback";
            return fallbackSprite;
        }
    }
}
