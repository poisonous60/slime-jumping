using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// Straight beam line for <see cref="GraniteBlastEmitter"/>'s Beam mode and Burst-mode flashes.
    /// Single LineRenderer (Sprites/Default + whiteTexture, matches TurretHazard's tracer setup
    /// which is verifiably visible). Direction locks at fire time; origin tracks the muzzle so
    /// a rotating mount still pivots its laser. On the first frame it hits a slime, applies the
    /// hazard once and spawns an impact prefab.
    /// </summary>
    public class GraniteBlastBeam : MonoBehaviour
    {
        private GraniteBlastEmitter owner;
        private Transform muzzle;
        private Vector2 lockedDir;
        private LineRenderer lr;
        private float baseWidth;

        private float duration;
        private float fadeTime;
        private float age;
        private bool hitResolved;
        private ParticleSystem activeImpact;

        public void Init(GraniteBlastEmitter emitter, Transform muzzle, Vector2 fireDir,
                         float duration, float fadeTime, float coreWidth)
        {
            this.owner = emitter;
            this.muzzle = muzzle;
            Vector2 d = fireDir;
            if (d.sqrMagnitude < 0.0001f) d = Vector2.right;
            else d.Normalize();
            this.lockedDir = d;
            this.duration = Mathf.Max(0.05f, duration);
            this.fadeTime = Mathf.Max(0.001f, fadeTime);
            this.baseWidth = Mathf.Max(0.001f, coreWidth);
            CreateLine();
        }

        private void CreateLine()
        {
            lr = gameObject.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 0;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Stretch;
            lr.sortingOrder = 100;
            lr.widthMultiplier = baseWidth;
            lr.positionCount = 2;

            Material mat = owner != null ? owner.GetCoreMaterial() : null;
            if (mat == null)
            {
                Shader sh = Shader.Find("Sprites/Default");
                if (sh == null) sh = Shader.Find("UI/Default");
                mat = new Material(sh);
                mat.mainTexture = Texture2D.whiteTexture;
            }
            lr.material = mat;

            Color c = owner != null ? owner.CoreColor : new Color(0.85f, 0.96f, 1f, 1f);
            lr.startColor = c;
            lr.endColor = c;

            // Start with both endpoints at the muzzle so we never render a stray line at world origin.
            Vector3 origin = muzzle != null ? muzzle.position : transform.position;
            float zOff = owner != null ? owner.LineZOffset : -0.5f;
            Vector3 p = new Vector3(origin.x, origin.y, origin.z + zOff);
            lr.SetPosition(0, p);
            lr.SetPosition(1, p);
        }

        void Update()
        {
            age += Time.deltaTime;

            if (owner == null || muzzle == null)
            {
                Destroy(gameObject);
                return;
            }

            Vector3 origin = muzzle.position;
            Vector2 dir = lockedDir;

            float reach = owner.MaxRange;
            Vector3 hitPoint = Vector3.zero;
            Vector3 hitNormal = Vector3.up;
            bool blocked = false;

            if (owner.BulletHitMask != 0)
            {
                var hit = owner.RaycastExcludingSelf(origin, dir, owner.MaxRange, owner.BulletHitMask);
                if (hit.collider != null)
                {
                    reach = hit.distance;
                    hitPoint = hit.point;
                    hitNormal = hit.normal;
                    blocked = true;
                }
            }

            float zOff = owner.LineZOffset;
            Vector3 start = new Vector3(origin.x, origin.y, origin.z + zOff);
            Vector3 endWorld = new Vector3(origin.x + dir.x * reach,
                                           origin.y + dir.y * reach,
                                           origin.z + zOff);
            lr.SetPosition(0, start);
            lr.SetPosition(1, endWorld);

            // Width pulse for the energy crackle.
            float pulse = 1f + 0.18f * (Mathf.PerlinNoise(age * 22f, 0f) - 0.5f);
            lr.widthMultiplier = baseWidth * pulse;

            if (!hitResolved && blocked)
            {
                var slime = TryFindSlimeAlongBeam(origin, dir, reach);
                if (slime != null)
                {
                    var effect = owner.GetHazardEffect();
                    effect.knockbackDirection = dir;
                    slime.ApplyHazardEffect(effect);
                    hitResolved = true;
                    SpawnImpactAt(hitPoint, hitNormal);
                }
                else if (activeImpact == null)
                {
                    activeImpact = SpawnImpactAt(hitPoint, hitNormal);
                }
            }

            float fadeStart = duration - fadeTime;
            if (age >= fadeStart)
            {
                float k = Mathf.Clamp01(1f - (age - fadeStart) / fadeTime);
                Color c = owner.CoreColor;
                c.a *= k;
                lr.startColor = c;
                lr.endColor = c;
                lr.widthMultiplier = baseWidth * pulse * Mathf.Lerp(0.4f, 1f, k);
            }

            if (age >= duration) Destroy(gameObject);
        }

        private SlimeController TryFindSlimeAlongBeam(Vector3 origin, Vector2 dir, float reach)
        {
            var hits = Physics2D.RaycastAll(origin, dir, reach, owner.BulletHitMask);
            for (int i = 0; i < hits.Length; i++)
            {
                var col = hits[i].collider;
                if (col == null) continue;
                if (col.transform.IsChildOf(owner.transform)) continue;
                var slime = col.GetComponentInParent<SlimeController>();
                if (slime != null) return slime;
            }
            return null;
        }

        private ParticleSystem SpawnImpactAt(Vector3 point, Vector3 normal)
        {
            if (owner == null || owner.ImpactPrefab == null) return null;
            Vector3 n = normal.sqrMagnitude > 0.0001f ? normal : Vector3.up;
            Quaternion rot = Quaternion.LookRotation(Vector3.forward, n);
            Vector3 spawnPos = new Vector3(point.x, point.y, point.z + owner.LineZOffset);
            var inst = Instantiate(owner.ImpactPrefab, spawnPos, rot);
            if (!inst.gameObject.activeSelf) inst.gameObject.SetActive(true);
            inst.Play(true);
            var main = inst.main;
            float life = main.duration + main.startLifetime.constantMax + 0.2f;
            Destroy(inst.gameObject, life);
            return inst;
        }
    }
}
