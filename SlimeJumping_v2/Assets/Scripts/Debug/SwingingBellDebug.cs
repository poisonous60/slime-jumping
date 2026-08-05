using System.Text;
using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;
using SlimeJumping.Platforms.Bell;

namespace SlimeJumping.Debugging
{
    /// <summary>
    /// Attach to the SLIME to trace why a bell slam did or did not ring: the slime's velocity at the
    /// instant of contact, the angular kick the bell took and its sign, which way the slime left, and
    /// where a strike was swallowed on the way to the speaker. Read-only — never writes slime or bell.
    /// </summary>
    [DefaultExecutionOrder(3000)]
    [DisallowMultipleComponent]
    [AddComponentMenu("SlimeJumping/Debug/Swinging Bell Debug")]
    public class SwingingBellDebug : MonoBehaviour
    {
        [Header("Display")]
        [SerializeField] bool showGui = true;
        [SerializeField] Vector2 guiPos = new Vector2(10f, 10f);
        [SerializeField] KeyCode dumpKey = KeyCode.F8;
        [SerializeField, Range(20, 240)] int samplesToDump = 90;

        [Header("Logging")]
        [Tooltip("Log one line the frame the slime touches a bell, with its velocity and the kick the bell took.")]
        [SerializeField] bool logContacts = true;
        [Tooltip("Log one line per clapper strike, saying whether it reached the speaker or was gated.")]
        [SerializeField] bool logStrikes = true;
        [Tooltip("Log one line whenever the slime's state changes while touching the bell — this is where a throw shows up.")]
        [SerializeField] bool logSlimeStateChanges = true;

        [Header("Target")]
        [Tooltip("Leave empty to latch onto whichever bell the slime touches first.")]
        [SerializeField] SwingingBell bell;

        const string Tag = "[BellDebug]";
        const int RingSize = 240;

        SlimeController slime;
        Rigidbody2D slimeRb;
        readonly ContactPoint2D[] contacts = new ContactPoint2D[8];

        struct Sample
        {
            public float t;
            public bool touching;
            public SlimeController.SlimeState slimeState;
            public Vector2 slimeVel;
            public float bellAngle, bellOmega, clapperRel;
            public bool onWall;
            public float lastImpactSpeed, lastImpactKick;
            public bool lastImpactBounced;
            public int strikes, silent;
        }

        readonly Sample[] ring = new Sample[RingSize];
        int ringHead, ringCount;

        bool wasTouching;
        SlimeController.SlimeState lastState;
        int seenStrikes, seenSilent;
        SwingingBell.ContactVerdict seenVerdict = SwingingBell.ContactVerdict.Clear;
        Vector2 velAtContact;

        void Awake()
        {
            slime = GetComponent<SlimeController>();
            slimeRb = GetComponent<Rigidbody2D>();
            if (slime == null) Debug.LogWarning($"{Tag} put this on the SLIME — no SlimeController here.", this);
        }

        void OnEnable()
        {
            if (bell != null) Subscribe(bell);
            Debug.Log($"{Tag} armed. soundService=" +
                      (SoundService.Instance != null ? "present" : "MISSING — every strike will be silent"), this);
        }

        void OnDisable()
        {
            if (bell != null) bell.Struck -= OnStruck;
        }

        void Subscribe(SwingingBell b)
        {
            b.Struck -= OnStruck;
            b.Struck += OnStruck;
            var cfg = b.Config;
            Debug.Log($"{Tag} watching '{b.name}'. clips={b.StrikeClipCount}" +
                      (cfg != null ? $" hardHitSpeed={cfg.hardHitSpeed} lightTouchScale={cfg.lightTouchScale} " +
                                     $"bounceSpeed={cfg.bounceSpeed} minStrikeSpeed={cfg.minStrikeSpeed} " +
                                     $"strikeAngle={cfg.strikeAngle}" : ""), this);
        }

        void OnStruck(float closingSpeed)
        {
            if (!logStrikes || bell == null) return;
            // Ring's own record of what it did, not a re-derivation of its gates.
            Debug.Log($"{Tag} STRIKE #{bell.StrikeCount} closing={closingSpeed:F2} rad/s bell={bell.BellAngle:F1}deg " +
                      $"-> {bell.LastRingResult}", this);
        }

        void FixedUpdate()
        {
            if (slime == null) return;

            bool touching = ResolveTouchedBell(out Vector2 contactPoint);

            if (touching && !wasTouching)
            {
                velAtContact = slime.Velocity;
                if (logContacts)
                    Debug.Log($"{Tag} CONTACT slimeVel={velAtContact} |v|={velAtContact.magnitude:F2} " +
                              $"state={slime.CurrentState} point={contactPoint}", this);
            }
            wasTouching = touching;

            if (bell != null)
            {
                // The bell's own verdict, not a re-derivation of its gates. Its dir is read a tick before
                // the slime's own landing spends it, so it disagrees with the debug's read by design —
                // a horizontal slam must give kick with the SAME sign as dirX, else the swing is inverted.
                if (logContacts && bell.LastContact != seenVerdict)
                {
                    seenVerdict = bell.LastContact;
                    var d = bell.LastImpactDir;
                    Debug.Log($"{Tag} CONTACT {seenVerdict} approach={bell.LastImpactSpeed:F2} " +
                              $"kick={bell.LastImpactKick:+0.000;-0.000} dir=({d.x:+0.00;-0.00},{d.y:+0.00;-0.00}) " +
                              $"lever={bell.LastImpactLever:+0.00;-0.00} thrown={bell.LastImpactBounced} " +
                              $"slime={slime.CurrentState}", this);
                }

                if (bell.StrikeCount != seenStrikes || bell.SilentStrikeCount != seenSilent)
                {
                    seenStrikes = bell.StrikeCount;
                    seenSilent = bell.SilentStrikeCount;
                }
            }

            if (logSlimeStateChanges && slime.CurrentState != lastState)
            {
                if (touching || wasTouching)
                    Debug.Log($"{Tag} slime {lastState} -> {slime.CurrentState} vel={slime.Velocity} " +
                              $"|v|={slime.Velocity.magnitude:F2}", this);
                lastState = slime.CurrentState;
            }

            Push(touching);
        }

        // Latches the bell from the slime's own contacts, so no scene search and no wrong bell.
        bool ResolveTouchedBell(out Vector2 point)
        {
            point = Vector2.zero;
            if (slimeRb == null) return false;

            int n = slimeRb.GetContacts(contacts);
            for (int i = 0; i < n; i++)
            {
                var col = contacts[i].collider;
                if (col == null) continue;
                var b = col.GetComponentInParent<SwingingBell>();
                if (b == null) continue;
                if (bell != b) { bell = b; Subscribe(b); }
                point = contacts[i].point;
                return true;
            }
            return false;
        }

        void Push(bool touching)
        {
            var s = new Sample
            {
                t = Time.time,
                touching = touching,
                slimeState = slime.CurrentState,
                slimeVel = slime.Velocity,
            };
            if (bell != null)
            {
                s.bellAngle = bell.BellAngle;
                s.bellOmega = bell.BellAngularSpeed;
                s.clapperRel = bell.ClapperRelativeAngle;
                s.onWall = bell.ClapperOnWall;
                s.lastImpactSpeed = bell.LastImpactSpeed;
                s.lastImpactKick = bell.LastImpactKick;
                s.lastImpactBounced = bell.LastImpactBounced;
                s.strikes = bell.StrikeCount;
                s.silent = bell.SilentStrikeCount;
            }
            ring[ringHead] = s;
            ringHead = (ringHead + 1) % RingSize;
            if (ringCount < RingSize) ringCount++;
        }

        void Update()
        {
            if (Input.GetKeyDown(dumpKey)) Dump();
        }

        void Dump()
        {
            var sb = new StringBuilder($"{Tag} last {Mathf.Min(samplesToDump, ringCount)} ticks\n");
            sb.AppendLine("t | touch | slimeState | |v| | bellDeg | w | clapperRel | onWall | strikes/silent");
            int take = Mathf.Min(samplesToDump, ringCount);
            for (int i = take; i > 0; i--)
            {
                var s = ring[(ringHead - i + RingSize) % RingSize];
                sb.AppendLine($"{s.t:F2} | {(s.touching ? "Y" : ".")} | {s.slimeState,-9} | {s.slimeVel.magnitude,5:F2} | " +
                              $"{s.bellAngle,7:F1} | {s.bellOmega,6:F2} | {s.clapperRel,7:F1} | {(s.onWall ? "Y" : ".")} | " +
                              $"{s.strikes}/{s.silent}");
            }
            Debug.Log(sb.ToString(), this);
        }

        void OnGUI()
        {
            if (!showGui || slime == null) return;

            var sb = new StringBuilder();
            sb.AppendLine($"slime {slime.CurrentState}  |v|={slime.Velocity.magnitude:F2}  v={slime.Velocity}");
            if (bell == null)
            {
                sb.AppendLine("bell: not touched yet");
            }
            else
            {
                var cfg = bell.Config;
                sb.AppendLine($"bell {bell.BellAngle:F1}deg  w={bell.BellAngularSpeed:F2}");
                sb.AppendLine($"clapper rel={bell.ClapperRelativeAngle:F1}deg onWall={bell.ClapperOnWall}" +
                              (cfg != null ? $"  (wall at {cfg.strikeAngle:F1})" : ""));
                sb.AppendLine($"lastSlam approach={bell.LastImpactSpeed:F2} kick={bell.LastImpactKick:+0.000;-0.000} " +
                              $"bounced={bell.LastImpactBounced} contact={bell.LastContact}");
                sb.AppendLine($"strikes={bell.StrikeCount} silent={bell.SilentStrikeCount} " +
                              $"lastClosing={bell.LastStrikeSpeed:F2} clips={bell.StrikeClipCount}");
                sb.AppendLine($"lastRing: {bell.LastRingResult}");
                sb.AppendLine($"soundService={(SoundService.Instance != null ? "ok" : "MISSING")}");
            }
            sb.AppendLine($"[{dumpKey}] dump last {samplesToDump} ticks");

            GUI.Box(new Rect(guiPos.x, guiPos.y, 520f, 170f), sb.ToString());
        }
    }
}
