using SlimeJumping.Controller;

namespace SlimeJumping.Interfaces
{
    /// <summary>Composable platform-surface effect; several may coexist on one GameObject. Dispatched by SlimeController.</summary>
    public interface IPlatformEffect
    {
        void OnSlimeAttach(SlimeController slime);
        void OnSlimeDetach(SlimeController slime);
        void OnSlimeStay(SlimeController slime, float dt);
    }
}
