using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.InputHandling;

namespace SlimeJumping.UI.Framework
{
    /// Per-scene UI root. Owns the 4 layer Canvases, registers every UIPanel at
    /// Start, and manages the LIFO modal stack. While any modal is open, the
    /// referenced SlimeInputReader is disabled — its OnDisable shuts off the
    /// Input System "Player" action map, freezing slime input.
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [Header("Layer Canvases")]
        [SerializeField] private Canvas hudLayer;
        [SerializeField] private Canvas menuLayer;
        [SerializeField] private Canvas modalLayer;
        [SerializeField] private Canvas fullscreenLayer;

        [Header("Input Gate (optional)")]
        [Tooltip("When a modal is open, this input reader is disabled so the slime can't be driven. Leave null in scenes without a slime.")]
        [SerializeField] private SlimeInputReader slimeInputReader;

        private readonly List<UIPanel> registered = new List<UIPanel>();
        private readonly Stack<UIPanel> modalStack = new Stack<UIPanel>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[UIManager] A second UIManager was created in the scene — destroying {name}.", this);
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Register(UIPanel panel)
        {
            if (panel == null) return;
            if (registered.Contains(panel)) return;
            registered.Add(panel);
            ReparentToLayer(panel);
        }

        public void Unregister(UIPanel panel)
        {
            if (panel == null) return;
            registered.Remove(panel);
            // Drain and rebuild the modal stack skipping the destroyed panel — if we
            // only popped when it was on top, a mid-stack destroy would leak the
            // input gate and permanently freeze slime input.
            if (ContainsInStack(panel))
            {
                RemoveFromStack(panel);
                UpdateInputGate();
            }
        }

        private bool ContainsInStack(UIPanel panel)
        {
            foreach (var m in modalStack) if (m == panel) return true;
            return false;
        }

        private void RemoveFromStack(UIPanel panel)
        {
            var tmp = new List<UIPanel>(modalStack.Count);
            while (modalStack.Count > 0)
            {
                var m = modalStack.Pop();
                if (m != panel) tmp.Add(m);
            }
            for (int i = tmp.Count - 1; i >= 0; i--) modalStack.Push(tmp[i]);
        }

        public void Open(UIPanel panel)
        {
            if (panel == null) return;
            if (panel.IsModal)
            {
                modalStack.Push(panel);
                UpdateInputGate();
            }
            panel.Open();
        }

        public void Close(UIPanel panel)
        {
            if (panel == null) return;
            panel.Close();
            if (panel.IsModal)
            {
                RemoveFromStack(panel);
                UpdateInputGate();
            }
        }

        /// Find the first registered panel of type T. Useful for scripts that
        /// want to pop the options panel without holding a direct reference.
        public T Get<T>() where T : UIPanel
        {
            for (int i = 0; i < registered.Count; i++)
                if (registered[i] is T match) return match;
            return null;
        }

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Escape)) return;

            if (modalStack.Count > 0)
            {
                Close(modalStack.Peek());
                return;
            }

            // Empty stack: open the first panel flagged OpenOnEscape (typically the Options panel).
            for (int i = 0; i < registered.Count; i++)
            {
                var p = registered[i];
                if (p != null && p.OpenOnEscape && !p.IsOpen)
                {
                    Open(p);
                    return;
                }
            }
        }

        private void ReparentToLayer(UIPanel panel)
        {
            var target = LayerCanvas(panel.Layer);
            if (target == null) return;
            // Keep local scale / position when moving into the layer.
            panel.transform.SetParent(target.transform, worldPositionStays: false);
            var rt = panel.transform as RectTransform;
            if (rt != null)
            {
                // A panel's prefab is authored full-rect by default; honor that.
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
        }

        private Canvas LayerCanvas(UILayer layer)
        {
            switch (layer)
            {
                case UILayer.Hud: return hudLayer;
                case UILayer.Menu: return menuLayer;
                case UILayer.Modal: return modalLayer;
                case UILayer.Fullscreen: return fullscreenLayer;
                default: return menuLayer;
            }
        }

        private void UpdateInputGate()
        {
            if (slimeInputReader == null) return;
            bool shouldEnable = modalStack.Count == 0;
            if (slimeInputReader.enabled != shouldEnable)
                slimeInputReader.enabled = shouldEnable;
        }
    }
}
