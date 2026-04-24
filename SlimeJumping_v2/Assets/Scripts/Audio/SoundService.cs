using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace SlimeJumping.Audio
{
    /// Scene-persistent one-shot, looping and BGM audio playback backed by per-category
    /// AudioSource pools. Routes all output through the SoundSettings mixer so volume can
    /// be controlled per bus. Survives scene loads via DontDestroyOnLoad — scenes that
    /// also carry a prefab instance are deduped by the singleton guard in Awake.
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("SlimeJumping/Audio/Sound Service")]
    public class SoundService : MonoBehaviour
    {
        public static SoundService Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        [SerializeField] private SoundSettings settings;

        private class PooledSource
        {
            public AudioSource source;
            public bool locked;          // true while owned by a loop SoundHandle
            public double releaseDspTime; // when a one-shot is expected to end; older = steal candidate
        }

        private readonly Dictionary<SoundCategory, List<PooledSource>> pools =
            new Dictionary<SoundCategory, List<PooledSource>>();

        // Two dedicated BGM sources for crossfade. Both route to settings.bgmGroup, loop=true,
        // spatialBlend=0. Only one plays at a time except during a crossfade.
        private AudioSource bgmA;
        private AudioSource bgmB;
        private AudioSource bgmActive;
        private Coroutine bgmFadeCoroutine;

        public static AudioClip CurrentBgmClip
        {
            get { var i = Instance; return i != null && i.bgmActive != null ? i.bgmActive.clip : null; }
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (settings == null)
            {
                Debug.LogError("[SoundService] SoundSettings is not assigned.");
                return;
            }

            BuildPools();
            BuildBgmSources();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void BuildPools()
        {
            foreach (SoundCategory cat in System.Enum.GetValues(typeof(SoundCategory)))
            {
                var list = new List<PooledSource>(settings.poolSizePerCategory);
                var group = ResolveGroup(cat);
                for (int i = 0; i < settings.poolSizePerCategory; i++)
                {
                    var go = new GameObject($"Pool_{cat}_{i}");
                    go.transform.SetParent(transform, false);
                    var src = go.AddComponent<AudioSource>();
                    src.playOnAwake = false;
                    src.outputAudioMixerGroup = group;
                    src.rolloffMode = settings.default3DRolloff;
                    src.minDistance = settings.default3DMinDistance;
                    src.maxDistance = settings.default3DMaxDistance;
                    list.Add(new PooledSource { source = src });
                }
                pools[cat] = list;
            }
        }

        private void BuildBgmSources()
        {
            bgmA = CreateBgmSource("BGM_A");
            bgmB = CreateBgmSource("BGM_B");
            bgmActive = bgmA;
        }

        private AudioSource CreateBgmSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.outputAudioMixerGroup = settings.bgmGroup;
            src.spatialBlend = 0f;
            src.loop = true;
            src.playOnAwake = false;
            src.volume = 0f;
            return src;
        }

        private AudioMixerGroup ResolveGroup(SoundCategory cat)
        {
            switch (cat)
            {
                case SoundCategory.BGM: return settings.bgmGroup;
                case SoundCategory.SFX_Slime: return settings.sfxSlimeGroup;
                case SoundCategory.SFX_Environment: return settings.sfxEnvironmentGroup;
                case SoundCategory.SFX_Object: return settings.sfxObjectGroup;
                case SoundCategory.UI: return settings.uiGroup;
            }
            return null;
        }

        // Returns an unlocked source, preferring one whose one-shot has already finished.
        private PooledSource Acquire(SoundCategory cat)
        {
            if (!pools.TryGetValue(cat, out var list)) return null;

            PooledSource oldest = null;
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (p.locked) continue;
                if (!p.source.isPlaying) return p;
                if (oldest == null || p.releaseDspTime < oldest.releaseDspTime) oldest = p;
            }
            return oldest; // soft-steal
        }

        // ---- Public API ----

        public static void PlayAt(AudioClip clip, Vector3 worldPos,
                                  SoundCategory category = SoundCategory.SFX_Object,
                                  float volume = 1f, float pitch = 1f)
        {
            if (clip == null) return;
            var svc = Instance;
            if (svc == null) { Debug.LogWarning("[SoundService] No instance; sound dropped."); return; }

            var p = svc.Acquire(category);
            if (p == null) return;

            var src = p.source;
            src.transform.position = worldPos;
            src.spatialBlend = 1f;
            src.loop = false;
            src.volume = volume;
            src.pitch = pitch;
            src.outputAudioMixerGroup = svc.ResolveGroup(category);
            src.PlayOneShot(clip, 1f); // volume already applied on src
            p.locked = false;
            p.releaseDspTime = AudioSettings.dspTime + (clip.length / Mathf.Max(0.01f, pitch));
        }

        public static void Play2D(AudioClip clip,
                                  SoundCategory category = SoundCategory.UI,
                                  float volume = 1f, float pitch = 1f)
        {
            if (clip == null) return;
            var svc = Instance;
            if (svc == null) { Debug.LogWarning("[SoundService] No instance; sound dropped."); return; }

            var p = svc.Acquire(category);
            if (p == null) return;

            var src = p.source;
            src.spatialBlend = 0f;
            src.loop = false;
            src.volume = volume;
            src.pitch = pitch;
            src.outputAudioMixerGroup = svc.ResolveGroup(category);
            src.PlayOneShot(clip, 1f);
            p.locked = false;
            p.releaseDspTime = AudioSettings.dspTime + (clip.length / Mathf.Max(0.01f, pitch));
        }

        public static SoundHandle PlayLooping(AudioClip clip, Transform follow,
                                              SoundCategory category = SoundCategory.SFX_Environment,
                                              float volume = 1f, bool spatial3D = true)
        {
            if (clip == null) return default;
            var svc = Instance;
            if (svc == null) return default;

            var p = svc.Acquire(category);
            if (p == null) return default;

            var src = p.source;
            if (follow != null) src.transform.SetParent(follow, false);
            src.spatialBlend = spatial3D ? 1f : 0f;
            src.loop = true;
            src.volume = volume;
            src.pitch = 1f;
            src.clip = clip;
            src.outputAudioMixerGroup = svc.ResolveGroup(category);
            src.Play();
            p.locked = true;

            return new SoundHandle { source = src };
        }

        public static void StopLoop(SoundHandle handle)
        {
            if (!handle.IsValid) return;
            handle.source.Stop();
            handle.source.clip = null;
            handle.source.loop = false;
            if (Instance != null)
                handle.source.transform.SetParent(Instance.transform, false);
            var list = Instance != null ? Instance.FindPoolContaining(handle.source) : null;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                    if (list[i].source == handle.source) list[i].locked = false;
            }
        }

        private List<PooledSource> FindPoolContaining(AudioSource s)
        {
            foreach (var kv in pools)
                for (int i = 0; i < kv.Value.Count; i++)
                    if (kv.Value[i].source == s) return kv.Value;
            return null;
        }

        // ---- BGM ----

        /// Start or crossfade to the given BGM clip. Safe to call every frame — if the clip
        /// matches the currently-playing BGM, the call is a no-op. If a fade is in progress,
        /// it is cancelled and replaced. Final source volume is <paramref name="targetVolume"/>;
        /// perceptual level is also scaled by the BGM bus (BGMVolume slider).
        public static void PlayBgm(AudioClip clip, float crossfadeSeconds = 1.5f, float targetVolume = 1f)
        {
            if (clip == null) return;
            var svc = Instance;
            if (svc == null) { Debug.LogWarning("[SoundService] No instance; BGM request dropped."); return; }
            if (svc.bgmActive != null && svc.bgmActive.clip == clip && svc.bgmActive.isPlaying) return;

            var outgoing = svc.bgmActive;
            var incoming = (outgoing == svc.bgmA) ? svc.bgmB : svc.bgmA;

            if (svc.bgmFadeCoroutine != null) svc.StopCoroutine(svc.bgmFadeCoroutine);

            incoming.clip = clip;
            incoming.volume = 0f;
            incoming.Play();

            svc.bgmActive = incoming;
            svc.bgmFadeCoroutine = svc.StartCoroutine(
                svc.BgmFade(outgoing, incoming, targetVolume, Mathf.Max(0f, crossfadeSeconds)));
        }

        /// Fade the currently-playing BGM out and stop. No-op if nothing is playing.
        public static void StopBgm(float fadeSeconds = 1f)
        {
            var svc = Instance;
            if (svc == null) return;
            var outgoing = svc.bgmActive;
            if (outgoing == null || !outgoing.isPlaying) return;

            if (svc.bgmFadeCoroutine != null) svc.StopCoroutine(svc.bgmFadeCoroutine);
            svc.bgmFadeCoroutine = svc.StartCoroutine(
                svc.BgmFade(outgoing, null, 0f, Mathf.Max(0f, fadeSeconds)));
        }

        private IEnumerator BgmFade(AudioSource outgoing, AudioSource incoming,
                                    float incomingTargetVolume, float duration)
        {
            float outStart = outgoing != null ? outgoing.volume : 0f;
            if (duration <= 0f)
            {
                if (outgoing != null) { outgoing.volume = 0f; outgoing.Stop(); outgoing.clip = null; }
                if (incoming != null) incoming.volume = incomingTargetVolume;
                bgmFadeCoroutine = null;
                yield break;
            }

            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / duration);
                if (outgoing != null) outgoing.volume = Mathf.Lerp(outStart, 0f, u);
                if (incoming != null) incoming.volume = Mathf.Lerp(0f, incomingTargetVolume, u);
                yield return null;
            }

            if (outgoing != null)
            {
                outgoing.volume = 0f;
                outgoing.Stop();
                outgoing.clip = null;
            }
            if (incoming != null) incoming.volume = incomingTargetVolume;
            bgmFadeCoroutine = null;
        }

        public static void SetCategoryVolumeDb(SoundCategory category, float db)
        {
            var svc = Instance;
            if (svc == null || svc.settings == null || svc.settings.mixer == null) return;

            string param = null;
            switch (category)
            {
                case SoundCategory.BGM:             param = svc.settings.bgmVolumeParam; break;
                case SoundCategory.UI:              param = svc.settings.uiVolumeParam; break;
                case SoundCategory.SFX_Slime:       param = svc.settings.sfxSlimeVolumeParam; break;
                case SoundCategory.SFX_Environment: param = svc.settings.sfxEnvironmentVolumeParam; break;
                case SoundCategory.SFX_Object:      param = svc.settings.sfxObjectVolumeParam; break;
            }
            if (!string.IsNullOrEmpty(param))
                svc.settings.mixer.SetFloat(param, db);
        }

        public static void SetMasterVolumeDb(float db)
        {
            var svc = Instance;
            if (svc == null || svc.settings == null || svc.settings.mixer == null) return;
            if (!string.IsNullOrEmpty(svc.settings.masterVolumeParam))
                svc.settings.mixer.SetFloat(svc.settings.masterVolumeParam, db);
        }
    }
}
