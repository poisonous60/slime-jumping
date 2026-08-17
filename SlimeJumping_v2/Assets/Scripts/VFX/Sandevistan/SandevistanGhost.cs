using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.VFX.Sandevistan
{
    /// <summary>
    /// One cosmetic afterimage: a frozen clone of the slime's body mesh (plus face) that fades out
    /// and dies. Deliberately NOT AfterimageStamp — that one hardens into a real platform, sits on
    /// the ground layer and drives a shockwave. This has no collider, no layer change and no event
    /// subscription, so nothing here can touch gameplay.
    /// The clone copies the source MaterialPropertyBlock because every deformation the slime does
    /// lives in MPB scalars, not in the mesh — without it the ghost snaps back to rest pose.
    /// </summary>
    public class SandevistanGhost : MonoBehaviour
    {
        static readonly int GhostAlphaId = Shader.PropertyToID("_GhostAlpha");
        static readonly int GhostDesatId = Shader.PropertyToID("_GhostDesat");
        static readonly int GhostTintId = Shader.PropertyToID("_GhostTint");

        MeshRenderer bodyRenderer;
        SpriteRenderer[] faceRenderers = System.Array.Empty<SpriteRenderer>();
        Color[] faceBaseColors = System.Array.Empty<Color>();
        MaterialPropertyBlock mpb;
        AnimationCurve fade;
        float age;
        float lifetime;
        float peakAlpha;
        float desaturation;
        Color tint;

        public static SandevistanGhost Spawn(SlimeController slime, Transform parent, Material ghostMaterial,
                                             float lifetime, float peakAlpha, Color tint, float desaturation,
                                             AnimationCurve fade, bool includeFace)
        {
            var visual = slime.GetComponentInChildren<SlimeVisual>(true);
            Transform body3d = visual != null ? visual.Body3d : null;
            MeshRenderer srcRenderer = body3d != null ? body3d.GetComponentInChildren<MeshRenderer>() : null;
            if (srcRenderer == null) return null;

            var root = new GameObject("SandevistanGhost");
            root.transform.SetParent(parent, worldPositionStays: true);
            root.transform.position = slime.Position;

            var ghost = root.AddComponent<SandevistanGhost>();
            ghost.lifetime = Mathf.Max(0.05f, lifetime);
            ghost.peakAlpha = peakAlpha;
            ghost.tint = tint;
            ghost.desaturation = desaturation;
            ghost.fade = fade;

            // Whole subtree — the align squash is R·S·R⁻¹ and that shear dies in a flattened TRS.
            var clone = Instantiate(body3d, body3d.position, body3d.rotation, root.transform);
            clone.name = "Visual";
            clone.localScale = body3d.lossyScale;
            foreach (var mb in clone.GetComponentsInChildren<MonoBehaviour>(true))
                Destroy(mb);

            foreach (var r in clone.GetComponentsInChildren<Renderer>(true))
            {
                var mr = r as MeshRenderer;
                if (ghost.bodyRenderer == null && mr != null)
                {
                    ghost.bodyRenderer = mr;
                    continue;
                }
                r.enabled = false;
            }
            if (ghost.bodyRenderer == null)
            {
                Destroy(root);
                return null;
            }

            if (ghostMaterial != null) ghost.bodyRenderer.sharedMaterial = ghostMaterial;
            ghost.bodyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ghost.bodyRenderer.receiveShadows = false;

            Transform face = includeFace && visual != null ? visual.FaceTransform : null;
            if (face != null)
            {
                var faceClone = Instantiate(face.gameObject, face.position, face.rotation, root.transform).transform;
                faceClone.name = "Face";
                faceClone.localScale = face.lossyScale;
                foreach (var mb in faceClone.GetComponentsInChildren<MonoBehaviour>(true))
                    Destroy(mb);
                ghost.faceRenderers = faceClone.GetComponentsInChildren<SpriteRenderer>(true);
                ghost.faceBaseColors = new Color[ghost.faceRenderers.Length];
                for (int i = 0; i < ghost.faceRenderers.Length; i++)
                    ghost.faceBaseColors[i] = ghost.faceRenderers[i].color;
            }

            ghost.mpb = new MaterialPropertyBlock();
            srcRenderer.GetPropertyBlock(ghost.mpb);
            ghost.Push(1f);
            return ghost;
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age >= lifetime)
            {
                Destroy(gameObject);
                return;
            }
            Push(1f - age / lifetime);
        }

        void Push(float remaining01)
        {
            float shape = fade != null && fade.length > 0 ? fade.Evaluate(1f - remaining01) : remaining01;
            float alpha = peakAlpha * Mathf.Clamp01(shape);

            mpb.SetFloat(GhostAlphaId, alpha);
            mpb.SetFloat(GhostDesatId, desaturation);
            mpb.SetColor(GhostTintId, tint);
            bodyRenderer.SetPropertyBlock(mpb);

            for (int i = 0; i < faceRenderers.Length; i++)
            {
                Color c = faceBaseColors[i];
                float grey = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
                c.r = Mathf.Lerp(c.r, grey * tint.r, desaturation);
                c.g = Mathf.Lerp(c.g, grey * tint.g, desaturation);
                c.b = Mathf.Lerp(c.b, grey * tint.b, desaturation);
                c.a *= alpha;
                faceRenderers[i].color = c;
            }
        }
    }
}
