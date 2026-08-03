using UnityEngine;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // Cut body = union of CircleCollider2D lobes filling K = disc(EffRadius) INTERSECT wound_j,
        // where a wound is the membrane disc (b > 0) or the cut halfplane. Every lobe is anchored on
        // dK and clipped by the other constraints, so the union stays inside K: the slime can sink,
        // never float. Extra lobes are off while uncut.

        public struct CutWound
        {
            public Vector2 normal;   // world unit, toward the REMOVED side
            public float off;        // plane offset from the body centre (world)
            public float b;          // membrane apex past the plane; <= 0 = flat / dented
            public float rho0;       // fixed rim circle radius (world)
        }

        private const int CutWoundMax = 2;
        private readonly CutWound[] cutWounds = new CutWound[CutWoundMax];
        private int cutWoundCount;

        private CircleCollider2D[] bodyColliders;
        private readonly Vector2[] lobeCentres = new Vector2[8];
        private readonly float[] lobeRadii = new float[8];
        private int lobeCount;

        /// <summary>Every collider the body is made of — ignore/overlap tests must cover all.</summary>
        public CircleCollider2D[] BodyColliders => bodyColliders;
        public CircleCollider2D BodyCollider => circleCollider;

        /// <summary>Live lobes; 1 while uncut. World units, centres relative to rb.position.</summary>
        public int CutLobeCount => Mathf.Max(1, lobeCount);
        public Vector2 GetCutLobeCentre(int i) => lobeCount > 0 ? lobeCentres[i] : Vector2.zero;
        public float GetCutLobeRadius(int i) => lobeCount > 0 ? lobeRadii[i] : SlimeRadius;

        /// <summary>Pushed by SlimeSplitSequence; count 0 restores the plain circle.</summary>
        public void SetCutWounds(CutWound[] wounds, int count)
        {
            cutWoundCount = Mathf.Clamp(count, 0, CutWoundMax);
            for (int i = 0; i < cutWoundCount; i++) cutWounds[i] = wounds[i];
            RebuildCutShape();
        }

        public void ClearCutWounds()
        {
            if (cutWoundCount == 0) return;
            cutWoundCount = 0;
            RebuildCutShape();
        }

        // Runs before IgnoredColliderList so passthrough can ignore every lobe.
        private void CreateCutLobes()
        {
            int extra = Mathf.Clamp(settings.cutColliderCount, 1, 8) - 1;
            bodyColliders = new CircleCollider2D[extra + 1];
            bodyColliders[0] = circleCollider;
            for (int i = 0; i < extra; i++)
            {
                var c = gameObject.AddComponent<CircleCollider2D>();
                c.sharedMaterial = circleCollider.sharedMaterial;
                c.excludeLayers = circleCollider.excludeLayers;
                c.includeLayers = circleCollider.includeLayers;
                c.layerOverridePriority = circleCollider.layerOverridePriority;
                c.radius = 0.001f;
                c.enabled = false;   // off until a wound needs it
                bodyColliders[i + 1] = c;
            }
            RebuildCutShape();
        }

        // kind 0 = disc(P, R), kind 1 = halfplane {p.n <= a}. Slot 0 is the body disc.
        private readonly int[] conKind = new int[1 + CutWoundMax];
        private readonly Vector2[] conP = new Vector2[1 + CutWoundMax];
        private readonly float[] conR = new float[1 + CutWoundMax];
        private int conCount;

        private void BuildConstraints(float R)
        {
            conKind[0] = 0; conP[0] = Vector2.zero; conR[0] = R;
            conCount = 1;
            for (int i = 0; i < cutWoundCount; i++)
            {
                CutWound w = cutWounds[i];
                float a = w.off + w.b;
                if (w.b > 1e-5f)
                {
                    float rc = (w.rho0 * w.rho0 + w.b * w.b) / (2f * w.b);
                    if (rc < 1e4f * R)
                    {
                        conKind[conCount] = 0;
                        conP[conCount] = w.normal * (w.off - rc + w.b);
                        conR[conCount] = rc;
                        conCount++;
                        continue;
                    }
                }
                conKind[conCount] = 1;
                conP[conCount] = w.normal;
                conR[conCount] = a;
                conCount++;
            }
        }

        // Centre to constraint i along dir; +inf when it cannot bound.
        private float ConExit(int i, Vector2 dir)
        {
            if (conKind[i] == 1)
            {
                float dn = Vector2.Dot(dir, conP[i]);
                return dn > 1e-6f ? conR[i] / dn : float.MaxValue;
            }
            float pu = Vector2.Dot(conP[i], dir);
            float disc = conR[i] * conR[i] - conP[i].sqrMagnitude + pu * pu;
            return disc <= 0f ? float.MaxValue : pu + Mathf.Sqrt(disc);
        }

        // Largest radius fitting constraint i, for a circle tangent at q along -nu.
        private float ConMaxRadius(int i, Vector2 q, Vector2 nu)
        {
            if (conKind[i] == 1)
            {
                float den = 1f - Vector2.Dot(nu, conP[i]);
                return den < 1e-6f ? float.MaxValue : (conR[i] - Vector2.Dot(q, conP[i])) / den;
            }
            Vector2 w = q - conP[i];
            float d = conR[i] - Vector2.Dot(w, nu);
            float num = conR[i] * conR[i] - w.sqrMagnitude;
            // d <= 0 forces |w| >= conR[i], so q sits on or past the rim: the squared form degenerates
            // to 0 <= 0 and drops the r <= conR[i] premise it was derived under.
            if (d <= 0f) return num > 0f ? conR[i] : 0f;
            float r = num / (2f * d);
            // conR[i] - r = |w - conR[i]*nu|^2 / 2d >= 0, so the clamp only absorbs float blow-up.
            return r < conR[i] ? r : conR[i];
        }

        // Inscribed circle anchored where the ray along dir leaves K.
        private bool LobeAlong(Vector2 dir, out Vector2 centre, out float radius)
        {
            int active = 0;
            float best = float.MaxValue;
            for (int i = 0; i < conCount; i++)
            {
                float e = ConExit(i, dir);
                if (e < best) { best = e; active = i; }
            }
            centre = Vector2.zero; radius = 0f;
            if (best >= float.MaxValue || best <= 0f) return false;

            Vector2 q = dir * best;
            Vector2 nu;
            if (conKind[active] == 1) nu = conP[active];
            else
            {
                Vector2 w = q - conP[active];
                float l = w.magnitude;
                if (l < 1e-6f) return false;
                nu = w / l;
            }

            float r = conKind[active] == 0 ? conR[active] : float.MaxValue;
            for (int i = 0; i < conCount; i++)
            {
                if (i == active) continue;
                float ri = ConMaxRadius(i, q, nu);
                if (ri < r) r = ri;
            }
            if (!(r > 1e-5f)) return false;
            radius = r;
            centre = q - nu * r;
            return true;
        }

        /// <summary>Re-authors the lobe colliders and hands the same circles to the probe.</summary>
        private void RebuildCutShape()
        {
            if (bodyColliders == null || circleCollider == null) return;

            float R = settings.slimeRadius * bodyScaleRatio * pinchColliderScale;
            int want = Mathf.Clamp(settings.cutColliderCount,
                                   1, bodyColliders.Length);
            bool cut = cutWoundCount > 0 && want > 1 && BuildLobes(R, want);

            if (!cut)
            {
                // Healed / uncut: base circle is the body, the rest hide inside it.
                lobeCount = 1;
                lobeCentres[0] = Vector2.zero;
                lobeRadii[0] = R;
            }

            float s = transform.localScale.x;
            float inv = Mathf.Abs(s) > 0.001f ? 1f / s : 1f;
            bool setChanged = false;
            for (int i = 0; i < bodyColliders.Length; i++)
            {
                var c = bodyColliders[i];
                if (c == null) continue;
                // Unused lobes go OFF: a live collider buried in the body still enters and leaves
                // every zone trigger. Slot 0 is the prefab's collider and always stays on.
                bool live = i == 0 || i < lobeCount;
                if (i > 0 && c.enabled != live)
                {
                    c.enabled = live;
                    setChanged = true;
                }
                if (!live) continue;

                Vector2 off = lobeCentres[i] * inv;
                float rad = lobeRadii[i] * inv;
                // Writing a shape rebuilds the Box2D fixture — skip unchanged frames.
                if (Mathf.Abs(rad - lastRadius[i]) < 1e-6f
                    && (off - lastOffset[i]).sqrMagnitude < 1e-12f) continue;
                lastRadius[i] = rad;
                lastOffset[i] = off;
                c.offset = off;
                c.radius = rad;
            }
            // IgnoreCollision is per pair and does not survive a disable.
            if (setChanged && ignoredColliderList != null) ignoredColliderList.ReapplySuppression();

            if (surfaceProbe != null)
            {
                if (lobeCount > 1) surfaceProbe.SetBodyCircles(lobeCentres, lobeRadii, lobeCount);
                else surfaceProbe.ClearBodyCircles();
            }
        }

        // Anchors even by arc length along dK, phase pinned to the newest wound so one lobe lands
        // tangent to the cut face — that is what seats flat ground at the drawn height.
        private bool BuildLobes(float R, int want)
        {
            if (R <= 1e-4f) return false;
            BuildConstraints(R);

            int m = Mathf.Clamp(settings.cutColliderArcSamples, 16, 128);
            Vector2 n0 = cutWounds[cutWoundCount - 1].normal;
            float base0 = Mathf.Atan2(n0.y, n0.x);

            Vector2 prev = Vector2.zero;
            float total = 0f;
            for (int i = 0; i <= m; i++)
            {
                float t = base0 + (2f * Mathf.PI * i) / m;
                Vector2 u = new Vector2(Mathf.Cos(t), Mathf.Sin(t));
                float e = float.MaxValue;
                for (int k = 0; k < conCount; k++) e = Mathf.Min(e, ConExit(k, u));
                if (e >= float.MaxValue) return false;
                Vector2 p = u * e;
                if (i > 0) total += (p - prev).magnitude;
                arcCum[i] = total;
                prev = p;
            }
            if (total <= 1e-5f) return false;

            lobeCount = 0;
            for (int k = 0; k < want; k++)
            {
                float target = (total * k) / want;
                int i = 0;
                while (i < m && arcCum[i + 1] < target) i++;
                float t = base0 + (2f * Mathf.PI * i) / m;
                if (LobeAlong(new Vector2(Mathf.Cos(t), Mathf.Sin(t)),
                              out Vector2 c, out float r))
                {
                    lobeCentres[lobeCount] = c;
                    lobeRadii[lobeCount] = r;
                    lobeCount++;
                }
            }
#if UNITY_EDITOR
            for (int i = 0; i < lobeCount; i++)
                Debug.Assert(lobeCentres[i].magnitude + lobeRadii[i] <= R + 1e-4f,
                    "[CutShape] lobe escapes EffRadius — broadphase, CCD and launch clearance assume it does not");
#endif
            return lobeCount > 1;
        }

        private readonly float[] arcCum = new float[129];
        private readonly Vector2[] lastOffset = new Vector2[8];
        private readonly float[] lastRadius = new float[8];
    }
}
