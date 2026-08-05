using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms.Afterimage
{
    /// <summary>One snapshot of the slime inside an AfterimageZone: ghost (no collider) →
    /// hardened platform → fade-out at end of lifetime. Launching off it fires the zone's
    /// shockwave. Visual is a frozen clone of the slime's mesh subtree; collider is an
    /// independent circle approximation.</summary>
    public class AfterimageStamp : MonoBehaviour
    {
        // Shockwave erasure must reach ghost stamps too (collider still disabled),
        // so a registry is used instead of a physics overlap.
        public static readonly List<AfterimageStamp> Active = new List<AfterimageStamp>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Active.Clear();

        static readonly int GhostAlphaId = Shader.PropertyToID("_GhostAlpha");
        static readonly int GhostDesatId = Shader.PropertyToID("_GhostDesat");
        static readonly int GhostTintId = Shader.PropertyToID("_GhostTint");

        AfterimageZone owner;
        SlimeController slime;
        AfterimageConfig cfg;
        CircleCollider2D col;
        MeshRenderer ghostRenderer;
        SpriteRenderer[] faceRenderers = System.Array.Empty<SpriteRenderer>();
        Color[] faceBaseColors = System.Array.Empty<Color>();
        MaterialPropertyBlock mpb;
        float age;
        float hardenAge = -1f;
        bool visible;
        bool expired;

        public static AfterimageStamp Spawn(AfterimageZone owner, SlimeController slime, AfterimageConfig cfg)
        {
            var visual = slime.GetComponentInChildren<SlimeVisual>(true);
            Transform body3d = visual != null ? visual.Body3d : null;
            MeshRenderer srcRenderer = body3d != null ? body3d.GetComponentInChildren<MeshRenderer>() : null;
            if (srcRenderer == null) return null;

            var root = new GameObject("AfterimageStamp");
            root.transform.SetParent(owner.transform, worldPositionStays: true);
            root.transform.position = slime.Position;
            root.layer = FirstLayerIndex(cfg.stampLayer);

            var stamp = root.AddComponent<AfterimageStamp>();
            stamp.owner = owner;
            stamp.slime = slime;
            stamp.cfg = cfg;

            stamp.col = root.AddComponent<CircleCollider2D>();
            stamp.col.radius = slime.SlimeRadius * cfg.stampSizeScale;
            stamp.col.enabled = false;

            // Clone the whole visual subtree so the body3d/meshBaseline inverse-rotation
            // pair survives — the R·S·R⁻¹ surface-align squash cannot be flattened into
            // a single TRS (lossyScale drops the shear).
            var clone = Instantiate(body3d, body3d.position, body3d.rotation, root.transform);
            clone.name = "Visual";
            // Stamp root is unit-scale, so world scale == local scale here.
            clone.localScale = body3d.lossyScale;
            foreach (var mb in clone.GetComponentsInChildren<MonoBehaviour>(true))
                Destroy(mb);

            foreach (var r in clone.GetComponentsInChildren<Renderer>(true))
            {
                var mr = r as MeshRenderer;
                if (stamp.ghostRenderer == null && mr != null)
                {
                    stamp.ghostRenderer = mr;
                    continue;
                }
                r.enabled = false; // ghost is body-only
            }
            if (stamp.ghostRenderer == null)
            {
                Destroy(root);
                return null;
            }

            if (cfg.ghostMaterial != null)
                stamp.ghostRenderer.sharedMaterial = cfg.ghostMaterial;
            else
                Debug.LogWarning("AfterimageStamp: no ghostMaterial in config — stamp renders opaque.", stamp);
            stamp.ghostRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Face snapshot — separate subtree (face lives beside body3d, not inside it).
            Transform face = visual.FaceTransform;
            if (face != null)
            {
                var faceClone = Instantiate(face.gameObject, face.position, face.rotation,
                    root.transform).transform;
                faceClone.name = "Face";
                faceClone.localScale = face.lossyScale;
                foreach (var mb in faceClone.GetComponentsInChildren<MonoBehaviour>(true))
                    Destroy(mb);
                stamp.faceRenderers = faceClone.GetComponentsInChildren<SpriteRenderer>(true);
                stamp.faceBaseColors = new Color[stamp.faceRenderers.Length];
                for (int i = 0; i < stamp.faceRenderers.Length; i++)
                    stamp.faceBaseColors[i] = stamp.faceRenderers[i].color;
            }

            // Instantiate does not carry the MPB — without this copy the clone renders
            // at rest pose (all deform lives in MPB scalars, not the mesh).
            stamp.mpb = new MaterialPropertyBlock();
            srcRenderer.GetPropertyBlock(stamp.mpb);
            stamp.mpb.SetFloat(GhostAlphaId, cfg.ghostAlpha);
            stamp.ghostRenderer.SetPropertyBlock(stamp.mpb);

            // The afterimage materializes only after appearDelaySeconds.
            stamp.ghostRenderer.enabled = false;
            for (int i = 0; i < stamp.faceRenderers.Length; i++)
                stamp.faceRenderers[i].enabled = false;

            slime.OnLaunch += stamp.HandleLaunch;
            Active.Add(stamp);
            return stamp;
        }

        void Update()
        {
            if (expired)
            {
                TryFinalDestroy();
                return;
            }

            age += Time.deltaTime;
            if (age >= cfg.stampLifetimeSeconds)
            {
                Expire();
                return;
            }

            if (!visible && age >= cfg.appearDelaySeconds)
            {
                visible = true;
                ghostRenderer.enabled = true;
                for (int i = 0; i < faceRenderers.Length; i++)
                    faceRenderers[i].enabled = true;
            }

            if (hardenAge < 0f && age >= cfg.hardenDelaySeconds && !OverlapsSlime())
            {
                hardenAge = age;
                col.enabled = true;
            }

            if (visible) PushAlpha();
        }

        // Read-only debug surface (AfterimageStampDebugger pulls, never writes).
        public float Age => age;
        public bool Hardened => hardenAge >= 0f;
        public bool ColliderOn => col != null && col.enabled;
        public bool OverlapsSlimeNow => OverlapsSlime();

        /// <summary>Shockwave erasure — same exit as natural expiry, ghosts included.</summary>
        public void Erase() => Expire();

        void Expire()
        {
            if (expired) return;
            expired = true;
            // Disable, don't Destroy yet: a destroyed collider is fake-null and would skip
            // the Charging probe-grace that keeps a mid-charge slime on its phantom surface.
            col.enabled = false;
            ghostRenderer.enabled = false;
            for (int i = 0; i < faceRenderers.Length; i++)
                faceRenderers[i].enabled = false;
            Active.Remove(this);
            Unsubscribe();
            TryFinalDestroy();
        }

        void TryFinalDestroy()
        {
            // Waiting on the cached reference matters only while Charging — that is the
            // sole consumer of the grace surface, and any new charge overwrites the cache
            // on its first valid tick. Outside Charging, destroy immediately (the cache is
            // never cleared on cancel/landing, so waiting on it would leak the corpse).
            if (slime != null && slime.CurrentState == SlimeController.SlimeState.Charging &&
                (slime.SurfaceCollider == col || slime.CachedSurfaceCollider == col)) return;
            Destroy(gameObject);
        }

        bool OverlapsSlime()
        {
            // Harden-block region query lives on the zone (authored polygon or box fallback).
            return owner != null && owner.BlocksHarden(transform.position, col.radius);
        }

        void PushAlpha()
        {
            // Base fade over lifetime; ghosts sit dimmer AND desaturated (cold grey-blue),
            // hardening eases brightness + true colors back one-way (no blink).
            float baseAlpha = cfg.ghostAlpha * Mathf.Clamp01(1f - age / cfg.stampLifetimeSeconds);
            float harden01 = 0f;
            if (hardenAge >= 0f)
            {
                float riseT = Mathf.Clamp01((age - hardenAge) / cfg.hardenFadeInSeconds);
                harden01 = riseT * riseT * (3f - 2f * riseT);
            }
            float alpha = Mathf.Lerp(baseAlpha * cfg.ghostAlphaFactor, baseAlpha, harden01);
            float desat = (1f - harden01) * cfg.ghostDesaturation;
            mpb.SetFloat(GhostAlphaId, alpha);
            mpb.SetFloat(GhostDesatId, desat);
            mpb.SetColor(GhostTintId, cfg.ghostTint);
            ghostRenderer.SetPropertyBlock(mpb);
            for (int i = 0; i < faceRenderers.Length; i++)
            {
                Color c = faceBaseColors[i];
                float grey = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
                c.r = Mathf.Lerp(c.r, grey * cfg.ghostTint.r, desat);
                c.g = Mathf.Lerp(c.g, grey * cfg.ghostTint.g, desat);
                c.b = Mathf.Lerp(c.b, grey * cfg.ghostTint.b, desat);
                c.a *= alpha;
                faceRenderers[i].color = c;
            }
        }

        void HandleLaunch(Vector2 pos, Vector2 vel, bool wasHop)
        {
            if (expired || hardenAge < 0f) return;
            var surface = slime.SurfaceCollider;
            // Coyote launches carry a null SurfaceCollider — fall back to the cached one.
            bool launchedOffMe = surface == col ||
                                 (surface == null && slime.CachedSurfaceCollider == col);
            if (launchedOffMe && owner != null)
                owner.TriggerShockwave(pos);
        }

        void Unsubscribe()
        {
            if (slime != null) slime.OnLaunch -= HandleLaunch;
        }

        void OnDestroy()
        {
            Active.Remove(this);
            Unsubscribe();
        }

        static int FirstLayerIndex(LayerMask mask)
        {
            int v = mask.value;
            for (int i = 0; i < 32; i++)
                if ((v & (1 << i)) != 0) return i;
            return 0;
        }
    }
}
