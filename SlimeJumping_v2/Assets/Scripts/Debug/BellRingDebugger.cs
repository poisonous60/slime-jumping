using System.Reflection;
using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Platforms.Bell;

namespace SlimeJumping.Debugging
{
    /// <summary>Read-only probe for the V2 bell ring chain. Attached to the Slime GameObject
    /// (driver.mjs attach), resolves every bell in Start and logs one [BellRing] line per
    /// FixedUpdate while a bell is inside LogRadius: the three arrival-speed candidates the Ring
    /// gate picks from, the occupied/riding/tracked latches, and the retrigger timer. A visit that
    /// ends without a ring is promoted to a LogWarning marked <<< BELL-SILENT; every ring logs
    /// <<< BELL-RING with its evidence. Pulls everything via public getters and reflection —
    /// writes nothing back. Remove the component (or delete the file) to remove the tool.</summary>
    [DefaultExecutionOrder(3002)]
    public class BellRingDebugger : MonoBehaviour
    {
        const float LogRadius = 8f;
        const float GraceSeconds = 0.5f;

        static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly FieldInfo FOccupied   = typeof(SwingingBellV2).GetField("occupied", Priv);
        static readonly FieldInfo FRiding     = typeof(SwingingBellV2).GetField("riding", Priv);
        static readonly FieldInfo FRetrigger  = typeof(SwingingBellV2).GetField("retriggerTimer", Priv);
        static readonly FieldInfo FTracked    = typeof(SwingingBellV2).GetField("trackedSlime", Priv);
        static readonly FieldInfo FPrevVel    = typeof(SwingingBellV2).GetField("prevSlimeVel", Priv);
        static readonly FieldInfo FArrival    = typeof(SwingingBellV2).GetField("riderArrivalVel", Priv);
        static readonly FieldInfo FRelayCount = typeof(SwingingBell).GetField("impactCount", Priv);
        static readonly FieldInfo FRelayRing  = typeof(SwingingBell).GetField("lastRingResult", Priv);

        SlimeController slime;
        SwingingBellV2[] bells;
        int[] lastRingCounts;
        bool[] visitOpen;
        float[] visitClock;
        bool[] visitRang;

        void Start()
        {
            slime = GetComponent<SlimeController>();
            if (slime == null) slime = FindFirstObjectByType<SlimeController>();
            bells = FindObjectsByType<SwingingBellV2>(FindObjectsSortMode.None);
            lastRingCounts = new int[bells.Length];
            visitOpen  = new bool[bells.Length];
            visitClock = new float[bells.Length];
            visitRang  = new bool[bells.Length];
            for (int i = 0; i < bells.Length; i++) lastRingCounts[i] = bells[i] != null ? bells[i].RingCount : 0;
            Debug.Log($"[BellRing] debugger active — slime={(slime != null)} bells={bells.Length}");
        }

        void FixedUpdate()
        {
            if (slime == null || bells == null) return;
            Vector2 slimePos = slime.Position;

            for (int i = 0; i < bells.Length; i++)
            {
                var bell = bells[i];
                if (bell == null || !bell.isActiveAndEnabled) continue;
                float dist = Vector2.Distance(slimePos, bell.transform.position);

                int rings = bell.RingCount;
                bool rangNow = rings != lastRingCounts[i];
                lastRingCounts[i] = rings;

                bool occupied = (bool)FOccupied.GetValue(bell);
                bool riding = (bool)FRiding.GetValue(bell);

                // Visit = occupied latch or a ring; silence is judged when the visit closes or times out.
                if ((occupied || rangNow) && !visitOpen[i]) { visitOpen[i] = true; visitClock[i] = 0f; visitRang[i] = false; }
                if (rangNow) visitRang[i] = true;

                if (dist > LogRadius && !visitOpen[i]) continue;

                float retrig = (float)FRetrigger.GetValue(bell);
                var tracked = FTracked.GetValue(bell) as SlimeController;
                var prevVel = (Vector2)FPrevVel.GetValue(bell);
                var arrVel = (Vector2)FArrival.GetValue(bell);
                var relay = bell.GetComponent<SwingingBell>();
                int relayCount = relay != null ? (int)FRelayCount.GetValue(relay) : -1;

                Debug.Log($"[BellRing] #{Time.frameCount} {bell.name} d={dist:F2} occ={occupied} rid={riding} " +
                          $"trk={(tracked != null)} retrig={retrig:F2} vNow={slime.Velocity.magnitude:F2} " +
                          $"vPrev={prevVel.magnitude:F2} vArr={arrVel.magnitude:F2} rings={rings} " +
                          $"relayImpacts={relayCount} slimeState={slime.CurrentState} " +
                          $"minRing={bell.Config.minRingSpeed:F1}");

                if (rangNow)
                {
                    string relayResult = relay != null ? (string)FRelayRing.GetValue(relay) : "no relay";
                    Debug.LogWarning($"<<< BELL-RING {bell.name} #{Time.frameCount} rings={rings} " +
                                     $"relay='{relayResult}' vNow={slime.Velocity.magnitude:F2} " +
                                     $"vPrev={prevVel.magnitude:F2} vArr={arrVel.magnitude:F2}");
                }

                if (visitOpen[i])
                {
                    visitClock[i] += Time.fixedDeltaTime;
                    bool closed = !occupied && !rangNow && visitClock[i] > 0.1f;
                    if ((closed || visitClock[i] >= GraceSeconds) && !visitRang[i])
                        Debug.LogWarning($"<<< BELL-SILENT {bell.name} #{Time.frameCount} visit ended, no ring — " +
                                         $"occ={occupied} rid={riding} trk={(tracked != null)} retrig={retrig:F2} " +
                                         $"vNow={slime.Velocity.magnitude:F2} vPrev={prevVel.magnitude:F2} " +
                                         $"vArr={arrVel.magnitude:F2} minRing={bell.Config.minRingSpeed:F1}");
                    if (closed || visitClock[i] >= GraceSeconds) visitOpen[i] = false;
                }
            }
        }
    }
}
