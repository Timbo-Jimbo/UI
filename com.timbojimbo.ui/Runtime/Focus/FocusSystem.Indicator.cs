using TimboJimbo.UI.Layout;
using TimboJimbo.UI.Motion;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TimboJimbo.UI.Focus
{
    public static partial class FocusSystem
    {
        // ── The indicator ────────────────────────────────────────────────────────
        //
        // The indicator shown is that of the innermost active scope around what has focus with one of its own
        // (FocusScope.NewIndicator), made from its prefab (or the nearest one above it) the first time it shows, as the
        // last child of its indicator parent. It floats attached to what has focus (FloatingAttach.Element), so it flies
        // from one focused element to the next on its own animation; when focus moves to another scope's indicator, the
        // one leaves where it is and the other appears where focus is, in one change. It is hidden while focus is not
        // shown, and appears again where focus is then.

        // How far a move that found nothing to go to nudges the indicator that way, in its parent's units, before it
        // springs back.
        private const float NudgeDistance = 10f;

        // The indicator shown, and the node it is attached to.
        private static LayoutNode s_indicator;
        private static LayoutNode s_indicated;

        // Shows the indicator on `focus` (none for focus not shown).
        private static void UpdateIndicator(Selectable focus)
        {
            var target = focus != null ? focus.GetComponentInParent<LayoutNode>() : null;
            var scope = target != null ? IndicatorScope(focus.transform) : null;
            var prefab = scope != null ? PrefabOf(scope) : null;
            if (prefab == null)
            {
                if (s_indicator != null)
                {
                    var gone = s_indicator;
                    MotionSystem.Animate(() => gone.Display = DisplayMode.None);
                }
                s_indicator = null;
                s_indicated = null;
                return;
            }

            var indicator = scope.Indicator;
            if (indicator != null && indicator == s_indicator && target == s_indicated)
            {
                // Kept over what has been added to its parent since.
                var at = indicator.transform;
                if (at.GetSiblingIndex() != at.parent.childCount - 1)
                    at.SetAsLastSibling();
                return;
            }

            var previous = s_indicator;
            MotionSystem.Animate(() =>
            {
                if (previous != null && previous != indicator)
                    previous.Display = DisplayMode.None;
                // Made inside the change, so layout meets it there and it appears where it is attached.
                if (indicator == null)
                    indicator = scope.Indicator = Make(scope, prefab);
                indicator.transform.SetAsLastSibling();
                var floating = indicator.Floating;
                floating.AttachTo = FloatingAttach.Element;
                floating.Element = target;
                indicator.Floating = floating;
                indicator.Display = DisplayMode.Visible;
            });
            s_indicator = indicator;
            s_indicated = target;
        }

        // The innermost active scope at or above `transform` with an indicator of its own.
        private static FocusScope IndicatorScope(Transform transform)
        {
            for (var at = transform; at != null; at = at.parent)
            {
                if (at.TryGetComponent(out FocusScope scope) && scope.Active && scope.NewIndicator)
                    return scope;
            }
            return null;
        }

        // The prefab of a scope's indicator: its own, or that of the nearest scope above it with one of its own.
        private static LayoutNode PrefabOf(FocusScope scope)
        {
            for (var at = scope.transform; at != null; at = at.parent)
            {
                if (at.TryGetComponent(out FocusScope above) && above.NewIndicator && above.IndicatorPrefab != null)
                    return above.IndicatorPrefab;
            }
            return null;
        }

        private static LayoutNode Make(FocusScope scope, LayoutNode prefab)
        {
            var parent = scope.IndicatorParent != null ? scope.IndicatorParent.transform : scope.transform;
            var indicator = Object.Instantiate(prefab, parent, false);
            indicator.name = prefab.name;
            return indicator;
        }

        // Sets the indicator moving `direction` from where it is, as fast as takes it about NudgeDistance out on its
        // spring before it comes back: a critically damped spring set off from rest at v gets v / (omega e) out.
        private static void Nudge(MoveDirection direction)
        {
            var indicator = s_indicator;
            if (indicator == null) return;
            var animation = LayoutSystem.AnimationOf(indicator);
            if (animation.Duration <= 0f) return;
            Spring.Parameters(animation, out float omega, out _);
            Vector2 way = direction switch
            {
                MoveDirection.Left => Vector2.left,
                MoveDirection.Right => Vector2.right,
                MoveDirection.Up => Vector2.up,
                _ => Vector2.down,
            };
            var velocity = (Vector3)(way * (NudgeDistance * omega * Mathf.Exp(1f)));
            var parent = indicator.transform.parent;
            indicator.Fling(parent != null ? parent.TransformVector(velocity) : velocity);
        }
    }
}
