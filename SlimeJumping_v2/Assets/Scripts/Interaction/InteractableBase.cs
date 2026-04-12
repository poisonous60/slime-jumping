using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Interaction
{
    public abstract class InteractableBase : MonoBehaviour, IInteractable
    {
        [SerializeField] protected Animator animator;
        [SerializeField] protected string interactPrompt = "Interact";
        [SerializeField] protected float cooldown = 0f;
        [SerializeField] protected bool startsActive = true;

        public bool IsActive { get; protected set; }
        public string InteractPrompt => interactPrompt;

        private float nextAvailableTime;

        protected virtual void Awake()
        {
            IsActive = startsActive;

            int interactableLayer = LayerMask.NameToLayer("Interactable");
            if (interactableLayer != -1 && gameObject.layer != interactableLayer)
                Debug.LogWarning($"[{GetType().Name}] {name} is not on Interactable layer.", this);
        }

        public virtual bool CanInteract(SlimeController slime)
        {
            return IsActive && Time.time >= nextAvailableTime && ExtraCanInteract(slime);
        }

        public void Interact(SlimeController slime)
        {
            nextAvailableTime = Time.time + cooldown;
            OnInteract(slime);
        }

        protected abstract void OnInteract(SlimeController slime);
        protected virtual bool ExtraCanInteract(SlimeController slime) => true;

        public virtual void SetActive(bool active) => IsActive = active;
    }
}
