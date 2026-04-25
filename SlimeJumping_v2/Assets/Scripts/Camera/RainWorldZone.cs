using UnityEngine;
using Unity.Cinemachine;
using SlimeJumping.Controller;

namespace SlimeJumping.Camera
{
    /// <summary>
    /// Rain World style zone camera mode.
    /// Outer trigger: slime entry boots the zone, exit restores the global VCam.
    /// Inside the zone, child RainWorldSubRoom boxes drive sub-room VCams. Moving
    /// between sub-rooms is a hard cut (CinemachineBrain.DefaultBlend forced to Cut
    /// for one frame). Entering and leaving the zone uses the brain's normal blend
    /// (typically EaseInOut), so the asymmetry described in the design lands:
    /// smooth in / out, snap between rooms.
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public class RainWorldZone : MonoBehaviour
    {
        [Tooltip("Sub-rooms inside this zone. Order matters when boxes overlap — first match wins for the active room test.")]
        [SerializeField] private RainWorldSubRoom[] subRooms;

        [Tooltip("Brain on the Main Camera. Default blend is temporarily set to Cut while switching between sub-rooms.")]
        [SerializeField] private CinemachineBrain brain;

        [Tooltip("Priority assigned to the active sub-room VCam. Must exceed the global SlimeVCam priority (which defaults to 0).")]
        [SerializeField] private int activePriority = 100;

        [Tooltip("Priority assigned to inactive sub-room VCams. Must be BELOW the global SlimeVCam priority (which defaults to 0) so the global camera wins when the slime leaves the zone — otherwise a priority tie keeps Brain stuck on the sub-room VCam after exit.")]
        [SerializeField] private int inactivePriority = -100;

        private BoxCollider2D zoneTrigger;
        private SlimeController trackedSlime;
        private RainWorldSubRoom activeSubRoom;
        private int overlappingColliderCount;
        private bool isInZone;

        // Brain default blend snapshot. Captured the first time we override it,
        // restored when we exit the zone or finish a hard cut.
        private bool brainBlendSnapshotted;
        private CinemachineBlendDefinition originalDefaultBlend;
        private Coroutine restoreBlendCo;

        private void Reset()
        {
            GetComponent<BoxCollider2D>().isTrigger = true;
        }

        private void Awake()
        {
            zoneTrigger = GetComponent<BoxCollider2D>();
            zoneTrigger.isTrigger = true;

            if (subRooms != null)
            {
                for (int i = 0; i < subRooms.Length; i++)
                {
                    var sr = subRooms[i];
                    if (sr != null && sr.VCam != null)
                        sr.VCam.Priority = inactivePriority;
                }
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var slime = other.GetComponentInParent<SlimeController>();
            if (slime == null) return;

            overlappingColliderCount++;
            if (trackedSlime == slime) return;

            trackedSlime = slime;
            EnterZone(slime);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            var slime = other.GetComponentInParent<SlimeController>();
            if (slime == null || slime != trackedSlime) return;

            overlappingColliderCount--;
            if (overlappingColliderCount > 0) return;

            ExitZone();
            trackedSlime = null;
        }

        private void Update()
        {
            if (!isInZone || trackedSlime == null || subRooms == null) return;

            Vector2 slimePos = trackedSlime.Position;
            RainWorldSubRoom inside = FindContainingSubRoom(slimePos);

            // Outside every sub-room (gap between rooms): keep the previous active
            // room so the camera doesn't pop back to global mid-zone.
            if (inside == null) return;
            if (inside == activeSubRoom) return;

            HardCutTo(inside);
        }

        private RainWorldSubRoom FindContainingSubRoom(Vector2 worldPos)
        {
            for (int i = 0; i < subRooms.Length; i++)
            {
                var sr = subRooms[i];
                if (sr != null && sr.Contains(worldPos)) return sr;
            }
            return null;
        }

        private void EnterZone(SlimeController slime)
        {
            isInZone = true;

            RainWorldSubRoom first = FindContainingSubRoom(slime.Position);
            if (first == null && subRooms != null && subRooms.Length > 0)
                first = subRooms[0];

            if (first != null && first.VCam != null)
            {
                activeSubRoom = first;
                first.VCam.Priority = activePriority;
            }
        }

        private void ExitZone()
        {
            isInZone = false;

            if (activeSubRoom != null && activeSubRoom.VCam != null)
                activeSubRoom.VCam.Priority = inactivePriority;
            activeSubRoom = null;

            // Cancel any pending hard-cut restore — we're restoring right now.
            if (restoreBlendCo != null)
            {
                StopCoroutine(restoreBlendCo);
                restoreBlendCo = null;
            }
            RestoreBrainBlend();
        }

        private void HardCutTo(RainWorldSubRoom newRoom)
        {
            if (brain != null && !brainBlendSnapshotted)
            {
                originalDefaultBlend = brain.DefaultBlend;
                brainBlendSnapshotted = true;
            }
            if (brain != null)
            {
                brain.DefaultBlend = new CinemachineBlendDefinition(
                    CinemachineBlendDefinition.Styles.Cut, 0f);
            }

            if (activeSubRoom != null && activeSubRoom.VCam != null)
                activeSubRoom.VCam.Priority = inactivePriority;

            activeSubRoom = newRoom;
            if (newRoom.VCam != null)
                newRoom.VCam.Priority = activePriority;

            // Restore the smooth blend two frames later — Brain consumes the cut
            // on its next blend evaluation, after which the zone-enter / zone-exit
            // transitions need to fade again.
            if (restoreBlendCo != null) StopCoroutine(restoreBlendCo);
            restoreBlendCo = StartCoroutine(RestoreBrainBlendNextFrame());
        }

        private System.Collections.IEnumerator RestoreBrainBlendNextFrame()
        {
            yield return null;
            yield return null;
            RestoreBrainBlend();
            restoreBlendCo = null;
        }

        private void RestoreBrainBlend()
        {
            if (brain != null && brainBlendSnapshotted)
            {
                brain.DefaultBlend = originalDefaultBlend;
                brainBlendSnapshotted = false;
            }
        }

        private void OnDisable()
        {
            // Disabling mid-overlap would otherwise leave the active sub-room VCam
            // stuck at high priority and Brain stuck on Cut.
            if (isInZone) ExitZone();
            overlappingColliderCount = 0;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            var col = GetComponent<BoxCollider2D>();
            if (col == null) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.08f);
            Gizmos.DrawCube(col.offset, col.size);
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.9f);
            Gizmos.DrawWireCube(col.offset, col.size);
        }
#endif
    }
}
