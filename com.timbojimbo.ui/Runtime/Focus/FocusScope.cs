using TimboJimbo.UI.Layout;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace TimboJimbo.UI.Focus
{
    /// <summary>What a <see cref="FocusScope"/> does with focus.</summary>
    public enum FocusScopeKind
    {
        /// <summary>
        /// A group focus moves in and out of freely, by where things are drawn, landing on whatever is nearest that way, as
        /// SwiftUI's focusSection (which remembers nothing): a sidebar, a list, a tab bar. It can have an indicator of its
        /// own, take Cancel, and say where focus goes when what had it in there goes (<see cref="FocusScope.DefaultFocus"/>).
        /// Entered at <see cref="FocusScopeEntry.LastFocusedOrDefault"/> (<see cref="FocusScope.EnterAt"/>), focus coming in
        /// lands where the section says instead.
        /// </summary>
        Section,

        /// <summary>
        /// While it is active nothing outside it can be focused, and focus cannot leave it, as the web's modal dialog and
        /// inert: a sheet over a screen. It takes focus as it becomes active, and gives it back to what had it when it goes,
        /// as UIKit restores focus after a modal transition. The most recently active modal wins.
        /// </summary>
        Modal,
    }

    /// <summary>Where keyboard or gamepad navigation coming into a <see cref="FocusScope"/> from outside lands.</summary>
    public enum FocusScopeEntry
    {
        /// <summary>On whatever inside it is nearest the way it moves, as anywhere else: lists side by side.</summary>
        Nearest,

        /// <summary>
        /// On what it last focused (<see cref="FocusScope.Remembered"/>), else its default: its
        /// <see cref="FocusScope.DefaultFocus"/>, else its first Selectable in reading order (top left), as a modal takes
        /// focus as it appears. As UIKit's preferred focus and remembersLastFocusedIndexPath: a page beside its sidebar, the
        /// sidebar, a tab bar, which focus comes into as a whole, and back to where it left.
        /// </summary>
        LastFocusedOrDefault,
    }

    /// <summary>
    /// A part of the UI that focus treats as one (see <see cref="FocusSystem"/>): a section, which focus moves in and out
    /// of freely, or a modal that keeps focus inside it. It covers the Selectables under
    /// it, remembers the last of them focused, and can take Cancel (Escape, a gamepad's B, Android's Back). It is active
    /// while it is enabled and what it is drawn in is shown, not on its way out, and taking the pointer.
    /// With <see cref="NewIndicator"/> it has a focus indicator of its own, shown on what has focus inside it (see
    /// <see cref="FocusSystem"/>); without, focus inside it shows the indicator of the scope around it.
    /// </summary>
    [AddComponentMenu("Timbo Jimbo/UI/Focus Scope")]
    [DisallowMultipleComponent]
    public sealed class FocusScope : MonoBehaviour
    {
        [Tooltip("Section: focus moves in and out of it freely, by where things are drawn. Modal: nothing outside it can be focused while it is active; it takes focus as it appears and gives it back as it goes.")]
        [SerializeField] private FocusScopeKind _kind = FocusScopeKind.Section;

        [Tooltip("What it focuses when it takes focus (a modal appearing, or focus coming back to it as what had it inside goes) and has nothing remembered: none for its first Selectable in reading order.")]
        [SerializeField] private Selectable _defaultFocus;

        [Tooltip("Whether Cancel (Escape, a gamepad's B, Android's Back) stops here, raising Cancelled, while focus is inside it (or, for a modal, while it is the topmost one). Off, Cancel goes on to the scope around it.")]
        [SerializeField] private bool _takesCancel;

        [Tooltip("Raised by Cancel, when it takes Cancel: close the sheet, go back.")]
        [SerializeField] private UnityEvent _cancelled = new();

        [Tooltip("Where navigation coming into it from outside lands. Nearest: whatever is nearest the way it moves (lists side by side). Last Focused Or Default: what it last focused, else its Default Focus, else its first in reading order (a page beside its sidebar, the sidebar, a tab bar). A modal always takes focus so as it appears.")]
        [SerializeField] private FocusScopeEntry _enterAt = FocusScopeEntry.Nearest;

        [Tooltip("Whether it has a focus indicator of its own, shown on what has focus inside it, rather than the one of the scope around it. Focus moving into it hides the other indicator and shows this one where focus lands, rather than flying across.")]
        [SerializeField] private bool _newIndicator;

        [Tooltip("With an indicator of its own, what it is made from: a prefab whose root is a layout node, attached to what has focus. None for the prefab of the nearest scope above it with one.")]
        [SerializeField] private LayoutNode _indicatorPrefab;

        [Tooltip("With an indicator of its own, the node it is made in, as its last child: where it is drawn and what clips it. None for this scope's own object.")]
        [SerializeField] private LayoutNode _indicatorParent;

        public FocusScopeKind Kind { get => _kind; set => _kind = value; }

        /// <summary>
        /// What it focuses when it takes focus (a modal appearing, or focus coming back to it as what had it inside goes)
        /// and has nothing remembered; null for its first in reading order.
        /// </summary>
        public Selectable DefaultFocus { get => _defaultFocus; set => _defaultFocus = value; }

        /// <summary>
        /// Whether Cancel stops here, raising <see cref="Cancelled"/>, while focus is inside it, or for a modal while it is
        /// the topmost; otherwise Cancel goes on to the scope around it.
        /// </summary>
        public bool TakesCancel { get => _takesCancel; set => _takesCancel = value; }

        /// <summary>Raised by Cancel (Escape, a gamepad's B, Android's Back) when it <see cref="TakesCancel"/>.</summary>
        public UnityEvent Cancelled => _cancelled;

        /// <summary>
        /// Whether it has a focus indicator of its own, shown on what has focus inside it (and in scopes inside it without
        /// one of their own), rather than the one of the scope around it. Focus moving between two indicators' scopes hides
        /// the one and shows the other where focus lands, rather than flying across: motion stays inside a group, and the
        /// indicator appearing anew shows where one group ends.
        /// </summary>
        public bool NewIndicator { get => _newIndicator; set => _newIndicator = value; }

        /// <summary>
        /// Where keyboard or gamepad navigation coming into it from outside lands: on whatever is nearest
        /// (<see cref="FocusScopeEntry.Nearest"/>, the default), or on what it last focused, else its default
        /// (<see cref="FocusScopeEntry.LastFocusedOrDefault"/>). A modal always takes focus at what it last focused, else
        /// its default, as it appears.
        /// </summary>
        public FocusScopeEntry EnterAt { get => _enterAt; set => _enterAt = value; }

        /// <summary>
        /// With <see cref="NewIndicator"/>, what its indicator is made from: a prefab whose root is a
        /// <see cref="LayoutNode"/>, made once, the first time it shows, and attached to what has focus (its
        /// <see cref="Floating.AttachTo"/> set to <see cref="FloatingAttach.Element"/>, its points and offset as the prefab
        /// has them), on its own <see cref="LayoutNode.Animation"/> and <see cref="LayoutNode.DisplayEffect"/>. Null for the
        /// prefab of the nearest scope above it with one; with none anywhere, there is no indicator. Its graphics should
        /// not take the pointer. Changed once it is made, it is used the next time the scope is enabled.
        /// </summary>
        public LayoutNode IndicatorPrefab { get => _indicatorPrefab; set => _indicatorPrefab = value; }

        /// <summary>
        /// With <see cref="NewIndicator"/>, the node its indicator is made in, kept as its last child so it draws over the
        /// rest: where it is drawn and what clips it (inside a scroll container, it is cut at its edges as the items are).
        /// It has to be in the same layout tree as what it shows focus on. Null for this scope's own object.
        /// </summary>
        public LayoutNode IndicatorParent { get => _indicatorParent; set => _indicatorParent = value; }

        /// <summary>The last Selectable focused inside it, while there is one.</summary>
        public Selectable Remembered { get; internal set; }

        /// <summary>
        /// Whether it is active, as the last frame found it: enabled, and what it is drawn in shown, not on its way out, and
        /// taking the pointer.
        /// </summary>
        public bool IsActive => Active;

        // Whether it is active, as the last frame found it, and when it last became so (for stacking modals).
        internal bool Active;
        internal int ActiveSince;

        // Its indicator, once made (NewIndicator).
        internal LayoutNode Indicator;

        private void OnEnable() => FocusSystem.Register(this);

        // Its indicator goes with it, made again from the prefab as it next shows.
        private void OnDisable()
        {
            FocusSystem.Unregister(this);
            if (Indicator != null)
                Destroy(Indicator.gameObject);
            Indicator = null;
        }
    }
}
