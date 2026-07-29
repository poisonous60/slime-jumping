using SlimeJumping.Controller;

namespace SlimeJumping.Interfaces
{
    /// <summary>Active override on SlimeController: takes over Update/FixedUpdate; the controller's state machine, input and physics are suspended.</summary>
    public interface IExternalController
    {
        void OnTakeControl(SlimeController slime);
        void OnReleaseControl(SlimeController slime);
        void ExternalUpdate(SlimeController slime);
        void ExternalFixedUpdate(SlimeController slime, float dt);
    }
}
