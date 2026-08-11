using UnityEngine;

namespace SlimeJumping.Platforms.Bell
{
    /// <summary>Shared tuning for every <see cref="SwingingBellV2"/>; canonical asset auto-loaded from
    /// <c>Assets/Resources/BellV2Config.asset</c>, per-bell override optional.
    ///
    /// The geometry and pendulum blocks are the tuned values from the physics bell and only drive the
    /// swing the player sees. Ringing, sound and the throw are decided at the contact frame instead,
    /// so nothing below the Contact header feeds the pendulum or reads from it.</summary>
    [CreateAssetMenu(fileName = "BellV2Config", menuName = "SlimeJumping/Bell/Config V2", order = 1)]
    public sealed class BellV2Config : ScriptableObject
    {
        [Header("Geometry — bell visual's local units (mesh units), single source of truth")]
        [Tooltip("Height of the hang point on the bell visual's Y axis. The bell swings about this. " +
                 "KayKit bell: the ring's inner top sits at 0.545.")]
        public float pivotLocalY = 0.545f;
        [Tooltip("Height of the bell's centre of mass. Below the pivot. Lower = longer restoring arm.")]
        public float bellComLocalY = -0.15f;
        [Tooltip("Height of the clapper's own rotation axis — where it hangs from inside the bell.")]
        public float clapperAxisLocalY = 0.2078f;
        [Tooltip("Height of the clapper ball's centre. KayKit clapper: -0.412.")]
        public float clapperComLocalY = -0.4123f;

        [Header("Swing — visual only, nothing here decides whether the bell rings")]
        [Tooltip("Bell mass. Only sets how little a given hit moves it; the swing speed is Swing Period.")]
        [Min(0.01f)] public float bellMass = 200f;
        [Tooltip("Seconds for one full swing back and forth at small amplitude. THIS is the heaviness — " +
                 "3-5s reads as a massive bronze bell. There is a floor set by the geometry; ask for " +
                 "less and the bell logs a warning and uses the floor.")]
        [Min(0.05f)] public float bellSwingPeriod = 4.6f;
        [Tooltip("Viscous damping on the bell. Read against its inertia, which is in the thousands — " +
                 "it takes about 1200 to settle inside three seconds.")]
        [Min(0f)] public float bellDamping = 1200f;
        [Tooltip("Hard cap on swing angle (degrees each way). Stops a huge hit from spinning it over the top.")]
        [Range(5f, 170f)] public float maxBellAngle = 85f;
        [Tooltip("How far a full-strength hit swings the bell (degrees from rest). Reached at Full " +
                 "Swing Speed and never exceeded, however much faster the slime was going.")]
        [Range(0f, 90f)] public float swingDegrees = 47f;
        [Tooltip("The swing a touch too soft to ring still gets (degrees). Never 0 — every contact " +
                 "moves the bell, a silent one just moves it a little.")]
        [Range(0f, 90f)] public float softSwingDegrees = 12f;
        [Tooltip("Closing speed (world u/s) at which the bell takes its full swing. Between Min Ring " +
                 "Speed and this the swing ramps from soft to full; above it nothing changes. Keep the " +
                 "window narrow and the two swing angles close if you want the bell to read the same " +
                 "however it was hit.")]
        [Min(0.1f)] public float fullSwingSpeed = 9f;

        [Header("Clapper — swings inside, purely decorative")]
        [Tooltip("Clapper mass. Real bells run 28:1 to 57:1 against the bell, so keep this small.")]
        [Min(0.001f)] public float clapperMass = 5f;
        [Tooltip("How far the clapper's mass is spread along its shaft. 1 = all of it in the ball, the " +
                 "fastest a clapper of this length can swing; higher = lazier, striking later.")]
        [Range(1f, 4f)] public float clapperInertiaSpread = 1.5f;
        [Tooltip("Viscous damping between clapper and bell. Higher = the clapper stops rattling sooner.")]
        [Min(0f)] public float clapperDamping = 3f;
        [Tooltip("Angle between clapper and bell at which the ball reaches the bell wall (degrees).")]
        [Range(1f, 89f)] public float strikeAngle = 20f;
        [Tooltip("Energy kept in the clapper's relative motion after it hits the wall.")]
        [Range(0f, 1f)] public float strikeRestitution = 0.4f;
        [Tooltip("How far the clapper must come off the wall (degrees) before the next contact counts " +
                 "as a fresh one rather than resting against it.")]
        [Range(0.05f, 10f)] public float clapperSeparationAngle = 1.5f;

        [Header("Contact — no direction condition; speed decides the sound, not the reaction")]
        [Tooltip("Contact speed (world u/s, flank-relative, any direction) a touch needs to actually " +
                 "sound the bell. Below it the bell still swings and the slime still bounces — it is " +
                 "just silent. Grazes along the flank count the same as head-on slams.")]
        [Min(0f)] public float minRingSpeed = 3f;
        [Tooltip("Seconds before the same slime can set this bell off again. 60-120ms is the range " +
                 "commercial bounce objects use.")]
        [Min(0f)] public float retriggerCooldown = 0.1f;
        [Tooltip("How strongly a slime clinging to the bell tilts it by its own weight. Only a charging " +
                 "slime stays on long enough to matter — any other touch is thrown clear at once. " +
                 "0 = weightless rider.")]
        [Min(0f)] public float riderWeightScale = 1f;

        [Header("Bounce — always straight out of the flank; only the speed comes from the slime")]
        [Tooltip("Fraction of its own arrival speed the slime leaves at. 1 = as fast as it arrived, " +
                 "0.5 = half. The bell adds nothing of its own, not even its swing, so how far the " +
                 "slime goes is how hard it came in — hit it hard and you are thrown far, drift into " +
                 "it and you barely move.")]
        [Range(0f, 1f)] public float bounceRestitution = 0.85f;
        [Tooltip("Slowest speed (world u/s) the slime may leave the flank at. The slime never sticks " +
                 "to a bell, so a drift-in that would reflect at almost nothing is pushed off at this " +
                 "instead. Keep it small — it is a floor, not a launch.")]
        [Min(0.01f)] public float minSeparationSpeed = 2f;
        [Tooltip("Seconds after a bounce during which the slime cannot re-stick to this bell. Without " +
                 "it the slime bounces and re-sticks in the same breath, which reads as never having " +
                 "bounced. Physics stays on, so it still rebounds off the bell.")]
        [Min(0f)] public float reattachBlockSeconds = 0.2f;

        [Header("Sound — played on contact, not when the clapper lands")]
        [Tooltip("Flat, not scaled by arrival speed — a struck bronze bell is loud however it was struck.")]
        public SlimeJumping.Audio.SoundDef strikeSound;

        [Header("Solver")]
        [Tooltip("Physics substeps per FixedUpdate for the swing.")]
        [Range(1, 16)] public int substeps = 4;

        // ---------------------------------------------------------------------

        const string ResourcesPath = "BellV2Config";
        static BellV2Config fallback;

        /// <summary>Override, else the Resources asset, else a shared in-memory defaults instance.</summary>
        public static BellV2Config Resolve(BellV2Config @override)
        {
            if (@override != null) return @override;

            var asset = Resources.Load<BellV2Config>(ResourcesPath);
            if (asset != null) return asset;

            if (fallback == null)
            {
                fallback = CreateInstance<BellV2Config>();
                Debug.LogWarning("BellV2Config: no Assets/Resources/BellV2Config.asset found — running " +
                                 "on built-in defaults. Create one via Assets > Create > SlimeJumping > " +
                                 "Bell > Config V2.");
            }
            return fallback;
        }
    }
}
