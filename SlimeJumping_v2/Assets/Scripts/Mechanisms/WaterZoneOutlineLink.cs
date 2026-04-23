using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SlimeJumping.Mechanisms
{
    /// Keeps this WaterZone_v2's outline points in sync with a source zone's.
    /// Intended use: a laid-flat (XZ-plane) decorative/visual water zone mirrors
    /// the authoritative vertical (XY-plane) zone's polygon, so editing the
    /// source automatically reshapes the horizontal pool. Points are copied
    /// verbatim as Vector2 — this object's transform rotation (e.g. 90° around
    /// X) re-maps the shared local XY plane into whatever orientation is needed.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WaterZone_v2))]
    [AddComponentMenu("SlimeJumping/Mechanisms/Water Zone Outline Link")]
    public class WaterZoneOutlineLink : MonoBehaviour
    {
        [Tooltip("Source WaterZone_v2 whose outline this one mirrors.")]
        [SerializeField] WaterZone_v2 source;

        [Tooltip("Uniform scale applied to source points before copying. " +
                 "Useful when the horizontal footprint should be a shrunken/" +
                 "enlarged version of the vertical cross-section.")]
        [SerializeField] Vector2 scale = Vector2.one;

        [Tooltip("Offset added after scaling (source-local space).")]
        [SerializeField] Vector2 offset = Vector2.zero;

        WaterZone_v2 self;
        int lastSourceHash;
        Vector2 lastScale;
        Vector2 lastOffset;

        void OnEnable()
        {
            self = GetComponent<WaterZone_v2>();
            lastSourceHash = 0;
            SyncIfNeeded();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                self = GetComponent<WaterZone_v2>();
                lastSourceHash = 0;
                SyncIfNeeded();
            };
        }
#endif

        void LateUpdate() { SyncIfNeeded(); }

        void SyncIfNeeded()
        {
            if (self == null) self = GetComponent<WaterZone_v2>();
            if (self == null || source == null || source == self) return;

            int count = source.OutlinePointCount;
            if (count < 3) return;

            int hash = ComputeSourceHash(count);
            if (hash == lastSourceHash && scale == lastScale && offset == lastOffset) return;

            var buf = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                Vector2 p = source.GetOutlinePointLocal(i);
                buf[i] = new Vector2(p.x * scale.x, p.y * scale.y) + offset;
            }

            self.ReplaceOutlinePoints(buf);
            // RebuildRuntime only touches shape/collider/mesh buffers — it is
            // safe in edit mode, and lets the SceneView preview update live as
            // the source outline is dragged.
            self.RebuildRuntime();
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(self);
                SceneView.RepaintAll();
            }
#endif

            lastSourceHash = hash;
            lastScale = scale;
            lastOffset = offset;
        }

        int ComputeSourceHash(int count)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + count;
                for (int i = 0; i < count; i++)
                {
                    Vector2 p = source.GetOutlinePointLocal(i);
                    h = h * 31 + p.x.GetHashCode();
                    h = h * 31 + p.y.GetHashCode();
                }
                return h;
            }
        }
    }
}
