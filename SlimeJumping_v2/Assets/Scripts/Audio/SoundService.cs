using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace SlimeJumping.Audio
{
    /// One-shot / looping / BGM playback over per-category AudioSource pools, routed through the
    /// SoundSettings mixer. DontDestroyOnLoad; duplicate scene instances die in the Awake guard.
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("SlimeJumping/Audio/Sound Service")]
    public class SoundService : MonoBehaviour
    {
        public static SoundService Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            // Survives a no-domain-reload Play entry otherwise, gating against the previous run's clock.
            cooldownUntil.Clear();
        }

        [SerializeField] private SoundSettings settings;

        private class PooledSource
        {
            public AudioSource source;
            public bool locked;          // true while owned by a loop SoundHandle
            public double releaseDspTime; // when a one-shot is expected to end; older = steal candidate
            public Transform follow;      // loop target; tracked by position, never by parenting
            public bool tracksFollow;     // true when the loop was given a target, so losing it ends the loop
        }

        private readonly Dictionary<SoundCategory, List<PooledSource>> pools =
            new Dictionary<SoundCategory, List<PooledSource>>();

        // Loops mid-fade. A second StopLoop on the same source must not start a second ramp.
        private readonly HashSet<AudioSource> fadingLoops = new HashSet<AudioSource>();

        // Two BGM sources so a crossfade can overlap; only one plays otherwise.
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
            // DontDestroyOnLoad is root-only; the scene keeps this under a manager object.
            if (transform.parent != null) transform.SetParent(null, true);
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

        // Parenting would pull the pooled source into the target's scene; a lost target ends the loop.
        void LateUpdate()
        {
            foreach (var kv in pools)
            {
                var list = kv.Value;
                for (int i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    if (!p.locked || !p.tracksFollow || p.source == null) continue;

                    if (p.follow == null)
                    {
                        p.source.Stop();
                        p.source.clip = null;
                        p.source.loop = false;
                        RestorePoolDefaults(p.source);
                        p.locked = false;
                        p.tracksFollow = false;
                        continue;
                    }
                    p.source.transform.position = p.follow.position;
                }
            }
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
                    src.dopplerLevel = 0f;
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

        /// Mixer group for a category, for the few AudioSources that cannot go through the pool
        /// (media playback, the piano voice pool). Nobody drags a group into an inspector.
        /// Null when no service is up — callers must keep their current group rather than clear it.
        public static AudioMixerGroup GetGroup(SoundCategory cat)
        {
            var svc = Instance;
            if (svc == null || svc.settings == null) return null;
            return svc.ResolveGroup(cat);
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

        // ---- Public API (SoundDef) ----

        /// volumeScale/pitchScale multiply the def's own values rather than replacing them, so a call
        /// site can make one impact louder without owning the sound's level.
        public static void Play(SoundDef def, Vector3 worldPos, float volumeScale = 1f, float pitchScale = 1f)
        {
            if (def == null || !ConsumeCooldown(def)) return;
            PlayAt(def.PickClip(), worldPos, def.category,
                   def.PickVolume() * volumeScale, def.PickPitch() * pitchScale);
        }

        public static void Play2D(SoundDef def, float volumeScale = 1f, float pitchScale = 1f)
        {
            if (def == null || !ConsumeCooldown(def)) return;
            Play2D(def.PickClip(), def.category,
                   def.PickVolume() * volumeScale, def.PickPitch() * pitchScale);
        }

        // Not stored on the asset: a serialized timestamp outlives Play mode, whose clock restarts at 0.
        private static readonly Dictionary<SoundDef, float> cooldownUntil = new Dictionary<SoundDef, float>();

        private static bool ConsumeCooldown(SoundDef def)
        {
            if (def.cooldownSeconds <= 0f) return true;
            if (cooldownUntil.TryGetValue(def, out float until) && Time.time < until) return false;
            cooldownUntil[def] = Time.time + def.cooldownSeconds;
            return true;
        }

        public static SoundHandle PlayLooping(SoundDef def, Transform follow, float volumeScale = 1f)
        {
            if (def == null) return default;
            return PlayLooping(def.PickClip(), follow, def.category, def.PickVolume() * volumeScale,
                               def.spatial3D, def.minDistance, def.maxDistance);
        }

        // ---- Public API (raw clip; being migrated to SoundDef) ----

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

        /// minDistance/maxDistance below 0 keep the pool defaults. An ambient loop with its own
        /// falloff (a waterfall heard from far away) needs them; a point sound does not.
        public static SoundHandle PlayLooping(AudioClip clip, Transform follow,
                                              SoundCategory category = SoundCategory.SFX_Environment,
                                              float volume = 1f, bool spatial3D = true,
                                              float minDistance = -1f, float maxDistance = -1f)
        {
            if (clip == null) return default;
            var svc = Instance;
            if (svc == null) return default;

            var p = svc.Acquire(category);
            if (p == null) return default;

            var src = p.source;
            p.follow = follow;
            p.tracksFollow = follow != null;
            src.transform.position = follow != null ? follow.position : Vector3.zero;
            src.spatialBlend = spatial3D ? 1f : 0f;
            src.loop = true;
            src.volume = volume;
            src.pitch = 1f;
            src.clip = clip;
            if (minDistance >= 0f) src.minDistance = minDistance;
            if (maxDistance >= 0f) src.maxDistance = maxDistance;
            src.outputAudioMixerGroup = svc.ResolveGroup(category);
            src.Play();
            p.locked = true;

            return new SoundHandle { source = src };
        }

        /// fadeSeconds > 0 ramps the level down before releasing. The pool entry stays locked for
        /// the whole ramp, so nothing borrows a source that is still audible.
        public static void StopLoop(SoundHandle handle, float fadeSeconds = 0f)
        {
            if (!handle.IsValid) return;
            var svc = Instance;
            if (fadeSeconds > 0f && svc != null && svc.isActiveAndEnabled
                && svc.fadingLoops.Add(handle.source))
            {
                svc.StartCoroutine(svc.FadeOutLoop(handle.source, fadeSeconds));
                return;
            }
            ReleaseLoop(handle.source);
        }

        private IEnumerator FadeOutLoop(AudioSource src, float seconds)
        {
            float from = src.volume;
            for (float t = 0f; t < seconds && src != null; t += Time.unscaledDeltaTime)
            {
                src.volume = Mathf.Lerp(from, 0f, t / seconds);
                yield return null;
            }
            fadingLoops.Remove(src);
            ReleaseLoop(src);
        }

        private static void ReleaseLoop(AudioSource src)
        {
            if (src == null) return;
            src.Stop();
            src.clip = null;
            src.loop = false;
            if (Instance != null) Instance.RestorePoolDefaults(src);

            var list = Instance != null ? Instance.FindPoolContaining(src) : null;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                    if (list[i].source == src)
                    {
                        list[i].locked = false;
                        list[i].follow = null;
                        list[i].tracksFollow = false;
                    }
            }
        }

        // A loop can leave its own falloff on a pooled source; the next borrower must not inherit it.
        private void RestorePoolDefaults(AudioSource src)
        {
            src.transform.localPosition = Vector3.zero;
            if (settings == null) return;
            src.minDistance = settings.default3DMinDistance;
            src.maxDistance = settings.default3DMaxDistance;
            src.rolloffMode = settings.default3DRolloff;
            src.spatialBlend = 0f;
        }

        private List<PooledSource> FindPoolContaining(AudioSource s)
        {
            foreach (var kv in pools)
                for (int i = 0; i < kv.Value.Count; i++)
                    if (kv.Value[i].source == s) return kv.Value;
            return null;
        }

        // ---- BGM ----

        /// Start or crossfade to a BGM clip. Safe every frame — same clip is a no-op, an in-flight
        /// fade is cancelled and replaced.
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
