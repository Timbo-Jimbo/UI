using UnityEngine.EventSystems;

namespace TimboJimbo.UI.Focus
{
    /// <summary>
    /// On a Selectable's object, a component that takes some moves itself rather than letting them move focus (scrolling
    /// what it shows, swiping it away, moving it somewhere), as a slider takes its own axis to change its value, and as
    /// UIKit's shouldUpdateFocus keeps focus where it is. <see cref="FocusSystem"/> leaves a move it says it handles to
    /// it: it points that way nowhere, so UGUI moves focus no further, and does not nudge the indicator for a move that
    /// went nowhere. It does the move itself, as an <see cref="IMoveHandler"/> on the same object.
    /// </summary>
    public interface IFocusMoveHandler
    {
        /// <summary>Whether a move <paramref name="direction"/> is its own, as things stand.</summary>
        bool HandlesMove(MoveDirection direction);
    }
}
