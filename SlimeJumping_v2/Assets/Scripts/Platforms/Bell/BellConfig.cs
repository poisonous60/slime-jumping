using UnityEngine;

namespace SlimeJumping.Platforms.Bell
{
    /// <summary>Shared tuning for every <see cref="SwingingBell"/>; canonical asset auto-loaded from
    /// <c>Assets/Resources/BellConfig.asset</c>, per-bell override optional.</summary>
    [CreateAssetMenu(fileName = "BellConfig", menuName = "SlimeJumping/Bell/Config", order = 0)]
    public sealed class BellConfig : ScriptableObject
    {
        [Header("Geometry — bell visual's local units (mesh units), single source of truth")]
        [Tooltip("Height of the hang point on the bell visual's Y axis. The bell swings about this. " +
                 "KayKit bell: the ring's inner top sits at 0.545.")]
        public float pivotLocalY = 0.545f;
        [Tooltip("Height of the bell's centre of mass. Below the pivot. Lower = longer restoring arm " +
                 "= the bell fights harder to hang straight.")]
        public float bellComLocalY = -0.15f;
        [Tooltip("Height of the clapper's own rotation axis — where it hangs from inside the bell. " +
                 "KayKit clapper: the shaft top at 0.208.")]
        public float clapperAxisLocalY = 0.2078f;
        [Tooltip("Height of the clapper ball's centre. KayKit clapper: -0.412.")]
        public float clapperComLocalY = -0.4123f;

        [Header("Bell — the heavy part")]
        [Tooltip("Bell mass. Only sets how little a given slam moves it; the swing speed is Swing Period.")]
        [Min(0.01f)] public float bellMass = 200f;
        [Tooltip("Seconds for one full swing back and forth at small amplitude. THIS is the heaviness — " +
                 "3-5s reads as a massive bronze bell. There is a floor set by the geometry (about 3.7s " +
                 "at the default arm); ask for less and the bell logs a warning and uses the floor.")]
        [Min(0.05f)] public float bellSwingPeriod = 3.8f;
        [Tooltip("Viscous damping on the bell. Higher = fewer swings before it hangs still again. Read " +
                 "against the bell's inertia, which is in the thousands here — a few hundred still rings " +
                 "for a minute, and it takes about 1200 to be done inside three seconds.")]
        [Min(0f)] public float bellDamping = 1200f;
        [Tooltip("Hard cap on swing angle (degrees each way). Stops a huge slam from spinning it over the top.")]
        [Range(5f, 170f)] public float maxBellAngle = 85f;

        [Header("Clapper — the part that makes the sound")]
        [Tooltip("Clapper mass. Real bells run 28:1 to 57:1 against the bell, so keep this small.")]
        [Min(0.001f)] public float clapperMass = 5f;
        [Tooltip("How far the clapper's mass is spread along its shaft. 1 = all of it in the ball, which is " +
                 "the fastest a clapper of this length can possibly swing; higher = mass spread up the " +
                 "shaft, so it swings lazier and strikes later. A period knob would be dead here — the arm " +
                 "length already fixes the minimum period at 2*pi*sqrt(arm/g).")]
        [Range(1f, 4f)] public float clapperInertiaSpread = 1f;
        [Tooltip("Viscous damping between clapper and bell. Higher = the clapper stops rattling sooner.")]
        [Min(0f)] public float clapperDamping = 3f;
        [Tooltip("Angle between clapper and bell at which the ball reaches the bell wall (degrees). " +
                 "Derived from the KayKit mesh: asin((innerRadius - ballRadius) / clapperArm) = 28.9.")]
        [Range(1f, 89f)] public float strikeAngle = 28.9f;
        [Tooltip("Energy kept in the clapper's relative motion after it hits the wall. 0.05 is the " +
                 "measured value on a real bell — nearly all of it goes into the sound.")]
        [Range(0f, 1f)] public float strikeRestitution = 0.05f;
        [Tooltip("How far the clapper must come off the wall (degrees) before the next contact counts as " +
                 "a new strike. Below this it is resting against the wall, not hitting it.")]
        [Range(0.05f, 10f)] public float clapperSeparationAngle = 1.5f;

        [Header("Slime impact — how a body slam enters the bell")]
        [Tooltip("How far a slam at Slam Reference Speed, landing on the bell's flank, swings the bell " +
                 "(degrees from rest). This is the whole strength knob — it is converted to an angular " +
                 "kick against whatever inertia the mass and period imply, so retuning the bell's " +
                 "heaviness does not silently make slams stop registering.")]
        [Range(0f, 90f)] public float slamSwingDegrees = 50f;
        [Tooltip("The slam speed (world u/s) that counts as a full-strength hit. Faster scales up, " +
                 "slower scales down, both linearly.")]
        [Min(0.1f)] public float slamReferenceSpeed = 10f;
        [Tooltip("Slam speed below which nothing happens (world u/s). Stops resting contact chatter " +
                 "from pumping the bell.")]
        [Min(0f)] public float minImpactSpeed = 2f;
        [Tooltip("Cap on the angular speed a single slam can add (rad/s).")]
        [Min(0f)] public float maxImpactKick = 1.3f;
        [Tooltip("Angular speed a registered slam adds at minimum (rad/s). The ringing pattern only holds " +
                 "over a narrow band of kick — below it the bell stays quiet for seconds and then rings " +
                 "on its own, which reads as a haunted bell. Any hit strong enough to count gets at least this.")]
        [Min(0f)] public float minImpactKick = 0.9f;
        [Tooltip("Smallest moment arm a slam is allowed to have, as a fraction of its distance from the " +
                 "pivot. A slam aimed straight at the pivot has no arm and would rigidly produce no swing " +
                 "at all — which reads as the bell randomly ignoring hits. This floors it so every solid " +
                 "hit shoves the bell the way the slime was travelling. 0 = strict rigid-body torque.")]
        [Range(0f, 1f)] public float minLeverFraction = 0.6f;
        [Tooltip("Seconds after a slam before another one counts.")]
        [Min(0f)] public float impactCooldown = 0.12f;
        [Tooltip("How strongly a slime clinging to the bell tilts it by its own weight. 0 = weightless rider.")]
        [Min(0f)] public float riderWeightScale = 1f;

        [Header("Bounce off — fast arrivals slam, slow ones cling")]
        [Tooltip("Arrival speed (world u/s) at or above which the slime is thrown off the bell instead of " +
                 "sticking to it. Below this it clings and crawls like any other surface. Set very high " +
                 "to always cling, 0 to never cling.")]
        [Min(0f)] public float bounceSpeedThreshold = 6f;
        [Tooltip("Speed (world u/s) the slime leaves with, straight out of the bell's flank. Flat, not " +
                 "scaled by how hard it arrived — this only has to break the grip, not launch the slime.")]
        [Min(0f)] public float bounceSpeed = 2.5f;
        [Tooltip("How much of the bell's own surface motion is added to the throw. 1 = a bell already " +
                 "swinging toward the slime hurls it harder, 0 = the throw ignores the swing.")]
        [Range(0f, 1f)] public float bounceCarriesSwing = 0f;

        [Header("Sound")]
        [Tooltip("Strike clips ordered soft to loud. The clapper's closing speed picks one.")]
        public AudioClip[] strikeClips;
        [Tooltip("Clapper closing speed (rad/s) that counts as a full-strength strike. Measured strikes " +
                 "from a solid slam land around 0.9-1.5, so anything much above that keeps the bell " +
                 "permanently quiet and stuck on the soft clips.")]
        [Min(0.01f)] public float soundRefSpeed = 1.2f;
        [Tooltip("Closing speed below which a wall contact is silent. The clapper bounces on the wall " +
                 "several times per strike — this keeps only the first, real hit.")]
        [Min(0f)] public float minStrikeSpeed = 0.12f;
        [Tooltip("Seconds of silence after a strike.")]
        [Min(0f)] public float strikeSoundCooldown = 0.25f;
        [Tooltip("Volume against normalized strike strength (0..1). The floor matters more than the top — " +
                 "a soft strike is still a struck bronze bell, not a whisper.")]
        public AnimationCurve volumeCurve = AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1f);
        [Tooltip("Overall loudness multiplier on top of the curve, for matching the bell to the rest of the mix.")]
        [Range(0f, 4f)] public float volumeScale = 1.6f;
        [Tooltip("Pitch against normalized strike strength (0..1). A harder hit rings slightly brighter.")]
        public AnimationCurve pitchCurve = AnimationCurve.EaseInOut(0f, 0.94f, 1f, 1.06f);
        [Tooltip("Random pitch spread added on every strike, so repeats never sound identical.")]
        [Range(0f, 0.5f)] public float pitchJitter = 0.03f;

        [Header("Solver")]
        [Tooltip("Physics substeps per FixedUpdate. Higher resolves the clapper hitting the wall closer " +
                 "to the exact instant it happens.")]
        [Range(1, 16)] public int substeps = 4;

        // ---------------------------------------------------------------------

        const string ResourcesPath = "BellConfig";
        static BellConfig fallback;

        /// <summary>Override, else the Resources asset, else a shared in-memory defaults instance.</summary>
        public static BellConfig Resolve(BellConfig @override)
        {
            if (@override != null) return @override;

            var asset = Resources.Load<BellConfig>(ResourcesPath);
            if (asset != null) return asset;

            if (fallback == null)
            {
                fallback = CreateInstance<BellConfig>();
                Debug.LogWarning("BellConfig: no Assets/Resources/BellConfig.asset found — running on " +
                                 "built-in defaults. Create one via Assets > Create > SlimeJumping > Bell > Config.");
            }
            return fallback;
        }
    }
}
