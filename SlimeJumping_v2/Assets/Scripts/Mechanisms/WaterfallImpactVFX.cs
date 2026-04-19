using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    /// Stream path defined as a list of local-space offset points from this GameObject's
    /// transform. The LineRenderer connects them in order, and from the LAST point a ray
    /// is cast along rayDirection — the hit point on targetCollider becomes the final
    /// line vertex. A splash PS is positioned at the hit.
    ///
    /// Editor scene handles (custom inspector): yellow sphere per point, shift-click an
    /// edge to insert, ctrl-click a point to delete, drag to move.
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
        [SerializeField] Collider2D targetCollider;
        [SerializeField] Vector2 rayDirection = Vector2.down;
        [Min(0.01f)] [SerializeField] float maxDistance = 25f;
        [SerializeField] LayerMask hitLayers = ~0;

        [Header("Stream (LineRenderer)")]
        [SerializeField] LineRenderer streamLine;
        [ColorUsage(true, true)]
        [SerializeField] Color streamColor = new Color(0.45f, 0.85f, 1f, 1f);
        [SerializeField] bool applyStreamColor = true;
        [Tooltip("라인 Z를 transform.z에서 이만큼 당김. 음수 = 카메라 쪽(2.5D에서 슬라임 앞 렌더).")]
        [SerializeField] float streamZOffset = -0.5f;

        [Header("Splash (ParticleSystem — at hit point)")]
        [SerializeField] ParticleSystem splash;
        [ColorUsage(true, true)]
        [SerializeField] Color splashColor = new Color(0.45f, 0.85f, 1f, 1f);
        [SerializeField] bool applySplashColor = true;

        static readonly RaycastHit2D[] rayBuffer = new RaycastHit2D[8];
        static ContactFilter2D rayFilter;
        static readonly int IdColor = Shader.PropertyToID("_Color");
        MaterialPropertyBlock mpb;

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
            ApplyColors();
        }

        void LateUpdate()
        {
            if (targetCollider == null || pathPointsLocal == null || pathPointsLocal.Count == 0)
            {
                SetVisualActive(false);
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
            if (n <= 0)
            {
                SetVisualActive(false);
                return;
            }

            // 슬라임 같은 중간 콜라이더에 의해 "가려짐"으로 오판되지 않도록, 가장 가까운 hit이
            // target이 아니더라도 hits 중 target이 있으면 그 점을 사용. 엄격한 폐색 판정이
            // 필요하면 hitLayers로 target 레이어만 허용하도록 세팅.
            Vector2 hit = default;
            Vector2 normal = default;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                if (rayBuffer[i].collider == targetCollider)
                {
                    hit = rayBuffer[i].point;
                    normal = rayBuffer[i].normal;
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                SetVisualActive(false);
                return;
            }

            SetVisualActive(true);
            UpdateStreamLine(hit);
            UpdateSplashTransform(hit, normal);
        }

        void UpdateStreamLine(Vector2 hit)
        {
            if (streamLine == null) return;

            float z = transform.position.z + streamZOffset;
            int pathCount = pathPointsLocal.Count;
            int total = pathCount + 1;
            if (streamLine.positionCount != total) streamLine.positionCount = total;

            for (int i = 0; i < pathCount; i++)
            {
                Vector2 w = transform.TransformPoint(pathPointsLocal[i]);
                streamLine.SetPosition(i, new Vector3(w.x, w.y, z));
            }
            streamLine.SetPosition(total - 1, new Vector3(hit.x, hit.y, z));
        }

        void UpdateSplashTransform(Vector2 hit, Vector2 normal)
        {
            if (splash == null) return;
            var st = splash.transform;
            st.position = new Vector3(hit.x, hit.y, st.position.z);
            if (normal.sqrMagnitude > 1e-6f)
                st.rotation = Quaternion.LookRotation(Vector3.forward, normal);
        }

        void SetVisualActive(bool on)
        {
            if (streamLine != null && streamLine.enabled != on)
                streamLine.enabled = on;

            if (splash != null)
            {
                if (on && !splash.isPlaying) splash.Play(true);
                else if (!on && splash.isPlaying) splash.Stop(true, ParticleSystemStopBehavior.StopEmitting);
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

                if (mpb == null) mpb = new MaterialPropertyBlock();
                streamLine.GetPropertyBlock(mpb);
                mpb.SetColor(IdColor, streamColor);
                streamLine.SetPropertyBlock(mpb);
            }
            if (applySplashColor && splash != null)
            {
                var main = splash.main;
                main.startColor = new ParticleSystem.MinMaxGradient(splashColor);
            }
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();
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
