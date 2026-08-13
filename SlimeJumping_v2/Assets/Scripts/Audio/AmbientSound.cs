using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SlimeJumping.Audio
{
    /// Looping ambience (waterfall, wind, machinery) played through SoundService instead of an
    /// AudioSource dropped on the object: the mixer group, level and falloff all come from the
    /// SoundDef, so nothing about the sound is wired by hand on the scene object. Selecting the
    /// object draws its audible sphere, the same way RectAmbienceZone shows its rectangle.
    [AddComponentMenu("SlimeJumping/Audio/Ambient Sound")]
    public class AmbientSound : MonoBehaviour
    {
        [SerializeField] private SoundDef sound;

        private SoundHandle handle;

        void OnEnable()
        {
            handle = SoundService.PlayLooping(sound, transform);
        }

        void OnDisable()
        {
            SoundService.StopLoop(handle);
            handle = default;
        }

#if UNITY_EDITOR
        private static SoundSettings cachedSettings;

        // Colours match RectAmbienceZone: green = full volume, orange = silence.
        private void OnDrawGizmosSelected()
        {
            if (sound == null || !sound.spatial3D) return; // 2D loop has no region to draw.

            float min = sound.minDistance, max = sound.maxDistance;
            if (min < 0f || max < 0f)
            {
                var settings = ResolveSettings();
                if (settings == null) return;
                if (min < 0f) min = settings.default3DMinDistance;
                if (max < 0f) max = settings.default3DMaxDistance;
            }

            Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, min);
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, max);
        }

        // A negative distance on the SoundDef means "keep the pool default", which only SoundSettings knows.
        private static SoundSettings ResolveSettings()
        {
            if (cachedSettings != null) return cachedSettings;
            var guids = AssetDatabase.FindAssets("t:" + nameof(SoundSettings));
            if (guids.Length == 0) return null;
            cachedSettings = AssetDatabase.LoadAssetAtPath<SoundSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
            return cachedSettings;
        }
#endif
    }
}
