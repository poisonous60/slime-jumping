using System.Collections;
using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Effects;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Mechanisms
{
    [RequireComponent(typeof(Collider2D))]
    public class KnifeHazard : MonoBehaviour, IHazardSource
    {
        [Header("Hazard")]
        [Range(0f, 1f)]
        [Tooltip("Fraction of incoming speed retained after pass-through. 1 = no slowdown, 0 = full stop.")]
        [SerializeField] private float speedRetainRatio = 0.4f;
        [Tooltip("Extra impulse added on top of retained velocity. Pulls slime toward the blade edge for 'sliced' feel.")]
        [SerializeField] private float pullForce = 6f;
        [SerializeField] private float stunDuration = 0.8f;
        [Tooltip("Seconds between hits on the same slime. Prevents continuous re-trigger.")]
        [SerializeField] private float hitCooldown = 1.2f;

        [Header("Slice FX")]
        [SerializeField] private SliceEffect sliceEffectPrefab;
        [SerializeField] private ImpulseEmitter impulseEmitter;
        [SerializeField] private float impulseForce = 0.2f;
        [Tooltip("Extra velocity along the blade-edge tangent, in the slime's slide direction. 'Scratch on inertia' feel.")]
        [SerializeField] private float sliceKick = 2f;
        [Tooltip("Local +X should point along the cutting edge. Leave null to use this GameObject's right.")]
        [SerializeField] private Transform bladeEdgeRef;

        [Header("Slice (optional)")]
        [Tooltip("Assign to make blade hits ALSO slice the slime (visual wound + falling piece + heal), layered on top of the normal stun. Leave null for a plain stun.")]
        [SerializeField] private SlimeSplitSequence splitSequence;

        [Header("Body Passthrough")]
        [Tooltip("Root transform of the knife body. All Collider2D in its subtree (except this hazard's own trigger) are ignored after a hit for `passthroughDuration` seconds. Assign the body GO or any common ancestor.")]
        [SerializeField] private Transform bodyRoot;
        [Tooltip("Extra colliders to include (in addition to bodyRoot subtree). Leave empty if bodyRoot covers everything.")]
        [SerializeField] private Collider2D[] extraColliders;
        [Tooltip("How long the body is passable after a hit (seconds). Independent of stun duration — set longer if the slime needs more time to drift through.")]
        [SerializeField] private float passthroughDuration = 2f;

        private SlimeController cachedSlime;
        private float nextHitTime;
        private bool passthroughActive;

        public bool IsActive => isActiveAndEnabled;

        private void Reset()
        {
            var col = GetComponent<Collider2D>();
            if (col != null) col.isTrigger = true;
        }

        public HazardEffect GetHazardEffect()
        {
            Vector2 finalVel = Vector2.zero;

            if (cachedSlime != null)
            {
                Vector2 retained = cachedSlime.Velocity * speedRetainRatio;
                Vector2 toEdge = (Vector2)transform.position - cachedSlime.Position;
                Vector2 pullDir = toEdge.sqrMagnitude > 0.0001f ? toEdge.normalized : Vector2.zero;
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
            if (passthroughActive) return; // already mid-passthrough; don't re-hit while slime is transiting
            if (Time.time < nextHitTime) return;

            var slime = other.GetComponentInParent<SlimeController>();
            if (slime == null) return;

            cachedSlime = slime;

            Vector2 edge = (bladeEdgeRef != null ? (Vector2)bladeEdgeRef.right
                                                 : (Vector2)transform.right).normalized;
            float edgeDot = Vector2.Dot(slime.Velocity, edge);
            float edgeSign = Mathf.Abs(edgeDot) > 0.01f ? Mathf.Sign(edgeDot) : 1f;
            Vector2 signedTangent = edge * edgeSign;
            Vector2 contact = GetComponent<Collider2D>().ClosestPoint(slime.Position);
            if (sliceEffectPrefab != null)
                SliceEffect.Spawn(sliceEffectPrefab, contact, signedTangent);
            if (impulseEmitter != null) impulseEmitter.Emit(impulseForce);

            // Slice is part of the stun reaction: every hit slices AND stuns. Eyes/control
            // come from the Stunned state below — read velocity for the cut angle BEFORE
            // ApplyHazardEffect overwrites it with knockback. Skip only if another override
            // owns the slime.
            if (splitSequence != null && !slime.HasOverride)
                splitSequence.Begin(slime, slime.Velocity);

            slime.AddExternalVelocity(signedTangent * sliceKick);

            slime.ApplyHazardEffect(GetHazardEffect());
            nextHitTime = Time.time + hitCooldown;

            StartCoroutine(IgnoreBodyDuringStun(slime, passthroughDuration));
        }

        private IEnumerator IgnoreBodyDuringStun(SlimeController slime, float duration)
        {
            var ownCol = GetComponent<Collider2D>();
            var bodyCols = CollectBodyColliders(ownCol);
            // Include the blade trigger itself — without it, SurfaceProbe treats blade's
            // overlap as a surface and slime attaches. IgnoredColliderList auto-skips
            // Physics2D.IgnoreCollision for triggers so OnTriggerEnter/Stay still fire.
            if (ownCol != null) bodyCols.Add(ownCol);
            if (bodyCols.Count == 0) yield break;

            var slimeCol = slime.GetComponent<CircleCollider2D>();
            if (slimeCol == null) yield break;

            passthroughActive = true;

            // IgnoredColliderList manages Physics2D.IgnoreCollision lifecycle — registering
            // here suppresses both surface-detection (probe) and physics contact.
            for (int b = 0; b < bodyCols.Count; b++)
                slime.RegisterIgnoredCollider(bodyCols[b], duration);

            yield return new WaitForSeconds(duration);

            // Don't release while slime is still overlapping — refresh timers each frame
            // until the slime has fully cleared the body.
            while (AnyOverlap(slimeCol, bodyCols))
            {
                for (int b = 0; b < bodyCols.Count; b++)
                    if (bodyCols[b] != null) slime.RegisterIgnoredCollider(bodyCols[b], 0.2f);
                yield return new WaitForFixedUpdate();
            }

            for (int b = 0; b < bodyCols.Count; b++)
                if (bodyCols[b] != null) slime.UnregisterIgnoredCollider(bodyCols[b]);

            passthroughActive = false;
        }

        private static bool AnyOverlap(Collider2D a, System.Collections.Generic.List<Collider2D> b)
        {
            if (a == null) return false;
            for (int j = 0; j < b.Count; j++)
            {
                if (b[j] == null) continue;
                if (a.Distance(b[j]).isOverlapped) return true;
            }
            return false;
        }

        private System.Collections.Generic.List<Collider2D> CollectBodyColliders(Collider2D exclude)
        {
            var list = new System.Collections.Generic.List<Collider2D>();
            if (bodyRoot != null)
            {
                var found = bodyRoot.GetComponentsInChildren<Collider2D>(true);
                for (int i = 0; i < found.Length; i++)
                {
                    if (found[i] != null && found[i] != exclude)
                        list.Add(found[i]);
                }
            }
            if (extraColliders != null)
            {
                for (int i = 0; i < extraColliders.Length; i++)
                {
                    if (extraColliders[i] != null && extraColliders[i] != exclude && !list.Contains(extraColliders[i]))
                        list.Add(extraColliders[i]);
                }
            }
            return list;
        }
    }
}
