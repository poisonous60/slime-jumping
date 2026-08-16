using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Interaction;

namespace SlimeJumping.UI
{
    /// Puts the slime into <see cref="startInteraction"/> (BoxSleep, SitPose…) at scene load:
    /// teleported to its sitAnchor and driven by its IExternalController.
    public class SlimeStartStateManager : MonoBehaviour
    {
        public enum TriggerMode
        {
            /// Fire only when GameFlow.StartGame() brought us here (real menu→play).
            OnlyFromMenu,
            /// Fire every scene load — useful for designer iteration in the editor.
            Always,
            /// Never fire — keep the reference wired but stay inert.
            Disabled,
        }

        [SerializeField] private SlimeController slime;
        [Tooltip("Interactable (BoxSleep, SitPose, etc.) the slime begins the scene inside. Leave null to disable.")]
        [SerializeField] private InteractableBase startInteraction;
        [SerializeField] private TriggerMode triggerMode = TriggerMode.OnlyFromMenu;

        private IEnumerator Start()
        {
            if (!ShouldTrigger()) yield break;
            if (startInteraction == null)
            {
                Debug.LogWarning("[SlimeStartStateManager] No startInteraction assigned — nothing to do.", this);
                yield break;
            }

            // One-frame grace: every Awake/OnEnable must settle before we push an override.
            yield return null;

            if (slime == null) slime = Object.FindFirstObjectByType<SlimeController>();
            if (slime == null)
            {
                Debug.LogWarning("[SlimeStartStateManager] No SlimeController found in scene.", this);
                yield break;
            }
            if (slime.HasOverride) yield break;

            // 기록은 조작권을 넘겨받는 순간부터다 — 깨어나는 연출은 빼고 센다.
            RunClock.IntroHold = true;

            // Interact() bypasses CanInteract; at scene start the slime isn't inside the trigger yet.
            Vector2 before = slime.Position;
            startInteraction.Interact(slime);
            // 카메라도 즉시 워프 — 없으면 씬 저장 위치에서 시작 지점까지 댐핑 팬이 도중 존들을 지난다.
            CinemachineCore.OnTargetObjectWarped(slime.transform, (Vector3)(slime.Position - before));

            // 이 루프만 인트로 오버라이드를 본다 — 나중에 해저드가 잡는 오버라이드는 시계를 못 건드린다.
            while (slime != null && slime.HasOverride) yield return null;
            RunClock.IntroHold = false;
        }

        // 연출 도중 꺼지면 hold 가 남아 시계가 영영 안 돈다.
        private void OnDisable() => RunClock.IntroHold = false;

        private bool ShouldTrigger()
        {
            switch (triggerMode)
            {
                case TriggerMode.Always: return true;
                case TriggerMode.OnlyFromMenu: return GameFlow.WasStartedFromMenu;
                default: return false;
            }
        }
    }
}
