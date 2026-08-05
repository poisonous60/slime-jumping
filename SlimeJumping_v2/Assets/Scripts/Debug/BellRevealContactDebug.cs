using UnityEngine;

namespace SlimeJumping.Debugging
{
    /// <summary>Read-only probe for the hidden-platform "can't stand on it" report: attached to the
    /// slime, logs every 2D collision enter/exit with collider identity so the console shows whether
    /// contact events fire at the reveal platform at all. Delete when the question is answered.</summary>
    [DefaultExecutionOrder(3000)]
    public class BellRevealContactDebug : MonoBehaviour
    {
        // Unity delivers collision messages to disabled MonoBehaviours on purpose, so the checkbox has
        // to be honoured here by hand or unticking the probe does nothing.
        void OnCollisionEnter2D(Collision2D c)
        {
            if (!enabled) return;
            Debug.Log($"[RevealDebug] ENTER {c.collider.name} layer={LayerMask.LayerToName(c.collider.gameObject.layer)} " +
                      $"relVel={c.relativeVelocity} point={(c.contactCount > 0 ? c.GetContact(0).point.ToString() : "n/a")}");
        }

        void OnCollisionExit2D(Collision2D c)
        {
            if (!enabled) return;
            Debug.Log($"[RevealDebug] EXIT {c.collider.name}");
        }
    }
}
