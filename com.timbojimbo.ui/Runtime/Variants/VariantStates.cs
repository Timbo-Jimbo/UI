using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TimboJimbo.UI.Variants
{
    /// <summary>
    /// Selects a variant of a group by how the pointer and selection treat its object: Hover, Pressed, Focused (selected
    /// by keyboard or gamepad navigation, or by code, but not by a click, as CSS's :focus-visible) and Disabled, for a
    /// custom hover effect or a custom button as much as a UGUI
    /// control. One state shows at a time, as UIKit's and Selectable's do: Disabled, then Pressed, then Focused, then
    /// Hover, else Default; a state given no variant passes to the next one that holds. Pressed holds while the press is
    /// down and the pointer is still over it, as UIKit's highlight drops when a touch drags out. Disabled is its own
    /// <see cref="Interactable"/> off, or a Selectable on the same object that is not interactable (a CanvasGroup above it
    /// included); disabled, it shows Disabled or Default, whatever the pointer does. It switches through
    /// <see cref="VariantSet.Set(string, string)"/>, so the switch animates as the group does. It hears the pointer as any
    /// pointer handler does: through a raycast target on it or under it. A Selectable's own transition is left as it is
    /// (set it to None to leave the look to the variants).
    /// </summary>
    [AddComponentMenu("Timbo Jimbo/Variants/Variant States")]
    public sealed class VariantStates : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
        ISelectHandler, IDeselectHandler
    {
        [Tooltip("The variant set it selects in: the one on this object or above it, when none is given.")]
        [SerializeField] private VariantSet _target;

        [Tooltip("The group it selects in.")]
        [SerializeField] private string _group = "State";

        [Tooltip("The variant while the pointer is over it; empty for none.")]
        [SerializeField] private string _hover = "Hover";

        [Tooltip("The variant while a press on it is down and the pointer still over it; empty for none.")]
        [SerializeField] private string _pressed = "Pressed";

        [Tooltip("The variant while it is selected by keyboard or gamepad navigation, or by code (the EventSystem's selected object), but not by a click, as CSS's :focus-visible; empty for none.")]
        [SerializeField] private string _focused = "Focused";

        [Tooltip("The variant while it is not interactable; empty for Default.")]
        [SerializeField] private string _disabled = "Disabled";

        [Tooltip("Whether it can be used: off, it is Disabled. A Selectable on the same object that is not interactable disables it too.")]
        [SerializeField] private bool _interactable = true;

        private bool _inside;
        private bool _down;
        private bool _focus;
        private Selectable _selectable;
        private bool _selectableInteractable = true;

        /// <summary>The variant set it selects in: the one given, or else the one on this object or above it.</summary>
        public VariantSet Target => _target != null ? _target : GetComponentInParent<VariantSet>(true);

        public string Group => _group;

        /// <summary>Whether it can be used. Off, it is Disabled, as it is while a Selectable on it is not interactable.</summary>
        public bool Interactable
        {
            get => _interactable;
            set
            {
                if (_interactable == value) return;
                _interactable = value;
                Refresh();
            }
        }

        /// <summary>Whether it is disabled: not <see cref="Interactable"/>, or a Selectable on it is not interactable.</summary>
        public bool IsDisabled => !_interactable || (_selectable != null && !_selectable.IsInteractable());

        /// <summary>Selects in <paramref name="target"/>'s <paramref name="group"/>, with these variants for its states (empty for none).</summary>
        public void Setup(VariantSet target, string group, string hover, string pressed, string focused, string disabled)
        {
            _target = target;
            _group = group;
            _hover = hover;
            _pressed = pressed;
            _focused = focused;
            _disabled = disabled;
            Refresh();
        }

        private void OnEnable()
        {
            TryGetComponent(out _selectable);
            _selectableInteractable = _selectable == null || _selectable.IsInteractable();
            Refresh();
        }

        // Gone, it is neither under the pointer nor pressed nor focused, and shows as it would at rest.
        private void OnDisable()
        {
            _inside = _down = _focus = false;
            Refresh();
        }

        // A Selectable says nothing when it stops or starts being interactable (its own flag, or a CanvasGroup's).
        private void Update()
        {
            if (_selectable == null) return;
            bool interactable = _selectable.IsInteractable();
            if (interactable == _selectableInteractable) return;
            _selectableInteractable = interactable;
            Refresh();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _inside = true;
            Refresh();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _inside = false;
            Refresh();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            _down = true;
            Refresh();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            _down = false;
            Refresh();
        }

        // Focused only when selected other than by a press, as CSS's :focus-visible: keyboard or gamepad navigation, or
        // code. A Selectable pressed selects itself with the press's event data; it is focused, but shows no ring.
        public void OnSelect(BaseEventData eventData)
        {
            _focus = eventData is not PointerEventData;
            Refresh();
        }

        public void OnDeselect(BaseEventData eventData)
        {
            _focus = false;
            Refresh();
        }

        // Shows the state that holds, as the group's own selection (Default included), so the group decides for itself
        // and passes its state down rather than inheriting one. Outside play mode it selects nothing: what an object
        // looks like in the editor is its own selection's, not the pointer's.
        private void Refresh()
        {
            if (!Application.isPlaying) return;
            var target = Target;
            if (target == null) return;
            int group = target.IndexOfGroup(_group);
            if (group < 0) return;

            string variant = Pick(target.Groups[group]);
            if (target.Groups[group].Inherits || target.Groups[group].Selected != variant)
                target.Set(_group, variant);
        }

        internal string Pick(VariantGroup group)
        {
            if (IsDisabled)
                return Has(group, _disabled) ? _disabled : "";
            if (_down && _inside && Has(group, _pressed)) return _pressed;
            if (_focus && Has(group, _focused)) return _focused;
            if (_inside && Has(group, _hover)) return _hover;
            return "";
        }

        private static bool Has(VariantGroup group, string variant) => !string.IsNullOrEmpty(variant) && group.IndexOf(variant) >= 0;
    }
}
