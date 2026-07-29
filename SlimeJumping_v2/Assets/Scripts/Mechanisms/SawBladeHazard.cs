using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Effects;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Mechanisms
{
    // Trigger Collider2D must sit on THIS GameObject — OnTriggerEnter2D only fires on the
    // collider's own GameObject. The rotating visual (bladeTransform) is a separate child.
    [RequireComponent(typeof(Collider2D))]
    public class SawBladeHazard : MonoBehaviour, IHazardSource
    {
        [Header("Rotation (visual)")]
        [SerializeField] private Transform bladeTransform;
        [SerializeField] private float rotationRPM = 600f;

        [Header("Hazard")]
        [Range(0f, 1f)]
        [Tooltip("Fraction of incoming speed retained after pass-through. 1 = no slowdown, 0 = full stop. 0.4 = keep 40% of entry speed.")]
        [SerializeField] private float speedRetainRatio = 0.4f;
        [Tooltip("Extra impulse added on top of retained velocity. Pulls slime toward the blade for 'sliced' feel.")]
        [SerializeField] private float pullForce = 6f;
        [Range(0f, 1f)]
        [Tooltip("0 = pure pull toward blade, 1 = pure tangent (spin-aligned drag). 0.3 = mostly inward with a twist.")]
        [SerializeField] private float tangentWeight = 0.3f;
        [SerializeField] private float stunDuration = 0.6f;
        [Tooltip("Seconds between hits on the same slime. Prevents continuous pull once they slide off.")]
        [SerializeField] private float hitCooldown = 1.2f;

        [Header("Slice FX")]
        [SerializeField] private SliceEffect sliceEffectPrefab;
        [SerializeField] private ImpulseEmitter impulseEmitter;
        [SerializeField] private float impulseForce = 0.2f;
        [Tooltip("Extra velocity along the rotational tangent, in the slime's slide direction.")]
        [SerializeField] private float sliceKick = 3f;

        [Header("Slice (optional)")]
        [Tooltip("Assign to make blade hits ALSO slice the slime (visual wound + falling piece + heal), layered on top of the normal stun. Leave null for a plain stun.")]
        [SerializeField] private SlimeSplitSequence splitSequence;

        private SlimeController cachedSlime;
        private float nextHitTime;

        public bool IsActive => isActiveAndEnabled;

        private void Reset()
        {
            var col = GetComponent<Collider2D>();
            if (col != null) col.isTrigger = true;
        }

        private void Update()
        {
            if (bladeTransform != null)
            {
                bladeTransform.Rotate(Vector3.forward, rotationRPM * 6f * Time.deltaTime);
            }
        }

        public HazardEffect GetHazardEffect()
        {
            Vector2 finalVel = Vector2.zero;

            if (cachedSlime != null && bladeTransform != null)
            {
                Vector2 retained = cachedSlime.Velocity * speedRetainRatio;

                Vector2 toSlime = cachedSlime.Position - (Vector2)bladeTransform.position;
                toSlime = toSlime.sqrMagnitude > 0.0001f ? toSlime.normalized : Vector2.up;
                Vector2 pullIn = -toSlime;
                Vector2 tangent = new Vector2(-toSlime.y, toSlime.x) * Mathf.Sign(rotationRPM);
                Vector2 pullDir = (pullIn * (1f - tangentWeight) + tangent * tangentWeight).normalized;

                finalVel = retained + pullDir * pullForce;
            }

            float mag = finalVel.magnitude;
            Vector2 dir = mag > 0.0001f ? finalVel / mag : Vector2.zero;

            return new HazardEffect
            {
                knockbackForce = mag,
                knockbackDirection = dir,
                stunDuration = stunDuration,
                damageType = DamageType.Slice
            };
        }

        private void OnTriggerEnter2D(Collider2D other) => TryHit(other);
        private void OnTriggerStay2D(Collider2D other) => TryHit(other);

        private void TryHit(Collider2D other)
        {
            if (Time.time < nextHitTime) return;

            var slime = other.GetComponentInParent<SlimeController>();
            if (slime == null) return;

            cachedSlime = slime;

            Vector2 bladePos = bladeTransform != null ? (Vector2)bladeTransform.position
                                                       : (Vector2)transform.position;
            Vector2 toSlime = slime.Position - bladePos;
            Vector2 radial = toSlime.sqrMagnitude > 0.0001f ? toSlime.normalized : Vector2.up;
            Vector2 perp = new Vector2(-radial.y, radial.x);
            float perpDot = Vector2.Dot(slime.Velocity, perp);
            float perpSign = Mathf.Abs(perpDot) > 0.01f ? Mathf.Sign(perpDot) : Mathf.Sign(rotationRPM);
            Vector2 signedTangent = perp * perpSign;
            Vector2 contact = GetComponent<Collider2D>().ClosestPoint(slime.Position);
            if (sliceEffectPrefab != null)
                SliceEffect.Spawn(sliceEffectPrefab, contact, signedTangent);
            if (impulseEmitter != null) impulseEmitter.Emit(impulseForce);

            // Must run before ApplyHazardEffect — the cut angle reads slime.Velocity, which
            // the hazard effect overwrites.
            if (splitSequence != null && !slime.HasOverride)
                splitSequence.Begin(slime, slime.Velocity);

            slime.AddExternalVelocity(signedTangent * sliceKick);

            slime.ApplyHazardEffect(GetHazardEffect());
            nextHitTime = Time.time + hitCooldown;
        }
    }
}
