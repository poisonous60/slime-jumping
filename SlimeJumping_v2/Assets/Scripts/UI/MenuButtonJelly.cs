using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SlimeJumping.Controller;

namespace SlimeJumping.UI
{
    /// Jelly menu button: a parametric rounded-rect contour (CCW, ink-on-left)
    /// drives the shared jelly collider/wave in world space; the on-screen button
    /// image is deformed by MenuButtonJellyVisual, which samples this wave through
    /// SampleWorldOffset. The label text stays rigid — only the plate wobbles.
    [DefaultExecutionOrder(-100)] // 속성은 상속 보장이 없어 구체 클래스에도 명시.
    public sealed class MenuButtonJelly : MenuJellyBase
    {
        MenuButtonJellyVisual visual;

        public static MenuButtonJelly Create(Transform parent, string name, int layer,
            Vector2 worldMin, Vector2 worldMax, MenuSlimeArenaConfig cfg,
            SlimeController slime, Graphic buttonGraphic, UnityEngine.Camera arenaCamera)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = (worldMin + worldMax) * 0.5f;
            var jelly = go.AddComponent<MenuButtonJelly>();

            Vector2 half = (worldMax - worldMin) * 0.5f;
            float radius = cfg.buttonCornerRadius * half.y * 2f;
            if (half.x <= 0f || half.y <= 0f ||
                !jelly.InitJelly(cfg, slime, new[] { RoundedRect(half, radius) }, 1f))
            {
                Destroy(go);
                return null;
            }

            if (buttonGraphic != null)
            {
                jelly.visual = buttonGraphic.gameObject.AddComponent<MenuButtonJellyVisual>();
                jelly.visual.Bind(jelly, arenaCamera, cfg);
            }
            return jelly;
        }

        // CCW = 진행방향 좌측이 내부 (winding 계약). 직선 구간 세분은 wave.Build 몫.
        static Vector2[] RoundedRect(Vector2 half, float radius)
        {
            radius = Mathf.Clamp(radius, 0f, Mathf.Min(half.x, half.y));
            const int arcSteps = 6;
            var pts = new List<Vector2>();
            void Arc(Vector2 c, float a0, float a1)
            {
                for (int i = 0; i <= arcSteps; i++)
                {
                    float a = Mathf.Deg2Rad * Mathf.Lerp(a0, a1, (float)i / arcSteps);
                    pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
                }
            }
            Arc(new Vector2(half.x - radius, -half.y + radius), -90f, 0f);
            Arc(new Vector2(half.x - radius, half.y - radius), 0f, 90f);
            Arc(new Vector2(-half.x + radius, half.y - radius), 90f, 180f);
            Arc(new Vector2(-half.x + radius, -half.y + radius), 180f, 270f);
            return pts.ToArray();
        }

        public Vector2 SampleWorldOffset(Vector2 worldPoint)
        {
            Vector2 local = transform.InverseTransformPoint(worldPoint);
            Vector2 off = wave.SampleOffset(local, cfg.visualInfluenceRadius,
                                            cfg.mappingArcWindow, cfg.maxDisplacement);
            off += BodyOffset(local);
            float maxOff = cfg.maxDisplacement;
            if (off.sqrMagnitude > maxOff * maxOff) off *= maxOff / off.magnitude;
            return transform.TransformVector(off);
        }

        protected override void ApplyVisual(float maxOff)
        {
            if (visual != null) visual.SetDirty();
        }

        protected override void ResetVisual()
        {
            if (visual != null) visual.SetDirty();
        }
    }
}
