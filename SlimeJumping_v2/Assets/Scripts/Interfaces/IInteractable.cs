using SlimeJumping.Controller;

namespace SlimeJumping.Interfaces
{
    public interface IInteractable
    {
        bool CanInteract(SlimeController slime);
        void Interact(SlimeController slime);
        string InteractPrompt { get; }
    }
}
