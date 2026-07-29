using TMPro;
using UnityEngine;

namespace SlimeJumping.UI.Animation
{
    /// Value changes go through Set() and fire a bump tween; callers wanting a silent
    /// update touch .text directly via the Label property.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TextMeshProUGUI))]
    [RequireComponent(typeof(RectTransform))]
    public class BumpableLabel : MonoBehaviour
    {
        [SerializeField] private JuiceProfile profile;
        [Tooltip("Skip the bump the first time Set() is called so a panel can populate its labels without flashing on open.")]
        [SerializeField] private bool skipFirstSet = true;

        private TextMeshProUGUI label;
        private RectTransform rt;
        private bool primed;
        private string lastValue;

        public TextMeshProUGUI Label
        {
            get
            {
                EnsureRefs();
                return label;
            }
        }

        public void SetProfile(JuiceProfile p) => profile = p;

        private void Awake() => EnsureRefs();

        private void EnsureRefs()
        {
            if (label == null) label = GetComponent<TextMeshProUGUI>();
            if (rt == null) rt = GetComponent<RectTransform>();
        }

        public void Set(string value)
        {
            EnsureRefs();
            if (value == lastValue)
            {
                return;
            }
            label.text = value;
            lastValue = value;
            if (!primed && skipFirstSet)
            {
                primed = true;
                return;
            }
            primed = true;
            UITweenLibrary.Bump(rt, profile);
        }

        /// Force the next Set() to bump even if skipFirstSet was true.
        public void PrimeBumping() => primed = true;

        private void OnDisable()
        {
            // Un-prime so the next panel open's initial population is silent again.
            primed = false;
            lastValue = null;
        }
    }
}
