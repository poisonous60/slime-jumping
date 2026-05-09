using UnityEngine;

namespace SlimeJumping.Platforms.ThinIceV2
{
    /// Per-instance runtime behavior on a flying shard. Drives lifetime + alpha fade and
    /// destroys itself when its time runs out. No Collider2D is attached — shards are
    /// purely visual (gravity + spin, no collisions with anything).
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(Rigidbody2D))]
    public class ThinIceShardRuntime : MonoBehaviour
    {
        public float lifetime  = 1.5f;
        public float fadeStart = 1.1f;

        [Tooltip("Shader property set during the fade window. Default '_Alpha' matches " +
                 "ThinIceV2.shadergraph's alpha output. URP/Lit fallback is '_BaseColor'.")]
        [SerializeField] string fadeProperty = "_Alpha";

        float age;
        MeshRenderer rend;
        MaterialPropertyBlock mpb;
        static readonly int FadeAlphaID = Shader.PropertyToID("_Alpha");
        static readonly int WorldSpaceSamplingID = Shader.PropertyToID("_WorldSpaceSampling");
        int fadePropID;

        void Awake()
        {
            rend = GetComponent<MeshRenderer>();
            mpb = new MaterialPropertyBlock();
            fadePropID = string.IsNullOrEmpty(fadeProperty) ? FadeAlphaID : Shader.PropertyToID(fadeProperty);

            // Lock the shader to object-space Voronoi sampling so cells stay attached
            // to this shard's mesh as it translates / rotates after launch (the
            // platform's shared material samples in world space — see ThinIceV2.shader).
            rend.GetPropertyBlock(mpb);
            mpb.SetFloat(WorldSpaceSamplingID, 0f);
            rend.SetPropertyBlock(mpb);
        }

        public void Begin(float lifetime, float fadeWindow)
        {
            this.lifetime  = lifetime;
            this.fadeStart = Mathf.Max(0f, lifetime - fadeWindow);
            age = 0f;
            ApplyAlpha(1f);
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age >= lifetime)
            {
                Destroy(gameObject);
                return;
            }
            if (age > fadeStart && lifetime > fadeStart)
            {
                float a = 1f - (age - fadeStart) / (lifetime - fadeStart);
                ApplyAlpha(a);
            }
        }

        void ApplyAlpha(float a)
        {
            if (rend == null) return;
            rend.GetPropertyBlock(mpb);
            mpb.SetFloat(fadePropID, a);
            rend.SetPropertyBlock(mpb);
        }
    }
}
