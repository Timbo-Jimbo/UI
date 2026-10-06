namespace TimboJimbo.UI.Focus
{
    /// <summary>A key <see cref="FocusSystem"/> acts on, which an <see cref="IFocusKeyHandler"/> can keep for itself.</summary>
    public enum FocusKey
    {
        /// <summary>The input module's Submit (Enter, a gamepad's A), which enters a group on its stop.</summary>
        Submit,

        /// <summary>
        /// The input module's Cancel (Escape, a gamepad's B, Android's Back), which leaves the group focus is in or raises
        /// the Cancelled of the scope around it that takes Cancel.
        /// </summary>
        Cancel,

        /// <summary>Tab and Shift-Tab, which move focus through reading order.</summary>
        Tab,
    }

    /// <summary>
    /// On a Selectable's object, a component that takes some of the keys focus acts on itself, as a text field being
    /// edited takes Enter and Escape, and as a key press goes to UIKit's first responder before anything up the responder
    /// chain. <see cref="FocusSystem"/> leaves a key it says it handles to it entirely: Submit enters no group, Cancel
    /// closes no sheet, and Tab moves focus nowhere. It reads the key itself. While it has focus and keeps any key, Tab
    /// out of it moves focus at once, even while focus does not show (<see cref="FocusSystem.FocusVisible"/>): what is
    /// being typed into is plainly where focus is. UGUI's input module still sends it Submit and Cancel
    /// (<see cref="UnityEngine.EventSystems.ISubmitHandler"/>, <see cref="UnityEngine.EventSystems.ICancelHandler"/>)
    /// unless it uses the frame's <see cref="UnityEngine.EventSystems.IUpdateSelectedHandler"/> event, as a field being
    /// edited does to keep its keys from UGUI's navigation.
    /// </summary>
    public interface IFocusKeyHandler
    {
        /// <summary>
        /// Whether <paramref name="key"/> is its own, as things stand, from its own state rather than from having focus
        /// (a field: Submit and Cancel while it is being edited). Asked once a frame while it has focus, as focus finishes
        /// the frame: a key pressed in the next frame goes by what it said then, as things stood when the key came in,
        /// since it may act on the key before focus does (Escape stopping a field's editing, which then takes Escape no
        /// longer).
        /// </summary>
        bool HandlesKey(FocusKey key);
    }
}
