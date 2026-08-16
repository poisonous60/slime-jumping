using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SlimeJumping.UI
{
    /// UI-side deformer for a jelly menu button: slices the button image into
    /// vertical strips (Sutherland–Hodgman triangle clipping, sliced-sprite safe)
    /// and displaces every vertex by the world-space wave sampled from its
    /// MenuButtonJelly. Overlay canvas contract: TransformPoint == screen px, so
    /// screen↔world runs through the arena camera. Rebuilds only while the jelly
    /// is awake (SetDirty from the jelly's tick).
    public sealed class MenuButtonJellyVisual : BaseMeshEffect
    {
        MenuButtonJelly jelly;
        UnityEngine.Camera cam;
        MenuSlimeArenaConfig cfg;

        readonly List<UIVertex> input = new List<UIVertex>();
        readonly List<UIVertex> output = new List<UIVertex>();
        readonly List<UIVertex> polyBuf = new List<UIVertex>();
        readonly List<UIVertex> clipBuf = new List<UIVertex>();

        public void Bind(MenuButtonJelly owner, UnityEngine.Camera arenaCamera, MenuSlimeArenaConfig config)
        {
            jelly = owner;
            cam = arenaCamera;
            cfg = config;
            if (graphic != null) graphic.SetVerticesDirty();
        }

        public void SetDirty()
        {
            if (graphic != null) graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || jelly == null || cam == null) return;

            input.Clear();
            vh.GetUIVertexStream(input);
            var rt = (RectTransform)transform;
            Rect rect = rt.rect;
            int strips = Mathf.Max(1, cfg.buttonStrips);

            output.Clear();
            for (int i = 0; i < input.Count; i += 3)
            {
                for (int s = 0; s < strips; s++)
                {
                    float x0 = Mathf.Lerp(rect.xMin, rect.xMax, (float)s / strips);
                    float x1 = Mathf.Lerp(rect.xMin, rect.xMax, (float)(s + 1) / strips);
                    polyBuf.Clear();
                    polyBuf.Add(input[i]);
                    polyBuf.Add(input[i + 1]);
                    polyBuf.Add(input[i + 2]);
                    ClipX(polyBuf, clipBuf, x0, true);
                    ClipX(clipBuf, polyBuf, x1, false);
                    for (int j = 1; j < polyBuf.Count - 1; j++)
                    {
                        output.Add(polyBuf[0]);
                        output.Add(polyBuf[j]);
                        output.Add(polyBuf[j + 1]);
                    }
                }
            }

            float pxPerWorld = cam.pixelHeight / (2f * cam.orthographicSize);
            for (int i = 0; i < output.Count; i++)
            {
                var v = output[i];
                Vector3 screen = rt.TransformPoint(v.position);
                Vector2 off = jelly.SampleWorldOffset(cam.ScreenToWorldPoint(screen));
                if (off.sqrMagnitude > 1e-12f)
                {
                    v.position += rt.InverseTransformVector(new Vector3(off.x, off.y, 0f) * pxPerWorld);
                    output[i] = v;
                }
            }

            vh.Clear();
            vh.AddUIVertexTriangleStream(output);
        }

        static void ClipX(List<UIVertex> src, List<UIVertex> dst, float x, bool keepGreater)
        {
            dst.Clear();
            int n = src.Count;
            for (int i = 0; i < n; i++)
            {
                UIVertex a = src[i];
                UIVertex b = src[(i + 1) % n];
                bool inA = keepGreater ? a.position.x >= x : a.position.x <= x;
                bool inB = keepGreater ? b.position.x >= x : b.position.x <= x;
                if (inA) dst.Add(a);
                if (inA != inB)
                {
                    float t = Mathf.InverseLerp(a.position.x, b.position.x, x);
                    dst.Add(LerpVertex(a, b, t));
                }
            }
        }

        static UIVertex LerpVertex(UIVertex a, UIVertex b, float t)
        {
            var v = a;
            v.position = Vector3.Lerp(a.position, b.position, t);
            v.uv0 = Vector4.Lerp(a.uv0, b.uv0, t);
            v.color = Color32.Lerp(a.color, b.color, t);
            return v;
        }
    }
}
