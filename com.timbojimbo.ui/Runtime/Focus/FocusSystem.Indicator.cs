using System;
using System.Collections.Generic;
using TimboJimbo.UI.Layout;
using TimboJimbo.UI.Motion;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TimboJimbo.UI.Focus
{
    public static partial class FocusSystem
    {
        // ── The indicator ────────────────────────────────────────────────────────
        //
        // The indicator on what has focus is that of the innermost active scope around it with one of its own
        // (FocusScope.NewIndicator, or a group; for a group's own stop, the scope around the group), made from its prefab
        // (or the nearest one above it) the first time it shows, as the last child of its indicator parent. It floats
        // attached to what has focus (FloatingAttach.Element), so it flies from one focused element to the next on its own
        // animation; when focus moves to another scope's indicator, the one leaves where it is and the other appears where
        // focus is, in one change. Each group and modal focus is inside keeps its way in ringed too, dimmed, by the
        // indicator that was on it: a group's stop (itself, or the button it is entered through), and what opened a modal
        // (while it is there to go back to). Going in dims the indicator that was on the way in, and coming back out
        // brightens it again. All are hidden while focus is not shown, and appear again where focus is then.

        // How far a move that found nothing to go to nudges the indicator that way, in its parent's units, before it
        // springs back.
        private const float NudgeDistance = 10f;

        // How bright the indicator on the way into a group or modal focus is inside stays.
        private const float EnteredOpacity = 0.35f;

        private struct Shown
        {
            public FocusScope Scope;
            public LayoutNode Indicator;
            public LayoutNode Target;
            public bool Dim;
        }

        // The indicators shown, and those wanted this frame; the one on what has focus, which a move nudges.
        private static readonly List<Shown> s_shownIndicators = new();
        private static readonly List<Shown> s_wanted = new();
        private static LayoutNode s_indicator;
        private static readonly Action s_showIndicators = ShowIndicators;

        // Shows the indicator on `focus` (none for focus not shown), and on the way into each group or modal focus is
        // inside, innermost first.
        private static void UpdateIndicators(Selectable focus)
        {
            s_wanted.Clear();
            if (focus != null)
            {
                Want(focus, false);
                for (int i = s_entered.Count - 1; i >= 0; i--)
                {
                    var (entered, restore) = s_entered[i];
                    Want(entered.Kind == FocusScopeKind.Group ? entered.Stop : Opener(restore), true);
                }
            }

            bool changed = false;
            foreach (var shown in s_shownIndicators)
            {
                if (shown.Indicator != null && IndexOf(s_wanted, shown.Scope) < 0)
                    changed = true;
            }
            for (int i = 0; i < s_wanted.Count && !changed; i++)
                changed = !Unchanged(s_wanted[i]);

            if (changed)
            {
                MotionSystem.Animate(s_showIndicators);
            }
            else
            {
                // Kept over what has been added to their parents since.
                for (int i = 0; i < s_wanted.Count; i++)
                {
                    var wanted = s_wanted[i];
                    wanted.Indicator = wanted.Scope.Indicator;
                    s_wanted[i] = wanted;
                    var at = wanted.Indicator.transform;
                    if (at.GetSiblingIndex() != at.parent.childCount - 1)
                        at.SetAsLastSibling();
                }
            }

            s_shownIndicators.Clear();
            s_shownIndicators.AddRange(s_wanted);
            s_indicator = s_wanted.Count > 0 && !s_wanted[0].Dim ? s_wanted[0].Indicator : null;
        }

        // The change showing the indicators wanted: those no longer wanted leave where they are; those new, moved or
        // brightened or dimmed are attached, shown and made as bright as they are wanted. One made here, inside the
        // change, is met by layout there, and appears where it is attached.
        private static void ShowIndicators()
        {
            foreach (var shown in s_shownIndicators)
            {
                if (shown.Indicator != null && IndexOf(s_wanted, shown.Scope) < 0)
                    shown.Indicator.Display = DisplayMode.None;
            }
            for (int i = 0; i < s_wanted.Count; i++)
            {
                var wanted = s_wanted[i];
                bool unchanged = Unchanged(wanted);
                var indicator = wanted.Scope.Indicator;
                if (indicator == null)
                    indicator = wanted.Scope.Indicator = Make(wanted.Scope, PrefabOf(wanted.Scope));
                wanted.Indicator = indicator;
                s_wanted[i] = wanted;
                indicator.transform.SetAsLastSibling();
                if (unchanged) continue;
                var floating = indicator.Floating;
                floating.AttachTo = FloatingAttach.Element;
                floating.Element = wanted.Target;
                indicator.Floating = floating;
                indicator.Opacity = wanted.Dim ? EnteredOpacity : 1f;
                indicator.Display = DisplayMode.Visible;
            }
        }

        // Wants the indicator of the scope around `stop` on it, unless that is wanted already (on what has focus, which comes
        // first, or on a way in further in).
        private static void Want(Selectable stop, bool dim)
        {
            if (stop == null) return;
            var scope = IndicatorScope(Around(stop));
            var target = stop.GetComponentInParent<LayoutNode>();
            if (scope == null || target == null || PrefabOf(scope) == null || IndexOf(s_wanted, scope) >= 0) return;
            s_wanted.Add(new Shown { Scope = scope, Target = target, Dim = dim });
        }

        // What opened a modal, `restore`, while it is there to go back to: usable, and drawn shown and staying (an item a
        // panel lifted out of, hidden meanwhile, has nothing to show).
        private static Selectable Opener(GameObject restore)
        {
            var opener = Focused(restore);
            if (opener == null || !opener.IsActive() || !opener.IsInteractable()) return null;
            var node = opener.GetComponentInParent<LayoutNode>();
            return LayoutSystem.Focusable(node) || LayoutSystem.Waiting(node) ? opener : null;
        }

        // Whether a wanted indicator is shown already as it is wanted.
        private static bool Unchanged(Shown wanted)
        {
            int at = IndexOf(s_shownIndicators, wanted.Scope);
            if (at < 0) return false;
            var shown = s_shownIndicators[at];
            return shown.Indicator != null && shown.Indicator == wanted.Scope.Indicator && shown.Target == wanted.Target && shown.Dim == wanted.Dim;
        }

        private static int IndexOf(List<Shown> list, FocusScope scope)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Scope == scope) return i;
            }
            return -1;
        }

        // The innermost active scope at or above `transform` with an indicator of its own (a group always has one).
        private static FocusScope IndicatorScope(Transform transform)
        {
            for (var at = transform; at != null; at = at.parent)
            {
                if (at.TryGetComponent(out FocusScope scope) && scope.Active && HasIndicator(scope))
                    return scope;
            }
            return null;
        }

        private static bool HasIndicator(FocusScope scope) => scope.NewIndicator || scope.Kind == FocusScopeKind.Group;

        // The prefab of a scope's indicator: its own, or that of the nearest scope above it with one of its own.
        private static LayoutNode PrefabOf(FocusScope scope)
        {
            for (var at = scope.transform; at != null; at = at.parent)
            {
                if (at.TryGetComponent(out FocusScope above) && HasIndicator(above) && above.IndicatorPrefab != null)
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

        // Sets the indicator on what has focus moving `direction` from where it is, as fast as takes it about
        // NudgeDistance out on its spring before it comes back: a critically damped spring set off from rest at v gets
        // v / (omega e) out.
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
