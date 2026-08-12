using System.Text;
using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;
using SlimeJumping.Platforms;

namespace SlimeJumping.Debugging
{
    /// <summary>
    /// Attach to the SLIME to find out why a PlatformCreakAudio platform is or is not groaning:
    /// reports each watched platform's rotation speed against the config's trigger, the peak it
    /// actually reached, and whether the slime is riding it. Read-only — the creak component itself
    /// carries no logging. Targets are resolved at Start, never wired by hand.
    /// </summary>
    [DefaultExecutionOrder(3000)]
    [DisallowMultipleComponent]
    [AddComponentMenu("SlimeJumping/Debug/Platform Creak Debug")]
    public class PlatformCreakDebug : MonoBehaviour
    {
        [Header("Display")]
        [SerializeField] bool showGui = true;
        [SerializeField] Vector2 guiPos = new Vector2(10f, 320f);

        [Header("Logging")]
        [Tooltip("Log a line per interval while any watched platform is turning faster than Log Speed Floor.")]
        [SerializeField] bool logSamples = true;
        [SerializeField, Min(0.05f)] float logInterval = 0.5f;
        [Tooltip("Ticks below this rotation speed (deg/s) are ignored, so a resting beam is silent in the log.")]
        [SerializeField, Min(0f)] float logSpeedFloor = 0.5f;
        [Tooltip("Log a line the moment a platform's speed crosses the config trigger — the frame the groan should fire.")]
        [SerializeField] bool logTriggerCrossings = true;

        SlimeController slime;
        PlatformCreakAudio[] creaks;
        PlatformMotor[] motors;
        float[] peakSpeed;
        bool[] overTrigger;
        PlatformCreakConfig config;
        float nextLogTime;

        void Start()
        {
            slime = GetComponent<SlimeController>();
            if (slime == null) slime = FindFirstObjectByType<SlimeController>();

            creaks = FindObjectsByType<PlatformCreakAudio>(FindObjectsSortMode.None);
            motors = new PlatformMotor[creaks.Length];
            peakSpeed = new float[creaks.Length];
            overTrigger = new bool[creaks.Length];
            for (int i = 0; i < creaks.Length; i++)
                motors[i] = creaks[i].GetComponent<PlatformMotor>();

            config = Resources.Load<PlatformCreakConfig>("PlatformCreakConfig");

            string cfgText = "NOT FOUND in Resources";
            if (config != null)
            {
                var snd = config.creakSound;
                string sndName = snd != null ? snd.name : "NULL";
                int clipCount = snd != null && snd.clips != null ? snd.clips.Length : 0;
                cfgText = $"trigger={config.triggerSpeed} rearm={config.rearmSpeed} sound={sndName} clips={clipCount}";
            }
            string svc = SoundService.Instance != null ? "ok" : "MISSING — every groan would be silent";
            Debug.Log($"[PlatformCreakDebug] watching {creaks.Length} creak platform(s), config: {cfgText}, soundService={svc}", this);
        }

        void FixedUpdate()
        {
            if (creaks == null || creaks.Length == 0) return;

            float trigger = config != null ? config.triggerSpeed : 0f;
            float fastest = 0f;

            for (int i = 0; i < creaks.Length; i++)
            {
                if (motors[i] == null) continue;
                float speed = Mathf.Abs(motors[i].AngularVelocity);
                if (speed > peakSpeed[i]) peakSpeed[i] = speed;
                if (speed > fastest) fastest = speed;

                bool over = trigger > 0f && speed >= trigger;
                if (logTriggerCrossings && over != overTrigger[i])
                {
                    Debug.Log($"[PlatformCreakDebug] {motors[i].name} {(over ? "CROSSED" : "fell under")} " +
                              $"trigger {trigger:F1} at speed={speed:F1} deg/s (peak so far {peakSpeed[i]:F1})", this);
                }
                overTrigger[i] = over;
            }

            if (!logSamples || fastest < logSpeedFloor || Time.time < nextLogTime) return;
            nextLogTime = Time.time + logInterval;

            var sb = new StringBuilder("[PlatformCreakDebug]");
            for (int i = 0; i < creaks.Length; i++)
            {
                if (motors[i] == null) continue;
                sb.Append($" {motors[i].name}: w={Mathf.Abs(motors[i].AngularVelocity):F1} peak={peakSpeed[i]:F1}" +
                          $" rot={motors[i].transform.eulerAngles.z:F1} riding={RidingIndex(i)}");
            }
            sb.Append($" | trigger={trigger:F1}");
            Debug.Log(sb.ToString(), this);
        }

        bool RidingIndex(int i)
        {
            if (slime == null) return false;
            var col = slime.SurfaceCollider;
            return col != null && col.transform.IsChildOf(motors[i].transform);
        }

        void OnGUI()
        {
            if (!showGui || creaks == null) return;

            var sb = new StringBuilder();
            sb.AppendLine($"creak platforms: {creaks.Length}   trigger: {(config != null ? config.triggerSpeed.ToString("F1") : "no config")}");
            for (int i = 0; i < creaks.Length; i++)
            {
                if (motors[i] == null) continue;
                sb.AppendLine($"{motors[i].name}  w={Mathf.Abs(motors[i].AngularVelocity):F1}  peak={peakSpeed[i]:F1}" +
                              $"  riding={RidingIndex(i)}");
            }

            GUI.Label(new Rect(guiPos.x, guiPos.y, 620f, 20f + 18f * (creaks.Length + 1)), sb.ToString());
        }
    }
}
