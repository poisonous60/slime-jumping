using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.Background
{
    // Continuously drifts cloud leaves along a wind vector. When a cloud exits
    // the bounds box, it wraps to the opposite edge with a randomized Y so the
    // distribution genuinely changes over time instead of looping identically.
    //
    // Set boundsSize wider than the camera view — the wrap happens off-screen,
    // so the player only sees a continuous slow drift, never a pop.
    //
    // Default leafDepth = 3 matches CloudField → CloudBackground → CloudsN → Cloud_NN.
    [ExecuteAlways]
    public class CloudWindDrift : MonoBehaviour
    {
        [Tooltip("Hierarchy depth from this transform to the leaves to drift. " +
                 "3 = great-grandchildren (CloudField → CloudBackground → CloudsN → Cloud_NN).")]
        [SerializeField] int leafDepth = 3;

        [Tooltip("World-space wind vector. Magnitude is normalized — speed lives in windSpeed.")]
        [SerializeField] Vector2 windDirection = new Vector2(1f, 0f);

        [Tooltip("Drift speed in units per second.")]
        [SerializeField] float windSpeed = 0.4f;

        [Tooltip("Local-space rectangle around this transform that clouds wrap inside. " +
                 "Make this wider than the camera view so wraps happen off-screen.")]
        [SerializeField] Vector2 boundsSize = new Vector2(80f, 20f);

        [Tooltip("On wrap, re-randomize the cloud's perpendicular axis so the pattern keeps shifting.")]
        [SerializeField] bool randomizeOnWrap = true;

        [Tooltip("Per-cloud speed jitter (multiplied with windSpeed). 0 = all clouds same speed.")]
        [Range(0f, 1f)]
        [SerializeField] float speedJitter = 0.3f;

        [Tooltip("Run drift in Edit mode too (handy for previewing in Scene view).")]
        [SerializeField] bool driftInEditMode = false;

        readonly List<Transform> _leaves = new();
        readonly List<float>     _speedScale = new();
        bool _initialized;

#if UNITY_EDITOR
        double _lastEditorTime = -1;
#endif

        void OnEnable()
        {
            _initialized = false;
#if UNITY_EDITOR
            _lastEditorTime = -1;
#endif
        }

        void Update()
        {
            if (!Application.isPlaying && !driftInEditMode) return;

            if (!_initialized) Init();
            if (_leaves.Count == 0) return;

            float dt = ComputeDeltaTime();
            if (dt <= 0f) return;

            Vector3 dir = ((Vector3)windDirection.normalized);
            if (dir.sqrMagnitude < 1e-6f) return;

            Vector3 center = transform.position;
            float halfX = boundsSize.x * 0.5f;
            float halfY = boundsSize.y * 0.5f;
            float minX = center.x - halfX, maxX = center.x + halfX;
            float minY = center.y - halfY, maxY = center.y + halfY;

            for (int i = 0; i < _leaves.Count; i++)
            {
                var t = _leaves[i];
                if (t == null) continue;

                Vector3 p = t.position + dir * (windSpeed * _speedScale[i] * dt);

                bool wrapped = false;
                if (dir.x > 0 && p.x > maxX) { p.x = minX; wrapped = true; }
                else if (dir.x < 0 && p.x < minX) { p.x = maxX; wrapped = true; }
                if (dir.y > 0 && p.y > maxY) { p.y = minY; wrapped = true; }
                else if (dir.y < 0 && p.y < minY) { p.y = maxY; wrapped = true; }

                if (wrapped && randomizeOnWrap)
                {
                    // Re-randomize the perpendicular axis AND jitter the wind axis a bit,
                    // so when many clouds wrap in the same frame they don't pile up in a column.
                    if (Mathf.Abs(dir.x) > Mathf.Abs(dir.y))
                    {
                        p.y = Random.Range(minY, maxY);
                        p.x += Random.Range(0f, boundsSize.x * 0.15f) * Mathf.Sign(dir.x);
                    }
                    else
                    {
                        p.x = Random.Range(minX, maxX);
                        p.y += Random.Range(0f, boundsSize.y * 0.15f) * Mathf.Sign(dir.y);
                    }
                }

                t.position = p;
            }
        }

        float ComputeDeltaTime()
        {
            if (Application.isPlaying) return Time.deltaTime;

#if UNITY_EDITOR
            double now = UnityEditor.EditorApplication.timeSinceStartup;
            if (_lastEditorTime < 0)
            {
                _lastEditorTime = now;
                return 0f; // First tick: no motion, just calibrate.
            }
            float dt = (float)(now - _lastEditorTime);
            _lastEditorTime = now;
            // Clamp huge jumps from editor refocus / inspector pumping bursts —
            // without this, a single burst can wrap every cloud into one column.
            return Mathf.Min(dt, 0.05f);
#else
            return 0f;
#endif
        }

        void Init()
        {
            _leaves.Clear();
            _speedScale.Clear();
            Collect(transform, 0);
            for (int i = 0; i < _leaves.Count; i++)
            {
                _speedScale.Add(1f + Random.Range(-speedJitter, speedJitter));
            }
            _initialized = true;
        }

        void Collect(Transform t, int currentDepth)
        {
            if (currentDepth == leafDepth)
            {
                _leaves.Add(t);
                return;
            }
            for (int i = 0; i < t.childCount; i++)
            {
                Collect(t.GetChild(i), currentDepth + 1);
            }
        }

        public void ResetDistribution()
        {
            Init();
            if (_leaves.Count == 0) return;

            Vector3 center = transform.position;
            float minX = center.x - boundsSize.x * 0.5f;
            float maxX = center.x + boundsSize.x * 0.5f;
            float minY = center.y - boundsSize.y * 0.5f;
            float maxY = center.y + boundsSize.y * 0.5f;

            for (int i = 0; i < _leaves.Count; i++)
            {
                var t = _leaves[i];
                if (t == null) continue;
                Vector3 p = t.position;
                p.x = Random.Range(minX, maxX);
                p.y = Random.Range(minY, maxY);
                t.position = p;
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireCube(transform.position, new Vector3(boundsSize.x, boundsSize.y, 0.1f));

            // Wind direction arrow
            Vector3 dir = (Vector3)windDirection.normalized;
            if (dir.sqrMagnitude > 1e-6f)
            {
                Gizmos.color = Color.cyan;
                Vector3 from = transform.position;
                Vector3 to = from + dir * 3f;
                Gizmos.DrawLine(from, to);
                Gizmos.DrawSphere(to, 0.15f);
            }
        }
    }
}
