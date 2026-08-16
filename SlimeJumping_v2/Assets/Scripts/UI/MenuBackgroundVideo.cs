using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace SlimeJumping.UI
{
    /// Main-menu backdrop. Picks a random clip from the shared config, loops it into a
    /// runtime RenderTexture shown on the RawImage; the image stays hidden until the
    /// first frame is decoded. All tuning lives on MenuBackgroundVideoConfig.
    [RequireComponent(typeof(VideoPlayer))]
    public class MenuBackgroundVideo : MonoBehaviour
    {
        [SerializeField] private RawImage target;
        [SerializeField] private RawImage vignette;

        [Tooltip("Optional override of the shared config. Leave null to auto-load " +
                 "Assets/Resources/MenuBackgroundVideoConfig.asset.")]
        [SerializeField] private MenuBackgroundVideoConfig configOverride;

        private VideoPlayer player;
        private RenderTexture texture;
        private static Texture2D vignetteTexture;

        private void Awake()
        {
            if (target != null) target.enabled = false;

            var config = MenuBackgroundVideoConfig.Resolve(configOverride);
            if (config == null || config.clips == null || config.clips.Length == 0 || target == null)
            {
                enabled = false;
                return;
            }

            var clip = config.clips[Random.Range(0, config.clips.Length)];
            if (clip == null)
            {
                enabled = false;
                return;
            }
            texture = new RenderTexture((int)clip.width, (int)clip.height, 0);

            player = GetComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.source = VideoSource.VideoClip;
            player.clip = clip;
            player.isLooping = true;
            player.skipOnDrop = false;
            player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            player.audioOutputMode = VideoAudioOutputMode.None;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = texture;
            player.playbackSpeed = config.playbackSpeed;

            target.texture = texture;
            target.color = config.tint;
            ApplyVignette(config);
            player.prepareCompleted += HandlePrepared;
            player.Prepare();
        }

        private void ApplyVignette(MenuBackgroundVideoConfig config)
        {
            if (vignette == null) return;
            if (config.vignetteStrength <= 0f)
            {
                vignette.enabled = false;
                return;
            }
            vignette.texture = GetVignetteTexture();
            var color = config.vignetteColor;
            color.a = config.vignetteStrength;
            vignette.color = color;
        }

        /// Radial alpha ramp: transparent center, opaque toward the corners.
        private static Texture2D GetVignetteTexture()
        {
            if (vignetteTexture != null) return vignetteTexture;
            const int size = 256;
            vignetteTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size - 0.5f;
                    float dy = (y + 0.5f) / size - 0.5f;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy) / 0.7071f;
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 1f, dist));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            vignetteTexture.SetPixels32(pixels);
            vignetteTexture.Apply(false, true);
            return vignetteTexture;
        }

        private void HandlePrepared(VideoPlayer source)
        {
            target.enabled = true;
            source.Play();
        }

        private void OnDestroy()
        {
            if (player != null)
            {
                player.prepareCompleted -= HandlePrepared;
                player.targetTexture = null;
            }
            if (texture != null)
            {
                texture.Release();
                Destroy(texture);
            }
        }
    }
}
