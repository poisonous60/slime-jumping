using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;
using SlimeJumping.InputHandling;

namespace SlimeJumping.Platforms.Bell
{
    // Hanging bell, rebuilt around the contact frame. Touching it swings the bell, bounces the slime
    // and — if the touch was fast enough — sounds it, all in the same tick. The swing is what the hit
    // looks like, never what decides it: the physics bell decided all three from the clapper reaching
    // the wall, which put the sound 0.3-2s behind the touch and let a hit that arrived on the wrong
    // phase produce nothing at all.
    //
    // Three rules the tuning has to keep. Nothing has a direction condition — where the contact sits
    // decides which way the bell goes, never which way the slime was travelling, so a hit straight
    // down counts for as much as a flat one. Every contact moves BOTH: the slime bounces off an
    // elastic collision with the moving flank and can never come to rest on a bell, and the bell
    // always takes at least its soft swing. Speed decides only whether it SOUNDS, plus where in a
    // narrow soft-to-full window the swing lands — a fast hit must not read as a different bell.
    //
    // Holding the charge key is the ONLY thing that may keep a slime on a bell — that slime leaves
    // under its own launch instead of being thrown, since cancelling a charge is forbidden outright.
    // Anything else is pushed off every tick it is present, and presence counts the carrier's rider
    // as well as the contact list, because a seated slime often reports no contact at all.
    //
    // Drops next to the existing SwingingBell rather than replacing it: that component stays on the
    // object, switched off, because BellSoundWave and BellRevealCamera hold typed references to it.
    // This drives its ring surface so the hidden-platform reveal still fires — on contact now.
    [DisallowMultipleComponent]
    public class SwingingBellV2 : PlatformMotor
    {
        [SerializeField] BellV2Config configOverride;
        [Tooltip("The bell mesh transform. All geometry in the config is measured in ITS local space. " +
                 "Empty = the first child.")]
        [SerializeField] Transform bellVisual;
        [Tooltip("The clapper mesh transform, a child of the bell visual. Empty = found by name.")]
        [SerializeField] Transform clapperVisual;
        [Tooltip("The physics bell this replaces, kept only so the reveal keeps its reference. Empty = " +
                 "the one on this GameObject. It is switched off at startup and never simulates.")]
        [SerializeField] SwingingBell revealRelay;

        BellV2Config cfg;

        Vector2 pivotWorld;
        Vector2 restPos;
        float restRot;
        float visualScale;
        Vector3 clapperRestLocalPos;
        Quaternion clapperRestLocalRot;

        float phi1, phi1Dot;
        float phi2, phi2Dot;
        bool onWall;
        bool warnedPeriodFloor;
        bool warnedSwingWindow;

        float retriggerTimer;
        float riderTorque;
        bool occupied;
        bool riding;
        int nextClip;
        int ringCount;

        readonly ContactPoint2D[] contactScratch = new ContactPoint2D[8];
        readonly List<Collider2D> ownColliders = new List<Collider2D>();
        readonly Vector2[] velScratch = new Vector2[3];
        SlimeController trackedSlime;
        SlimeInputReader input;
        Vector2 prevSlimeVel;
        Vector2 riderArrivalVel;
        Vector2 lastRiderPos;

        FieldInfo relayRingResult, relayImpactCount, relayStruck;

        /// <summary>Rings so far. Read-only view for debug tooling.</summary>
        public int RingCount => ringCount;
        /// <summary>Bell tilt in degrees, + = counter-clockwise.</summary>
        public float BellAngle => phi1 * Mathf.Rad2Deg;
        /// <summary>The config actually in use after override/Resources resolution.</summary>
        public BellV2Config Config => cfg;

        protected override void Awake()
        {
            base.Awake();   // kinematic + interpolated Rigidbody2D

            cfg = BellV2Config.Resolve(configOverride);
            restPos = rb.position;
            restRot = rb.rotation;
            rb.GetAttachedColliders(ownColliders);

            if (bellVisual == null) bellVisual = transform.childCount > 0 ? transform.GetChild(0) : transform;
            if (clapperVisual == null && bellVisual != transform) clapperVisual = bellVisual.Find("bell_clapper");
            visualScale = Mathf.Abs(bellVisual.lossyScale.y);
            if (visualScale < 0.0001f) visualScale = 1f;

            pivotWorld = bellVisual.TransformPoint(new Vector3(0f, cfg.pivotLocalY, 0f));
            WarnOnInvertedGeometry();

            if (clapperVisual != null)
            {
                clapperRestLocalPos = clapperVisual.localPosition;
                clapperRestLocalRot = clapperVisual.localRotation;
            }

            BindRevealRelay();
            WarnOnMotorOrder();
        }

        // Riders bind to the FIRST PlatformMotor on the object, and a switched-off one still counts.
        void WarnOnMotorOrder()
        {
            var motors = GetComponents<PlatformMotor>();
            if (motors.Length == 0 || motors[0] == this) return;
            Debug.LogError($"[SwingingBellV2] '{name}': {motors[0].GetType().Name} sits above this " +
                           "component, so the slime binds to that one instead — it will not be carried " +
                           "by the swing. Move Swinging Bell V2 above it in the Inspector.", this);
        }

        // ---- Reveal relay ----

        // Two PlatformMotors on one body would both call MovePosition, hence switching it off.
        void BindRevealRelay()
        {
            if (revealRelay == null) revealRelay = GetComponent<SwingingBell>();
            if (revealRelay == null) return;

            revealRelay.enabled = false;

            const BindingFlags priv = BindingFlags.NonPublic | BindingFlags.Instance;
            relayRingResult = typeof(SwingingBell).GetField("lastRingResult", priv);
            relayImpactCount = typeof(SwingingBell).GetField("impactCount", priv);
            relayStruck = typeof(SwingingBell).GetField("Struck", priv);

            if (relayRingResult != null && relayImpactCount != null && relayStruck != null) return;

            Debug.LogError($"[SwingingBellV2] '{name}': SwingingBell no longer has the fields this " +
                           "reaches into (lastRingResult / impactCount / Struck) — they were renamed or " +
                           "removed. Hidden platforms will not be revealed until this is updated.", this);
            relayRingResult = relayImpactCount = relayStruck = null;
        }

        // Both listeners gate on LastRingResult and dedupe on ImpactCount, so both precede the event.
        void PulseRevealRelay()
        {
            if (relayStruck == null) return;

            relayRingResult.SetValue(revealRelay, $"PLAYED — SwingingBellV2 ring #{ringCount}");
            relayImpactCount.SetValue(revealRelay, (int)relayImpactCount.GetValue(revealRelay) + 1);

            var handler = relayStruck.GetValue(revealRelay) as System.Action<float>;
            if (handler != null) handler(Mathf.Abs(phi1Dot));
        }

        // ---- Derived physical quantities ----

        float ArmBellCom => Mathf.Max(0.0001f, (cfg.pivotLocalY - cfg.bellComLocalY) * visualScale);
        float ArmClapperAxis => (cfg.pivotLocalY - cfg.clapperAxisLocalY) * visualScale;
        float ArmClapperCom => Mathf.Max(0.0001f, (cfg.clapperAxisLocalY - cfg.clapperComLocalY) * visualScale);

        static float Gravity => Mathf.Max(0.0001f, -Physics2D.gravity.y);

        // Heights authored out of order would otherwise pass as a bell that behaves like nothing.
        void WarnOnInvertedGeometry()
        {
            if (cfg.bellComLocalY < cfg.pivotLocalY && cfg.clapperComLocalY < cfg.clapperAxisLocalY) return;
            Debug.LogWarning($"[SwingingBellV2] '{name}': geometry heights are out of order. Expected " +
                             "Pivot Local Y > Bell Com Local Y and Clapper Axis Local Y > Clapper Com " +
                             $"Local Y, got pivot={cfg.pivotLocalY} bellCom={cfg.bellComLocalY} " +
                             $"clapperAxis={cfg.clapperAxisLocalY} clapperCom={cfg.clapperComLocalY}.", this);
        }

        // Inverts T = 2*pi*sqrt((Bb + m*lc^2) / ((M*L + m*lc)*g)), floored at parallel-axis M*L^2.
        float BellInertia()
        {
            float m = cfg.clapperMass, lc = ArmClapperAxis, L = ArmBellCom;
            float w = 2f * Mathf.PI / Mathf.Max(0.05f, cfg.bellSwingPeriod);
            float bb = (cfg.bellMass * L + m * lc) * Gravity / (w * w) - m * lc * lc;
            float floor = cfg.bellMass * L * L;
            if (bb < floor && !warnedPeriodFloor)
            {
                warnedPeriodFloor = true;
                float minPeriod = 2f * Mathf.PI * Mathf.Sqrt((floor + m * lc * lc) /
                                                             ((cfg.bellMass * L + m * lc) * Gravity));
                Debug.LogWarning($"[SwingingBellV2] '{name}': Bell Swing Period {cfg.bellSwingPeriod:F2}s " +
                                 $"is below the {minPeriod:F2}s this geometry can reach, so it swings at " +
                                 $"{minPeriod:F2}s instead. Raise the period, or move Bell Com Local Y " +
                                 "closer to the pivot to shorten the arm.", this);
            }
            return Mathf.Max(bb, floor);
        }

        // Bc measured out from the point-mass minimum m*l^2, the least a rigid clapper can have.
        float ClapperInertia()
        {
            float l = ArmClapperCom;
            return cfg.clapperMass * l * l * Mathf.Max(1f, cfg.clapperInertiaSpread);
        }

        // ---- Motion ----

        protected override (Vector2 position, float rotation) ComputeMotion(float dt)
        {
            if (cfg == null) cfg = BellV2Config.Resolve(configOverride);

            retriggerTimer = Mathf.Max(0f, retriggerTimer - dt);
            ScanContact();
            // After the scan, so the scan sees the tick before this one — the one still in flight.
            if (trackedSlime != null) prevSlimeVel = trackedSlime.Velocity;

            int n = Mathf.Max(1, cfg.substeps);
            float h = dt / n;
            for (int i = 0; i < n; i++)
            {
                Integrate(h);
                ResolveWallContact();
            }

            // Consumed, not held: the carrier re-supplies it every tick the slime is still attached.
            riderTorque = 0f;

            float maxRad = cfg.maxBellAngle * Mathf.Deg2Rad;
            if (phi1 > maxRad) { phi1 = maxRad; if (phi1Dot > 0f) phi1Dot = 0f; }
            else if (phi1 < -maxRad) { phi1 = -maxRad; if (phi1Dot < 0f) phi1Dot = 0f; }

            // Rigid rotation about the hang point, which is above the body origin.
            Vector2 arm = restPos - pivotWorld;
            float c = Mathf.Cos(phi1), s = Mathf.Sin(phi1);
            Vector2 rotated = new Vector2(arm.x * c - arm.y * s, arm.x * s + arm.y * c);
            return (pivotWorld + rotated, restRot + phi1 * Mathf.Rad2Deg);
        }

        // Double physical pendulum, eq. 2.2/2.3 of arXiv:1505.04389, semi-implicit Euler by Cramer.
        void Integrate(float h)
        {
            float M = cfg.bellMass, m = cfg.clapperMass;
            float L = ArmBellCom, l = ArmClapperCom, lc = ArmClapperAxis;
            float g = Gravity;
            float Bb = BellInertia(), Bc = ClapperInertia();
            float dc = cfg.clapperDamping;

            float d = phi2 - phi1;
            float cos = Mathf.Cos(d), sin = Mathf.Sin(d);
            float k = m * lc * l;

            float a11 = Bb + m * lc * lc;
            float a12 = k * cos;
            float a21 = k * cos;
            float a22 = Bc;

            float b1 = k * sin * phi2Dot * phi2Dot
                     - (M * L + m * lc) * g * Mathf.Sin(phi1)
                     - cfg.bellDamping * phi1Dot
                     + dc * (phi2Dot - phi1Dot)
                     + riderTorque;
            float b2 = -k * sin * phi1Dot * phi1Dot
                     - m * g * l * Mathf.Sin(phi2)
                     - dc * (phi2Dot - phi1Dot);

            float det = a11 * a22 - a12 * a21;
            if (Mathf.Abs(det) < 1e-8f) return;

            float acc1 = (b1 * a22 - a12 * b2) / det;
            float acc2 = (a11 * b2 - b1 * a21) / det;

            phi1Dot += acc1 * h;
            phi2Dot += acc2 * h;
            phi1 += phi1Dot * h;
            phi2 += phi2Dot * h;
        }

        // eq. 2.1 contact at |phi1 - phi2| = alpha, resolved by eq. 2.5/2.6. Makes no sound now.
        void ResolveWallContact()
        {
            float alpha = cfg.strikeAngle * Mathf.Deg2Rad;
            float rel = phi2 - phi1;

            if (Mathf.Abs(rel) < alpha)
            {
                if (Mathf.Abs(rel) < alpha - cfg.clapperSeparationAngle * Mathf.Deg2Rad) onWall = false;
                return;
            }

            float w = phi2Dot - phi1Dot;
            if (w * rel > 0f)   // still travelling into the wall, not already leaving it
            {
                float m = cfg.clapperMass, lc = ArmClapperAxis, l = ArmClapperCom;
                float cos = Mathf.Cos(rel);
                float A = BellInertia() + m * lc * lc + m * lc * l * cos;
                float B = ClapperInertia() + m * lc * l * cos;
                float sum = A + B;
                if (Mathf.Abs(sum) > 1e-8f)
                {
                    // Resting contact loses its inward speed rather than bouncing every substep.
                    float restitution = onWall ? 0f : cfg.strikeRestitution;
                    float momentum = A * phi1Dot + B * phi2Dot;
                    float wAfter = -Mathf.Sqrt(restitution) * w;
                    phi1Dot = (momentum - B * wAfter) / sum;
                    phi2Dot = phi1Dot + wAfter;
                }
                onWall = true;
            }

            // Park it exactly on the wall so a fast clapper cannot pass through in one substep.
            phi2 = phi1 + Mathf.Sign(rel) * alpha;
        }

        protected override void OnAfterMotion(float dt)
        {
            if (clapperVisual == null) return;

            // Local to the bell visual, so it composes with the body rotation instead of replacing it.
            var q = Quaternion.Euler(0f, 0f, (phi2 - phi1) * Mathf.Rad2Deg);
            var axis = new Vector3(0f, cfg.clapperAxisLocalY, 0f);
            clapperVisual.localRotation = q * clapperRestLocalRot;
            clapperVisual.localPosition = axis + q * (clapperRestLocalPos - axis);
        }

        // ---- Contact ----

        // The rider flag holds the latch through the contact list's mid-ride flicker.
        void ScanContact()
        {
            int n = rb.GetContacts(contactScratch);
            SlimeController slime = null;
            Vector2 point = Vector2.zero, reported = Vector2.zero;

            for (int i = 0; i < n; i++)
            {
                var col = contactScratch[i].collider;
                if (col == null) continue;
                var s = col.GetComponentInParent<SlimeController>();
                if (s == null) continue;
                slime = s;
                trackedSlime = s;
                point = contactScratch[i].point;
                reported = contactScratch[i].normal;
                break;
            }

            // The probe attaches off an overlap query, so a seated slime can report no contact at all.
            if (slime == null && riding && trackedSlime != null && trackedSlime.isActiveAndEnabled)
            {
                slime = trackedSlime;
                point = lastRiderPos;
                reported = Vector2.zero;
            }

            if (slime != null)
            {
                Vector2 surf = SurfaceNormal(point, reported);
                if (!occupied && retriggerTimer <= 0f) Ring(slime, point, surf);
                else PushOff(slime, point, surf);   // a bell is never somewhere the slime can rest
            }
            else if (!riding) riderArrivalVel = Vector2.zero;   // else it wins a later, slower visit

            // riding may only sustain a latch, never set one: the probe attaches a tick early.
            occupied = slime != null || (occupied && riding);
        }

        void Ring(SlimeController slime, Vector2 point, Vector2 surf)
        {
            retriggerTimer = cfg.retriggerCooldown;

            Vector2 r = point - pivotWorld;
            Vector2 rel = FlankRelative(slime, r, surf, out Vector2 surfaceVel);

            // Speed INTO the surface, so sliding down the bell's face is the graze it looks like.
            float speed = -Vector2.Dot(rel, surf);

            // Speed only picks a point in the soft-to-full window; outside it nothing changes.
            float t = Mathf.InverseLerp(cfg.minRingSpeed, FullSwingSpeed(), speed);
            float swing = Mathf.Lerp(cfg.softSwingDegrees, cfg.swingDegrees, t);
            // Which way it goes still comes from where the contact sits, never from the travel.
            phi1Dot += -Mathf.Sign(OutwardAt(r).x) * ReferenceKick(swing);

            if (speed >= cfg.minRingSpeed)
            {
                ringCount++;
                PlayClip(point);
                PulseRevealRelay();
            }

            ReflectSlime(slime, rel, surfaceVel, surf);
        }

        // Away from the bell's axis; a hit dead on the axis has no flank, so it takes the right one.
        Vector2 OutwardAt(Vector2 r)
        {
            Vector2 down = new Vector2(Mathf.Sin(phi1), -Mathf.Cos(phi1));
            Vector2 outward = r - down * Vector2.Dot(r, down);
            return outward.sqrMagnitude < 1e-6f ? new Vector2(Mathf.Cos(phi1), Mathf.Sin(phi1))
                                                : outward.normalized;
        }

        // Solves 1/2*I*w^2 = (M*L + m*lc)*g*(1 - cos(theta)) for the swing angle asked for.
        float ReferenceKick(float swingDegrees)
        {
            float m = cfg.clapperMass, lc = ArmClapperAxis;
            float inertia = Mathf.Max(0.0001f, BellInertia() + m * lc * lc);
            float restore = (cfg.bellMass * ArmBellCom + m * lc) * Gravity;
            float rise = 1f - Mathf.Cos(swingDegrees * Mathf.Deg2Rad);
            return Mathf.Sqrt(2f * restore * rise / inertia);
        }

        void PlayClip(Vector2 point)
        {
            var clips = cfg.strikeClips;
            if (clips == null || clips.Length == 0) return;

            var clip = clips[nextClip % clips.Length];
            nextClip = (nextClip + 1) % clips.Length;
            if (clip == null) return;

            float p = cfg.pitch + Random.Range(-cfg.pitchJitter, cfg.pitchJitter);
            SoundService.PlayAt(clip, point, SoundCategory.SFX_Object, cfg.volume, Mathf.Max(0.01f, p));
        }

        // Contact the ring latch already spent. Still pushed off, just not re-rung or re-swung.
        void PushOff(SlimeController slime, Vector2 point, Vector2 surf)
        {
            Vector2 rel = FlankRelative(slime, point - pivotWorld, surf, out Vector2 surfaceVel);
            ReflectSlime(slime, rel, surfaceVel, surf);
        }

        // Box2D's normal sign depends on which collider reports it, so it is faced out of the body.
        Vector2 SurfaceNormal(Vector2 point, Vector2 reported)
        {
            Vector2 com = pivotWorld + new Vector2(Mathf.Sin(phi1), -Mathf.Cos(phi1)) * ArmBellCom;
            Vector2 outFromBody = point - com;
            if (reported.sqrMagnitude < 1e-6f)
                return outFromBody.sqrMagnitude < 1e-6f ? OutwardAt(point - pivotWorld)
                                                        : outFromBody.normalized;
            return Vector2.Dot(reported, outFromBody) < 0f ? -reported : reported;
        }

        // Arrival measured against the flank's own motion, which is what a collision actually sees.
        Vector2 FlankRelative(SlimeController slime, Vector2 r, Vector2 n, out Vector2 surfaceVel)
        {
            surfaceVel = new Vector2(-r.y, r.x) * (phi1Dot * cfg.bounceSwingTransfer);
            return ArrivalVelocity(slime, n) - surfaceVel;
        }

        // The ramp needs an ordered window; an inverted one would map slow hits to the full swing.
        float FullSwingSpeed()
        {
            if (cfg.fullSwingSpeed > cfg.minRingSpeed) return cfg.fullSwingSpeed;
            if (!warnedSwingWindow)
            {
                warnedSwingWindow = true;
                Debug.LogWarning($"[SwingingBellV2] '{name}': Full Swing Speed {cfg.fullSwingSpeed:F1} is " +
                                 $"not above Min Ring Speed {cfg.minRingSpeed:F1}, so the swing would never " +
                                 "ramp. Using a window just above Min Ring Speed instead.", this);
            }
            return cfg.minRingSpeed + 0.01f;
        }

        // Elastic collision resolved in the flank's own moving frame: v' = vs + reflect(v - vs).
        void ReflectSlime(SlimeController slime, Vector2 rel, Vector2 surfaceVel, Vector2 n)
        {
            if (HoldingCharge(slime)) return;   // the one exception, see the header

            float closing = -Vector2.Dot(rel, n);
            Vector2 tangent = rel + closing * n;

            // Floored, never gated: a bell is not something the slime can come to rest on.
            float leaving = Mathf.Max(closing * cfg.bounceRestitution, cfg.minSeparationSpeed);
            Vector2 outVel = surfaceVel + tangent + leaving * n;

            BlockReattach(slime);
            slime.ForceAirborneWithVelocity(outVel);
        }

        // The held key, not the Charging state, because the state only turns on a tick later.
        bool HoldingCharge(SlimeController slime)
        {
            if (slime.IsCharging) return true;
            if (input == null) input = slime.GetComponentInParent<SlimeInputReader>();
            return input != null && input.IsJumpHeld;
        }

        // Whichever source closes hardest wins; the longest can be a stale one pointing away.
        Vector2 ArrivalVelocity(SlimeController slime, Vector2 n)
        {
            velScratch[0] = slime.Velocity;
            velScratch[1] = prevSlimeVel;
            velScratch[2] = riderArrivalVel;

            Vector2 best = Vector2.zero;
            float bestClosing = float.NegativeInfinity;
            for (int i = 0; i < velScratch.Length; i++)
            {
                float closing = -Vector2.Dot(velScratch[i], n);
                if (closing > bestClosing) { bestClosing = closing; best = velScratch[i]; }
            }
            return best;
        }

        // A slime hanging off one side is real weight on that side, so it tilts the bell.
        public override void ApplyRiderInfluence(Vector2 riderWorldPos, float riderMass, Vector2 riderVelocity, bool isCharging)
        {
            // The carrier's first call lands before the attach zeroes the slime: a measured arrival.
            if (!riding) riderArrivalVel = riderVelocity;
            riding = true;
            lastRiderPos = riderWorldPos;
            if (cfg == null) return;
            riderTorque = -riderMass * Gravity * (riderWorldPos.x - pivotWorld.x) * cfg.riderWeightScale;
        }

        public override void RemoveRider()
        {
            riding = false;
            riderTorque = 0f;
        }

        // Hides the bell from the probe, physics left on — else the probe re-sticks the slime next tick.
        void BlockReattach(SlimeController slime)
        {
            if (cfg.reattachBlockSeconds <= 0f) return;
            for (int i = 0; i < ownColliders.Count; i++)
            {
                if (ownColliders[i] != null)
                    slime.RegisterReattachGuard(ownColliders[i], cfg.reattachBlockSeconds);
            }
        }

        void OnDrawGizmosSelected()
        {
            var c = BellV2Config.Resolve(configOverride);
            var vis = bellVisual != null ? bellVisual : transform;
            Vector3 pivot = Application.isPlaying ? (Vector3)pivotWorld
                                                  : vis.TransformPoint(new Vector3(0f, c.pivotLocalY, 0f));
            float s = Mathf.Abs(vis.lossyScale.y);
            float armAxis = (c.pivotLocalY - c.clapperAxisLocalY) * s;
            float armBall = (c.clapperAxisLocalY - c.clapperComLocalY) * s;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(pivot, 0.15f * s);

            Vector3 axis = pivot + Vector3.down * armAxis;
            Gizmos.color = new Color(1f, 0.6f, 0.1f);
            Gizmos.DrawLine(pivot, axis);

            float a = c.strikeAngle * Mathf.Deg2Rad;
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.8f);
            Gizmos.DrawLine(axis, axis + new Vector3(Mathf.Sin(a), -Mathf.Cos(a)) * armBall);
            Gizmos.DrawLine(axis, axis + new Vector3(-Mathf.Sin(a), -Mathf.Cos(a)) * armBall);

            Gizmos.color = new Color(0.4f, 0.9f, 1f);
            Gizmos.DrawLine(axis, axis + Vector3.down * armBall);
            Gizmos.DrawWireSphere(pivot + Vector3.down * ((c.pivotLocalY - c.bellComLocalY) * s), 0.1f * s);
        }
    }
}
