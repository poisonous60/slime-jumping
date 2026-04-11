using SlimeJumping.Controller;

namespace SlimeJumping.Interfaces
{
    /// <summary>
    /// Composable effect on a platform surface. Multiple IPlatformEffect components
    /// can coexist on the same GameObject (e.g. ConveyorBelt + CrumblingEffect).
    /// Dispatched by SlimeController when the slime attaches/detaches.
    /// </summary>
    public interface IPlatformEffect
    {
        void OnSlimeAttach(SlimeController slime);
        void OnSlimeDetach(SlimeController slime);
        void OnSlimeStay(SlimeController slime, float dt);
    }
}
