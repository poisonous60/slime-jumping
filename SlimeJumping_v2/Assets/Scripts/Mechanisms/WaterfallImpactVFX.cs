using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace SlimeJumping.Mechanisms
{
    /// Stream path defined as a list of local-space offset points from this GameObject's
    /// transform. The LineRenderer connects them in order, and from the LAST point a ray
    /// is cast along rayDirection. Every collider in targetColliders that the ray hits
    /// (distance-sorted, up to maxHits) becomes a chained vertex on the line and gets its
    /// own splash PS. This allows water to visually flow through multiple water wheels
    /// stacked vertically.
    ///
    /// Editor scene handles (custom inspector): yellow sphere per path point, shift-click
    /// an edge to insert, ctrl-click a point to delete, drag to move.
    [DisallowMultipleComponent]
    [AddComponentMenu("SlimeJumping/Mechanisms/Waterfall Impact VFX")]
    public class WaterfallImpactVFX : MonoBehaviour
    {
        [Header("Path (local-space offsets — edit via scene handles)")]
        [SerializeField] List<Vector2> pathPointsLocal = new List<Vector2> {
            Vector2.zero,
            new Vector2(2f, 0f)
        };

        [Header("Contact (raycast from last point)")]
        [Tooltip("Ordered hit-eligible colliders. Ray hits are distance-sorted; any hit " +
                 "against a collider in this list becomes a chain vertex + splash.")]
        [SerializeField] List<Collider2D> targetColliders = new List<Collider2D>();
        [SerializeField] Vector2 rayDirection = Vector2.down;
        [Min(0.01f)] [SerializeField] float maxDistance = 25f;
        [SerializeField] LayerMask hitLayers = ~0;
        [Range(1, 8)] [SerializeField] int maxHits = 4;

        // Legacy single-collider field — kept hidden for one-shot migration of scenes
        // that were authored before targetColliders existed.
        [HideInInspector]
        [FormerlySerializedAs("targetCollider")]
        [SerializeField] Collider2D legacyTargetCollider;

        [Header("Stream (LineRenderer)")]
        [SerializeField] LineRenderer streamLine;
        [Tooltip("지정되면 LineRenderer.sharedMaterial 을 이것으로 강제. 비우면 LR 자체 " +
                 "Material 슬롯이 유지됨 (인스펙터에서 드래그 드롭 가능).")]
        [SerializeField] Material streamMaterialOverride;
        [ColorUsage(true, true)]
        [SerializeField] Color streamColor = new Color(0.45f, 0.85f, 1f, 1f);
        [SerializeField] bool applyStreamColor = true;
        [Tooltip("MaterialPropertyBlock 으로 틴팅할 셰이더 컬러 프로퍼티 이름. " +
                 "Sprites/Default → \"_Color\", URP/Unlit·Quibli StylizedLit → \"_BaseColor\".")]
        [SerializeField] string streamColorProperty = "_Color";
        [Tooltip("라인 Z 를 transform.z 에서 이만큼 더함. 양수 = 카메라에서 멀어짐 " +
                 "(wheel 스프라이트가 라인을 자연스럽게 가림).")]
        [SerializeField] float streamZOffset = 0.5f;

        [Header("Splash (ParticleSystem template — cloned per hit)")]
        [SerializeField] ParticleSystem splash;
        [ColorUsage(true, true)]
        [SerializeField] Color splashColor = new Color(0.45f, 0.85f, 1f, 1f);
        [SerializeField] bool applySplashColor = true;
        [Tooltip("각 splash 의 Z 를 transform.z 에서 이만큼 더함. 음수 = 카메라 쪽 " +
                 "(wheel 스프라이트 앞에 튀김).")]
        [SerializeField] float splashZOffset = -0.5f;

        static readonly RaycastHit2D[] rayBuffer = new RaycastHit2D[16];
        static ContactFilter2D rayFilter;
        MaterialPropertyBlock mpb;
        int cachedColorId;
        string cachedColorProp;

        readonly List<ParticleSystem> splashPool = new List<ParticleSystem>();
        readonly List<Vector2> hitPoints = new List<Vector2>(8);
        readonly List<Vector2> hitNormals = new List<Vector2>(8);

        // -------- Path point API (used by custom editor) --------
        public int PathPointCount => pathPointsLocal == null ? 0 : pathPointsLocal.Count;
        public Vector2 GetPathPointLocal(int i)               => pathPointsLocal[i];
        public void    SetPathPointLocal(int i, Vector2 v)    => pathPointsLocal[i] = v;
        public void    InsertPathPointLocal(int i, Vector2 v) => pathPointsLocal.Insert(i, v);
        public void    RemovePathPointAt(int i)               => pathPointsLocal.RemoveAt(i);

        void Reset()
        {
            if (streamLine == null) streamLine = GetComponentInChildren<LineRenderer>();
            if (splash     == null) splash     = GetComponentInChildren<ParticleSystem>();
            if (pathPointsLocal == null || pathPointsLocal.Count == 0)
                pathPointsLocal = new List<Vector2> { Vector2.zero, new Vector2(2f, 0f) };
        }

        void OnEnable()
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();
            if (splash != null && splashPool.Count == 0) splashPool.Add(splash);
            MigrateLegacyTarget();
            ApplyStreamMaterial();
            ApplyColors();
        }

        void LateUpdate()
        {
            if (pathPointsLocal == null || pathPointsLocal.Count == 0 ||
                targetColliders == null || targetColliders.Count == 0)
            {
                SetVisualActive(0);
                return;
            }

            Vector2 rayOrigin = transform.TransformPoint(pathPointsLocal[pathPointsLocal.Count - 1]);
            Vector2 dir = rayDirection.sqrMagnitude < 1e-6f ? Vector2.down : rayDirection.normalized;

            // useTriggers=false: 슬라임 InteractionDetector 등 트리거가 레이에 끼어
            // "가려짐"으로 오판되는 것을 방지.
            rayFilter.useTriggers = false;
            rayFilter.useLayerMask = true;
            rayFilter.layerMask = hitLayers;
            int n = Physics2D.Raycast(rayOrigin, dir, rayFilter, rayBuffer, maxDistance);

            hitPoints.Clear();
            hitNormals.Clear();
            for (int i = 0; i < n && hitPoints.Count < maxHits; i++)
            {
                var c = rayBuffer[i].collider;
                // `Contains` uses Equals — Unity's overloaded `==` handles destroyed refs,
                // so a destroyed collider in the list will not spuriously match.
                if (c != null && targetColliders.Contains(c))
                {
                    hitPoints.Add(rayBuffer[i].point);
                    hitNormals.Add(rayBuffer[i].normal);
                }
            }

            if (hitPoints.Count == 0)
            {
                SetVisualActive(0);
                return;
            }

            SetVisualActive(hitPoints.Count);
            UpdateStreamLine();
            UpdateSplashes();
        }

        void UpdateStreamLine()
        {
            if (streamLine == null) return;

            float z = transform.position.z + streamZOffset;
            int pathCount = pathPointsLocal.Count;
            int total = pathCount + hitPoints.Count;
            if (streamLine.positionCount != total) streamLine.positionCount = total;

            for (int i = 0; i < pathCount; i++)
            {
                Vector2 w = transform.TransformPoint(pathPointsLocal[i]);
                streamLine.SetPosition(i, new Vector3(w.x, w.y, z));
            }
            for (int i = 0; i < hitPoints.Count; i++)
            {
                Vector2 h = hitPoints[i];
                streamLine.SetPosition(pathCount + i, new Vector3(h.x, h.y, z));
            }
        }

        void UpdateSplashes()
        {
            if (splash == null) return;

            EnsurePoolSize(hitPoints.Count);

            float z = transform.position.z + splashZOffset;
            for (int i = 0; i < hitPoints.Count; i++)
            {
                var ps = splashPool[i];
                if (ps == null) continue;
                var st = ps.transform;
                Vector2 h = hitPoints[i];
                st.position = new Vector3(h.x, h.y, z);
                Vector2 nrm = hitNormals[i];
                if (nrm.sqrMagnitude > 1e-6f)
                    st.rotation = Quaternion.LookRotation(Vector3.forward, nrm);
                if (!ps.isPlaying) ps.Play(true);
            }
            for (int i = hitPoints.Count; i < splashPool.Count; i++)
            {
                var ps = splashPool[i];
                if (ps != null && ps.isPlaying)
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        void EnsurePoolSize(int n)
        {
            if (splash == null) return;
            if (splashPool.Count == 0) splashPool.Add(splash);
            if (!Application.isPlaying) return; // never instantiate in edit mode

            while (splashPool.Count < n)
            {
                var clone = Instantiate(splash, splash.transform.parent);
                clone.name = splash.name + "_" + splashPool.Count;
                clone.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                ApplySplashColor(clone);
                splashPool.Add(clone);
            }
        }

        void SetVisualActive(int hitCount)
        {
            bool lineOn = hitCount > 0;
            if (streamLine != null && streamLine.enabled != lineOn)
                streamLine.enabled = lineOn;

            if (hitCount == 0)
            {
                for (int i = 0; i < splashPool.Count; i++)
                {
                    var ps = splashPool[i];
                    if (ps != null && ps.isPlaying)
                        ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
        }

        public void ApplyColors()
        {
            if (applyStreamColor && streamLine != null)
            {
                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(streamColor, 0f), new GradientColorKey(streamColor, 1f) },
                    new[] { new GradientAlphaKey(streamColor.a, 0f), new GradientAlphaKey(streamColor.a, 1f) });
                streamLine.colorGradient = grad;

                var mat = streamLine.sharedMaterial;
                int id = GetColorId();
                if (mat != null && mat.HasProperty(id))
                {
                    if (mpb == null) mpb = new MaterialPropertyBlock();
                    streamLine.GetPropertyBlock(mpb);
                    mpb.SetColor(id, streamColor);
                    streamLine.SetPropertyBlock(mpb);
                }
            }
            if (applySplashColor)
            {
                ApplySplashColor(splash);
                for (int i = 0; i < splashPool.Count; i++)
                {
                    var ps = splashPool[i];
                    if (ps != null && ps != splash) ApplySplashColor(ps);
                }
            }
        }

        void ApplySplashColor(ParticleSystem ps)
        {
            if (ps == null) return;
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(splashColor);
        }

        void MigrateLegacyTarget()
        {
            if (legacyTargetCollider == null) return;
            if (targetColliders == null) targetColliders = new List<Collider2D>();
            if (!targetColliders.Contains(legacyTargetCollider))
                targetColliders.Insert(0, legacyTargetCollider);
            legacyTargetCollider = null;
        }

        void ApplyStreamMaterial()
        {
            if (streamLine == null || streamMaterialOverride == null) return;
            if (streamLine.sharedMaterial != streamMaterialOverride)
                streamLine.sharedMaterial = streamMaterialOverride;
        }

        int GetColorId()
        {
            string prop = string.IsNullOrEmpty(streamColorProperty) ? "_Color" : streamColorProperty;
            if (prop != cachedColorProp)
            {
                cachedColorProp = prop;
                cachedColorId = Shader.PropertyToID(prop);
            }
            return cachedColorId;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();
            MigrateLegacyTarget();
            ApplyStreamMaterial();
            ApplyColors();
        }

        void OnDrawGizmosSelected()
        {
            if (pathPointsLocal == null || pathPointsLocal.Count == 0) return;
            Vector3 last = transform.TransformPoint(pathPointsLocal[pathPointsLocal.Count - 1]);
            Vector2 dir = rayDirection.sqrMagnitude < 1e-6f ? Vector2.down : rayDirection.normalized;
            Gizmos.color = new Color(0.45f, 0.85f, 1f, 0.4f);
            Gizmos.DrawLine(last, last + (Vector3)(dir * maxDistance));
        }
#endif
    }
}
