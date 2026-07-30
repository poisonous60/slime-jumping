using UnityEngine;

namespace SlimeJumping.Effects
{
    /// <summary>The half of the face that left with the chunk: the body's wound plane, sign-flipped
    /// and frozen in this object's frame, re-pushed to world space every frame so it travels and
    /// rotates with the droplet.</summary>
    [DefaultExecutionOrder(120)]
    public class SlimeFaceShard : MonoBehaviour
    {
        private SpriteRenderer[] renderers;
        private MaterialPropertyBlock mpb;
        private Vector2 planeNormalLocal;   // toward the side the BODY kept
        private float planeOffsetLocal;     // along that normal, from this transform's origin
        private Color[] baseColors;

        private static readonly int SliceCountWSID = Shader.PropertyToID("_SliceCountWS");
        private static readonly int SlicePlaneWSID = Shader.PropertyToID("_SlicePlaneWS");
        private static readonly int SliceBottomGuardWSID = Shader.PropertyToID("_SliceBottomGuardWS");
        private static readonly Vector4[] planeScratch = new Vector4[3];

        /// <param name="normalWorld">Wound normal, pointing to the side that LEFT with the chunk.</param>
        /// <param name="pointWorld">Any point on the wound plane.</param>
        public void Init(Vector2 normalWorld, Vector3 pointWorld)
        {
            renderers = GetComponentsInChildren<SpriteRenderer>(true);
            baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) baseColors[i] = renderers[i].color;
            mpb = new MaterialPropertyBlock();

            // The shader discards the side the normal points at; this piece keeps the other one.
            Vector3 n3 = new Vector3(-normalWorld.x, -normalWorld.y, 0f);
            Vector3 nLocal = transform.InverseTransformDirection(n3);
            nLocal.z = 0f;
            float l = nLocal.magnitude;
            if (l < 1e-6f) { enabled = false; return; }
            nLocal /= l;
            planeNormalLocal = nLocal;
            planeOffsetLocal = Vector3.Dot(nLocal, transform.InverseTransformPoint(pointWorld));
        }

        /// <summary>Chunk round-up progress 0..1 — the shard dissolves as the droplet becomes a ball.</summary>
        public void SetFade(float t)
        {
            if (renderers == null) return;
            float a = Mathf.Clamp01(1f - t);
            for (int i = 0; i < renderers.Length; i++)
            {
                var sr = renderers[i];
                if (sr == null) continue;
                Color c = baseColors[i];
                c.a = baseColors[i].a * a;
                sr.color = c;
            }
            if (a <= 0.001f) Destroy(gameObject);
        }

        private void LateUpdate()
        {
            if (renderers == null) return;
            Vector3 nWS = transform.TransformDirection(new Vector3(planeNormalLocal.x, planeNormalLocal.y, 0f));
            nWS.z = 0f;
            float l = nWS.magnitude;
            if (l < 1e-6f) return;
            nWS /= l;
            Vector3 pWS = transform.TransformPoint(
                new Vector3(planeNormalLocal.x, planeNormalLocal.y, 0f) * planeOffsetLocal);

            planeScratch[0] = new Vector4(nWS.x, nWS.y, 0f, Vector3.Dot(nWS, pWS));
            planeScratch[1] = new Vector4(0f, 1f, 0f, 1e9f);
            planeScratch[2] = new Vector4(0f, 1f, 0f, 1e9f);

            for (int i = 0; i < renderers.Length; i++)
            {
                var sr = renderers[i];
                if (sr == null) continue;
                sr.GetPropertyBlock(mpb);
                mpb.SetFloat(SliceCountWSID, 1f);
                mpb.SetVectorArray(SlicePlaneWSID, planeScratch);
                // No ground guard — it would restore the whole face mid-air.
                mpb.SetVector(SliceBottomGuardWSID, new Vector4(0f, 1f, 0f, -1e9f));
                sr.SetPropertyBlock(mpb);
            }
        }
    }
}
