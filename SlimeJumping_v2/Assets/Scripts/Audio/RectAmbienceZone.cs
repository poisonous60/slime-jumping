using UnityEngine;
using UnityCamera = UnityEngine.Camera;

namespace SlimeJumping.Audio
{
    /// <summary>
    /// Looping ambience audible inside a rectangle instead of a sphere. A 3D AudioSource can only fall
    /// off radially, which cannot express a region that is wide and short, so the loop plays 2D and this
    /// drives its level from the listener's XY position: full inside the rect, ramping to silence across
    /// falloff. Both rectangles are drawn as gizmos, so the audible region is what you see in the scene.
    /// </summary>
    [AddComponentMenu("SlimeJumping/Audio/Rect Ambience Zone")]
    public sealed class RectAmbienceZone : MonoBehaviour
    {
        [SerializeField] private SoundDef sound;

        [Tooltip("Full volume region, centred on this object.")]
        [SerializeField] private Vector2 size = new Vector2(96f, 112f);

        [Tooltip("World units the volume takes to reach silence past the left and right edges.")]
        [Min(0.01f)][SerializeField] private float falloffSides = 20f;

        [Tooltip("World units the volume takes to reach silence above the top edge.")]
        [Min(0.01f)][SerializeField] private float falloffUp = 20f;

        [Tooltip("World units the volume takes to reach silence below the bottom edge.")]
        [Min(0.01f)][SerializeField] private float falloffDown = 20f;

        [Tooltip("Listener to measure against. Falls back to Camera.main.")]
        [SerializeField] private UnityCamera listener;

        [Tooltip("Name it and the whole group shares ONE loop, driven by whichever member is loudest. " +
                 "Seven saw blades in earshot would otherwise stack into a wall of noise and hold " +
                 "seven of the category's eight pooled sources.")]
        [SerializeField] private string exclusiveGroup;

        private static readonly System.Collections.Generic.List<RectAmbienceZone> live =
            new System.Collections.Generic.List<RectAmbienceZone>();

        /// One loop per group, owned by the group rather than by whichever zone is currently
        /// nearest. Handing the same running source between members is what makes the crossover
        /// silent: stopping one loop and starting another restarts the clip at sample 0 (a click),
        /// and re-rolls its level (a step). The holder only decides who computes the volume.
        private sealed class GroupLoop
        {
            public SoundHandle handle;
            public SoundDef def;
            public float volumeRoll;
            public RectAmbienceZone holder;
        }

        private static readonly System.Collections.Generic.Dictionary<string, GroupLoop> groupLoops =
            new System.Collections.Generic.Dictionary<string, GroupLoop>();

        // Incumbent's edge in the loudness contest — without it two zones straddling the listener trade the loop every frame.
        private const float HolderBias = 0.05f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Handles here outlive a no-domain-reload Play entry, pointing at last run's sources.
            live.Clear();
            groupLoops.Clear();
        }

        private SoundHandle handle;
        private float volumeRoll;

        // Keep the roll, not the volume: re-rolling per frame would make a randomised range flicker.
        private void OnEnable()
        {
            volumeRoll = Random.value;
            live.Add(this);
            if (string.IsNullOrEmpty(exclusiveGroup)) StartLoop();
        }

        private void OnDisable()
        {
            live.Remove(this);
            StopLoop();

            if (!string.IsNullOrEmpty(exclusiveGroup)
                && groupLoops.TryGetValue(exclusiveGroup, out var gl) && gl.holder == this)
            {
                // The next winner inherits the running source; the last member out strands it.
                if (GroupHasMember(exclusiveGroup)) gl.holder = null;
                else StopGroupLoop(gl);
            }
        }

        private void StartLoop()
        {
            if (sound == null || handle.IsPlaying) return;
            handle = SoundService.PlayLooping(sound, transform);
            handle.SetVolume(0f);
        }

        private void StopLoop()
        {
            SoundService.StopLoop(handle);
            handle = default;
        }

        private void Update()
        {
            if (sound == null) return;

            if (!string.IsNullOrEmpty(exclusiveGroup)) { DriveGroup(); return; }

            if (listener == null) listener = UnityCamera.main;
            if (listener == null || !handle.IsValid) return;
            handle.SetVolume(Mathf.Lerp(sound.volumeRange.x, sound.volumeRange.y, volumeRoll)
                             * Attenuation(listener.transform.position));
        }

        private void DriveGroup()
        {
            var groupListener = GroupListener(exclusiveGroup);
            if (groupListener == null) return;

            if (!groupLoops.TryGetValue(exclusiveGroup, out var gl))
                groupLoops[exclusiveGroup] = gl = new GroupLoop();

            // Every member runs the same scan off the same listener, so exactly one drives.
            var winner = PickWinner(exclusiveGroup, groupListener.transform.position,
                                    gl.holder, out float loudest);
            if (winner != this) return;

            if (loudest <= 0f) { StopGroupLoop(gl); return; }

            // A different SoundDef is different content, so that crossover does restart the clip.
            if (!gl.handle.IsPlaying || gl.def != sound)
            {
                StopGroupLoop(gl);
                gl.def = sound;
                gl.volumeRoll = Random.value;
                gl.handle = SoundService.PlayLooping(sound, null);
            }

            gl.holder = this;
            gl.handle.SetPosition(transform.position);
            // Level tracks the group's loudest RAW attenuation, so the holder bias never steps it.
            gl.handle.SetVolume(Mathf.Lerp(sound.volumeRange.x, sound.volumeRange.y, gl.volumeRoll) * loudest);
        }

        // Holder goes with the source: a bias for an incumbent that stopped protects nothing.
        private static void StopGroupLoop(GroupLoop gl)
        {
            SoundService.StopLoop(gl.handle);
            gl.handle = default;
            gl.def = null;
            gl.holder = null;
        }

        // ponytail: every member rescans its group each frame — a group is a handful of blades.
        private static RectAmbienceZone PickWinner(string group, Vector3 listenerPos,
                                                   RectAmbienceZone holder, out float loudest)
        {
            RectAmbienceZone best = null;
            float bestScore = float.NegativeInfinity;
            loudest = 0f;
            for (int i = 0; i < live.Count; i++)
            {
                var z = live[i];
                if (z == null || z.sound == null || z.exclusiveGroup != group) continue;
                float raw = z.Attenuation(listenerPos);
                if (raw > loudest) loudest = raw;
                // A silent holder must not out-score an audible member, or the group goes quiet.
                float score = raw + (z == holder && raw > 0f ? HolderBias : 0f);
                // Instance id breaks exact ties, so all members agree on the same single winner.
                if (score > bestScore || (score == bestScore && best != null
                                          && z.GetInstanceID() < best.GetInstanceID()))
                {
                    bestScore = score;
                    best = z;
                }
            }
            return best;
        }

        // One listener for the whole group: two members disagreeing would both claim the loop.
        private static UnityCamera GroupListener(string group)
        {
            for (int i = 0; i < live.Count; i++)
            {
                var z = live[i];
                if (z != null && z.sound != null && z.exclusiveGroup == group && z.listener != null)
                    return z.listener;
            }
            return UnityCamera.main;
        }

        // Soundless members never reach DriveGroup, so they cannot inherit the loop or stop it.
        private static bool GroupHasMember(string group)
        {
            for (int i = 0; i < live.Count; i++)
            {
                var z = live[i];
                if (z != null && z.sound != null && z.exclusiveGroup == group) return true;
            }
            return false;
        }

        // Each axis normalises by its own falloff before combining, else the longest ramp drags the corners.
        private float Attenuation(Vector3 listenerPos)
        {
            Vector3 c = transform.position;
            float outX = Mathf.Max(Mathf.Abs(listenerPos.x - c.x) - size.x * 0.5f, 0f) / falloffSides;
            float dy = listenerPos.y - c.y;
            float outY = Mathf.Max(Mathf.Abs(dy) - size.y * 0.5f, 0f) / (dy >= 0f ? falloffUp : falloffDown);
            return 1f - Mathf.Clamp01(new Vector2(outX, outY).magnitude);
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 c = transform.position;
            float hx = size.x * 0.5f, hy = size.y * 0.5f;

            Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.9f);
            DrawRect(c.x - hx, c.y - hy, c.x + hx, c.y + hy);

            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.7f);
            DrawRect(c.x - hx - falloffSides, c.y - hy - falloffDown, c.x + hx + falloffSides, c.y + hy + falloffUp);
        }

        private static void DrawRect(float minX, float minY, float maxX, float maxY)
        {
            Vector3 p0 = new Vector3(minX, minY, 0f);
            Vector3 p1 = new Vector3(maxX, minY, 0f);
            Vector3 p2 = new Vector3(maxX, maxY, 0f);
            Vector3 p3 = new Vector3(minX, maxY, 0f);
            Gizmos.DrawLine(p0, p1);
            Gizmos.DrawLine(p1, p2);
            Gizmos.DrawLine(p2, p3);
            Gizmos.DrawLine(p3, p0);
        }
    }
}
