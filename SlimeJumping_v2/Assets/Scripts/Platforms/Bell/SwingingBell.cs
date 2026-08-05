using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;

namespace SlimeJumping.Platforms.Bell
{
    // Hanging bell: the body swings about a fixed pivot, the clapper inside lags behind, and the sound
    // fires when the clapper reaches the wall — never on the slime's own impact.
    // Yoke-bell-clapper double physical pendulum, eq. 2.1-2.6 of arXiv:1505.04389.
    // phi1 = bell angle from straight down, phi2 = clapper angle from straight down (rad, CCW+).
    // No joint: a Box2D hinge cannot express the impact law, and this repo rejected dynamic hinges twice.
    [DisallowMultipleComponent]
    public class SwingingBell : PlatformMotor
    {
        [SerializeField] BellConfig configOverride;
        [Tooltip("The bell mesh transform. All geometry in the config is measured in ITS local space.")]
        [SerializeField] Transform bellVisual;
        [Tooltip("The clapper mesh transform, a child of the bell visual.")]
        [SerializeField] Transform clapperVisual;

        BellConfig cfg;

        // Captured once at Awake. Reading the pivot live would let it ride the body it is meant to anchor.
        Vector2 pivotWorld;
        Vector2 restPos;
        float restRot;
        float visualScale;
        Vector3 clapperRestLocalPos;
        Quaternion clapperRestLocalRot;

        float phi1, phi1Dot;
        float phi2, phi2Dot;
        float riderTorque;
        float impactCooldown, soundCooldown;
        bool onWall;

        readonly ContactPoint2D[] contactScratch = new ContactPoint2D[8];
        readonly List<Collider2D> ownColliders = new List<Collider2D>();
        readonly Vector2[] velScratch = new Vector2[3];
        float offSlimeSeconds = float.MaxValue;
        SlimeController trackedSlime;
        Vector2 prevSlimeVel;
        bool riding;
        bool onBell;
        Vector2 lastRiderPos;
        Vector2 riderArrivalVel;

        /// <summary>What the bell did with the slime's last touch, so a hit that produced nothing says why.</summary>
        public enum ContactVerdict { Clear, Riding, Debounced, OnAxis, Grazing, LightTouch, Slam }
        ContactVerdict lastContact = ContactVerdict.Clear;

        // Pulled by SwingingBellDebug; nothing here feeds the simulation.
        float lastImpactSpeed, lastImpactKick, lastImpactLever;
        Vector2 lastImpactDir;
        bool lastImpactBounced;
        int strikeCount, silentStrikeCount, impactCount;
        float lastStrikeSpeed;
        string lastRingResult = "-";

        public float BellAngularSpeed => phi1Dot;
        public float LastImpactSpeed => lastImpactSpeed;
        public float LastImpactKick => lastImpactKick;
        /// <summary>Travel direction the bell actually used, read a tick before the slime's own landing spends it.</summary>
        public Vector2 LastImpactDir => lastImpactDir;
        public float LastImpactLever => lastImpactLever;
        public bool LastImpactBounced => lastImpactBounced;
        public int StrikeCount => strikeCount;
        public int SilentStrikeCount => silentStrikeCount;
        /// <summary>Slime touches that swung the bell — one per hit, not per clapper ring.</summary>
        public int ImpactCount => impactCount;
        public float LastStrikeSpeed => lastStrikeSpeed;
        /// <summary>Why the last touch did or did not swing the bell.</summary>
        public ContactVerdict LastContact => lastContact;
        /// <summary>What the last strike actually did on the way to the speaker: the clip it played, or
        /// the gate that swallowed it. Recorded inside Ring, so it is the played fact and not a guess.</summary>
        public string LastRingResult => lastRingResult;
        public int StrikeClipCount => cfg != null && cfg.strikeClips != null ? cfg.strikeClips.Length : 0;
        /// <summary>The config actually in use after override/Resources resolution.</summary>
        public BellConfig Config => cfg;

        /// <summary>Bell tilt in degrees, + = counter-clockwise. Read-only view for debug tooling.</summary>
        public float BellAngle => phi1 * Mathf.Rad2Deg;
        /// <summary>Clapper tilt relative to the bell in degrees; ±StrikeAngle means it is on the wall.</summary>
        public float ClapperRelativeAngle => (phi2 - phi1) * Mathf.Rad2Deg;
        /// <summary>True while the clapper is resting against the wall rather than swinging free.</summary>
        public bool ClapperOnWall => onWall;
        /// <summary>Raised on every clapper-to-wall hit, with its closing speed in rad/s. Fires whether or
        /// not the hit was loud enough to be heard, so listeners can react to soft taps too.</summary>
        public event System.Action<float> Struck;

        protected override void Awake()
        {
            base.Awake();   // kinematic + interpolated Rigidbody2D

            cfg = BellConfig.Resolve(configOverride);
            restPos = rb.position;
            restRot = rb.rotation;
            rb.GetAttachedColliders(ownColliders);

            if (bellVisual == null) bellVisual = transform.childCount > 0 ? transform.GetChild(0) : transform;
            visualScale = Mathf.Abs(bellVisual.lossyScale.y);
            if (visualScale < 0.0001f) visualScale = 1f;

            pivotWorld = bellVisual.TransformPoint(new Vector3(0f, cfg.pivotLocalY, 0f));
            WarnOnInvertedGeometry();

            if (clapperVisual != null)
            {
                clapperRestLocalPos = clapperVisual.localPosition;
                clapperRestLocalRot = clapperVisual.localRotation;
            }
        }

        // ---- Derived physical quantities (config is authored in feel, not in kg m^2) ----

        // World-space lever arms. The config measures them on the mesh; the visual's scale makes them world.
        float ArmBellCom => Mathf.Max(0.0001f, (cfg.pivotLocalY - cfg.bellComLocalY) * visualScale);
        float ArmClapperAxis => (cfg.pivotLocalY - cfg.clapperAxisLocalY) * visualScale;
        float ArmClapperCom => Mathf.Max(0.0001f, (cfg.clapperAxisLocalY - cfg.clapperComLocalY) * visualScale);

        // An arm that is zero or upside down means the heights were authored out of order, which the
        // Max() above would otherwise turn into a plausible-looking bell that behaves like nothing.
        void WarnOnInvertedGeometry()
        {
            if (cfg.bellComLocalY < cfg.pivotLocalY && cfg.clapperComLocalY < cfg.clapperAxisLocalY) return;
            Debug.LogWarning($"[SwingingBell] '{name}': geometry heights are out of order. Expected " +
                             "Pivot Local Y > Bell Com Local Y and Clapper Axis Local Y > Clapper Com " +
                             $"Local Y, got pivot={cfg.pivotLocalY} bellCom={cfg.bellComLocalY} " +
                             $"clapperAxis={cfg.clapperAxisLocalY} clapperCom={cfg.clapperComLocalY}.", this);
        }

        static float Gravity => Mathf.Max(0.0001f, -Physics2D.gravity.y);

        // Bb from the authored swing period. Small-angle form of eq. 2.2 with the clapper riding along:
        //   T = 2*pi*sqrt((Bb + m*lc^2) / ((M*L + m*lc)*g))
        // Floored at the parallel-axis minimum M*L^2 — a rigid body cannot have less inertia than that.
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
                Debug.LogWarning($"[SwingingBell] '{name}': Bell Swing Period {cfg.bellSwingPeriod:F2}s is " +
                                 $"below the {minPeriod:F2}s this geometry can reach, so it swings at " +
                                 $"{minPeriod:F2}s instead. Raise the period, or move Bell Com Local Y " +
                                 "closer to the pivot to shorten the arm.", this);
            }
            return Mathf.Max(bb, floor);
        }

        bool warnedPeriodFloor;

        // Bc measured out from the point-mass minimum m*l^2, which is the least a rigid clapper can have.
        // Its period follows: T = 2*pi*sqrt(Bc / (m*g*l)), so spread 1 is the eagerest possible clapper.
        float ClapperInertia()
        {
            float l = ArmClapperCom;
            return cfg.clapperMass * l * l * Mathf.Max(1f, cfg.clapperInertiaSpread);
        }

        // ---- Motion ----

        protected override (Vector2 position, float rotation) ComputeMotion(float dt)
        {
            if (cfg == null) cfg = BellConfig.Resolve(configOverride);

            ScanSlimeContact(dt);
            // After the scan, so the scan sees the tick before this one — the one still holding flight speed.
            if (trackedSlime != null) prevSlimeVel = trackedSlime.Velocity;

            int n = Mathf.Max(1, cfg.substeps);
            float h = dt / n;
            for (int i = 0; i < n; i++)
            {
                Integrate(h);
                ResolveWallContact();
            }

            // Consumed, not held: PlatformCarrier re-supplies it every tick the slime is still attached,
            // so zeroing here makes a detach fade the load out on its own.
            riderTorque = 0f;
            impactCooldown = Mathf.Max(0f, impactCooldown - dt);
            soundCooldown = Mathf.Max(0f, soundCooldown - dt);

            float maxRad = cfg.maxBellAngle * Mathf.Deg2Rad;
            if (phi1 > maxRad) { phi1 = maxRad; if (phi1Dot > 0f) phi1Dot = 0f; }
            else if (phi1 < -maxRad) { phi1 = -maxRad; if (phi1Dot < 0f) phi1Dot = 0f; }

            // Rigid rotation about the hang point, which is above the body origin.
            float deg = phi1 * Mathf.Rad2Deg;
            Vector2 arm = restPos - pivotWorld;
            float rad = phi1;
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            Vector2 rotated = new Vector2(arm.x * c - arm.y * s, arm.x * s + arm.y * c);
            return (pivotWorld + rotated, restRot + deg);
        }

        // Semi-implicit Euler on the 2x2 system in (phi1'', phi2''), solved by Cramer's rule.
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

        // eq. 2.1: contact when |phi1 - phi2| reaches the strike angle. eq. 2.5/2.6 resolve it by keeping
        // the system's angular momentum and returning only sqrt(k) of the relative speed.
        void ResolveWallContact()
        {
            float alpha = cfg.strikeAngle * Mathf.Deg2Rad;
            float rel = phi2 - phi1;

            if (Mathf.Abs(rel) < alpha)
            {
                // Only count it as off the wall once it has visibly come clear, so the next contact is a
                // fresh strike rather than the tail of this one.
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
                    // Bouncing only on the fresh hit. A clapper already leaning on the wall is resting
                    // contact, so it just loses its inward speed instead of being flung off every substep.
                    float restitution = onWall ? 0f : cfg.strikeRestitution;
                    float momentum = A * phi1Dot + B * phi2Dot;
                    float wAfter = -Mathf.Sqrt(restitution) * w;
                    phi1Dot = (momentum - B * wAfter) / sum;
                    phi2Dot = phi1Dot + wAfter;
                }

                // The bounce series after a hit is the same hit still ringing out, not new strikes.
                if (!onWall)
                {
                    onWall = true;
                    Ring(Mathf.Abs(w), rel);
                    // After Ring, so a listener reads this hit's own LastRingResult.
                    if (Struck != null) Struck(Mathf.Abs(w));
                }
            }

            // Park it exactly on the wall so a fast clapper cannot pass through in one substep.
            phi2 = phi1 + Mathf.Sign(rel) * alpha;
        }

        void Ring(float closingSpeed, float side)
        {
            strikeCount++;
            lastStrikeSpeed = closingSpeed;

            if (soundCooldown > 0f)
            {
                silentStrikeCount++;
                lastRingResult = $"GATED — sound cooldown, {soundCooldown:F2}s of {cfg.strikeSoundCooldown:F2} left";
                return;
            }
            if (closingSpeed < cfg.minStrikeSpeed)
            {
                silentStrikeCount++;
                lastRingResult = $"GATED — closing {closingSpeed:F2} < minStrikeSpeed {cfg.minStrikeSpeed:F2}";
                return;
            }

            var clips = cfg.strikeClips;
            if (clips == null || clips.Length == 0)
            {
                silentStrikeCount++;
                lastRingResult = "GATED — no strikeClips on the config";
                return;
            }

            float strength = Mathf.Clamp01(closingSpeed / cfg.soundRefSpeed);
            int index = Mathf.Clamp(Mathf.FloorToInt(strength * clips.Length), 0, clips.Length - 1);
            var clip = clips[index];
            if (clip == null)
            {
                silentStrikeCount++;
                lastRingResult = $"GATED — strikeClips[{index}] is empty";
                return;
            }

            float volume = cfg.volumeCurve.Evaluate(strength) * cfg.volumeScale;
            float pitch = cfg.pitchCurve.Evaluate(strength) + Random.Range(-cfg.pitchJitter, cfg.pitchJitter);
            SoundService.PlayAt(clip, StrikeWorldPoint(side), SoundCategory.SFX_Object,
                                volume, Mathf.Max(0.01f, pitch));

            lastRingResult = SoundService.Instance == null
                ? "DROPPED — PlayAt called but no SoundService instance"
                : $"PLAYED clip[{index}] '{clip.name}' vol={volume:F2} pitch={pitch:F2}";
            soundCooldown = cfg.strikeSoundCooldown;
        }

        // Where the ball met the wall, so the sound comes from the struck side rather than the pivot.
        Vector3 StrikeWorldPoint(float side)
        {
            float armAxis = ArmClapperAxis, armBall = ArmClapperCom;
            float a = Mathf.Sign(side) * cfg.strikeAngle * Mathf.Deg2Rad + phi1;
            Vector2 axis = pivotWorld + new Vector2(Mathf.Sin(phi1), -Mathf.Cos(phi1)) * armAxis;
            return axis + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * armBall;
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

        // ---- Slime -> bell ----

        // One judgement per visit: contacts miss the arrival tick because SurfaceProbe attaches a tick earlier, and they flicker mid-ride, so the carrier's rider counts as contact too.
        void ScanSlimeContact(float dt)
        {
            int n = rb.GetContacts(contactScratch);
            SlimeController touched = null;
            Vector2 point = Vector2.zero;

            for (int i = 0; i < n; i++)
            {
                var col = contactScratch[i].collider;
                if (col == null) continue;
                var s = col.GetComponentInParent<SlimeController>();
                if (s == null) continue;
                touched = s;
                point = contactScratch[i].point;
                break;
            }

            if (touched != null) Track(touched);

            // isActiveAndEnabled: a pooled slime disabled while attached never detaches, and would
            // otherwise hold the bell occupied forever.
            bool present = touched != null
                        || (riding && trackedSlime != null && trackedSlime.isActiveAndEnabled
                            && trackedSlime.CurrentState != SlimeController.SlimeState.Airborne);

            if (!present)
            {
                onBell = false;
                riderArrivalVel = Vector2.zero;
                offSlimeSeconds += dt;
                lastContact = ContactVerdict.Clear;
                return;
            }

            var slime = touched != null ? touched : trackedSlime;
            if (onBell) { lastContact = riding ? ContactVerdict.Riding : ContactVerdict.Debounced; return; }

            onBell = true;
            if (offSlimeSeconds < cfg.impactCooldown) { lastContact = ContactVerdict.Debounced; return; }
            offSlimeSeconds = 0f;
            if (touched == null) point = lastRiderPos;

            lastImpactBounced = false;
            lastImpactKick = 0f;

            Vector2 r = point - pivotWorld;
            Vector2 outward = OutwardAt(r);
            if (outward == Vector2.zero) { lastImpactSpeed = 0f; lastContact = ContactVerdict.OnAxis; return; }

            float approach = ApproachSpeed(slime, r, outward, out Vector2 dir);
            lastImpactSpeed = approach;
            if (approach <= 0f) { lastContact = ContactVerdict.Grazing; return; }

            ApplyKick(r, dir, approach);

            bool hard = approach >= cfg.hardHitSpeed;
            lastContact = hard ? ContactVerdict.Slam : ContactVerdict.LightTouch;
            if (hard) lastImpactBounced = ThrowSlimeOff(slime, r);
        }

        // Speed the slime is closing on the flank at, relative to the flank's own motion. Only the part
        // of the travel that goes INTO the bell is a hit — sliding along it is perpendicular, so it reads
        // as zero however fast the slime is going.
        // Scored over all three velocity estimates rather than taking the longest vector: the longest one
        // can point away, which would veto the real hit next to it.
        float ApproachSpeed(SlimeController slime, Vector2 r, Vector2 outward, out Vector2 dir)
        {
            Vector2 surfaceVel = new Vector2(-r.y, r.x) * phi1Dot;
            velScratch[0] = slime.Velocity;
            velScratch[1] = prevSlimeVel;
            velScratch[2] = riderArrivalVel;

            Vector2 best = Vector2.zero;
            float bestApproach = float.NegativeInfinity;
            for (int i = 0; i < velScratch.Length; i++)
            {
                float a = -Vector2.Dot(velScratch[i] - surfaceVel, outward);
                if (a > bestApproach) { bestApproach = a; best = velScratch[i]; }
            }

            dir = best.sqrMagnitude > 1e-8f ? best.normalized : -outward;
            return bestApproach;
        }

        // Away from the bell's axis at a point on it, which is the direction a hit has to oppose.
        Vector2 OutwardAt(Vector2 r)
        {
            Vector2 down = new Vector2(Mathf.Sin(phi1), -Mathf.Cos(phi1));
            Vector2 outward = r - down * Vector2.Dot(r, down);
            return outward.sqrMagnitude < 1e-6f ? Vector2.zero : outward.normalized;
        }

        // One swing law for both directions of contact: a slam from outside and a rider pushing off both
        // shove the bell at the contact point, and only the sign of dir differs.
        void ApplyKick(Vector2 r, Vector2 dir, float approach)
        {
            impactCount++;
            lastImpactDir = dir;

            // The bell goes the way the slime was travelling. The moment arm only breaks the tie for a
            // hit straight down the axis, where the travel direction says nothing about which way to go.
            float lever = r.x * dir.y - r.y * dir.x;
            lastImpactLever = lever;
            float side = Mathf.Abs(dir.x) > 1e-4f ? Mathf.Sign(dir.x)
                       : (Mathf.Abs(lever) > 1e-6f ? Mathf.Sign(lever) : 1f);

            // Two regimes, not a ramp. At or over Hard Hit Speed the bell always takes the same full
            // swing, which is what makes the ringing pattern repeatable; under it, a fraction that fades
            // out with speed, so brushing past moves the bell without ringing it.
            float strength = approach >= cfg.hardHitSpeed
                ? 1f
                : cfg.lightTouchScale * (approach / cfg.hardHitSpeed);

            float kick = side * ReferenceKick() * strength;
            phi1Dot += kick;
            lastImpactKick = kick;
        }

        // Angular speed that swings the bell to Slam Swing Degrees from rest, from energy:
        //   1/2*I*w^2 = (M*L + m*lc)*g*(1 - cos(theta))
        float ReferenceKick()
        {
            float m = cfg.clapperMass, lc = ArmClapperAxis;
            float inertia = Mathf.Max(0.0001f, BellInertia() + m * lc * lc);
            float restore = (cfg.bellMass * ArmBellCom + m * lc) * Gravity;
            float rise = 1f - Mathf.Cos(cfg.hardHitSwingDegrees * Mathf.Deg2Rad);
            return Mathf.Sqrt(2f * restore * rise / inertia);
        }

        // Fast arrivals slam and are thrown clear; slow ones fall through and cling like any surface.
        // ForceAirborneWithVelocity is the same entry point JumpPad and the bamboo throw use — it drops
        // the carrier, registers the launch-clearance ignore so the slime cannot re-stick, and goes Airborne.
        bool ThrowSlimeOff(SlimeController slime, Vector2 r)
        {
            if (cfg.bounceSpeed <= 0f) return false;
            if (slime.IsCharging) return false;   // a charge always ends in its own launch

            // Outward from the bell's axis, so the throw leaves the flank it was struck on. Taken from
            // the geometry rather than the contact normal, whose sign depends on which collider reports.
            Vector2 outward = OutwardAt(r);
            if (outward == Vector2.zero) return false;

            // Flat, not scaled by how hard it arrived: this only has to break the slime's grip, and a
            // speed-scaled version launched it across the room.
            Vector2 throwVel = outward * cfg.bounceSpeed
                             + new Vector2(-r.y, r.x) * (phi1Dot * cfg.bounceCarriesSwing);

            // Before the throw: launch clearance would otherwise own the entry and impose its own duration.
            BlockReattach(slime);
            slime.ForceAirborneWithVelocity(throwVel);
            return true;
        }

        // Hides the bell from the probe, physics left on — else the probe re-sticks the slime next tick.
        void BlockReattach(SlimeController slime)
        {
            if (cfg.reattachBlockSeconds <= 0f) return;
            for (int i = 0; i < ownColliders.Count; i++)
            {
                if (ownColliders[i] != null) slime.RegisterReattachGuard(ownColliders[i], cfg.reattachBlockSeconds);
            }
        }

        // A slime hanging off one side is real weight on that side, so it tilts the bell.
        public override void ApplyRiderInfluence(Vector2 riderWorldPos, float riderMass, Vector2 riderVelocity, bool isCharging)
        {
            // The carrier's first call comes from CaptureLandingImpact, before the attach zeroes the
            // slime's velocity, so this is the arrival speed itself rather than a guess at it.
            if (!riding) riderArrivalVel = riderVelocity;
            riding = true;
            lastRiderPos = riderWorldPos;
            if (cfg == null) return;
            float arm = riderWorldPos.x - pivotWorld.x;
            riderTorque = -riderMass * Gravity * arm * cfg.riderWeightScale;
        }

        public override void RemoveRider()
        {
            riderTorque = 0f;
            riding = false;
        }

        void Track(SlimeController slime)
        {
            if (trackedSlime == slime) return;
            if (trackedSlime != null) trackedSlime.OnLaunch -= OnSlimeLaunch;
            trackedSlime = slime;
            trackedSlime.OnLaunch += OnSlimeLaunch;
        }

        void OnDestroy()
        {
            if (trackedSlime != null) trackedSlime.OnLaunch -= OnSlimeLaunch;
        }

        // Launching off the bell shoves it back. Hooked on OnLaunch rather than RemoveRider: the carrier
        // detaches during a landing too, which fired this at the moment the slime touched down elsewhere.
        void OnSlimeLaunch(Vector2 pos, Vector2 launchVel, bool wasHop)
        {
            if (cfg == null || !onBell) return;

            Vector2 r = pos - pivotWorld;
            Vector2 outward = OutwardAt(r);
            if (outward == Vector2.zero) return;

            float away = Vector2.Dot(launchVel, outward);
            if (away <= 0f) return;

            lastImpactSpeed = away;
            lastImpactBounced = false;
            ApplyKick(r, -outward, away);
        }

        void OnDrawGizmosSelected()
        {
            var c = BellConfig.Resolve(configOverride);
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
            Gizmos.DrawWireSphere(axis, 0.08f * s);

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
