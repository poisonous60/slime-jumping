using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Interaction
{
    /// <summary>
    /// Tier 3 verification interactable: logs note text to console on interact.
    /// Requires a trigger Collider2D on the same GameObject, set to the Interactable layer.
    /// </summary>
    public class MemoNote : MonoBehaviour, IInteractable
    {
        [SerializeField] [TextArea] private string noteText = "Hello, Slime!";

        public string InteractPrompt => "Read";

        public bool CanInteract(SlimeController slime)
        {
            return true;
        }

        public void Interact(SlimeController slime)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[MemoNote] Read note: {noteText}");
#endif
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, 0.5f);
            Gizmos.DrawIcon(transform.position + Vector3.up * 0.8f, "d_TextAsset Icon", true);
        }
    }
}
