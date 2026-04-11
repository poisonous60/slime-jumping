using SlimeJumping.Controller;

namespace SlimeJumping.Interfaces
{
    /// <summary>
    /// When set as the active override on SlimeController, receives
    /// full control of Update and FixedUpdate. SlimeController's own
    /// state machine, input handlers, and physics are suspended.
    /// Used by: snake/big slime swallow, bench sitting, puzzle overlay, cinematic intro.
    /// </summary>
    public interface IExternalController
    {
        void OnTakeControl(SlimeController slime);
        void OnReleaseControl(SlimeController slime);
        void ExternalUpdate(SlimeController slime);
        void ExternalFixedUpdate(SlimeController slime, float dt);
    }
}
